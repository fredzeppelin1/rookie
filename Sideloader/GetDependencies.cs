using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompress.Compressors.Xz;

namespace AndroidSideloader.Sideloader
{
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

            Console.WriteLine($"Rclone not found, downloading...");

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
                Directory.CreateDirectory(rclonePath);

                var extractedFolder = Path.GetFileNameWithoutExtension(rcloneZipUrl);
                var extractedFiles = Directory.GetFiles(Path.Combine(extractPath, extractedFolder), "*").ToList();

                // Copy extracted files to rclone subdirectory
                foreach (var mFile in extractedFiles.Select(file => new FileInfo(file)))
                {
                    mFile.CopyTo(Path.Combine(rclonePath, mFile.Name), true);
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

            // Check if 7z executable already exists (7zz on macOS/Linux, 7z.exe on Windows)
            var sevenZipExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "7z.exe" : "7zz";
            var sevenZipExePath = Path.Combine(extractPath, sevenZipExeName);

            if (File.Exists(sevenZipExePath))
            {
                Console.WriteLine($"7-Zip already exists at: {sevenZipExePath}");
                return;
            }

            Console.WriteLine($"7-Zip not found, downloading...");

            var sevenZipZipUrl = Get7ZipDownloadUrl();
            var sevenZipZipFileName = Path.GetFileName(sevenZipZipUrl);
            var depFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dep");
            var sevenZipZipPath = Path.Combine(depFolderPath, sevenZipZipFileName);

            if (!Directory.Exists(depFolderPath))
            {
                Directory.CreateDirectory(depFolderPath);
            }

            try
            {
                using (var client = new HttpClient())
                {
                    Console.WriteLine($"Downloading 7-Zip from: {sevenZipZipUrl}");
                    var response = await client.GetAsync(sevenZipZipUrl);
                    response.EnsureSuccessStatusCode();
                    await using (var fileStream = new FileStream(sevenZipZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                Console.WriteLine($"Extracting 7-Zip to: {extractPath}");
                await using (var xz = new XZStream(File.OpenRead(sevenZipZipPath)))
                await using (var stream = new MemoryStream())
                {
                    await xz.CopyToAsync(stream);
                    stream.Seek(0, SeekOrigin.Begin);
                    await TarFile.ExtractToDirectoryAsync(stream, extractPath, true);
                    Console.WriteLine("7-Zip downloaded and extracted successfully.");
                }
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
            var adbExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "adb.exe" : "adb";
            var adbExePath = Path.Combine(platformToolsPath, adbExeName);

            // Check if ADB already exists
            if (File.Exists(adbExePath))
            {
                Console.WriteLine($"ADB already exists at: {adbExePath}");
                return;
            }

            Console.WriteLine($"ADB not found, downloading...");

            var adbZipUrl = GetAdbDownloadUrl();
            var adbZipFileName = "platform-tools.zip";
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
            const string version = "2301"; // Use a specific version for stability

            // 7-Zip doesn't provide direct download links for specific architectures like rclone.
            // It's usually a single installer or a generic zip.
            // For cross-platform, p7zip is the common alternative on Linux/macOS.
            // For simplicity, we'll use a placeholder for now.
            // In a real scenario, you might need to bundle p7zip or instruct user to install it.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Example: return "https://www.7-zip.org/a/7z2301-x64.exe";
                return $"https://www.7-zip.org/a/7z{version}-x64.zip"; // Placeholder for Windows 7-Zip
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // For macOS, p7zip is usually installed via Homebrew. Bundling is complex.
                // This URL is a placeholder and might not work directly for bundling.
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
}