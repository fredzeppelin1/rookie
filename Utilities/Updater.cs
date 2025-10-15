using AndroidSideloader.Utilities;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AndroidSideloader.Utilities
{
    public class Updater
    {
        public static string AppName { get; set; } = "Rookie";
        public static string Repository { get; set; } = "VRPirates/rookie";

        private static readonly string RawGitHubUrl = "https://raw.githubusercontent.com/VRPirates/rookie";

        // Reserved for future update download functionality
#pragma warning disable CS0414
        private static readonly string GitHubUrl = "https://github.com/VRPirates/rookie";
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

        public static string CurrentVersion { get; private set; } = string.Empty;
        public static string Changelog { get; private set; } = string.Empty;

        /// <summary>
        /// Check if there is a new version of the sideloader available
        /// </summary>
        public static async Task<bool> IsUpdateAvailableAsync()
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
                    // TODO: Show update dialog when UI is implemented
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
    }
}
