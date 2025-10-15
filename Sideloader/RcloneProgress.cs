using System;
using System.Text.RegularExpressions;
using AndroidSideloader.Utilities;

namespace AndroidSideloader.Sideloader;

/// <summary>
/// Progress information from rclone operations
/// </summary>
public class RcloneProgress
{
    public double Percentage { get; set; }
    public long TransferredBytes { get; set; }
    public long TotalBytes { get; set; }
    public double SpeedMBps { get; set; }
    public string SpeedText { get; set; }
    public int EtaSeconds { get; set; }
    public string EtaText { get; set; }
    public string StatusText { get; set; }
    public int CurrentFile { get; set; }
    public int TotalFiles { get; set; }

    public RcloneProgress()
    {
        SpeedText = "Speed: -- MB/s";
        EtaText = "ETA: --:--";
        StatusText = "Starting...";
        CurrentFile = 0;
        TotalFiles = 0;
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

            // Extract transferred / total (e.g., "1.234 GiB / 2.468 GiB")
            var sizeMatch = Regex.Match(line, @"([\d.]+)\s*(Ki?B|Mi?B|Gi?B)\s*/\s*([\d.]+)\s*(Ki?B|Mi?B|Gi?B)");
            if (sizeMatch.Success)
            {
                var transferred = double.Parse(sizeMatch.Groups[1].Value);
                var transferredUnit = sizeMatch.Groups[2].Value;
                var total = double.Parse(sizeMatch.Groups[3].Value);
                var totalUnit = sizeMatch.Groups[4].Value;

                progress.TransferredBytes = (long)(transferred * GetBytesMultiplier(transferredUnit));
                progress.TotalBytes = (long)(total * GetBytesMultiplier(totalUnit));
            }

            // Extract speed (e.g., "12.5 MiB/s" or "1.2 GiB/s")
            var speedMatch = Regex.Match(line, @"([\d.]+)\s*(Ki?B|Mi?B|Gi?B)/s");
            if (speedMatch.Success)
            {
                var speed = double.Parse(speedMatch.Groups[1].Value);
                var speedUnit = speedMatch.Groups[2].Value;
                progress.SpeedMBps = speed * GetBytesMultiplier(speedUnit) / (1024 * 1024); // Convert to MB/s
                progress.SpeedText = $"Speed: {speed:F1} {speedUnit}/s";
            }

            // Extract ETA (e.g., "ETA 1m34s" or "ETA 5s")
            var etaMatch = Regex.Match(line, @"ETA\s+(?:(\d+)m)?(?:(\d+)s)?");
            if (etaMatch.Success)
            {
                var minutes = etaMatch.Groups[1].Success ? int.Parse(etaMatch.Groups[1].Value) : 0;
                var seconds = etaMatch.Groups[2].Success ? int.Parse(etaMatch.Groups[2].Value) : 0;
                progress.EtaSeconds = (minutes * 60) + seconds;

                if (progress.EtaSeconds > 0)
                {
                    var hours = progress.EtaSeconds / 3600;
                    var mins = (progress.EtaSeconds % 3600) / 60;
                    var secs = progress.EtaSeconds % 60;

                    if (hours > 0)
                        progress.EtaText = $"ETA: {hours:D2}:{mins:D2}:{secs:D2}";
                    else
                        progress.EtaText = $"ETA: {mins:D2}:{secs:D2}";
                }
                else
                {
                    progress.EtaText = "ETA: Calculating...";
                }
            }

            // Build status text (percentage shown in progress bar, not status text)
            if (progress.Percentage > 0)
            {
                if (!string.IsNullOrEmpty(Rclone.CurrentGameName))
                {
                    progress.StatusText = $"Downloading {Rclone.CurrentGameName}...";
                }
                else
                {
                    progress.StatusText = "Downloading...";
                }
            }

            return progress;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error parsing rclone progress: {ex.Message}");
            return null;
        }
    }

    private static double GetBytesMultiplier(string unit)
    {
        return unit.ToUpper() switch
        {
            "B" => 1,
            "KB" or "KIB" => 1024,
            "MB" or "MIB" => 1024 * 1024,
            "GB" or "GIB" => 1024 * 1024 * 1024,
            _ => 1
        };
    }
}