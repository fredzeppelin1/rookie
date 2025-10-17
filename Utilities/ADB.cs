using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AndroidSideloader.Services;
using AndroidSideloader.Sideloader;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Cross-platform Android Debug Bridge (ADB) wrapper for device communication
/// </summary>
public class Adb
{
    private static readonly SettingsManager Settings = SettingsManager.Instance;
    private static IDialogService _dialogService;

    // Device state
    public static string DeviceId = "";

    // ADB path configuration
    private static string AdbFolderPath => Settings.AdbFolder;
    private static string AdbFilePath => Settings.AdbPath;

    /// <summary>
    /// Set the dialog service for showing user dialogs
    /// </summary>
    public static void SetDialogService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    /// <summary>
    /// Core process execution helper that handles common process setup and execution
    /// </summary>
    private static ProcessOutput ExecuteProcess(
        string fileName,
        string arguments,
        string workingDirectory = null,
        int? timeoutMs = null,
        Func<string, string> sanitizeOutput = null,
        Action<string, string> postProcessErrors = null)
    {
        var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                error.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (timeoutMs.HasValue)
            {
                process.WaitForExit(timeoutMs.Value);
            }
            else
            {
                process.WaitForExit();
            }

            var stdout = output.ToString();
            var stderr = error.ToString();

            // Apply sanitization if provided
            if (sanitizeOutput != null)
            {
                stdout = sanitizeOutput(stdout);
                stderr = sanitizeOutput(stderr);
            }

            // Call post-processing for error handling
            postProcessErrors?.Invoke(stdout, stderr);

            return new ProcessOutput(stdout, stderr);
        }
        catch (Exception ex)
        {
            Logger.Log($"Exception running process: {ex.Message}", LogLevel.Error);
            return new ProcessOutput("", ex.Message);
        }
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

        // Determine timeout for connect commands
        int? timeout = command.Contains("connect") ? 3000 : null;

        // Execute the process with ADB-specific configuration
        var result = ExecuteProcess(
            fileName: AdbFilePath,
            arguments: command,
            workingDirectory: AdbFolderPath,
            timeoutMs: timeout,
            sanitizeOutput: output => output.Replace(Environment.CurrentDirectory, "CurrentDirectory"),
            postProcessErrors: (stdout, stderr) =>
            {
                // Log output if needed
                if (shouldLog && !string.IsNullOrWhiteSpace(stdout))
                {
                    Logger.Log($"ADB Output: {stdout}");
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Logger.Log($"ADB Error: {stderr}", LogLevel.Warning);
                }

                // Check for common errors
                if (stderr!.Contains("device unauthorized") || stderr.Contains("ADB_VENDOR_KEYS"))
                {
                    if (!Settings.AdbDebugWarned)
                    {
                        AdbDebugWarning().GetAwaiter().GetResult();
                    }
                }

                if (stderr.Contains("not enough space") || stdout!.Contains("not enough space"))
                {
                    ShowInsufficientStorageWarning().GetAwaiter().GetResult();
                }
            });

        return result;
    }

    /// <summary>
    /// Run a command through the system shell
    /// </summary>
    public static ProcessOutput RunAdbCommandToStringWithoutAdb(string command, string path)
    {
        Logger.Log($"Running shell command: {command}");

        // Determine timeout for connect commands
        int? timeout = command.Contains("connect") ? 3000 : null;

        // Execute through system shell
        return ExecuteProcess(
            fileName: PlatformHelper.GetShellPath(),
            arguments: $"{PlatformHelper.GetShellCommandPrefix()} \"{command}\"",
            workingDirectory: path,
            timeoutMs: timeout);
    }

    /// <summary>
    /// Generic command execution wrapper
    /// </summary>
    public static ProcessOutput RunCommandToString(string command, string path = "")
    {
        Logger.Log($"Running command: {command}");

        // Execute through system shell with optional working directory
        return ExecuteProcess(
            fileName: PlatformHelper.GetShellPath(),
            arguments: $"{PlatformHelper.GetShellCommandPrefix()} \"{command}\"",
            workingDirectory: !string.IsNullOrEmpty(path) ? path : null,
            postProcessErrors: (_, stderr) =>
            {
                if (stderr.Contains("No such file or directory"))
                {
                    Logger.Log("Asset path error detected", LogLevel.Error);
                }
            });
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
        // Return formatted N/A if no device is connected
        if (string.IsNullOrEmpty(DeviceId))
        {
            return "Total space: N/A\nUsed space: N/A\nFree space: N/A";
        }

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
        return $"Total space: {(double)totalSize / 1000:0.00}GB\nUsed space: {(double)usedSize / 1000:0.00}GB\nFree space: {(double)freeSize / 1000:0.00}GB";
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

        // It's a directory
        if (Directory.Exists(path))
        {
            // Remove existing and push entire folder
            RunAdbCommandToString($"shell rm -rf \"/sdcard/Android/obb/{folder}\"");
            return RunAdbCommandToString($"push \"{path}\" \"/sdcard/Android/obb/\"");
        }

        Logger.Log("No OBB folder found", LogLevel.Warning);
        return new ProcessOutput("No OBB Folder found");
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
    private static (long versionCode, string versionName) GetPackageVersion(string packageName)
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
            var startIndex = versionCodeLine.IndexOf("versionCode=", StringComparison.Ordinal);
            if (startIndex >= 0)
            {
                startIndex += "versionCode=".Length;
                var endIndex = versionCodeLine.IndexOf(' ', startIndex);
                if (endIndex < 0)
                {
                    endIndex = versionCodeLine.Length;
                }

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

            var startIndex = versionNameLine.IndexOf("versionName=", StringComparison.Ordinal);
            if (startIndex >= 0)
            {
                startIndex += "versionName=".Length;
                var endIndex = versionNameLine.IndexOf(' ', startIndex);
                if (endIndex < 0)
                {
                    endIndex = versionNameLine.Length;
                }

                versionName = versionNameLine.Substring(startIndex, endIndex - startIndex).Trim();
            }
        }

        return (versionCode, versionName);
    }

    /// <summary>
    /// Get a dictionary of all installed packages with their version information
    /// Key: package name, Value: (versionCode, versionName)
    /// </summary>
    public static Dictionary<string, (long versionCode, string versionName)> GetAllInstalledPackagesWithVersions()
    {
        var result = new Dictionary<string, (long, string)>();
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

    /// <summary>
    /// Get the APK path for a package on the device
    /// For packages with multiple APKs (split APKs), returns the first path (typically base.apk)
    /// </summary>
    /// <param name="packageName">Package name like "com.beatgames.beatsaber"</param>
    /// <returns>APK path on device, or null if package not found</returns>
    public static string GetPackageApkPath(string packageName)
    {
        var pathResult = RunAdbCommandToString($"shell pm path {packageName}");

        if (string.IsNullOrEmpty(pathResult.Output) || !pathResult.Output.Contains("package:"))
        {
            Logger.Log($"Package not found on device: {packageName}", LogLevel.Warning);
            return null;
        }

        // Parse first line (format: "package:/data/app/com.example.app-hash/base.apk")
        var firstLine = pathResult.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        var apkPath = firstLine.Replace("package:", "").Trim();

        return apkPath;
    }

    /// <summary>
    /// Get battery information from the device
    /// </summary>
    /// <returns>BatteryInfo object, or null if unable to retrieve info</returns>
    public static BatteryInfo GetBatteryInfo()
    {
        try
        {
            var batteryResult = RunAdbCommandToString("shell dumpsys battery");

            if (string.IsNullOrEmpty(batteryResult.Output))
            {
                Logger.Log("Failed to get battery info - no output", LogLevel.Warning);
                return null;
            }

            var info = new BatteryInfo();
            var lines = batteryResult.Output.Split('\n');

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("level:"))
                {
                    var parts = trimmedLine.Split(':');
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var level))
                    {
                        info.Level = level;
                    }
                }
                else if (trimmedLine.StartsWith("status:"))
                {
                    var parts = trimmedLine.Split(':');
                    if (parts.Length > 1)
                    {
                        // Status codes: 1=Unknown, 2=Charging, 3=Discharging, 4=Not charging, 5=Full
                        var statusCode = parts[1].Trim();
                        info.Status = statusCode switch
                        {
                            "1" => "Unknown",
                            "2" => "Charging",
                            "3" => "Discharging",
                            "4" => "Not charging",
                            "5" => "Full",
                            _ => statusCode
                        };
                    }
                }
                else if (trimmedLine.StartsWith("health:"))
                {
                    var parts = trimmedLine.Split(':');
                    if (parts.Length > 1)
                    {
                        // Health codes: 1=Unknown, 2=Good, 3=Overheat, 4=Dead, 5=Over voltage, 6=Unspecified failure, 7=Cold
                        var healthCode = parts[1].Trim();
                        info.Health = healthCode switch
                        {
                            "1" => "Unknown",
                            "2" => "Good",
                            "3" => "Overheat",
                            "4" => "Dead",
                            "5" => "Over voltage",
                            "6" => "Unspecified failure",
                            "7" => "Cold",
                            _ => healthCode
                        };
                    }
                }
                else if (trimmedLine.StartsWith("temperature:"))
                {
                    var parts = trimmedLine.Split(':');
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var temp))
                    {
                        // Temperature is in tenths of a degree Celsius
                        info.Temperature = temp / 10.0;
                    }
                }
            }

            return info;
        }
        catch (Exception ex)
        {
            Logger.Log($"Exception getting battery info: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    #region Wireless ADB Support

    /// <summary>
    /// Check if device is connected wirelessly or via USB
    /// Updates Settings.Wired accordingly
    /// </summary>
    private static void CheckConnectionType()
    {
        var devicesResult = RunAdbCommandToString("devices");

        if (devicesResult.Output.Contains("more than one"))
        {
            // Multiple devices connected (probably USB + wireless)
            Settings.Wired = true;
            Settings.Save();
            Logger.Log("Multiple devices detected - prioritizing wired connection");
        }
        else if (devicesResult.Output.Contains("found"))
        {
            // Single device found
            Settings.Wired = false;
            Settings.Save();
            Logger.Log("Single device detected - wireless mode");
        }
    }

    /// <summary>
    /// Enable wireless ADB on the connected device
    /// </summary>
    public static async Task<(bool success, string ipAddress)> EnableWirelessAdb(IDialogService dialogService = null)
    {
        try
        {
            Logger.Log("Enabling wireless ADB...");

            // Get device IP address using "adb shell ip route"
            var ipRouteResult = RunAdbCommandToString("shell ip route");

            if (string.IsNullOrEmpty(ipRouteResult.Output))
            {
                Logger.Log("Failed to get IP route information", LogLevel.Error);
                return (false, "");
            }

            // Parse IP from output (format: "192.168.1.x src 192.168.1.y")
            // We want the second IP (the device's IP)
            var srcIndex = ipRouteResult.Output.IndexOf(" src ", StringComparison.Ordinal);
            if (srcIndex < 0)
            {
                Logger.Log("Failed to parse device IP from route info", LogLevel.Error);
                return (false, "");
            }

            var ipStart = srcIndex + 5; // Length of " src "
            var ipEnd = ipRouteResult.Output.IndexOf(' ', ipStart);
            if (ipEnd < 0) ipEnd = ipRouteResult.Output.IndexOf('\n', ipStart);
            if (ipEnd < 0) ipEnd = ipRouteResult.Output.Length;

            var deviceIp = ipRouteResult.Output.Substring(ipStart, ipEnd - ipStart).Trim();

            if (string.IsNullOrEmpty(deviceIp))
            {
                Logger.Log("Failed to extract device IP address", LogLevel.Error);
                return (false, "");
            }

            Logger.Log($"Device IP detected: {deviceIp}");

            // Start ADB on TCP port 5555
            var tcpResult = RunAdbCommandToString("tcpip 5555");

            // Check if tcpip command succeeded
            if (!string.IsNullOrEmpty(tcpResult.Error))
            {
                Logger.Log($"Failed to enable TCP mode: {tcpResult.Error}", LogLevel.Warning);
            }

            await Task.Delay(1000); // Wait for ADB to restart in TCP mode

            // Build connection string (IP:port)
            var ipCommand = $"{deviceIp}:5555";

            // Connect to wireless ADB
            Logger.Log($"Connecting to: {ipCommand}");
            var connectResult = RunAdbCommandToString($"connect {ipCommand}");

            if (connectResult.Output.Contains("connected"))
            {
                // Success! Save the IP and enable wireless flag
                Settings.IpAddress = ipCommand;
                Settings.WirelessAdb = true;
                Settings.Save();

                // Save IP to StoredIP.txt for auto-reconnect
                var storedIpPath = Path.Combine(AdbFolderPath, "StoredIP.txt");
                await File.WriteAllTextAsync(storedIpPath, ipCommand);

                Logger.Log($"Wireless ADB enabled successfully on {ipCommand}");

                // Enable WiFi wakeup feature on device
                RunAdbCommandToString("shell settings put global wifi_wakeup_available 1");

                if (dialogService != null)
                {
                    await dialogService.ShowInfoAsync(
                        $"Wireless ADB enabled!\n\nYou can now disconnect the USB cable.\n\nDevice IP: {ipCommand}",
                        "Wireless ADB");
                }

                return (true, ipCommand);
            }

            Logger.Log($"Failed to connect to wireless ADB: {connectResult.Output} {connectResult.Error}", LogLevel.Error);

            if (dialogService != null)
            {
                await dialogService.ShowErrorAsync(
                    $"Failed to enable wireless ADB.\n\nError: {connectResult.Error}",
                    "Wireless ADB Failed");
            }

            return (false, "");
        }
        catch (Exception ex)
        {
            Logger.Log($"Exception enabling wireless ADB: {ex.Message}", LogLevel.Error);
            return (false, "");
        }
    }

    /// <summary>
    /// Attempt to reconnect to saved wireless IP on app startup
    /// </summary>
    public static async Task<bool> ReconnectWirelessAdb()
    {
        await Task.CompletedTask; // Suppress async warning

        try
        {
            // Check if we have a saved IP
            if (string.IsNullOrEmpty(Settings.IpAddress))
            {
                Logger.Log("No saved IP address for wireless ADB");
                return false;
            }

            // Check connection type
            CheckConnectionType();

            // Only try to reconnect if not wired
            if (Settings.Wired)
            {
                Logger.Log("Wired connection detected - skipping wireless reconnect");
                return false;
            }

            // Check if StoredIP.txt exists
            var storedIpPath = Path.Combine(AdbFolderPath, "StoredIP.txt");
            var savedIp = Settings.IpAddress;

            if (File.Exists(storedIpPath))
            {
                savedIp = (await File.ReadAllTextAsync(storedIpPath)).Trim();
                Settings.IpAddress = savedIp;
                Settings.Save();
            }

            Logger.Log($"Attempting to reconnect to wireless ADB: {savedIp}");

            // Try to connect
            var connectResult = RunAdbCommandToString($"connect {savedIp}");

            if (connectResult.Output.Contains("connected"))
            {
                Settings.WirelessAdb = true;
                Settings.Save();
                Logger.Log($"Wireless ADB reconnected successfully: {savedIp}");

                // Wake the device screen (matching original behavior)
                Logger.Log("Sending wake keyevent to device...");
                var wakeResult = RunAdbCommandToString("shell input keyevent KEYCODE_WAKEUP");

                // Check if multiple devices are connected (USB + wireless)
                if (wakeResult.Output.Contains("more than one") || wakeResult.Error.Contains("more than one"))
                {
                    Settings.Wired = true;
                    Settings.Save();
                    Logger.Log("Multiple devices detected after wake - setting Wired=true");
                }

                return true;
            }

            // Connection failed - clear saved IP
            Logger.Log($"Failed to reconnect to saved IP: {connectResult.Output} {connectResult.Error}", LogLevel.Warning);

            Settings.IpAddress = "";
            Settings.WirelessAdb = false;
            Settings.Save();

            // Delete StoredIP.txt
            if (File.Exists(storedIpPath))
            {
                try
                {
                    File.Delete(storedIpPath);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to delete StoredIP.txt: {ex.Message}", LogLevel.Warning);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"Exception reconnecting wireless ADB: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// Disable wireless ADB and return to USB mode
    /// </summary>
    public static void DisableWirelessAdb()
    {
        try
        {
            Logger.Log("Disabling wireless ADB");

            // Disconnect wireless connection if active
            if (!string.IsNullOrEmpty(Settings.IpAddress))
            {
                RunAdbCommandToString($"disconnect {Settings.IpAddress}");
            }

            // Switch back to USB mode
            RunAdbCommandToString("usb");

            // Clear settings
            Settings.IpAddress = "";
            Settings.WirelessAdb = false;
            Settings.Save();

            // Delete StoredIP.txt
            var storedIpPath = Path.Combine(AdbFolderPath, "StoredIP.txt");
            if (File.Exists(storedIpPath))
            {
                try
                {
                    File.Delete(storedIpPath);
                    Logger.Log("Deleted StoredIP.txt");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to delete StoredIP.txt: {ex.Message}", LogLevel.Warning);
                }
            }

            Logger.Log("Wireless ADB disabled");
        }
        catch (Exception ex)
        {
            Logger.Log($"Exception disabling wireless ADB: {ex.Message}", LogLevel.Error);
        }
    }

    #endregion
}

/// <summary>
/// Battery information retrieved from the device
/// </summary>
public class BatteryInfo
{
    public int Level { get; set; }
    public string Status { get; set; }
    public string Health { get; set; }
    public double Temperature { get; set; }

    public BatteryInfo()
    {
        Level = 0;
        Status = "Unknown";
        Health = "Unknown";
        Temperature = 0.0;
    }
}