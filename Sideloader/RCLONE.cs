using AndroidSideloader.Utilities;
using AndroidSideloader.Models;
using AndroidSideloader.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AndroidSideloader.Sideloader
{
    public class Rclone
    {
        private static readonly SettingsManager Settings = SettingsManager.Instance;
        private static IDialogService _dialogService;

        // Public config management
        public static PublicConfig PublicConfigFile { get; set; }
        public static bool HasPublicConfig { get; set; }
        public static bool UsingPublicConfig { get; set; }
        public static string PublicMirrorExtraArgs { get; set; } = "";

        // Mode flags
        public static bool IsOffline { get; set; }
        public static bool DebugMode { get; set; }

        // Progress reporting
        public static Action<RcloneProgress> OnProgress { get; set; }

        // Current game name for progress display
        public static string CurrentGameName { get; set; }

        /// <summary>
        /// Set the dialog service for showing user dialogs
        /// </summary>
        public static void SetDialogService(IDialogService dialogService)
        {
            _dialogService = dialogService;
        }

        // Kill RCLONE Processes that were started from Rookie by looking for child processes.
        public static void KillRclone()
        {
            var parentProcessId = Process.GetCurrentProcess().Id;
            var processes = Process.GetProcessesByName("rclone");

            foreach (var process in processes)
            {
                try
                {
                    // Cross-platform way to check parent process
                    // On macOS/Linux we can't use ManagementObject, so we just kill all rclone processes
                    // This is safer for the cross-platform port
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // On Windows, we could use WMI but would need System.Management package
                        // For now, just kill all rclone processes
                        process.Kill();
                    }
                    else
                    {
                        // On Unix-like systems, kill all rclone processes
                        process.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Exception occurred while attempting to shut down RCLONE with exception message: {ex.Message}");
                }
            }
        }

        // For custom configs that use a password
        public static void Init()
        {
            var pwTxtPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone", "pw.txt");
            if (File.Exists(pwTxtPath))
            {
                _rclonepw = File.ReadAllText(pwTxtPath);
            }
        }

        // Change if you want to use a config
        private const string DownloadConfigPath = "vrp.download.config";
        private const string UploadConfigPath = "vrp.upload.config";
        private static string _rclonepw = "";

        private static string GetRcloneExecutablePath()
        {
            var executableName = "rclone";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                executableName += ".exe";
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone", executableName);
        }

        // Run an RCLONE Command that accesses the Download Config.
        public static async Task<ProcessOutput> runRcloneCommand_DownloadConfig(string command)
        {
            // Check for offline mode
            if (IsOffline)
            {
                Logger.Log("Offline mode is enabled - skipping rclone download command");
                return new ProcessOutput("", "No internet - Offline mode enabled");
            }

            var prcoutput = new ProcessOutput();
            var rclone = new Process();

            // Rclone output is unicode, else it will show garbage instead of unicode characters
            rclone.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            var originalCommand = command;

            // set configpath if there is any
            if (DownloadConfigPath.Length > 0)
            {
                command += $" --config {DownloadConfigPath}";
            }

            command += " --inplace";

            // Add bandwidth limiting if configured
            if (Settings.BandwidthLimit > 0)
            {
                command += $" --bwlimit {Settings.BandwidthLimit}M";
            }

            // set rclonepw
            if (_rclonepw.Length > 0)
            {
                command += " --ask-password=false";
            }

            var logcmd = command;
            if (logcmd.Contains($"\"{Settings.CurrentLogPath}\""))
            {
                logcmd = logcmd.Replace($"\"{Settings.CurrentLogPath}\"", "\"Logs\"");
            }

            if (logcmd.Contains(AppDomain.CurrentDomain.BaseDirectory))
            {
                logcmd = logcmd.Replace($"{AppDomain.CurrentDomain.BaseDirectory}", "CurrentDirectory");
            }

            Logger.Log($"Running Rclone command: {logcmd}");

            rclone.StartInfo.FileName = GetRcloneExecutablePath();
            rclone.StartInfo.Arguments = command;
            rclone.StartInfo.RedirectStandardError = true;
            rclone.StartInfo.RedirectStandardOutput = true;
            rclone.StartInfo.WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone");
            rclone.StartInfo.CreateNoWindow = !DebugMode;  // Show window in debug mode
            rclone.StartInfo.UseShellExecute = false;

            // Use event-based reading for real-time progress updates
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            rclone.OutputDataReceived += (_, e) => {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            rclone.ErrorDataReceived += (_, e) => {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);

                    // Parse progress from stderr (rclone writes progress to stderr)
                    var progress = RcloneProgress.ParseFromOutput(e.Data);
                    if (progress != null && OnProgress != null)
                    {
                        OnProgress(progress);
                    }
                }
            };

            rclone.Start();
            rclone.BeginOutputReadLine();
            rclone.BeginErrorReadLine();
            await rclone.WaitForExitAsync();

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (error.Contains("There is not enough space"))
            {
                Logger.Log($"NOT ENOUGH SPACE: {error}");
                await ShowInsufficientDiskSpaceWarning(Settings.DownloadDir);
                return new ProcessOutput("Download failed.", "Insufficient disk space");
            }

            // Switch mirror upon matching error output.
            if (error.Contains("400 Bad Request") || error.Contains("cannot fetch token") || error.Contains("authError") || error.Contains("quota") || error.Contains("exceeded") || error.Contains("directory not found") || error.Contains("Failed to"))
            {
                Logger.Log($"Mirror error detected: {error}");

                // Extract remote name from command if possible
                var currentRemote = ExtractRemoteFromCommand(originalCommand);

                if (!string.IsNullOrEmpty(currentRemote) && SideloaderRclone.RemotesList.Count > 0)
                {
                    // Try to switch to next available mirror
                    var currentIndex = SideloaderRclone.RemotesList.IndexOf(currentRemote);
                    if (currentIndex >= 0 && currentIndex < SideloaderRclone.RemotesList.Count - 1)
                    {
                        var nextRemote = SideloaderRclone.RemotesList[currentIndex + 1];
                        Logger.Log($"Switching from mirror '{currentRemote}' to '{nextRemote}'");

                        // Retry command with new remote
                        var newCommand = originalCommand.Replace($"{currentRemote}:", $"{nextRemote}:");
                        prcoutput = await runRcloneCommand_DownloadConfig(newCommand);
                        return prcoutput;
                    }

                    Logger.Log("No more mirrors available to try");
                    return new ProcessOutput("All mirrors are on quota or down...", "All mirrors are on quota or down...");
                }
            }

            prcoutput.Output = output;
            prcoutput.Error = error;

            if (!output.Contains("Game Name;Release Name;") && !output.Contains("package:") && !output.Contains(".meta"))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.Log($"Rclone error: {error}");
                }

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Logger.Log($"Rclone Output: {output}");
                }
            }
            return prcoutput;
        }

        public static async Task<ProcessOutput> runRcloneCommand_UploadConfig(string command)
        {
            var processOutput = new ProcessOutput();
            var rclone = new Process();

            // Rclone output is unicode, else it will show garbage instead of unicode characters
            rclone.StartInfo.StandardOutputEncoding = Encoding.UTF8;

            // set config path if there is any
            if (UploadConfigPath.Length > 0)
            {
                command += $" --config {UploadConfigPath}";
            }

            var logcmd = command;
            if (logcmd.Contains($"\"{Settings.CurrentLogPath}\""))
            {
                logcmd = logcmd.Replace($"\"{Settings.CurrentLogPath}\"", "\"Logs\"");
            }

            if (logcmd.Contains(AppDomain.CurrentDomain.BaseDirectory))
            {
                logcmd = logcmd.Replace($"{AppDomain.CurrentDomain.BaseDirectory}", "CurrentDirectory");
            }

            Logger.Log($"Running Rclone command: {logcmd}");

            command += " --checkers 1 --retries 2 --inplace";

            // Add bandwidth limiting if configured
            if (Settings.BandwidthLimit > 0)
            {
                command += $" --bwlimit {Settings.BandwidthLimit}M";
            }

            rclone.StartInfo.FileName = GetRcloneExecutablePath();
            rclone.StartInfo.Arguments = command;
            rclone.StartInfo.RedirectStandardError = true;
            rclone.StartInfo.RedirectStandardOutput = true;
            rclone.StartInfo.WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone");
            rclone.StartInfo.CreateNoWindow = !DebugMode;  // Show window in debug mode
            rclone.StartInfo.UseShellExecute = false;

            // Use event-based reading for real-time progress updates
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            rclone.OutputDataReceived += (_, e) => {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            rclone.ErrorDataReceived += (_, e) => {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);

                    // Parse progress from stderr (rclone writes progress to stderr)
                    var progress = RcloneProgress.ParseFromOutput(e.Data);
                    if (progress != null && OnProgress != null)
                    {
                        OnProgress(progress);
                    }
                }
            };

            rclone.Start();
            rclone.BeginOutputReadLine();
            rclone.BeginErrorReadLine();
            await rclone.WaitForExitAsync();

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            // if there is one of these errors, we switch the mirrors
            if (error.Contains("400 Bad Request") || error.Contains("cannot fetch token") || error.Contains("authError") || error.Contains("quota") || error.Contains("exceeded") || error.Contains("directory not found") || error.Contains("Failed to"))
            {
                Logger.Log($"Upload error: {error}");
                return new ProcessOutput("Upload Failed.", "Upload failed.");
            }
            else
            {
                processOutput.Output = output;
                processOutput.Error = error;
            }

            if (!output.Contains("Game Name;Release Name;") && !output.Contains("package:") && !output.Contains(".meta"))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.Log($"Rclone error: {error}");
                }

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Logger.Log($"Rclone Output: {output}");
                }
            }
            return processOutput;
        }

        public static async Task<ProcessOutput> runRcloneCommand_PublicConfig(string command)
        {
            // Check for offline mode
            if (IsOffline)
            {
                Logger.Log("Offline mode is enabled - skipping rclone public command");
                return new ProcessOutput("", "No internet - Offline mode enabled");
            }

            var prcoutput = new ProcessOutput();
            var rclone = new Process();

            // Rclone output is unicode, else it will show garbage instead of unicode characters
            rclone.StartInfo.StandardOutputEncoding = Encoding.UTF8;

            var logcmd = command;
            if (logcmd.Contains($"\"{Settings.CurrentLogPath}\""))
            {
                logcmd = logcmd.Replace($"\"{Settings.CurrentLogPath}\"", "\"Logs\"");
            }

            if (logcmd.Contains(AppDomain.CurrentDomain.BaseDirectory))
            {
                logcmd = logcmd.Replace($"{AppDomain.CurrentDomain.BaseDirectory}", "CurrentDirectory");
            }

            command += " --inplace";

            // Add bandwidth limiting if configured
            if (Settings.BandwidthLimit > 0)
            {
                command += $" --bwlimit {Settings.BandwidthLimit}M";
            }

            // Add HTTP URL from PublicConfig if available
            if (HasPublicConfig && PublicConfigFile != null && !string.IsNullOrEmpty(PublicConfigFile.BaseUri))
            {
                command += $" --http-url {PublicConfigFile.BaseUri}";
                if (!string.IsNullOrEmpty(PublicMirrorExtraArgs))
                {
                    command += $" {PublicMirrorExtraArgs}";
                }
            }

            Logger.Log($"Running Rclone command: {logcmd}");

            rclone.StartInfo.FileName = GetRcloneExecutablePath();
            rclone.StartInfo.Arguments = command;
            rclone.StartInfo.RedirectStandardError = true;
            rclone.StartInfo.RedirectStandardOutput = true;
            rclone.StartInfo.WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone");
            rclone.StartInfo.CreateNoWindow = !DebugMode;  // Show window in debug mode
            rclone.StartInfo.UseShellExecute = false;

            // Use event-based reading for real-time progress updates
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            rclone.OutputDataReceived += (_, e) => {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            rclone.ErrorDataReceived += (_, e) => {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);

                    // Parse progress from stderr (rclone writes progress to stderr)
                    var progress = RcloneProgress.ParseFromOutput(e.Data);
                    if (progress != null && OnProgress != null)
                    {
                        OnProgress(progress);
                    }
                }
            };

            rclone.Start();
            rclone.BeginOutputReadLine();
            rclone.BeginErrorReadLine();
            await rclone.WaitForExitAsync();

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (error.Contains("There is not enough space"))
            {
                Logger.Log($"NOT ENOUGH SPACE: {error}");
                await ShowInsufficientDiskSpaceWarning(Settings.DownloadDir);
                return new ProcessOutput("Download failed.", "Insufficient disk space");
            }

            if (error.Contains("Only one usage of each socket address (protocol/network address/port) is normally permitted"))
            {
                Logger.Log($"Socket error: {error}");
                return new ProcessOutput("Failed to fetch from public mirror.", "Failed to fetch from public mirror.\nYou may have a running RCLONE Task!\nCheck your Task Manager, Sort by Network Usage, and kill the process Rsync for Cloud Storage/Rclone");
            }

            if (error.Contains("400 Bad Request")
                || error.Contains("cannot fetch token")
                || error.Contains("authError")
                || error.Contains("quota")
                || error.Contains("exceeded")
                || error.Contains("directory not found")
                || error.Contains("Failed to"))
            {
                Logger.Log($"Public mirror error: {error}");
                return new ProcessOutput("Failed to fetch from public mirror.", "Failed to fetch from public mirror.");
            }

            prcoutput.Output = output;
            prcoutput.Error = error;

            if (!output.Contains("Game Name;Release Name;") && !output.Contains("package:") && !output.Contains(".meta"))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.Log($"Rclone error: {error}");
                }

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Logger.Log($"Rclone Output: {output}");
                }
            }

            return prcoutput;
        }

        // Helper methods for ViewModel compatibility
        public static async Task DownloadPublicConfig()
        {
            try
            {
                const string configUrl = "https://raw.githubusercontent.com/vrpyou/quest/main/vrp-public.json";
                const string fallbackUrl = "https://vrpirates.wiki/downloads/vrp-public.json";
                var publicConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vrp-public.json");

                using var client = new HttpClient();
                try
                {
                    // Try main URL first
                    var response = await client.GetAsync(configUrl);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    await File.WriteAllTextAsync(publicConfigPath, json);
                    Logger.Log($"Downloaded public config from: {configUrl}");
                }
                catch (Exception mainEx)
                {
                    // Try fallback URL
                    Logger.Log($"Failed to download from main URL: {mainEx.Message}, trying fallback");
                    var response = await client.GetAsync(fallbackUrl);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    await File.WriteAllTextAsync(publicConfigPath, json);
                    Logger.Log($"Downloaded public config from fallback: {fallbackUrl}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to download public config: {ex.Message}");
                // Continue without public config - app should still work with local files
            }
        }

        public static async Task<List<string>> ListRemotes()
        {
            var arguments = "listremotes";
            if (!string.IsNullOrEmpty(DownloadConfigPath))
            {
                arguments += $" --config {DownloadConfigPath}";
            }

            try
            {
                var output = await runRcloneCommand_DownloadConfig(arguments);
                var remotes = output.Output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Select(r => r.TrimEnd(':'))
                    .ToList();
                return remotes;
            }
            catch (Exception ex)
            {
                Logger.Log($"ListRemotes failed: {ex.Message}");
                return [];
            }
        }

        public static async Task<List<string>> ListDir(string remotePath)
        {
            var arguments = $"lsd \"{remotePath}\"";
            if (!string.IsNullOrEmpty(DownloadConfigPath))
            {
                arguments += $" --config {DownloadConfigPath}";
            }

            try
            {
                var output = await runRcloneCommand_DownloadConfig(arguments);
                return output.Output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Log($"ListDir failed for '{remotePath}': {ex.Message}");
                return [];
            }
        }

        public static async Task MountRemote(string remoteName, string mountPoint)
        {
            var arguments = $"mount \"{remoteName}:\" \"{mountPoint}\" --vfs-cache-mode full";
            if (!string.IsNullOrEmpty(DownloadConfigPath))
            {
                arguments += $" --config {DownloadConfigPath}";
            }
            await runRcloneCommand_DownloadConfig(arguments);
        }

        public static async Task CopyFile(string sourcePath, string destinationPath)
        {
            var arguments = $"copy \"{sourcePath}\" \"{destinationPath}\"";
            if (!string.IsNullOrEmpty(DownloadConfigPath))
            {
                arguments += $" --config {DownloadConfigPath}";
            }
            await runRcloneCommand_DownloadConfig(arguments);
        }

        /// <summary>
        /// Extract remote name from rclone command
        /// </summary>
        private static string ExtractRemoteFromCommand(string command)
        {
            try
            {
                // Look for pattern like "remotename:" in the command
                // Commands typically look like: copy "mirror1:Quest Games/..." "destination"
                var startQuote = command.IndexOf('"');
                if (startQuote >= 0)
                {
                    var colonIndex = command.IndexOf(':', startQuote);
                    if (colonIndex > startQuote)
                    {
                        var remotePart = command.Substring(startQuote + 1, colonIndex - startQuote - 1);
                        return remotePart.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to extract remote from command: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Show insufficient disk space warning dialog
        /// </summary>
        private static async Task ShowInsufficientDiskSpaceWarning(string downloadDir)
        {
            Logger.Log($"Insufficient disk space in {downloadDir}", LogLevel.Error);

            if (_dialogService != null)
            {
                await _dialogService.ShowErrorAsync(
                    $"There isn't enough disk space to download this game.\n\nPlease ensure you have at least 2x the game size available in {downloadDir} and try again.",
                    "NOT ENOUGH SPACE");
            }
        }
    }

    // Main game management class
    public class SideloaderRclone
    {
        public static List<string> RemotesList = [];

        public static string RcloneGamesFolder = "Quest Games";

        // Game list array indices
        public static int GameNameIndex = 0;
        public static int ReleaseNameIndex = 1;
        public static int PackageNameIndex = 2;
        public static int VersionCodeIndex = 3;
        public static int ReleaseApkPathIndex = 4;
        public static int VersionNameIndex = 5;
        public static int DownloadsIndex = 6;

        public static List<string> GameProperties = [];
        public static List<string[]> Games = [];

        public static string Nouns = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nouns");
        public static string ThumbnailsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "thumbnails");
        public static string NotesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes");

        public static async Task UpdateNouns(string remote)
        {
            Logger.Log($"Updating Nouns");
            await Rclone.runRcloneCommand_DownloadConfig($"sync \"{remote}:{RcloneGamesFolder}/.meta/nouns\" \"{Nouns}\"");
        }

        public static async Task UpdateGamePhotos(string remote)
        {
            Logger.Log($"Updating Thumbnails");
            await Rclone.runRcloneCommand_DownloadConfig($"sync \"{remote}:{RcloneGamesFolder}/.meta/thumbnails\" \"{ThumbnailsFolder}\" --transfers 10");
        }

        public static async Task UpdateGameNotes(string remote)
        {
            Logger.Log($"Updating Game Notes");
            await Rclone.runRcloneCommand_DownloadConfig($"sync \"{remote}:{RcloneGamesFolder}/.meta/notes\" \"{NotesFolder}\"");
        }

        public static async Task UpdateMetadataFromPublic()
        {
            Logger.Log("Downloading Metadata");
            // The :http: backend expects just the filename, the --http-url provides the base URL
            var rclonecommand = $"copy :http:meta.7z \"{AppDomain.CurrentDomain.BaseDirectory}\"";
            await Rclone.runRcloneCommand_PublicConfig(rclonecommand);
        }

        public static async Task ProcessMetadataFromPublic()
        {
            try
            {
                Logger.Log($"Extracting Metadata");

                // Extract meta.7z with password from PublicConfig
                var archivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta.7z");
                var extractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta");

                string password = null;
                if (Rclone.HasPublicConfig && Rclone.PublicConfigFile != null && !string.IsNullOrEmpty(Rclone.PublicConfigFile.Password))
                {
                    password = Rclone.PublicConfigFile.Password;
                    Logger.Log("Using password from PublicConfig for extraction");
                }

                await SevenZip.ExtractArchive(archivePath, extractPath, password);

                Logger.Log("Updating Metadata");

                if (Directory.Exists(Nouns))
                {
                    Directory.Delete(Nouns, true);
                }

                if (Directory.Exists(ThumbnailsFolder))
                {
                    Directory.Delete(ThumbnailsFolder, true);
                }

                if (Directory.Exists(NotesFolder))
                {
                    Directory.Delete(NotesFolder, true);
                }

                Directory.Move(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta", ".meta", "nouns"), Nouns);
                Directory.Move(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta", ".meta", "thumbnails"), ThumbnailsFolder);
                Directory.Move(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta", ".meta", "notes"), NotesFolder);

                Logger.Log($"Initializing Games List");
                var gameList = await File.ReadAllTextAsync(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta", "VRP-GameList.txt"));

                var splitList = gameList.Split('\n');
                splitList = splitList.Skip(1).ToArray();
                foreach (var game in splitList)
                {
                    if (game.Length > 1)
                    {
                        var splitGame = game.Split(';');
                        Games.Add(splitGame);
                    }
                }

                Directory.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta"), true);
            }
            catch (Exception e)
            {
                Logger.Log(e.Message);
                Logger.Log(e.StackTrace);
            }
        }

        public static void InitGames(string remote)
        {
            Logger.Log($"Initializing Games List");

            GameProperties.Clear();
            Games.Clear();
            var tempGameList = Rclone.runRcloneCommand_DownloadConfig($"cat \"{remote}:{RcloneGamesFolder}/VRP-GameList.txt\"").Result.Output;

            // Save to file in debug mode for inspection
            if (Rclone.DebugMode)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VRP-GamesList.txt"), tempGameList);
                Logger.Log("Saved game list to VRP-GamesList.txt (debug mode)");
            }

            if (!tempGameList.Equals(""))
            {
                var gameListSplited = tempGameList.Split(new[] { '\n' });
                gameListSplited = gameListSplited.Skip(1).ToArray();
                foreach (var game in gameListSplited)
                {
                    if (game.Length > 1)
                    {
                        var splitGame = game.Split(';');
                        Games.Add(splitGame);
                    }
                }
            }
        }

        public static async Task UpdateDownloadConfig()
        {
            Logger.Log("Attempting to Update Download Config");

            const string downloadConfigFilename = "vrp.download.config";

            try
            {
                const string configUrl = $"https://vrpirates.wiki/downloads/{downloadConfigFilename}";

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(configUrl);
                    response.EnsureSuccessStatusCode();
                    var resultString = await response.Content.ReadAsStringAsync();

                    Logger.Log($"Retrieved updated config from: {configUrl}");

                    var rcloneDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone");
                    if (!Directory.Exists(rcloneDir))
                    {
                        Directory.CreateDirectory(rcloneDir);
                    }

                    var newConfigPath = Path.Combine(rcloneDir, $"{downloadConfigFilename}_new");
                    if (File.Exists(newConfigPath))
                    {
                        File.Delete(newConfigPath);
                    }

                    await File.WriteAllTextAsync(newConfigPath, resultString);

                    var hashPath = Path.Combine(rcloneDir, "hash.txt");
                    if (!File.Exists(hashPath))
                    {
                        await File.WriteAllTextAsync(hashPath, string.Empty);
                    }

                    var newConfig = CalculateMd5(newConfigPath);
                    var oldConfig = await File.ReadAllTextAsync(hashPath);

                    var configPath = Path.Combine(rcloneDir, downloadConfigFilename);
                    if (!File.Exists(configPath))
                    {
                        oldConfig = "Config Doesnt Exist!";
                    }

                    Logger.Log($"Online Config Hash: {newConfig}; Local Config Hash: {oldConfig}");

                    if (newConfig != oldConfig)
                    {
                        Logger.Log("Updated Config Hash is different than the current Config. Updating Configuration File.");

                        if (File.Exists(configPath))
                        {
                            File.Delete(configPath);
                        }

                        File.Move(newConfigPath, configPath);
                        await File.WriteAllTextAsync(hashPath, newConfig);
                    }
                    else
                    {
                        Logger.Log("Updated Config Hash matches last download. Not updating.");

                        if (File.Exists(newConfigPath))
                        {
                            File.Delete(newConfigPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to update download config: {ex.Message}");
            }
        }

        public static async Task UpdateUploadConfig()
        {
            Logger.Log("Attempting to Update Upload Config");
            try
            {
                const string configUrl = "https://vrpirates.wiki/downloads/vrp.upload.config";

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(configUrl);
                    response.EnsureSuccessStatusCode();
                    var resultString = await response.Content.ReadAsStringAsync();

                    Logger.Log($"Retrieved updated config from: {configUrl}");

                    var rcloneDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone");
                    if (!Directory.Exists(rcloneDir))
                    {
                        Directory.CreateDirectory(rcloneDir);
                    }

                    await File.WriteAllTextAsync(Path.Combine(rcloneDir, "vrp.upload.config"), resultString);

                    Logger.Log("Upload config updated successfully.");
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to update Upload config: {e.Message}");
            }
        }

        private static string CalculateMd5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return Convert.ToHexStringLower(hash);
                }
            }
        }
    }
}
