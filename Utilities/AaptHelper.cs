using System;
using System.IO;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Helper for AAPT (Android Asset Packaging Tool) operations
/// Used to extract release names from APK files
/// </summary>
public static class AaptHelper
{
    /// <summary>
    /// Gets the path to AAPT executable (platform-specific)
    /// </summary>
    public static string GetAaptPath()
    {
        var platformToolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools");
        var aaptExeName = PlatformHelper.IsWindows ? "aapt.exe" : "aapt";
        return Path.Combine(platformToolsDir, aaptExeName);
    }

    /// <summary>
    /// Checks if AAPT is available
    /// </summary>
    public static bool IsAaptAvailable()
    {
        var aaptPath = GetAaptPath();
        var exists = File.Exists(aaptPath);

        if (!exists)
        {
            Logger.Log("AAPT not found - contributor workflow will use package names as fallback", LogLevel.Warning);
        }

        return exists;
    }

    /// <summary>
    /// Extracts the application label (release name) from an APK using AAPT
    /// Returns null if AAPT is not available or extraction fails
    /// </summary>
    public static string ExtractReleaseName(string apkPath)
    {
        try
        {
            if (!IsAaptAvailable())
            {
                return null;
            }

            var aaptPath = GetAaptPath();

            // Run AAPT dump badging command
            var command = $"dump badging \"{apkPath}\"";
            var result = Adb.RunCommandToString(command, aaptPath);

            if (string.IsNullOrEmpty(result.Output))
            {
                Logger.Log("AAPT returned no output", LogLevel.Warning);
                return null;
            }

            // Parse output for application-label
            // Format: application-label:'App Name'
            var lines = result.Output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("application-label"))
                {
                    // Extract text between single quotes
                    var startIdx = line.IndexOf('\'');
                    var endIdx = line.LastIndexOf('\'');

                    if (startIdx >= 0 && endIdx > startIdx)
                    {
                        var releaseName = line.Substring(startIdx + 1, endIdx - startIdx - 1);
                        Logger.Log($"Extracted release name via AAPT: {releaseName}");
                        return releaseName;
                    }
                }
            }

            Logger.Log("Could not find application-label in AAPT output", LogLevel.Warning);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error extracting release name with AAPT: {ex.Message}", LogLevel.Error);
            return null;
        }
    }
}
