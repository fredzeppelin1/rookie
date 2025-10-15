using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AndroidSideloader.Utilities
{
    /// <summary>
    /// Handles anonymous download metrics reporting to VRPirates API
    /// </summary>
    public static class Metrics
    {
        private static readonly HttpClient HttpClient = new();
        private const string ApiUrl = "https://api.vrpirates.wiki/metrics/add";
        private const string AuthToken = "cm9va2llOkN0UHlyTE9oUGoxWXg1cE9KdDNBSkswZ25n";

        static Metrics()
        {
            // Configure HttpClient headers once
            HttpClient.DefaultRequestHeaders.Add("Authorization", AuthToken);
            HttpClient.DefaultRequestHeaders.Add("Origin", "rookie");
            HttpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Track a game download anonymously (non-blocking)
        /// </summary>
        /// <param name="packageName">Game package name (e.g., com.beatgames.beatsaber)</param>
        /// <param name="versionCode">Game version code</param>
        public static async void CountDownload(string packageName, string versionCode)
        {
            try
            {
                if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(versionCode))
                {
                    Logger.Log("Skipping metrics: missing package name or version", LogLevel.Debug);
                    return;
                }

                var requestBody = new
                {
                    packagename = packageName,
                    versioncode = versionCode
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Logger.Log($"Reporting download metrics for {packageName} v{versionCode}", LogLevel.Debug);

                var response = await HttpClient.PostAsync(ApiUrl, content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Logger.Log($"Metrics reported successfully: {responseText}", LogLevel.Debug);
                }
                else
                {
                    Logger.Log($"Metrics API returned {response.StatusCode}: {responseText}", LogLevel.Warning);
                }
            }
            catch (TaskCanceledException)
            {
                Logger.Log("Metrics request timed out (non-critical)", LogLevel.Debug);
            }
            catch (HttpRequestException ex)
            {
                Logger.Log($"Unable to report metrics (non-critical): {ex.Message}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log($"Unexpected error reporting metrics: {ex.Message}", LogLevel.Warning);
            }
        }
    }
}
