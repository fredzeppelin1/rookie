using System;
using System.Text.RegularExpressions;
using AndroidSideloader.Utilities;

namespace AndroidSideloader.Sideloader;

/// <summary>
/// Progress information from rclone operations
/// </summary>
public class RcloneProgress
{
    public double Percentage { get; private set; }
    public string SpeedText { get; private set; }
    private int EtaSeconds { get; set; }
    public string EtaText { get; private set; }
    public string StatusText { get; private set; }

    private RcloneProgress()
    {
        SpeedText = "Speed: -- MB/s";
        EtaText = "ETA: --:--";
        StatusText = "Starting...";
    }

    /// <summary>
    /// Parse rclone output line for progress information
    /// Example: "Transferred:   50.00 MiB / 100.00 MiB, 50%, 10.5 MiB/s, ETA 5s"
    /// Example: "Transferred:        1 / 3, 33%"
    /// Example with INFO prefix: "2025/10/14 18:15:27 INFO  : Transferred:   50.00 MiB..."
    /// </summary>
    public static RcloneProgress ParseFromOutput(string line)
    {
        var progress = new RcloneProgress();

        try
        {
            if (string.IsNullOrEmpty(line) || !line.Contains("Transferred:"))
            {
                return null;
            }

            // Strip INFO log prefix if present (from --log-level INFO mode)
            // Format: "2025/10/14 18:15:27 INFO  : Transferred:..."
            var infoIndex = line.IndexOf("INFO  :", StringComparison.Ordinal);
            if (infoIndex >= 0)
            {
                line = line.Substring(infoIndex + "INFO  :".Length).Trim();
            }

            // Example line: "Transferred:        1.234 GiB / 2.468 GiB, 50%, 12.5 MiB/s, ETA 1m34s"
            // Or: "Transferred:        1 / 3, 33%" (file count - IGNORE THIS, use byte progress only)

            // Check if this is a file count line (e.g., "1 / 3") and SKIP it
            // File count shows "0 of 7 completed" which is misleading - the real progress is byte-based
            var fileCountMatch = Regex.Match(line, @"Transferred:\s+(\d+)\s*/\s*(\d+),\s*(\d+)%");
            if (fileCountMatch.Success)
            {
                // This is a file count line like "Transferred: 0 / 7, 0%"
                // Skip it - we only care about byte-based progress
                // Returning null prevents this from triggering a UI update
                return null;
            }

            // Extract percentage (e.g., "50%")
            var percentMatch = Regex.Match(line, @"(\d+)%");
            if (percentMatch.Success)
            {
                progress.Percentage = double.Parse(percentMatch.Groups[1].Value);
            }

            // Extract speed (e.g., "12.5 MiB/s" or "1.2 GiB/s")
            var speedMatch = Regex.Match(line, @"([\d.]+)\s*(Ki?B|Mi?B|Gi?B)/s");
            if (speedMatch.Success)
            {
                var speed = double.Parse(speedMatch.Groups[1].Value);
                var speedUnit = speedMatch.Groups[2].Value;
                progress.SpeedText = $"Speed: {speed:F1} {speedUnit}/s";
            }

            // Extract ETA (e.g., "ETA 1m34s" or "ETA 5s")
            var etaMatch = Regex.Match(line, @"ETA\s+(?:(\d+)m)?(?:(\d+)s)?");
            if (etaMatch.Success)
            {
                var minutes = etaMatch.Groups[1].Success ? int.Parse(etaMatch.Groups[1].Value) : 0;
                var seconds = etaMatch.Groups[2].Success ? int.Parse(etaMatch.Groups[2].Value) : 0;
                progress.EtaSeconds = minutes * 60 + seconds;

                if (progress.EtaSeconds > 0)
                {
                    var hours = progress.EtaSeconds / 3600;
                    var mins = progress.EtaSeconds % 3600 / 60;
                    var secs = progress.EtaSeconds % 60;

                    progress.EtaText = hours > 0 
                        ? $"ETA: {hours:D2}:{mins:D2}:{secs:D2}" 
                        : $"ETA: {mins:D2}:{secs:D2}";
                }
                else
                {
                    progress.EtaText = "ETA: Calculating...";
                }
            }

            // Build status text (percentage shown in progress bar, not status text)
            if (progress.Percentage > 0)
            {
                progress.StatusText = !string.IsNullOrEmpty(Rclone.CurrentGameName) 
                    ? $"Downloading {Rclone.CurrentGameName}..." 
                    : "Downloading...";
            }

            return progress;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error parsing rclone progress: {ex.Message}");
            return null;
        }
    }
}