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
}