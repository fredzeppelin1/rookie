using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndroidSideloader.Models;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Detects apps on device that can be contributed to VRPirates
/// Identifies both updates and new apps
/// </summary>
public static class DonorAppDetector
{
    /// <summary>
    /// Detects donor apps by comparing installed apps with known game list
    /// </summary>
    /// <param name="allGames">List of known games from VRPirates</param>
    /// <param name="noAppCheck">Skip app checking if true</param>
    /// <returns>List of apps that can be donated</returns>
    public static List<DonorApp> DetectDonorAppsAsync(List<GameItem> allGames, bool noAppCheck)
    {
        var donorApps = new List<DonorApp>();

        if (noAppCheck)
        {
            Logger.Log("App checking disabled - skipping donor detection");
            return donorApps;
        }

        try
        {
            Logger.Log("Starting donor app detection...");

            // Get installed packages from device
            var installedPackages = Adb.GetInstalledPackages();
            if (installedPackages == null || installedPackages.Length == 0)
            {
                Logger.Log("No installed packages found on device");
                return donorApps;
            }

            Logger.Log($"Found {installedPackages.Length} installed packages on device");

            // Build lookup structures
            var gamesByPackage = allGames.ToDictionary(g => g.PackageName, g => g);
            var settings = SettingsManager.Instance;
            var blacklist = (settings.NonAppPackages ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var knownApps = (settings.AppPackages ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            // Check each installed package
            var newAppCount = 0;
            const int maxNewApps = 6; // Limit to 6 new apps to prevent overload

            foreach (var packageName in installedPackages)
            {
                // Skip if in blacklist
                if (blacklist.Contains(packageName))
                {
                    continue;
                }

                // Check if it's a known game with update available
                if (gamesByPackage.TryGetValue(packageName, out var game))
                {
                    if (game.IsInstalled && game.InstalledVersionCode > game.AvailableVersionCode)
                    {
                        // Newer version installed than what's in list
                        var donorApp = new DonorApp(
                            game.GameName,
                            packageName,
                            game.InstalledVersionCode,
                            "Update"
                        );
                        donorApps.Add(donorApp);
                        Logger.Log($"Found update candidate: {game.GameName} v{game.InstalledVersionCode}");
                    }
                }
                else if (!knownApps.Contains(packageName) && newAppCount < maxNewApps)
                {
                    // Potentially new app
                    Logger.Log($"Found potential new app: {packageName}");
                    newAppCount++;

                    // Try to get release name and version
                    var (releaseName, versionCode) = GetAppInfoAsync(packageName);

                    var donorApp = new DonorApp(
                        releaseName,
                        packageName,
                        versionCode,
                        "New App"
                    );
                    donorApps.Add(donorApp);
                }
            }

            Logger.Log($"Donor detection complete: {donorApps.Count} apps found ({donorApps.Count(a => a.Status == "Update")} updates, {donorApps.Count(a => a.Status == "New App")} new apps)");
            return donorApps;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during donor app detection: {ex.Message}", LogLevel.Error);
            return donorApps;
        }
    }

    /// <summary>
    /// Gets app info (release name and version code) for a package
    /// Attempts to extract release name via AAPT, falls back to package name
    /// </summary>
    private static (string releaseName, long versionCode) GetAppInfoAsync(string packageName)
    {
        try
        {
            // Get version code
            var versionResult = Adb.RunAdbCommandToString($"shell \"dumpsys package {packageName} | grep versionCode -F\"");
            var versionOutput = versionResult.Output;

            long versionCode = 0;
            if (!string.IsNullOrEmpty(versionOutput))
            {
                // Parse: versionCode=123 minSdk=...
                var versionLine = versionOutput.Split('\n')[0];
                if (versionLine.Contains("versionCode="))
                {
                    var startIdx = versionLine.IndexOf("versionCode=", StringComparison.Ordinal) + 12;
                    var endIdx = versionLine.IndexOf(' ', startIdx);
                    if (endIdx == -1) endIdx = versionLine.Length;

                    var versionStr = versionLine.Substring(startIdx, endIdx - startIdx).Trim();
                    long.TryParse(versionStr.Replace(",", ""), out versionCode);
                }
            }

            // Try to get release name via APK extraction + AAPT
            string releaseName = null;

            if (AaptHelper.IsAaptAvailable())
            {
                // Pull APK from device
                var platformToolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools");
                var apkPath = Path.Combine(platformToolsDir, "base.apk");

                // Delete old APK if exists
                if (File.Exists(apkPath))
                {
                    File.Delete(apkPath);
                }

                // Get APK path on device
                var pathResult = Adb.RunAdbCommandToString($"shell pm path {packageName}");
                var pathOutput = pathResult.Output;

                if (!string.IsNullOrEmpty(pathOutput) && pathOutput.Contains("package:"))
                {
                    var deviceApkPath = pathOutput.Replace("package:", "").Trim();

                    // Pull APK
                    Logger.Log($"Pulling APK for {packageName}...");
                    Adb.RunAdbCommandToString($"pull \"{deviceApkPath}\" \"{apkPath}\"");

                    if (File.Exists(apkPath))
                    {
                        // Extract release name with AAPT
                        releaseName = AaptHelper.ExtractReleaseName(apkPath);

                        // Clean up
                        try
                        {
                            File.Delete(apkPath);
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                    }
                }
            }

            // Fallback to package name-based release name
            if (string.IsNullOrEmpty(releaseName))
            {
                releaseName = PackageNameToGameName(packageName);
            }

            Logger.Log($"App info for {packageName}: {releaseName} v{versionCode}");
            return (releaseName, versionCode);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error getting app info for {packageName}: {ex.Message}", LogLevel.Error);
            return (PackageNameToGameName(packageName), 0);
        }
    }

    /// <summary>
    /// Converts package name to a readable game name
    /// Example: com.beatgames.beatsaber -> Beat Saber
    /// </summary>
    private static string PackageNameToGameName(string packageName)
    {
        try
        {
            // Remove common prefixes
            var name = packageName;
            if (name.StartsWith("com."))
            {
                name = name.Substring(4);
            }

            // Take the last segment (app name part)
            var segments = name.Split('.');
            name = segments.Length > 0 ? segments[^1] : name;

            // Convert camelCase/snake_case to Title Case
            name = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            name = name.Replace("_", " ").Replace("-", " ");

            // Capitalize first letter of each word
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            name = string.Join(" ", words.Select(w => char.ToUpper(w[0]) + w.Substring(1).ToLower()));

            return name;
        }
        catch
        {
            return packageName;
        }
    }
}
