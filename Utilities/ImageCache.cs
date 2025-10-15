using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AndroidSideloader.Sideloader;

namespace AndroidSideloader.Utilities
{
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
                if (File.Exists(localImagePath))
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

                    if (File.Exists(localImagePath))
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
        /// Download multiple game thumbnails in batch
        /// </summary>
        /// <param name="remoteName">Remote name</param>
        /// <param name="maxImages">Maximum number of images to download (0 = all)</param>
        /// <returns>Number of images successfully downloaded</returns>
        public static async Task<int> DownloadAllThumbnailsAsync(string remoteName, int maxImages = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(remoteName))
                {
                    Logger.Log("Cannot download thumbnails: no remote specified", LogLevel.Warning);
                    return 0;
                }

                Logger.Log($"Starting batch thumbnail download from {remoteName}...");

                // Use rclone sync to download entire thumbnails folder
                var remoteThumbnailsPath = $"{remoteName}:{SideloaderRclone.RcloneGamesFolder}/.meta/thumbnails";

                var result = await Rclone.runRcloneCommand_DownloadConfig(
                    $"sync \"{remoteThumbnailsPath}\" \"{CacheDirectory}\" --transfers 10");

                // Count downloaded files
                var downloadedCount = 0;
                if (Directory.Exists(CacheDirectory))
                {
                    downloadedCount = Directory.GetFiles(CacheDirectory, "*.jpg").Length;
                }

                Logger.Log($"Batch thumbnail download complete: {downloadedCount} images cached");
                return downloadedCount;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error downloading thumbnails batch: {ex.Message}", LogLevel.Error);
                return 0;
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
        /// Clear all cached images
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    var files = Directory.GetFiles(CacheDirectory);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Failed to delete cached image {file}: {ex.Message}", LogLevel.Warning);
                        }
                    }
                    Logger.Log($"Cleared image cache ({files.Length} files deleted)");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error clearing image cache: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Get cache size in MB
        /// </summary>
        public static long GetCacheSizeMb()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    return 0;

                var files = Directory.GetFiles(CacheDirectory);
                long totalBytes = 0;
                foreach (var file in files)
                {
                    totalBytes += new FileInfo(file).Length;
                }
                return totalBytes / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Sanitize game name for use as filename
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "unknown";

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
}
