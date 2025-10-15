using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AndroidSideloader.Services;
using AndroidSideloader.Sideloader;

namespace AndroidSideloader.Utilities
{
    /// <summary>
    /// Cross-platform Android Debug Bridge (ADB) wrapper for device communication
    /// </summary>
    public class Adb
    {
        private static readonly SettingsManager Settings = SettingsManager.Instance;
        private static IDialogService _dialogService;

        // Device state
        public static string DeviceId = "";
        public static string Package = "";
        public static bool WirelessadbOn;

        // ADB path configuration
        public static string AdbFolderPath => Settings.AdbFolder;
        public static string AdbFilePath => Settings.AdbPath;

        /// <summary>
        /// Set the dialog service for showing user dialogs
        /// </summary>
        public static void SetDialogService(IDialogService dialogService)
        {
            _dialogService = dialogService;
        }

        /// <summary>
        /// Executes an ADB command and returns the output
        /// </summary>
        /// <param name="command">The ADB command to execute (without "adb" prefix)</param>
        /// <returns>ProcessOutput with stdout and stderr</returns>
        public static ProcessOutput RunAdbCommandToString(string command)
        {
            // Strip "adb" prefix if user included it
            if (command.StartsWith("adb ", StringComparison.OrdinalIgnoreCase))
            {
                command = command.Substring(4);
            }

            // Add device ID if connected
            if (!string.IsNullOrEmpty(DeviceId) && !command.Contains("-s "))
            {
                command = $"-s {DeviceId} {command}";
            }

            // Selective logging (exclude verbose commands)
            var shouldLog = !command.Contains("dumpsys")
                            && !command.Contains("pm list")
                            && !command.Contains("shell input keyevent KEYCODE_WAKEUP");

            if (shouldLog)
            {
                Logger.Log($"Running ADB command: {command}");
            }

            var adb = new Process();
            adb.StartInfo.FileName = AdbFilePath;
            adb.StartInfo.Arguments = command;
            adb.StartInfo.UseShellExecute = false;
            adb.StartInfo.RedirectStandardOutput = true;
            adb.StartInfo.RedirectStandardError = true;
            adb.StartInfo.CreateNoWindow = true;
            adb.StartInfo.WorkingDirectory = AdbFolderPath;

            var output = new StringBuilder();
            var error = new StringBuilder();

            adb.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            adb.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            try
            {
                adb.Start();
                adb.BeginOutputReadLine();
                adb.BeginErrorReadLine();

                // Timeout for connect commands
                if (command.Contains("connect"))
                {
                    adb.WaitForExit(3000);
                }
                else
                {
                    adb.WaitForExit();
                }

                var stdout = output.ToString();
                var stderr = error.ToString();

                // Sanitize logs (replace full paths with CurrentDirectory)
                var sanitizedOutput = stdout.Replace(Environment.CurrentDirectory, "CurrentDirectory");
                var sanitizedError = stderr.Replace(Environment.CurrentDirectory, "CurrentDirectory");

                if (shouldLog && !string.IsNullOrWhiteSpace(sanitizedOutput))
                {
                    Logger.Log($"ADB Output: {sanitizedOutput}");
                }

                if (!string.IsNullOrWhiteSpace(sanitizedError))
                {
                    Logger.Log($"ADB Error: {sanitizedError}", LogLevel.Warning);
                }

                // Check for common errors
                if (stderr.Contains("device unauthorized") || stderr.Contains("ADB_VENDOR_KEYS"))
                {
                    if (!Settings.AdbDebugWarned)
                    {
                        AdbDebugWarning().GetAwaiter().GetResult();
                    }
                }

                if (stderr.Contains("not enough space") || stdout.Contains("not enough space"))
                {
                    ShowInsufficientStorageWarning().GetAwaiter().GetResult();
                }

                return new ProcessOutput(stdout, stderr);
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception running ADB command: {ex.Message}", LogLevel.Error);
                return new ProcessOutput("", ex.Message);
            }
        }

        /// <summary>
        /// Run a command through the system shell
        /// </summary>
        public static ProcessOutput RunAdbCommandToStringWoadb(string command, string path)
        {
            Logger.Log($"Running shell command: {command}");

            var process = new Process();
            process.StartInfo.FileName = PlatformHelper.GetShellPath();
            process.StartInfo.Arguments = $"{PlatformHelper.GetShellCommandPrefix()} \"{command}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WorkingDirectory = path;

            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (command.Contains("connect"))
                {
                    process.WaitForExit(3000);
                }
                else
                {
                    process.WaitForExit();
                }

                var stdout = output.ToString();
                var stderr = error.ToString();

                return new ProcessOutput(stdout, stderr);
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception running shell command: {ex.Message}", LogLevel.Error);
                return new ProcessOutput("", ex.Message);
            }
        }

        /// <summary>
        /// Generic command execution wrapper
        /// </summary>
        public static ProcessOutput RunCommandToString(string command, string path = "")
        {
            Logger.Log($"Running command: {command}");

            var process = new Process();
            process.StartInfo.FileName = PlatformHelper.GetShellPath();
            process.StartInfo.Arguments = $"{PlatformHelper.GetShellCommandPrefix()} \"{command}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            if (!string.IsNullOrEmpty(path))
            {
                process.StartInfo.WorkingDirectory = path;
            }

            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                var stdout = output.ToString();
                var stderr = error.ToString();

                if (stderr.Contains("No such file or directory"))
                {
                    Logger.Log("Asset path error detected", LogLevel.Error);
                }

                return new ProcessOutput(stdout, stderr);
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception running command: {ex.Message}", LogLevel.Error);
                return new ProcessOutput("", ex.Message);
            }
        }

        /// <summary>
        /// Uninstall a package from the connected device
        /// </summary>
        public static ProcessOutput UninstallPackage(string packageName)
        {
            Logger.Log($"Uninstalling package: {packageName}");
            return RunAdbCommandToString($"uninstall {packageName}");
        }

        /// <summary>
        /// Get available storage space on the device
        /// </summary>
        public static string GetAvailableSpace()
        {
            var output = RunAdbCommandToString("shell df");
            var lines = output.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            long totalSize = 0;
            long usedSize = 0;
            long freeSize = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("/dev/fuse") || line.StartsWith("/data/media"))
                {
                    var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        // Parse sizes in KB, divide by 1000 to get MB (matching original)
                        totalSize = long.Parse(parts[1]) / 1000;
                        usedSize = long.Parse(parts[2]) / 1000;
                        freeSize = long.Parse(parts[3]) / 1000;
                        break;
                    }
                }
            }

            // Format matching original: "Total space: X.XXGB\nUsed space: X.XXGB\nFree space: X.XXGB"
            return $"Total space: {((double)totalSize / 1000):0.00}GB\nUsed space: {((double)usedSize / 1000):0.00}GB\nFree space: {((double)freeSize / 1000):0.00}GB";
        }

        /// <summary>
        /// Copy OBB files to the device
        /// </summary>
        public static ProcessOutput CopyObb(string path)
        {
            var folder = Path.GetFileName(path);

            // Check if this is a file or directory
            if (folder.Contains("."))
            {
                // It's a file, get the parent directory name
                var lastFolder = Path.GetFileName(Path.GetDirectoryName(path));

                // Remove existing OBB folder and create new one
                RunAdbCommandToString($"shell rm -rf \"/sdcard/Android/obb/{lastFolder}\"");
                RunAdbCommandToString($"shell mkdir -p \"/sdcard/Android/obb/{lastFolder}\"");

                // Push the file
                return RunAdbCommandToString($"push \"{path}\" \"/sdcard/Android/obb/{lastFolder}/\"");
            }
            else
            {
                // It's a directory
                if (Directory.Exists(path))
                {
                    // Remove existing and push entire folder
                    RunAdbCommandToString($"shell rm -rf \"/sdcard/Android/obb/{folder}\"");
                    return RunAdbCommandToString($"push \"{path}\" \"/sdcard/Android/obb/\"");
                }
                else
                {
                    Logger.Log("No OBB folder found", LogLevel.Warning);
                    return new ProcessOutput("No OBB Folder found");
                }
            }
        }

        /// <summary>
        /// Install an APK on the device with automatic permissions grant
        /// Handles reinstall scenarios with automatic data backup/restore
        /// </summary>
        public static async Task<ProcessOutput> SideloadAsync(string path, string packagename = "", IDialogService dialogService = null)
        {
            Logger.Log($"Sideloading APK: {path}");

            var result = RunAdbCommandToString($"install -g \"{path}\"");
            var combinedOutput = result.Output + result.Error;

            // Check if installation succeeded
            if (result.Output.Contains("Success"))
            {
                Logger.Log("APK installed successfully");
                return result;
            }

            // Handle offline device
            if (combinedOutput.Contains("offline") && !Settings.NodeviceMode)
            {
                Logger.Log("Device is offline", LogLevel.Warning);
                if (dialogService != null)
                {
                    var shouldReconnect = await dialogService.ShowConfirmationAsync(
                        "Device is offline.\n\n" +
                        "Would you like to reconnect?",
                        "Device Offline");

                    if (shouldReconnect)
                    {
                        // Reconnect logic would go here
                        Logger.Log("User chose to reconnect device");
                    }
                }
                return result;
            }

            // Handle signature mismatch or version downgrade - requires reinstall
            if (combinedOutput.Contains("signatures do not match previously") ||
                combinedOutput.Contains("INSTALL_FAILED_VERSION_DOWNGRADE") ||
                combinedOutput.Contains("signatures do not match") ||
                combinedOutput.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") ||
                combinedOutput.Contains("failed to install"))
            {
                Logger.Log("Installation failed - signature or version mismatch detected", LogLevel.Warning);

                // Check if AutoReinstall is disabled - ask user for permission
                if (!Settings.AutoReinstall)
                {
                    if (dialogService != null)
                    {
                        var shouldReinstall = await dialogService.ShowOkCancelAsync(
                            "In place upgrade has failed.\n\n" +
                            "Rookie can attempt to backup your save data and reinstall the game automatically, " +
                            "however some games do not store their saves in an accessible location (less than 5%).\n\n" +
                            "Continue with reinstall?",
                            "In Place Upgrade Failed");

                        if (!shouldReinstall)
                        {
                            Logger.Log("User cancelled reinstall operation");
                            return result;
                        }
                    }
                    else
                    {
                        // No dialog service available - skip reinstall
                        Logger.Log("Reinstall required but no dialog service available to confirm", LogLevel.Warning);
                        return result;
                    }
                }

                // Perform reinstall workflow
                Logger.Log("Starting reinstall workflow...");

                // Validate package name
                if (string.IsNullOrEmpty(packagename))
                {
                    Logger.Log("Cannot reinstall: package name not provided", LogLevel.Error);
                    result.Error += "\nReinstall failed: package name required";
                    return result;
                }

                try
                {
                    // Create backup directory
                    var backupDir = Path.Combine(Environment.CurrentDirectory, "ReinstallBackup");
                    if (!Directory.Exists(backupDir))
                    {
                        Directory.CreateDirectory(backupDir);
                    }

                    var packageBackupPath = Path.Combine(backupDir, packagename);

                    // Step 1: Restart ADB server to ensure clean connection
                    Logger.Log("Restarting ADB server...");
                    RunAdbCommandToString("kill-server");
                    await Task.Delay(1000);
                    RunAdbCommandToString("devices");
                    await Task.Delay(500);

                    // Step 2: Backup game data
                    Logger.Log($"Backing up game data from /sdcard/Android/data/{packagename}...");
                    var backupResult = RunAdbCommandToString($"pull \"/sdcard/Android/data/{packagename}\" \"{packageBackupPath}\"");

                    var backupSucceeded = !backupResult.Error.Contains("does not exist") &&
                                          !backupResult.Error.Contains("failed");

                    if (backupSucceeded)
                    {
                        Logger.Log("Game data backed up successfully");
                    }
                    else
                    {
                        Logger.Log("Game data backup failed or no data to backup", LogLevel.Warning);
                    }

                    // Step 3: Uninstall the existing app
                    Logger.Log($"Uninstalling existing package: {packagename}");
                    var uninstallResult = UninstallPackage(packagename);

                    if (!uninstallResult.Output.Contains("Success"))
                    {
                        Logger.Log($"Uninstall warning: {uninstallResult.Output} {uninstallResult.Error}", LogLevel.Warning);
                    }

                    // Step 3.5: Clean up OBB and data folders (matching original Sideloader.UninstallGame behavior)
                    Logger.Log("Cleaning up OBB and data folders...");
                    RunAdbCommandToString($"shell rm -rf \"/sdcard/Android/obb/{packagename}\"");
                    RunAdbCommandToString($"shell rm -rf \"/sdcard/Android/data/{packagename}\"");

                    // Step 4: Install the new APK
                    Logger.Log("Installing new APK...");
                    result = RunAdbCommandToString($"install -g \"{path}\"");

                    if (!result.Output.Contains("Success"))
                    {
                        Logger.Log($"Reinstall failed: {result.Error}", LogLevel.Error);
                        return result;
                    }

                    Logger.Log("APK reinstalled successfully");

                    // Step 5: Restore game data if backup succeeded
                    if (backupSucceeded && Directory.Exists(packageBackupPath))
                    {
                        Logger.Log("Restoring game data...");
                        var restoreResult = RunAdbCommandToString($"push \"{packageBackupPath}\" \"/sdcard/Android/data/\"");

                        if (!restoreResult.Error.Contains("failed"))
                        {
                            Logger.Log("Game data restored successfully");
                        }
                        else
                        {
                            Logger.Log($"Game data restore failed: {restoreResult.Error}", LogLevel.Warning);
                        }

                        // Clean up backup directory
                        try
                        {
                            if (packageBackupPath != Environment.CurrentDirectory &&
                                Directory.Exists(packageBackupPath))
                            {
                                Directory.Delete(packageBackupPath, true);
                                Logger.Log("Backup directory cleaned up");
                            }
                        }
                        catch (Exception cleanupEx)
                        {
                            Logger.Log($"Failed to clean up backup directory: {cleanupEx.Message}", LogLevel.Warning);
                        }
                    }

                    result.Output = "Success (Reinstalled with data backup/restore)";
                    Logger.Log("Reinstall workflow completed successfully");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Exception during reinstall workflow: {ex.Message}", LogLevel.Error);
                    result.Error += $"\nReinstall exception: {ex.Message}";
                }
            }
            else if (combinedOutput.Contains("failed"))
            {
                // Other installation failures
                Logger.Log($"Installation failed: {combinedOutput}", LogLevel.Error);
            }

            return result;
        }

        /// <summary>
        /// Synchronous wrapper for Sideload - maintains backward compatibility
        /// </summary>
        public static ProcessOutput Sideload(string path, string packagename = "")
        {
            return SideloadAsync(path, packagename, null).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Show ADB debugging authorization warning dialog
        /// </summary>
        private static async Task AdbDebugWarning()
        {
            Logger.Log("Device unauthorized - ADB debugging not enabled", LogLevel.Error);

            if (_dialogService != null)
            {
                var userAcknowledged = await _dialogService.ShowOkCancelAsync(
                    "On your headset, click on the Notifications Bell, and then select the USB Detected notification to enable Connections.",
                    "ADB Debugging not enabled.");

                if (!userAcknowledged)
                {
                    // User cancelled - mark as warned so we don't spam them
                    Settings.AdbDebugWarned = true;
                    Settings.Save();
                }
            }
            else
            {
                // No dialog service available - just mark as warned
                Settings.AdbDebugWarned = true;
                Settings.Save();
            }
        }

        /// <summary>
        /// Show insufficient storage warning dialog
        /// </summary>
        private static async Task ShowInsufficientStorageWarning()
        {
            Logger.Log("Not enough space on device", LogLevel.Error);

            if (_dialogService != null)
            {
                await _dialogService.ShowErrorAsync(
                    "There is not enough room on your device to install this package. Please clear AT LEAST 2x the amount of the app you are trying to install.",
                    "Insufficient Storage");
            }
        }

        /// <summary>
        /// Get list of all third-party installed packages (excludes system apps)
        /// </summary>
        /// <returns>Array of package names like "com.beatgames.beatsaber"</returns>
        public static string[] GetInstalledPackages()
        {
            Logger.Log("Getting installed third-party packages");
            var result = RunAdbCommandToString("shell pm list packages -3");

            if (string.IsNullOrEmpty(result.Output))
            {
                return [];
            }

            // Output format: "package:com.example.app\npackage:com.other.app\n"
            var lines = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            var packages = lines
                .Where(line => line.StartsWith("package:"))
                .Select(line => line.Substring(8).Trim()) // Remove "package:" prefix
                .Where(pkg => !string.IsNullOrEmpty(pkg))
                .ToArray();

            Logger.Log($"Found {packages.Length} installed packages");
            return packages;
        }

        /// <summary>
        /// Get version information for a specific package
        /// </summary>
        /// <param name="packageName">Package name like "com.beatgames.beatsaber"</param>
        /// <returns>Tuple of (versionCode, versionName)</returns>
        public static (long versionCode, string versionName) GetPackageVersion(string packageName)
        {
            // Get version code using dumpsys
            var versionCodeResult = RunAdbCommandToString($"shell \"dumpsys package {packageName} | grep versionCode -F\"");
            var versionNameResult = RunAdbCommandToString($"shell \"dumpsys package {packageName} | grep versionName -F\"");

            long versionCode = 0;
            var versionName = "Unknown";

            // Parse version code (format: "    versionCode=123456789 minSdk=23 targetSdk=29")
            if (!string.IsNullOrEmpty(versionCodeResult.Output))
            {
                var versionCodeLine = versionCodeResult.Output.Trim();

                // Extract number after "versionCode="
                var startIndex = versionCodeLine.IndexOf("versionCode=");
                if (startIndex >= 0)
                {
                    startIndex += "versionCode=".Length;
                    var endIndex = versionCodeLine.IndexOf(' ', startIndex);
                    if (endIndex < 0) endIndex = versionCodeLine.Length;

                    var versionCodeStr = versionCodeLine.Substring(startIndex, endIndex - startIndex).Trim();

                    // Keep only numbers
                    versionCodeStr = new string(versionCodeStr.Where(char.IsDigit).ToArray());

                    if (long.TryParse(versionCodeStr, out var parsed))
                    {
                        versionCode = parsed;
                    }
                }
            }

            // Parse version name (format: "    versionName=1.2.3")
            if (!string.IsNullOrEmpty(versionNameResult.Output))
            {
                var versionNameLine = versionNameResult.Output.Trim();

                var startIndex = versionNameLine.IndexOf("versionName=");
                if (startIndex >= 0)
                {
                    startIndex += "versionName=".Length;
                    var endIndex = versionNameLine.IndexOf(' ', startIndex);
                    if (endIndex < 0) endIndex = versionNameLine.Length;

                    versionName = versionNameLine.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }

            return (versionCode, versionName);
        }

        /// <summary>
        /// Check if a specific package is installed on the device
        /// </summary>
        public static bool IsPackageInstalled(string packageName)
        {
            var result = RunAdbCommandToString($"shell pm list packages {packageName}");
            return result.Output.Contains($"package:{packageName}");
        }

        /// <summary>
        /// Get a dictionary of all installed packages with their version information
        /// Key: package name, Value: (versionCode, versionName)
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, (long versionCode, string versionName)> GetAllInstalledPackagesWithVersions()
        {
            var result = new System.Collections.Generic.Dictionary<string, (long, string)>();
            var packages = GetInstalledPackages();

            Logger.Log($"Getting version info for {packages.Length} packages...");

            foreach (var package in packages)
            {
                var versionInfo = GetPackageVersion(package);
                result[package] = versionInfo;
            }

            Logger.Log($"Retrieved version info for {result.Count} packages");
            return result;
        }
    }
}
