using AndroidSideloader.Sideloader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Utility methods for sideloading operations
/// </summary>
public class SideloaderUtilities
{
    private static readonly SettingsManager Settings = SettingsManager.Instance;

    #region Recursive Operations

    /// <summary>
    /// Recursively copy all OBB folders in a directory tree
    /// </summary>
    public static ProcessOutput RecursiveCopyObb(string folderPath)
    {
        var recursiveOutput = new ProcessOutput();

        try
        {
            // Check if current directory is an OBB folder (starts with com.)
            var folderName = Path.GetFileName(folderPath);
            if (folderName.StartsWith("com.", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"Copying OBB folder: {folderName}");
                recursiveOutput += Adb.CopyObb(folderPath);
                return recursiveOutput; // Don't recurse into OBB folders
            }

            // Process all files in current directory
            foreach (var file in Directory.GetFiles(folderPath))
            {
                var extension = Path.GetExtension(file).ToLower();
                if (extension is ".obb" or ".dat")
                {
                    Logger.Log($"Copying OBB file: {Path.GetFileName(file)}");
                    recursiveOutput += Adb.CopyObb(file);
                }
            }

            // Recursively process subdirectories
            foreach (var directory in Directory.GetDirectories(folderPath))
            {
                var subOutput = RecursiveCopyObb(directory);
                recursiveOutput += subOutput;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error in recursive OBB copy: {ex.Message}", LogLevel.Error);
            recursiveOutput.Error += $"\nRecursive OBB copy error: {ex.Message}";
        }

        return recursiveOutput;
    }

    #endregion

    #region Folder Operations

    /// <summary>
    /// Remove a folder from the device (with safety checks)
    /// </summary>
    private static ProcessOutput RemoveFolder(string path)
    {
        // Safety check - prevent deleting critical system folders
        if (path is "/sdcard/Android/obb/" or "/sdcard/Android/data/" or "/sdcard" or "/")
        {
            Logger.Log($"Prevented deletion of critical folder: {path}", LogLevel.Warning);
            return new ProcessOutput("", "Cannot delete critical system folder");
        }

        return Adb.RunAdbCommandToString($"shell rm -rf \"{path}\"");
    }

    #endregion

    #region APK/Package Operations

    /// <summary>
    /// Uninstall game and remove all associated data/OBB folders
    /// </summary>
    public static ProcessOutput UninstallGame(string packageName)
    {
        var output = Adb.UninstallPackage(packageName);

        // Remove OBB and data folders
        var removeObbOutput = RemoveFolder($"/sdcard/Android/obb/{packageName}");
        var removeDataOutput = RemoveFolder($"/sdcard/Android/data/{packageName}");

        Logger.Log($"Uninstalled game and cleaned up folders for: {packageName}");
        return new ProcessOutput
        {
            Output = output.Output + "\n" + removeObbOutput.Output + "\n" + removeDataOutput.Output,
            Error = output.Error + "\n" + removeDataOutput.Error+ "\n" + removeDataOutput.Error
        };
    }

    #endregion

    #region Custom Install Scripts

    /// <summary>
    /// Run custom ADB commands from a file (for games with special install requirements)
    /// </summary>
    public static async Task<ProcessOutput> RunAdbCommandsFromFile(string filePath)
    {
        var output = new ProcessOutput();

        try
        {
            if (!File.Exists(filePath))
            {
                output.Error = $"Command file not found: {filePath}";
                Logger.Log(output.Error, LogLevel.Error);
                return output;
            }

            var commands = await File.ReadAllLinesAsync(filePath);
            var currentFolder = Path.GetDirectoryName(filePath);

            // Extract any .7z files in the folder first
            var zipFiles = Directory.GetFiles(currentFolder!, "*.7z", SearchOption.AllDirectories);
            foreach (var zipFile in zipFiles)
            {
                Logger.Log($"Extracting: {Path.GetFileName(zipFile)}");
                await Zip.ExtractArchive(zipFile, currentFolder);
            }

            // Process each command
            foreach (var cmd in commands)
            {
                if (string.IsNullOrWhiteSpace(cmd) || cmd.StartsWith('#') || cmd.StartsWith("//"))
                {
                    continue; // Skip empty lines and comments
                }

                if (cmd.StartsWith("adb", StringComparison.OrdinalIgnoreCase))
                {
                    // Replace "adb" with actual ADB command
                    var replacement = string.IsNullOrEmpty(Adb.DeviceId)
                        ? Settings.AdbPath
                        : $"{Settings.AdbPath} -s {Adb.DeviceId}";

                    var rgx = new Regex("adb", RegexOptions.IgnoreCase);
                    var result = rgx.Replace(cmd, replacement, 1);

                    Logger.Log($"Running custom command: {result}");
                    var cmdOutput = Adb.RunAdbCommandToStringWithoutAdb(result, filePath);

                    // Ignore common harmless errors
                    if (cmdOutput.Error.Contains("mkdir") || cmdOutput.Error.Contains("File exists"))
                    {
                        cmdOutput.Error = "";
                    }

                    output += cmdOutput;
                }
            }

            output.Output += "\n\nCustom install script completed successfully!";
            Logger.Log("Custom install script execution completed");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error running custom install script: {ex.Message}", LogLevel.Error);
            output.Error += $"\nScript error: {ex.Message}";
        }

        return output;
    }

    #endregion

    #region User JSON Management

    /// <summary>
    /// User JSON files that need to be pushed to device
    /// </summary>
    private static readonly List<string> UserJsonFiles =
    [
        "user.json",
        "profile.json"
    ];

    /// <summary>
    /// Create a user.json file with generated username
    /// </summary>
    private static void CreateUserJsonByName(string username, string filename)
    {
        try
        {
            var json = $$"""
                         {
                             "username": "{{username}}",
                             "id": "{{Guid.NewGuid()}}",
                             "created": "{{DateTime.Now:yyyy-MM-ddTHH:mm:ssZ}}"
                         }
                         """;

            File.WriteAllText(filename, json);
            Logger.Log($"Created user JSON: {filename}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error creating user JSON: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Push user.json files to device (required for some games)
    /// </summary>
    public static void PushUserJsons()
    {
        foreach (var userJson in UserJsonFiles)
        {
            var filePath = Path.Combine(Environment.CurrentDirectory, userJson);

            if (!File.Exists(filePath))
            {
                // Create with random username if doesn't exist
                CreateUserJsonByName(RandomString(16), userJson);
            }

            if (File.Exists(filePath))
            {
                Logger.Log($"Pushing {userJson} to device");
                Adb.RunAdbCommandToString($"push \"{filePath}\" \"/sdcard/\"");

                // Cleanup local file after push
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Could not delete {filePath}: {ex.Message}");
                }
            }
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Generate a random alphanumeric string
    /// </summary>
    private static string RandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// Get or generate a persistent unique hardware ID (UUID)
    /// This is used for crash log identification, telemetry, and anonymization
    /// </summary>
    public static string Uuid()
    {
        // Check if UUID already exists in settings
        if (!string.IsNullOrEmpty(Settings.Uuid))
        {
            return Settings.Uuid;
        }

        // Generate new UUID using cryptographically secure random bytes
        var bytes = new byte[16];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        // Convert to hex string (no dashes)
        var uuid = Convert.ToHexString(bytes);

        // Save to settings for future use
        Settings.Uuid = uuid;
        Settings.Save();

        Logger.Log($"Generated new UUID: {uuid}");
        return uuid;
    }

    /// <summary>
    /// Compare OBB sizes between local folder and device OBB folder
    /// </summary>
    /// <param name="packageName">Package name (e.g., com.beatgames.beatsaber)</param>
    /// <param name="localObbPath">Local OBB folder path</param>
    /// <returns>Tuple: (matches, localSizeMB, remoteSizeMB)</returns>
    public static async Task<(bool matches, long localSizeMB, long remoteSizeMB)> CompareObbSizes(
        string packageName, string localObbPath)
    {
        try
        {
            // Calculate local folder size in MB
            long localSizeBytes = 0;
            if (Directory.Exists(localObbPath))
            {
                var dirInfo = new DirectoryInfo(localObbPath);
                localSizeBytes = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            var localSizeMb = localSizeBytes / (1024 * 1024);

            // Get remote OBB folder size on device using "du -m" (disk usage in MB)
            var deviceObbPath = $"/sdcard/Android/obb/{packageName}";
            var duResult = Adb.RunAdbCommandToString($"shell du -m \"{deviceObbPath}\"");

            // Parse output: "1234    /sdcard/Android/obb/com.package" -> extract "1234"
            long remoteSizeMb = 0;
            if (!string.IsNullOrEmpty(duResult.Output))
            {
                // Clean the output to extract just the number
                var cleanedSize = CleanRemoteFolderSize(duResult.Output);
                if (!string.IsNullOrEmpty(cleanedSize) && long.TryParse(cleanedSize, out var parsed))
                {
                    remoteSizeMb = parsed;
                }
            }

            Logger.Log($"OBB size comparison for {packageName}: Local={localSizeMb}MB, Device={remoteSizeMb}MB");

            // Remote should be >= local (allow small tolerance for rounding)
            var matches = remoteSizeMb >= localSizeMb - 5; // 5MB tolerance for rounding differences

            await Task.CompletedTask;
            return (matches, localSizeMb, remoteSizeMb);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error comparing OBB sizes: {ex.Message}", LogLevel.Error);
            return (false, 0, 0);
        }
    }

    /// <summary>
    /// Clean remote folder size output from "du" command
    /// </summary>
    private static string CleanRemoteFolderSize(string rawSize)
    {
        try
        {
            // Remove everything after the last newline character
            var trimmed = Regex.Replace(rawSize, "[^\n]*$", "");
            // Keep only digits
            var digitsOnly = Regex.Replace(trimmed, "[^0-9]", "");
            return digitsOnly;
        }
        catch
        {
            return "0";
        }
    }

    #endregion

    #region Custom Install Scripts

    /// <summary>
    /// Executes custom ADB commands from a text file
    /// Used for games that require non-standard installation procedures
    /// (e.g., custom folder structures, special file placements)
    /// </summary>
    /// <param name="scriptPath">Path to text file containing ADB commands</param>
    /// <returns>ProcessOutput with execution results</returns>
    public static async Task<ProcessOutput> RunADBCommandsFromFileAsync(string scriptPath)
    {
        var output = new ProcessOutput();
        var settings = SettingsManager.Instance;

        try
        {
            if (!File.Exists(scriptPath))
            {
                Logger.Log($"Custom install script not found: {scriptPath}", LogLevel.Error);
                return new ProcessOutput("", $"Script file not found: {scriptPath}");
            }

            Logger.Log($"Running custom install script: {scriptPath}");

            // Read all commands from file
            var commands = await File.ReadAllLinesAsync(scriptPath);
            var scriptFolder = Path.GetDirectoryName(scriptPath);

            // First, extract any .7z archives in the folder
            Logger.Log("Extracting any .7z archives in script folder...");
            var archives = Directory.GetFiles(scriptFolder, "*.7z", SearchOption.AllDirectories);

            foreach (var archivePath in archives)
            {
                Logger.Log($"Extracting: {Path.GetFileName(archivePath)}");
                try
                {
                    await Zip.ExtractArchive(archivePath, scriptFolder);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to extract {Path.GetFileName(archivePath)}: {ex.Message}", LogLevel.Warning);
                }
            }

            // Now execute each command
            foreach (var cmd in commands)
            {
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(cmd) || cmd.TrimStart().StartsWith("#"))
                    continue;

                // Only process lines that start with "adb"
                if (cmd.TrimStart().StartsWith("adb"))
                {
                    // Replace "adb" with actual ADB path and device ID if set
                    var processedCommand = ProcessAdbCommand(cmd.TrimStart());

                    Logger.Log($"Executing custom command: {processedCommand}");

                    // Execute the command (use sync version for custom scripts)
                    var result = Adb.RunAdbCommandToString(processedCommand);
                    output += result;

                    // Filter out common benign errors
                    if (result.Error.Contains("mkdir") || result.Error.Contains("already exists"))
                    {
                        // These are expected errors when directories already exist
                        output.Error = output.Error.Replace(result.Error, "");
                    }

                    if (result.Output.Contains("reserved"))
                    {
                        // Filter out "reserved" warnings
                        output.Output = output.Output.Replace(result.Output, "");
                    }

                    // Check for actual errors
                    if (!string.IsNullOrEmpty(result.Error) &&
                        !result.Error.Contains("mkdir") &&
                        !result.Error.Contains("already exists"))
                    {
                        Logger.Log($"Command error: {result.Error}", LogLevel.Warning);
                    }
                }
            }

            output.Output += "\nCustom install completed successfully!";
            Logger.Log("Custom install script completed");

            return output;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error running custom install script: {ex.Message}", LogLevel.Error);
            return new ProcessOutput("", $"Failed to run custom install script: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes an ADB command by replacing "adb" with the actual ADB path
    /// </summary>
    private static string ProcessAdbCommand(string command)
    {
        // Remove "adb" from the start of the command
        var commandWithoutAdb = command.Substring(3).TrimStart();

        // ADB.RunAdbCommandAsync already handles device ID and path,
        // so we just return the command without "adb"
        return commandWithoutAdb;
    }

    /// <summary>
    /// Checks if a game has a custom install script
    /// </summary>
    /// <param name="gameFolderPath">Path to downloaded game folder</param>
    /// <returns>Path to install script if found, null otherwise</returns>
    public static string FindCustomInstallScript(string gameFolderPath)
    {
        if (!Directory.Exists(gameFolderPath))
            return null;

        // Look for common script file names
        var possibleScriptNames = new[]
        {
            "install.txt",
            "custom_install.txt",
            "adb_commands.txt",
            "install_commands.txt"
        };

        foreach (var scriptName in possibleScriptNames)
        {
            var scriptPath = Path.Combine(gameFolderPath, scriptName);
            if (File.Exists(scriptPath))
            {
                Logger.Log($"Found custom install script: {scriptName}");
                return scriptPath;
            }
        }

        return null;
    }

    #endregion

    #region Donor Upload - Extract and Prepare

    /// <summary>
    /// Extracts APK and OBB from device and prepares for upload to VRP mirror
    /// </summary>
    /// <param name="gameName">Game name for display</param>
    /// <param name="packageName">Package name to extract</param>
    /// <param name="installedVersionCode">Installed version code</param>
    /// <param name="isUpdate">True if this is an update to existing game</param>
    /// <returns>Tuple of (success, extractedFolder)</returns>
    public static async Task<(bool success, string extractedFolder)> ExtractAndPrepareGameForUploadAsync(
        string gameName,
        string packageName,
        ulong installedVersionCode,
        bool isUpdate)
    {
        var settings = SettingsManager.Instance;
        var gameFolder = Path.Combine(settings.MainDir, packageName);

        try
        {
            Logger.Log($"Preparing {gameName} ({packageName}) for upload...");

            // Create game folder
            if (Directory.Exists(gameFolder))
            {
                Logger.Log($"Cleaning existing folder: {gameFolder}");
                Directory.Delete(gameFolder, true);
            }
            Directory.CreateDirectory(gameFolder);

            // Step 1: Extract APK from device
            Logger.Log("Extracting APK from device...");

            // Get APK path on device
            var pathResult = Adb.RunAdbCommandToString($"shell pm path {packageName}");
            if (string.IsNullOrEmpty(pathResult.Output) || pathResult.Output.Contains("Unknown package"))
            {
                Logger.Log($"Package not found on device: {packageName}", LogLevel.Error);
                return (false, null);
            }

            // Extract path from "package:/data/app/.../base.apk" format
            var apkPath = pathResult.Output.Trim();
            if (apkPath.StartsWith("package:"))
            {
                apkPath = apkPath.Substring(8);
            }
            apkPath = apkPath.Trim();

            Logger.Log($"APK path on device: {apkPath}");

            // Pull APK from device
            var localApkPath = Path.Combine(gameFolder, $"{packageName}.apk");
            var pullResult = Adb.RunAdbCommandToString($"pull \"{apkPath}\" \"{localApkPath}\"");

            if (!string.IsNullOrEmpty(pullResult.Error) && !pullResult.Error.Contains("bytes"))
            {
                Logger.Log($"Error extracting APK: {pullResult.Error}", LogLevel.Error);
                return (false, null);
            }

            // Verify APK was extracted
            if (!File.Exists(localApkPath))
            {
                Logger.Log("APK file not found after extraction", LogLevel.Error);
                return (false, null);
            }

            Logger.Log($"APK extracted: {Path.GetFileName(localApkPath)}");

            // Step 2: Pull OBB folder from device (if it exists)
            Logger.Log("Extracting OBB files (if they exist)...");
            var obbPath = $"/sdcard/Android/obb/{packageName}";
            var obbDestination = gameFolder;

            var obbPullResult = Adb.RunAdbCommandToString($"pull \"{obbPath}\" \"{obbDestination}\"");

            // OBB might not exist for all games - that's OK
            if (obbPullResult.Output.Contains("0 files pulled") ||
                obbPullResult.Error.Contains("does not exist"))
            {
                Logger.Log("No OBB files found for this game");
            }
            else if (!string.IsNullOrEmpty(obbPullResult.Error))
            {
                Logger.Log($"OBB pull warning: {obbPullResult.Error}", LogLevel.Warning);
            }
            else
            {
                var obbFiles = Directory.GetFiles(gameFolder, "*.obb", SearchOption.AllDirectories);
                Logger.Log($"OBB files extracted: {obbFiles.Length} files");
            }

            // Step 3: Create HWID.txt file (unique identifier for this upload)
            var hwid = Uuid();
            var hwidPath = Path.Combine(gameFolder, "HWID.txt");
            await File.WriteAllTextAsync(hwidPath, hwid);
            Logger.Log($"Created HWID: {hwid}");

            // Step 4: Create metadata file
            var metadata = new
            {
                GameName = gameName,
                PackageName = packageName,
                VersionCode = installedVersionCode,
                IsUpdate = isUpdate,
                UploadDate = DateTime.UtcNow,
                HWID = hwid
            };

            var metadataPath = Path.Combine(gameFolder, "upload_metadata.json");
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, metadataJson);
            Logger.Log("Created upload metadata");

            Logger.Log($"Game extraction completed successfully: {gameFolder}");
            return (true, gameFolder);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error preparing game for upload: {ex.Message}", LogLevel.Error);
            return (false, null);
        }
    }

    /// <summary>
    /// Compresses extracted game folder for upload
    /// </summary>
    /// <param name="gameFolder">Folder containing extracted game files</param>
    /// <param name="outputArchive">Path for output archive (optional)</param>
    /// <returns>Path to created archive</returns>
    public static async Task<string> CompressGameForUploadAsync(string gameFolder, string outputArchive = null)
    {
        try
        {
            if (!Directory.Exists(gameFolder))
            {
                Logger.Log($"Game folder not found: {gameFolder}", LogLevel.Error);
                return null;
            }

            // Default archive name: packagename.7z
            if (string.IsNullOrEmpty(outputArchive))
            {
                var packageName = Path.GetFileName(gameFolder);
                outputArchive = Path.Combine(Path.GetDirectoryName(gameFolder), $"{packageName}.7z");
            }

            Logger.Log($"Compressing game folder: {gameFolder}");
            Logger.Log($"Output archive: {outputArchive}");

            await Zip.CreateArchive(outputArchive, gameFolder);

            if (File.Exists(outputArchive))
            {
                var sizeBytes = new FileInfo(outputArchive).Length;
                var sizeMB = sizeBytes / (1024.0 * 1024.0);
                Logger.Log($"Archive created successfully: {sizeMB:F2} MB");
                return outputArchive;
            }
            else
            {
                Logger.Log("Archive creation failed - file not found", LogLevel.Error);
                return null;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error compressing game: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// Uploads compressed game archive to VRP mirror
    /// </summary>
    /// <param name="archivePath">Path to compressed archive</param>
    /// <param name="remotePath">Remote destination path</param>
    /// <param name="progress">Progress reporter (optional)</param>
    /// <returns>True if upload succeeded</returns>
    public static async Task<bool> UploadGameArchiveAsync(
        string archivePath,
        string remotePath,
        IProgress<double> progress = null)
    {
        try
        {
            if (!File.Exists(archivePath))
            {
                Logger.Log($"Archive not found: {archivePath}", LogLevel.Error);
                return false;
            }

            Logger.Log($"Uploading archive to: {remotePath}");

            // Use rclone upload with progress tracking
            var result = await Rclone.runRcloneCommand_UploadConfig(
                $"copy \"{archivePath}\" \"{remotePath}\" --progress");

            if (!string.IsNullOrEmpty(result.Error) && !result.Error.Contains("Transferred:"))
            {
                Logger.Log($"Upload error: {result.Error}", LogLevel.Error);
                return false;
            }

            Logger.Log("Upload completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error uploading game: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    #endregion
}