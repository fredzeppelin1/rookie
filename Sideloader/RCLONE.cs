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

namespace AndroidSideloader.Sideloader;

public class Rclone
{
    private static readonly SettingsManager Settings = SettingsManager.Instance;
    private static IDialogService _dialogService;

    // Public config management
    public static PublicConfig PublicConfigFile { get; set; }
    public static bool HasPublicConfig { get; set; }
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
        var parentProcessId = Environment.ProcessId;
        var processes = Process.GetProcessesByName("rclone");

        foreach (var process in processes)
        {
            try
            {
                // Try to check if this rclone process is a child of the current process
                var processParentId = GetParentProcessId(process);

                if (processParentId == null || processParentId == parentProcessId)
                {
                    // Either we can't determine parent (kill it anyway to be safe)
                    // Or it's confirmed to be a child process of Rookie
                    Logger.Log($"Killing rclone process (PID: {process.Id})");
                    process.Kill();
                }
                else
                {
                    Logger.Log($"Skipping rclone process (PID: {process.Id}) - not a child of Rookie");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception occurred while attempting to shut down RCLONE with exception message: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Get the parent process ID of a process (cross-platform)
    /// Returns null if unable to determine
    /// </summary>
    private static int? GetParentProcessId(Process process)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On Unix-like systems, read from /proc/{pid}/stat
                var statPath = $"/proc/{process.Id}/stat";
                if (File.Exists(statPath))
                {
                    var stat = File.ReadAllText(statPath);
                    // stat format: pid (name) state ppid ...
                    // Need to parse carefully because name can contain spaces and parentheses
                    var lastParen = stat.LastIndexOf(')');
                    if (lastParen > 0 && lastParen < stat.Length - 1)
                    {
                        var afterName = stat.Substring(lastParen + 1).Trim();
                        var parts = afterName.Split(' ');
                        if (parts.Length > 1 && int.TryParse(parts[1], out var ppid))
                        {
                            return ppid;
                        }
                    }
                }
            }
            // Note: On Windows, we would need System.Management or P/Invoke to get parent PID
            // For now, returning null on Windows means we'll kill all rclone processes
            // This matches the behavior before this fix
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to get parent process ID: {ex.Message}", LogLevel.Warning);
        }

        return null; // Unable to determine parent
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

    /// <summary>
    /// Execute an rclone process with customizable command building and error handling
    /// </summary>
    private static async Task<ProcessOutput> ExecuteRcloneProcess(
        string command,
        Func<string, string> buildCommandArgs,
        Func<Task<ProcessOutput>> preExecutionCheck = null,
        Func<string, ProcessOutput, Task<ProcessOutput>> postProcessOutput = null)
    {
        // Pre-execution check (e.g., offline mode)
        if (preExecutionCheck != null)
        {
            var checkResult = await preExecutionCheck();
            if (checkResult != null)
            {
                return checkResult; // Early return if check fails
            }
        }

        // Build full command with config/flags
        var fullCommand = buildCommandArgs(command);

        // Sanitize paths for logging
        var logCommand = fullCommand;
        if (logCommand.Contains($"\"{Settings.CurrentLogPath}\""))
        {
            logCommand = logCommand.Replace($"\"{Settings.CurrentLogPath}\"", "\"Logs\"");
        }
        if (logCommand.Contains(AppDomain.CurrentDomain.BaseDirectory))
        {
            logCommand = logCommand.Replace($"{AppDomain.CurrentDomain.BaseDirectory}", "CurrentDirectory");
        }

        Logger.Log($"Running Rclone command: {logCommand}");

        // Setup process
        var rclone = new Process();
        rclone.StartInfo.FileName = GetRcloneExecutablePath();
        rclone.StartInfo.Arguments = fullCommand;
        rclone.StartInfo.StandardOutputEncoding = Encoding.UTF8; // Unicode support
        rclone.StartInfo.RedirectStandardError = true;
        rclone.StartInfo.RedirectStandardOutput = true;
        rclone.StartInfo.WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone");
        rclone.StartInfo.CreateNoWindow = !DebugMode;
        rclone.StartInfo.UseShellExecute = false;

        // Use event-based reading for real-time progress updates
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        rclone.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        rclone.ErrorDataReceived += (_, e) =>
        {
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

        // Execute process
        rclone.Start();
        rclone.BeginOutputReadLine();
        rclone.BeginErrorReadLine();
        await rclone.WaitForExitAsync();

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        var result = new ProcessOutput(output, error);

        // Post-process output (error handling, retries, etc.)
        if (postProcessOutput != null)
        {
            result = await postProcessOutput(command, result);
        }

        // Log output if not suppressed
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

        return result;
    }

    // Run an RCLONE Command that accesses the Download Config.
    public static async Task<ProcessOutput> runRcloneCommand_DownloadConfig(string command)
    {
        var originalCommand = command;

        return await ExecuteRcloneProcess(
            command,
            // Build command with download config and flags
            cmd =>
            {
                if (DownloadConfigPath.Length > 0)
                {
                    cmd += $" --config {DownloadConfigPath}";
                }

                cmd += " --inplace";

                if (Settings.BandwidthLimit > 0)
                {
                    cmd += $" --bwlimit {Settings.BandwidthLimit}M";
                }

                if (Settings.LogRclone)
                {
                    cmd += " -vv";
                }

                if (_rclonepw.Length > 0)
                {
                    cmd += " --ask-password=false";
                }

                return cmd;
            },
            // Pre-execution check: offline mode
            async () =>
            {
                if (IsOffline)
                {
                    Logger.Log("Offline mode is enabled - skipping rclone download command");
                    return new ProcessOutput("", "No internet - Offline mode enabled");
                }
                await Task.CompletedTask;
                return null; // Continue execution
            },
            // Post-process: handle disk space and mirror switching
            async (_, result) =>
            {
                if (result.Error.Contains("There is not enough space"))
                {
                    Logger.Log($"NOT ENOUGH SPACE: {result.Error}");
                    await ShowInsufficientDiskSpaceWarning(Settings.DownloadDir);
                    return new ProcessOutput("Download failed.", "Insufficient disk space");
                }

                // Switch mirror upon matching error output
                if (result.Error.Contains("400 Bad Request") || result.Error.Contains("cannot fetch token") ||
                    result.Error.Contains("authError") || result.Error.Contains("quota") ||
                    result.Error.Contains("exceeded") || result.Error.Contains("directory not found") ||
                    result.Error.Contains("Failed to"))
                {
                    Logger.Log($"Mirror error detected: {result.Error}");

                    var currentRemote = ExtractRemoteFromCommand(originalCommand);

                    if (!string.IsNullOrEmpty(currentRemote) && SideloaderRclone.RemotesList.Count > 0)
                    {
                        var currentIndex = SideloaderRclone.RemotesList.IndexOf(currentRemote);
                        if (currentIndex >= 0 && currentIndex < SideloaderRclone.RemotesList.Count - 1)
                        {
                            var nextRemote = SideloaderRclone.RemotesList[currentIndex + 1];
                            Logger.Log($"Switching from mirror '{currentRemote}' to '{nextRemote}'");

                            var newCommand = originalCommand.Replace($"{currentRemote}:", $"{nextRemote}:");
                            return await runRcloneCommand_DownloadConfig(newCommand);
                        }

                        Logger.Log("No more mirrors available to try");
                        return new ProcessOutput("All mirrors are on quota or down...", "All mirrors are on quota or down...");
                    }
                }

                return result;
            });
    }

    public static async Task<ProcessOutput> runRcloneCommand_UploadConfig(string command)
    {
        return await ExecuteRcloneProcess(
            command,
            // Build command with upload config and flags
            cmd =>
            {
                if (UploadConfigPath.Length > 0)
                {
                    cmd += $" --config {UploadConfigPath}";
                }

                cmd += " --checkers 1 --retries 2 --inplace";

                if (Settings.BandwidthLimit > 0)
                {
                    cmd += $" --bwlimit {Settings.BandwidthLimit}M";
                }

                if (Settings.LogRclone)
                {
                    cmd += " -vv";
                }

                return cmd;
            },
            // No pre-execution check
            null,
            // Post-process: handle upload errors
            async (_, result) =>
            {
                if (result.Error.Contains("400 Bad Request") || result.Error.Contains("cannot fetch token") ||
                    result.Error.Contains("authError") || result.Error.Contains("quota") ||
                    result.Error.Contains("exceeded") || result.Error.Contains("directory not found") ||
                    result.Error.Contains("Failed to"))
                {
                    Logger.Log($"Upload error: {result.Error}");
                    return new ProcessOutput("Upload Failed.", "Upload failed.");
                }

                await Task.CompletedTask;
                return result;
            });
    }

    public static async Task<ProcessOutput> runRcloneCommand_PublicConfig(string command)
    {
        return await ExecuteRcloneProcess(
            command,
            // Build command with public config and flags
            cmd =>
            {
                cmd += " --inplace";

                if (Settings.BandwidthLimit > 0)
                {
                    cmd += $" --bwlimit {Settings.BandwidthLimit}M";
                }

                if (Settings.LogRclone)
                {
                    cmd += " -vv";
                }

                // Add HTTP URL from PublicConfig if available
                if (HasPublicConfig && PublicConfigFile != null && !string.IsNullOrEmpty(PublicConfigFile.BaseUri))
                {
                    // Use = syntax to avoid quote escaping issues on Windows
                    cmd += $" --http-url={PublicConfigFile.BaseUri}";
                    if (!string.IsNullOrEmpty(PublicMirrorExtraArgs))
                    {
                        cmd += $" {PublicMirrorExtraArgs}";
                    }
                }

                return cmd;
            },
            // Pre-execution check: offline mode
            async () =>
            {
                if (IsOffline)
                {
                    Logger.Log("Offline mode is enabled - skipping rclone public command");
                    return new ProcessOutput("", "No internet - Offline mode enabled");
                }
                await Task.CompletedTask;
                return null; // Continue execution
            },
            // Post-process: handle disk space, socket errors, and public mirror errors
            async (_, result) =>
            {
                if (result.Error.Contains("There is not enough space"))
                {
                    Logger.Log($"NOT ENOUGH SPACE: {result.Error}");
                    await ShowInsufficientDiskSpaceWarning(Settings.DownloadDir);
                    return new ProcessOutput("Download failed.", "Insufficient disk space");
                }

                if (result.Error.Contains("Only one usage of each socket address (protocol/network address/port) is normally permitted"))
                {
                    Logger.Log($"Socket error: {result.Error}");
                    return new ProcessOutput("Failed to fetch from public mirror.",
                        "Failed to fetch from public mirror.\nYou may have a running RCLONE Task!\nCheck your Task Manager, Sort by Network Usage, and kill the process Rsync for Cloud Storage/Rclone");
                }

                if (result.Error.Contains("400 Bad Request") || result.Error.Contains("cannot fetch token") ||
                    result.Error.Contains("authError") || result.Error.Contains("quota") ||
                    result.Error.Contains("exceeded") || result.Error.Contains("directory not found") ||
                    result.Error.Contains("Failed to"))
                {
                    Logger.Log($"Public mirror error: {result.Error}");
                    return new ProcessOutput("Failed to fetch from public mirror.", "Failed to fetch from public mirror.");
                }

                return result;
            });
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

    public static async Task MountRemote(string remoteName, string mountPoint)
    {
        var arguments = $"mount \"{remoteName}:\" \"{mountPoint}\" --vfs-cache-mode full";
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
    public static readonly List<string> RemotesList = [];

    public const string RcloneGamesFolder = "Quest Games";

    // Game list array indices
    public const int GameNameIndex = 0;
    public const int ReleaseNameIndex = 1;
    public const int PackageNameIndex = 2;
    public const int VersionCodeIndex = 3;
    public const int ReleaseApkPathIndex = 4;
    public const int VersionNameIndex = 5;
    public const int DownloadsIndex = 6;

    public static readonly List<string[]> Games = [];

    private static readonly string Nouns = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nouns");
    private static readonly string ThumbnailsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "thumbnails");
    public static readonly string NotesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes");

    public static async Task UpdateNouns(string remote)
    {
        Logger.Log("Updating Nouns");
        await Rclone.runRcloneCommand_DownloadConfig($"sync \"{remote}:{RcloneGamesFolder}/.meta/nouns\" \"{Nouns}\"");
    }

    public static async Task UpdateGamePhotos(string remote)
    {
        Logger.Log("Updating Thumbnails");
        await Rclone.runRcloneCommand_DownloadConfig($"sync \"{remote}:{RcloneGamesFolder}/.meta/thumbnails\" \"{ThumbnailsFolder}\" --transfers 10");
    }

    public static async Task UpdateGameNotes(string remote)
    {
        Logger.Log("Updating Game Notes");
        await Rclone.runRcloneCommand_DownloadConfig($"sync \"{remote}:{RcloneGamesFolder}/.meta/notes\" \"{NotesFolder}\"");
    }

    public static async Task UpdateMetadataFromPublic()
    {
        Logger.Log("Downloading Metadata");
        // The :http: backend expects just the filename, the --http-url provides the base URL
        // Quote the :http: source path to prevent Windows cmd.exe from misinterpreting the colon
        // Remove trailing backslash to prevent it from escaping the closing quote on Windows
        var destPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var rCloneCommand = $"copy \":http:meta.7z\" \"{destPath}\"";
        await Rclone.runRcloneCommand_PublicConfig(rCloneCommand);
    }

    public static async Task ProcessMetadataFromPublic()
    {
        try
        {
            Logger.Log("Extracting Metadata");

            // Extract meta.7z with password from PublicConfig
            var archivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta.7z");
            var extractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meta");

            string password = null;
            if (Rclone.HasPublicConfig && Rclone.PublicConfigFile != null && !string.IsNullOrEmpty(Rclone.PublicConfigFile.Password))
            {
                password = Rclone.PublicConfigFile.Password;
                Logger.Log("Using password from PublicConfig for extraction");
            }

            await Zip.ExtractArchive(archivePath, extractPath, password);

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

            Logger.Log("Initializing Games List");
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
        Logger.Log("Initializing Games List");

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
            var gameListSplited = tempGameList.Split('\n');
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

            using var client = new HttpClient();
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

            using var client = new HttpClient();
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
        catch (Exception e)
        {
            Logger.Log($"Failed to update Upload config: {e.Message}");
        }
    }

    /// <summary>
    /// Refresh and populate the RemotesList from rclone config
    /// Filters to only include remotes containing "mirror" in their name
    /// </summary>
    public static async Task RefreshRemotes()
    {
        Logger.Log("Refresh / List Remotes");
        RemotesList.Clear();

        var result = await Rclone.runRcloneCommand_DownloadConfig("listremotes");
        var remotes = result.Output.Split('\n');

        Logger.Log("Loaded following remotes: ");
        foreach (var r in remotes)
        {
            if (r.Length > 1)
            {
                var remote = r.TrimEnd(':').Trim();
                if (remote.Contains("mirror"))
                {
                    Logger.Log(remote);
                    RemotesList.Add(remote);
                }
            }
        }

        Logger.Log($"Total mirrors loaded: {RemotesList.Count}");
    }

    private static string CalculateMd5(string filename)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filename);
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }
}