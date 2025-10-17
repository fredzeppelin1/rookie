using System.IO;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Utility methods for file system operations
/// </summary>
public static class FileSystemUtilities
{
    /// <summary>
    /// Ensure a directory exists, creating it if necessary
    /// </summary>
    public static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path!);
        }
    }

    /// <summary>
    /// Safely delete a file if it exists
    /// </summary>
    public static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Check if a file exists and is not empty
    /// </summary>
    public static bool FileExistsAndNotEmpty(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var fileInfo = new FileInfo(path);
        return fileInfo.Length > 0;
    }
}