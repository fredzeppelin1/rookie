using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AndroidSideloader.Utilities
{
    public class SevenZip
    {
        private static readonly string SevenZipPath = GetSevenZipExecutablePath();

        private static string GetSevenZipExecutablePath()
        {
            // Try different executable names depending on platform
            string[] executableNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ["7z.exe", "7zz.exe"]
                : ["7zz", "7z"];

            // First, try the bundled version in the application directory
            foreach (var execName in executableNames)
            {
                var bundledPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, execName);
                if (File.Exists(bundledPath))
                {
                    Console.WriteLine($"Found 7z executable: {bundledPath}");
                    return bundledPath;
                }
            }

            // On macOS/Linux, check if 7z/7zz is installed in the system PATH
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var execName in executableNames)
                {
                    try
                    {
                        var result = Process.Start(new ProcessStartInfo
                        {
                            FileName = "which",
                            Arguments = execName,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        });

                        if (result != null)
                        {
                            var pathFromWhich = result.StandardOutput.ReadToEnd().Trim();
                            result.WaitForExit();

                            if (result.ExitCode == 0 && !string.IsNullOrEmpty(pathFromWhich) && File.Exists(pathFromWhich))
                            {
                                Console.WriteLine($"Found {execName} in system PATH: {pathFromWhich}");
                                return pathFromWhich;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors from 'which' command
                    }
                }
            }

            // Fall back to first executable name (even if it doesn't exist yet)
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, executableNames[0]);
        }

        private static async Task<string> RunSevenZipCommand(string arguments)
        {
            // Check if 7z executable exists
            if (!File.Exists(SevenZipPath))
            {
                var installInstructions = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "Please install p7zip using Homebrew: brew install p7zip"
                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "Please install p7zip using your package manager: sudo apt-get install p7zip-full"
                    : "Please download 7-Zip from https://www.7-zip.org/";

                throw new FileNotFoundException(
                    $"7z executable not found at: {SevenZipPath}\n\n{installInstructions}",
                    SevenZipPath);
            }

            var process = new Process();
            process.StartInfo.FileName = SevenZipPath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Run(() => process.WaitForExit());

            return process.ExitCode != 0 
                ? throw new Exception($"7z command failed with exit code {process.ExitCode}: {error}") 
                : output.ToString();
        }

        public static async Task ExtractArchive(string archivePath, string destinationPath, string password = null)
        {
            // Use 7z executable for extraction - much faster than SharpCompress for large archives
            var arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y";
            if (!string.IsNullOrEmpty(password))
            {
                arguments += $" -p\"{password}\"";
            }

            // Add multi-threading flag for 7z (uses all CPU cores)
            arguments += $" -mmt{Environment.ProcessorCount}";

            Console.WriteLine($"Extracting {archivePath} to {destinationPath} using 7z executable...");
            await RunSevenZipCommand(arguments);
            Console.WriteLine("Extraction complete");
        }

        public static async Task CreateArchive(string archivePath, string sourcePath)
        {
            // Use fast compression (-mx1) to match original behavior - much faster for large game files
            // -y flag overwrites without prompting
            var arguments = $"a -mx1 -y \"{archivePath}\" \"{sourcePath}\"";

            Console.WriteLine($"Creating archive {archivePath} from {sourcePath}...");
            await RunSevenZipCommand(arguments);
            Console.WriteLine("Archive created successfully");
        }
    }
}