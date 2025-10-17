using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AndroidSideloader.Sideloader;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Manages downloading and caching of game cover images/thumbnails
/// </summary>
public static class ImageCache
{
    private static readonly string CacheDirectory;

    static ImageCache()
    {
        // Initialize cache directory in app's base directory
        CacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "thumbnails");

        if (!Directory.Exists(CacheDirectory))
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                Logger.Log($"Created image cache directory: {CacheDirectory}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to create image cache directory: {ex.Message}", LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// Download game thumbnail from remote and cache locally
    /// </summary>
    /// <param name="gameName">Game name (used for filename)</param>
    /// <param name="remoteName">Remote name (e.g., mirror1)</param>
    /// <returns>Local cached file path, or null if download failed</returns>
    public static async Task<string> DownloadAndCacheImageAsync(string gameName, string remoteName = null)
    {
        try
        {
            if (string.IsNullOrEmpty(gameName))
            {
                return null;
            }

            // Sanitize game name for filename
            var safeFileName = SanitizeFileName(gameName);
            var localImagePath = Path.Combine(CacheDirectory, $"{safeFileName}.jpg");

            // Check if already cached
            if (FileSystemUtilities.FileExistsAndNotEmpty(localImagePath))
            {
                Logger.Log($"Using cached image for {gameName}");
                return localImagePath;
            }

            // Download using rclone from thumbnails folder
            if (!string.IsNullOrEmpty(remoteName))
            {
                var remotePath = $"{remoteName}:{SideloaderRclone.RcloneGamesFolder}/.meta/thumbnails/{safeFileName}.jpg";

                Logger.Log($"Downloading thumbnail: {remotePath}");

                var result = await Rclone.runRcloneCommand_DownloadConfig(
                    $"copy \"{remotePath}\" \"{CacheDirectory}\" --ignore-existing");

                // Check for download errors
                if (!string.IsNullOrEmpty(result.Error) && !result.Error.Contains("not found"))
                {
                    Logger.Log($"Thumbnail download error: {result.Error}", LogLevel.Warning);
                }

                if (FileSystemUtilities.FileExistsAndNotEmpty(localImagePath))
                {
                    Logger.Log($"Successfully cached image for {gameName}");
                    return localImagePath;
                }
            }

            Logger.Log($"Image not found for {gameName}", LogLevel.Warning);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error downloading image for {gameName}: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// Get cached image path for a game (without downloading)
    /// Checks for both .jpg and .png extensions
    /// </summary>
    /// <param name="gameName">Game name</param>
    /// <returns>Local path if cached, null otherwise</returns>
    public static string GetCachedImagePath(string gameName)
    {
        if (string.IsNullOrEmpty(gameName))
        {
            return null;
        }

        var safeFileName = SanitizeFileName(gameName);

        // Check both .jpg and .png extensions (jpg is more common, check first)
        string[] extensions = [".jpg", ".png"];
        return extensions
            .Select(ext => Path.Combine(CacheDirectory, $"{safeFileName}{ext}"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Sanitize game name for use as filename
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "unknown";
        }

        // Remove invalid filename characters
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        // Remove additional problematic characters
        fileName = fileName.Replace(' ', '_');
        fileName = fileName.Replace(':', '_');
        fileName = fileName.Replace('/', '_');
        fileName = fileName.Replace('\\', '_');

        return fileName;
    }
}