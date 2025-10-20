using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.Xz;

namespace AndroidSideloader.Sideloader;

public static class GetDependencies
{
    public static async Task DownloadRclone()
    {
        var extractPath = AppDomain.CurrentDomain.BaseDirectory;
        var rclonePath = Path.Combine(extractPath, "rclone");

        // Determine the executable name based on platform
        var rcloneExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rclone.exe" : "rclone";
        var rcloneExePath = Path.Combine(rclonePath, rcloneExeName);

        // Check if rclone already exists
        if (File.Exists(rcloneExePath))
        {
            Console.WriteLine($"Rclone already exists at: {rcloneExePath}");
            return;
        }

        Console.WriteLine("Rclone not found, downloading...");

        var rcloneZipUrl = GetRcloneDownloadUrl();
        var rcloneZipFileName = Path.GetFileName(rcloneZipUrl);
        var depFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dep");
        var rcloneZipPath = Path.Combine(depFolderPath, rcloneZipFileName);

        if (!Directory.Exists(depFolderPath))
        {
            Directory.CreateDirectory(depFolderPath);
        }

        try
        {
            using (var client = new HttpClient())
            {
                Console.WriteLine($"Downloading rclone from: {rcloneZipUrl}");
                var response = await client.GetAsync(rcloneZipUrl);
                response.EnsureSuccessStatusCode();
                await using (var fileStream = new FileStream(rcloneZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
            }

            Console.WriteLine($"Extracting rclone to: {extractPath}");
            ZipFile.ExtractToDirectory(rcloneZipPath, extractPath, true);

            // Create rclone subdirectory (rclonePath already declared at top of method)
            // Delete existing rclone directory if it exists to avoid conflicts
            if (Directory.Exists(rclonePath))
            {
                Directory.Delete(rclonePath, true);
            }
            Directory.CreateDirectory(rclonePath);

            var extractedFolder = Path.GetFileNameWithoutExtension(rcloneZipUrl);
            var extractedFolderPath = Path.Combine(extractPath, extractedFolder);
            var extractedFiles = Directory.GetFiles(extractedFolderPath, "*").ToList();

            // Copy extracted files to rclone subdirectory
            foreach (var mFile in extractedFiles.Select(file => new FileInfo(file)))
            {
                mFile.CopyTo(Path.Combine(rclonePath, mFile.Name), true);
            }

            // Clean up the extracted folder after copying files
            if (Directory.Exists(extractedFolderPath))
            {
                Directory.Delete(extractedFolderPath, true);
            }

            Console.WriteLine("Rclone downloaded and extracted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading or extracting rclone: {ex.Message}");
            // Log the error properly
        }
    }

    public static async Task Download7Zip()
    {
        var extractPath = AppDomain.CurrentDomain.BaseDirectory;
        var sevenZipPath = Path.Combine(extractPath, "7zip");

        // Check if 7z executable already exists
        // Windows: 7za.exe (standalone console version from extra package)
        // macOS: 7zz (from mac tar.xz package)
        var sevenZipExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "7za.exe" : "7zz";
        var sevenZipExePath = Path.Combine(sevenZipPath, sevenZipExeName);

        if (File.Exists(sevenZipExePath))
        {
            Console.WriteLine($"7-Zip already exists at: {sevenZipExePath}");
            return;
        }

        Console.WriteLine("7-Zip not found, downloading...");

        var sevenZipUrl = Get7ZipDownloadUrl();
        var sevenZipFileName = Path.GetFileName(sevenZipUrl);
        var depFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dep");
        var sevenZipFilePath = Path.Combine(depFolderPath, sevenZipFileName);

        if (!Directory.Exists(depFolderPath))
        {
            Directory.CreateDirectory(depFolderPath);
        }

        try
        {
            using (var client = new HttpClient())
            {
                Console.WriteLine($"Downloading 7-Zip from: {sevenZipUrl}");
                var response = await client.GetAsync(sevenZipUrl);
                response.EnsureSuccessStatusCode();
                await using (var fileStream = new FileStream(sevenZipFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
            }

            Console.WriteLine($"Extracting 7-Zip to: {sevenZipPath}");

            // Delete existing 7zip directory if it exists to avoid conflicts
            if (Directory.Exists(sevenZipPath))
            {
                Directory.Delete(sevenZipPath, true);
            }
            Directory.CreateDirectory(sevenZipPath);

            // Handle different archive formats based on platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: Extract .7z file using SharpCompress
                using var archive = ArchiveFactory.Open(sevenZipFilePath);
                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                {
                    entry.WriteToDirectory(sevenZipPath, new ExtractionOptions
                    {
                        ExtractFullPath = false, // We want files directly in 7zip subdirectory
                        Overwrite = true
                    });
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: Extract .tar.xz file
                await using var xz = new XZStream(File.OpenRead(sevenZipFilePath));
                await using var stream = new MemoryStream();
                await xz.CopyToAsync(stream);
                stream.Seek(0, SeekOrigin.Begin);
                await TarFile.ExtractToDirectoryAsync(stream, sevenZipPath, true);
            }

            Console.WriteLine("7-Zip downloaded and extracted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading or extracting 7-Zip: {ex.Message}");
            // Log the error properly
        }
    }

    public static async Task DownloadAdb()
    {
        var extractPath = AppDomain.CurrentDomain.BaseDirectory;
        var platformToolsPath = Path.Combine(extractPath, "platform-tools");

        // Determine the executable name based on platform
        var adbExeName = Utilities.PlatformHelper.GetAdbExecutableName();
        var adbExePath = Path.Combine(platformToolsPath, adbExeName);

        // Check if ADB already exists
        if (File.Exists(adbExePath))
        {
            Console.WriteLine($"ADB already exists at: {adbExePath}");
            return;
        }

        Console.WriteLine("ADB not found, downloading...");

        var adbZipUrl = GetAdbDownloadUrl();
        const string adbZipFileName = "platform-tools.zip";
        var depFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dep");
        var adbZipPath = Path.Combine(depFolderPath, adbZipFileName);

        if (!Directory.Exists(depFolderPath))
        {
            Directory.CreateDirectory(depFolderPath);
        }

        try
        {
            using (var client = new HttpClient())
            {
                Console.WriteLine($"Downloading ADB platform-tools from: {adbZipUrl}");
                var response = await client.GetAsync(adbZipUrl);
                response.EnsureSuccessStatusCode();
                await using (var fileStream = new FileStream(adbZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
            }

            Console.WriteLine($"Extracting ADB to: {extractPath}");
            ZipFile.ExtractToDirectory(adbZipPath, extractPath, true);

            Console.WriteLine("ADB platform-tools downloaded and extracted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading or extracting ADB: {ex.Message}");
            // Log the error properly
        }
    }

    private static string GetRcloneDownloadUrl()
    {
        const string baseUrl = "https://downloads.rclone.org/";
        const string version = "v1.67.0"; // Use a specific version for stability

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => $"{baseUrl}{version}/rclone-{version}-windows-amd64.zip",
                Architecture.Arm64 => $"{baseUrl}{version}/rclone-{version}-windows-arm64.zip",
                _ => $"{baseUrl}{version}/rclone-{version}-windows-386.zip"
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 
                ? $"{baseUrl}{version}/rclone-{version}-osx-arm64.zip" 
                : $"{baseUrl}{version}/rclone-{version}-osx-amd64.zip";
        }
            
        // Add other platforms as needed (Linux, etc.)
        throw new PlatformNotSupportedException("Unsupported operating system or architecture for rclone.");
    }

    private static string Get7ZipDownloadUrl()
    {
        const string version = "2501"; // Use latest stable version (25.01)

        // 7-Zip doesn't provide .zip archives for Windows.
        // The "extra" package contains standalone console executables (7za.exe, 7zr.exe, etc.)
        // in .7z format, which we'll extract using SharpCompress.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Use the extra package which contains standalone console tools
            return $"https://www.7-zip.org/a/7z{version}-extra.7z";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // For macOS, use the mac tar.xz version
            return $"https://www.7-zip.org/a/7z{version}-mac.tar.xz";
        }

        throw new PlatformNotSupportedException("Unsupported operating system for 7-Zip.");
    }

    private static string GetAdbDownloadUrl()
    {
        // Google provides official Android SDK platform-tools downloads
        const string baseUrl = "https://dl.google.com/android/repository/";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"{baseUrl}platform-tools-latest-windows.zip";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"{baseUrl}platform-tools-latest-darwin.zip";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return $"{baseUrl}platform-tools-latest-linux.zip";
        }

        throw new PlatformNotSupportedException("Unsupported operating system for ADB platform-tools.");
    }
}