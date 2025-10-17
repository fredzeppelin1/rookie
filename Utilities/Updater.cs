using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;

namespace AndroidSideloader.Utilities;

public class Updater
{
    public static string AppName { get; set; } = "Rookie";

    private const string RawGitHubUrl = "https://raw.githubusercontent.com/VRPirates/rookie";

    // Reserved for future update download functionality
#pragma warning disable CS0414
    private const string GitHubUrl = "https://github.com/VRPirates/rookie";
#pragma warning restore CS0414

    private static string _localVersion;

    /// <summary>
    /// Gets the local version by reading from the version file
    /// </summary>
    public static string LocalVersion
    {
        get
        {
            if (_localVersion == null)
            {
                try
                {
                    var versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version");
                    if (File.Exists(versionFile))
                    {
                        _localVersion = File.ReadAllText(versionFile).Trim();
                        Logger.Log($"Read version from file: {_localVersion}");
                    }
                    else
                    {
                        _localVersion = "Unknown"; // Fallback version
                        Logger.Log($"Version file not found, using fallback: {_localVersion}", LogLevel.Warning);
                    }
                }
                catch (Exception ex)
                {
                    _localVersion = "Unknown"; // Fallback version
                    Logger.Log($"Error reading version file: {ex.Message}", LogLevel.Error);
                }
            }
            return _localVersion;
        }
    }

    private static string CurrentVersion { get; set; } = string.Empty;
    private static string Changelog { get; set; } = string.Empty;

    /// <summary>
    /// Check if there is a new version of the sideloader available
    /// </summary>
    private static async Task<bool> IsUpdateAvailableAsync()
    {
        using var client = new HttpClient();
        try
        {
            CurrentVersion = await client.GetStringAsync($"{RawGitHubUrl}/master/version");
            Changelog = await client.GetStringAsync($"{RawGitHubUrl}/master/changelog.txt");
            CurrentVersion = CurrentVersion.Trim();

            Logger.Log($"Local version: {LocalVersion}, Remote version: {CurrentVersion}");
            return LocalVersion != CurrentVersion;
        }
        catch (HttpRequestException ex)
        {
            Logger.Log($"Failed to check for updates: {ex.Message}", LogLevel.Warning);
            return false;
        }
    }

    /// <summary>
    /// Check for updates and show dialog if available
    /// </summary>
    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            if (await IsUpdateAvailableAsync())
            {
                Logger.Log($"Update available: {CurrentVersion}");

                // Show update dialog on UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var updateWindow = new Views.UpdateWindow(LocalVersion, CurrentVersion, Changelog);
                    var result = await updateWindow.ShowDialog<bool?>(
                        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                            ? desktop.MainWindow
                            : null);

                    if (result == true)
                    {
                        Logger.Log("User accepted update");
                    }
                });
            }
            else
            {
                Logger.Log("Application is up to date");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error checking for updates: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Download and install the update (cross-platform)
    /// </summary>
    public static async Task DownloadAndInstallUpdateAsync()
    {
        try
        {
            Logger.Log("Starting update download and install process");

            // Kill ADB server before updating
            Adb.RunAdbCommandToString("kill-server");
            Logger.Log("Killed ADB server");

            // Determine download URL based on platform
            var downloadUrl = GetUpdateDownloadUrl();
            if (string.IsNullOrEmpty(downloadUrl))
            {
                Logger.Log("Unable to determine download URL for this platform", LogLevel.Error);
                return;
            }

            // Download the update
            var updateFileName = Path.GetFileName(downloadUrl);
            var updatePath = Path.Combine(
                Path.GetTempPath(),
                $"{AppName}-{CurrentVersion}-{updateFileName}");

            Logger.Log($"Downloading update from: {downloadUrl}");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(10); // Large timeout for downloads
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                await using (var fileStream = new FileStream(updatePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
            }

            Logger.Log($"Update downloaded to: {updatePath}");

            // Launch the update installer
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                // Windows: Launch the new .exe
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = updatePath,
                    UseShellExecute = true
                });

                Logger.Log("Launched new version, exiting current version");
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                // macOS: Open the downloaded file (likely a .dmg or .zip)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{updatePath}\"",
                    UseShellExecute = false
                });

                Logger.Log("Opened update package, please complete installation manually");
            }
            else
            {
                // Linux: Open file manager to the downloaded file
                var directory = Path.GetDirectoryName(updatePath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = false
                });

                Logger.Log("Opened download location, please complete installation manually");
            }

            // Exit the application
            await Task.Delay(1000); // Brief delay to ensure process launch
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Logger.Log($"Update failed: {ex.Message}", LogLevel.Error);
            Logger.Log($"Stack trace: {ex.StackTrace}", LogLevel.Error);
            throw;
        }
    }

    /// <summary>
    /// Get the appropriate download URL for the current platform
    /// </summary>
    private static string GetUpdateDownloadUrl()
    {
        // GitHub releases pattern: https://github.com/VRPirates/rookie/releases/download/v{version}/{filename}
        var baseUrl = $"{GitHubUrl}/releases/download/v{CurrentVersion}";

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            // Windows: Download the .exe installer
            return $"{baseUrl}/{AppName}-{CurrentVersion}-Windows.exe";
        }

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            // macOS: Download a .dmg or .zip
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            return arch == System.Runtime.InteropServices.Architecture.Arm64 
                ? $"{baseUrl}/{AppName}-{CurrentVersion}-macOS-arm64.dmg" 
                : $"{baseUrl}/{AppName}-{CurrentVersion}-macOS-x64.dmg";
        }

        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) 
            ? $"{baseUrl}/{AppName}-{CurrentVersion}-Linux-x64.tar.gz" // Linux: Download a .tar.gz or AppImage
            : null;
    }
}