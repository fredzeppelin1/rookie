using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AndroidSideloader.Models;
using AndroidSideloader.Services;
using AndroidSideloader.Sideloader;
using AndroidSideloader.Utilities;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;

namespace AndroidSideloader.ViewModels;

public enum GameFilterType
{
    UpToDate,
    UpdateAvailable,
    NewerThanList
}

public class MainViewModel : ReactiveObject
{
    private readonly IDialogService _dialogService;
    private readonly YouTubeTrailerService _youtubeService;
    private readonly WebViewTrailerPlayerService _trailerPlayerService;

    private string _searchText;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    private ObservableCollection<GameItem> _games;
    public ObservableCollection<GameItem> Games
    {
        get => _games;
        set => this.RaiseAndSetIfChanged(ref _games, value);
    }

    private ObservableCollection<UploadGame> _uploadQueue;
    public ObservableCollection<UploadGame> UploadQueue
    {
        get => _uploadQueue;
        set => this.RaiseAndSetIfChanged(ref _uploadQueue, value);
    }

    public ObservableCollection<string> GamesQueue { get; }
    public ObservableCollection<string> Remotes { get; }
    public ObservableCollection<string> Devices { get; }
    public ObservableCollection<string> InstalledAppNames { get; }

    // Track if a download is currently in progress
    private bool _isDownloading;

    // Track retry attempts for downloads (game name -> retry count)
    private readonly Dictionary<string, int> _downloadRetryCount = new();
    private const int MaxDownloadRetries = 3;

    private string _selectedQueueItem;
    public string SelectedQueueItem
    {
        get => _selectedQueueItem;
        set => this.RaiseAndSetIfChanged(ref _selectedQueueItem, value);
    }

    private string _selectedInstalledApp;
    public string SelectedInstalledApp
    {
        get => _selectedInstalledApp;
        set => this.RaiseAndSetIfChanged(ref _selectedInstalledApp, value);
    }

    private GameItem _selectedGame;
    public GameItem SelectedGame
    {
        get => _selectedGame;
        set => this.RaiseAndSetIfChanged(ref _selectedGame, value);
    }

    private string _selectedRemote;
    public string SelectedRemote
    {
        get => _selectedRemote;
        set => this.RaiseAndSetIfChanged(ref _selectedRemote, value);
    }

    private string _selectedDevice;
    public string SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDevice, value);
            // Update ADB.DeviceID when device selection changes
            if (!string.IsNullOrEmpty(value) && value != "Select your device")
            {
                Adb.DeviceId = value;
                Logger.Log($"Device selected: {value}");
                // Refresh device info (fire-and-forget to avoid blocking UI thread)
                _ = RefreshDeviceInfoAsync();
            }
        }
    }

    // Properties for dynamic labels and text boxes
    private string _speedText = "DLS: Speed in MBPS";
    public string SpeedText
    {
        get => _speedText;
        set => this.RaiseAndSetIfChanged(ref _speedText, value);
    }

    private string _etaText = "ETA: HH:MM:SS Left";
    public string EtaText
    {
        get => _etaText;
        set => this.RaiseAndSetIfChanged(ref _etaText, value);
    }

    private string _progressStatusText = "";
    public string ProgressStatusText
    {
        get => _progressStatusText;
        set => this.RaiseAndSetIfChanged(ref _progressStatusText, value);
    }

    private string _gameNotesText = "";
    public string GameNotesText
    {
        get => _gameNotesText;
        set => this.RaiseAndSetIfChanged(ref _gameNotesText, value);
    }

    private Bitmap _selectedGameImage;
    public Bitmap SelectedGameImage
    {
        get => _selectedGameImage;
        set => this.RaiseAndSetIfChanged(ref _selectedGameImage, value);
    }

    // Trailer video playback properties
    private bool _trailersOn;
    public bool TrailersOn
    {
        get => _trailersOn;
        set
        {
            this.RaiseAndSetIfChanged(ref _trailersOn, value);
            this.RaisePropertyChanged(nameof(ShowThumbnail));
            this.RaisePropertyChanged(nameof(ShowVideoPlayer));
        }
    }

    private bool _isVideoPlaying;
    public bool IsVideoPlaying
    {
        get => _isVideoPlaying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isVideoPlaying, value);
            this.RaisePropertyChanged(nameof(ShowThumbnail));
            this.RaisePropertyChanged(nameof(ShowVideoPlayer));
        }
    }

    // Show thumbnail when trailers are off OR when video hasn't started playing yet
    public bool ShowThumbnail => !TrailersOn || !IsVideoPlaying;

    // Show video player only when trailers are on AND video is playing
    public bool ShowVideoPlayer => TrailersOn && IsVideoPlaying;

    // TrailerPlayerService is exposed so MainWindow can set the WebView reference
    public WebViewTrailerPlayerService TrailerPlayerService => _trailerPlayerService;

    private double _progressPercentage;
    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => this.RaiseAndSetIfChanged(ref _progressPercentage, value);
    }

    private bool _isProgressVisible;
    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        set => this.RaiseAndSetIfChanged(ref _isProgressVisible, value);
    }

    private string _uploadStatusText = "Uploading to VRP...";
    public string UploadStatusText
    {
        get => _uploadStatusText;
        set => this.RaiseAndSetIfChanged(ref _uploadStatusText, value);
    }

    private string _versionText = "";
    public string VersionText
    {
        get => _versionText;
        set => this.RaiseAndSetIfChanged(ref _versionText, value);
    }

    private string _batteryLevelText = "";
    public string BatteryLevelText
    {
        get => _batteryLevelText;
        set => this.RaiseAndSetIfChanged(ref _batteryLevelText, value);
    }

    private string _diskSpaceText = "Total space: N/A\nUsed space: N/A\nFree space: N/A";
    public string DiskSpaceText
    {
        get => _diskSpaceText;
        set => this.RaiseAndSetIfChanged(ref _diskSpaceText, value);
    }

    private string _adbCommandText = "";
    public string AdbCommandText
    {
        get => _adbCommandText;
        set => this.RaiseAndSetIfChanged(ref _adbCommandText, value);
    }

    private bool _isAdbCommandBoxVisible;
    public bool IsAdbCommandBoxVisible
    {
        get => _isAdbCommandBoxVisible;
        set => this.RaiseAndSetIfChanged(ref _isAdbCommandBoxVisible, value);
    }

    private string _adbCommandBoxLabel = "Enter ADB Command";
    public string AdbCommandBoxLabel
    {
        get => _adbCommandBoxLabel;
        set => this.RaiseAndSetIfChanged(ref _adbCommandBoxLabel, value);
    }

    // Track what mode the command box is in (for handling Enter key differently)
    public enum AdbCommandBoxMode
    {
        AdbCommand,
        WirelessIpEntry
    }

    private AdbCommandBoxMode _adbCommandBoxCurrentMode = AdbCommandBoxMode.AdbCommand;
    public AdbCommandBoxMode AdbCommandBoxCurrentMode
    {
        get => _adbCommandBoxCurrentMode;
        set => this.RaiseAndSetIfChanged(ref _adbCommandBoxCurrentMode, value);
    }

    // Command line argument properties
    private bool IsOffline { get; set; }
    private bool NoAppCheck { get; set; }

    // Private list to hold all games
    private readonly List<GameItem> _allGames;

    // Favorites filter flag
    private bool _showFavoritesOnly;

    private bool ShowFavoritesOnly
    {
        get => _showFavoritesOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showFavoritesOnly, value);
            this.RaisePropertyChanged(nameof(GamesListButtonText)); // Update button text
            _ = ApplyFilters(); // Fire and forget - don't block UI thread
        }
    }

    // Button text that changes based on ShowFavoritesOnly
    public string GamesListButtonText => ShowFavoritesOnly ? "Favorited Games" : "Games List";

    // Status filter flags (mutually exclusive)
    private bool _showUpToDateOnly;

    private bool ShowUpToDateOnly
    {
        get => _showUpToDateOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showUpToDateOnly, value);
            _ = ApplyFilters(); // Fire and forget - don't block UI thread
        }
    }

    private bool _showUpdateAvailableOnly;

    private bool ShowUpdateAvailableOnly
    {
        get => _showUpdateAvailableOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showUpdateAvailableOnly, value);
            _ = ApplyFilters(); // Fire and forget - don't block UI thread
        }
    }

    private bool _showNewerThanListOnly;

    private bool ShowNewerThanListOnly
    {
        get => _showNewerThanListOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showNewerThanListOnly, value);
            _ = ApplyFilters(); // Fire and forget - don't block UI thread
        }
    }

    public ReactiveCommand<string, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> MouseClickCommand { get; }
    public ReactiveCommand<Unit, Unit> MouseDoubleClickCommand { get; }
    public ReactiveCommand<Unit, Unit> GamesQueueMouseClickCommand { get; }

    // Commands for buttons
    public ReactiveCommand<Unit, Unit> SideloadApkCommand { get; }
    public ReactiveCommand<Unit, Unit> ReconnectDeviceCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyObbCommand { get; }
    public ReactiveCommand<Unit, Unit> BackupAdbCommand { get; }
    public ReactiveCommand<Unit, Unit> BackupCommand { get; }
    public ReactiveCommand<Unit, Unit> RestoreCommand { get; }
    public ReactiveCommand<Unit, Unit> GetApkCommand { get; }
    public ReactiveCommand<Unit, Unit> UninstallAppCommand { get; }
    public ReactiveCommand<Unit, Unit> PullAppToDesktopCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyBulkObbCommand { get; }
    public ReactiveCommand<Unit, Unit> AboutCommand { get; }
    public ReactiveCommand<Unit, Unit> SettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> QuestOptionsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDownloadsCommand { get; }
    public ReactiveCommand<Unit, Unit> RunAdbCommand { get; }
    public ReactiveCommand<Unit, Unit> AdbWirelessDisableCommand { get; }
    public ReactiveCommand<Unit, Unit> AdbWirelessEnableCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateGamesCommand { get; }
    public ReactiveCommand<Unit, Unit> ListApkCommand { get; }
    public ReactiveCommand<Unit, Unit> DownloadInstallGameCommand { get; }
    public ReactiveCommand<Unit, Unit> MountCommand { get; }
    public ReactiveCommand<Unit, Unit> FavoriteCommand { get; }
    public ReactiveCommand<Unit, Unit> FilterUpToDateCommand { get; }
    public ReactiveCommand<Unit, Unit> FilterUpdateAvailableCommand { get; }
    public ReactiveCommand<Unit, Unit> FilterNewerThanListCommand { get; }
    public ReactiveCommand<Unit, Unit> ProcessUploadQueueCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelDownloadCommand { get; }
    public ReactiveCommand<Unit, Unit> DisableSideloadingCommand { get; }
    public ReactiveCommand<Unit, Unit> GamesListCommand { get; }

    // Status counts - dynamically calculated from game list
    public int UpToDateCount => _allGames.Count(g => g.IsInstalled && !g.HasUpdate);
    public int UpdateAvailableCount => _allGames.Count(g => g.HasUpdate);
    public int NewerThanListCount => _allGames.Count(g => g.IsInstalled && g.InstalledVersionCode > g.AvailableVersionCode && g.AvailableVersionCode > 0);

    /// <summary>
    /// Notify UI that status counts have changed
    /// </summary>
    private void NotifyStatusCountsChanged()
    {
        this.RaisePropertyChanged(nameof(UpToDateCount));
        this.RaisePropertyChanged(nameof(UpdateAvailableCount));
        this.RaisePropertyChanged(nameof(NewerThanListCount));
    }

    public MainViewModel(bool showUpdateAvailableOnly, IDialogService dialogService = null)
    {
        _showUpdateAvailableOnly = showUpdateAvailableOnly;
        _dialogService = dialogService; // Can be null initially, will be set later

        // Initialize trailer services
        _youtubeService = new YouTubeTrailerService();
        _trailerPlayerService = new WebViewTrailerPlayerService();
        Logger.Log("Trailer services initialized");

        // Load TrailersOn setting from settings
        TrailersOn = SettingsManager.Instance.TrailersOn;

        // Wire up RCLONE progress callback for real-time download updates
        Rclone.OnProgress = progress =>
        {
            // Update UI on UI thread (required for Avalonia)
            // Use Post instead of Invoke to avoid blocking the rclone stderr reader thread
            Dispatcher.UIThread.Post(() =>
            {
                SpeedText = progress.SpeedText;
                EtaText = progress.EtaText;
                ProgressStatusText = progress.StatusText;
                ProgressPercentage = progress.Percentage;
                IsProgressVisible = true;
            });
        };

        _games = [];
        _uploadQueue = [];
        GamesQueue = new ObservableCollection<string>();
        Remotes = new ObservableCollection<string>();
        Devices = new ObservableCollection<string>();
        InstalledAppNames = new ObservableCollection<string>();

        // Initialize _allGames with dummy data for now
        _allGames = [];

        SearchCommand = ReactiveCommand.CreateFromTask<string>(RunSearch);
        MouseClickCommand = ReactiveCommand.Create(OnMouseClick);
        MouseDoubleClickCommand = ReactiveCommand.Create(MouseDoubleClick);
        GamesQueueMouseClickCommand = ReactiveCommand.Create(OnGamesQueueClick);

        // Initialize button commands
        SideloadApkCommand = ReactiveCommand.CreateFromTask(SideloadApkAsync);
        ReconnectDeviceCommand = ReactiveCommand.CreateFromTask(ReconnectDeviceAsync);
        CopyObbCommand = ReactiveCommand.CreateFromTask(CopyObbAsync);
        BackupAdbCommand = ReactiveCommand.CreateFromTask(BackupAdbAsync);
        BackupCommand = ReactiveCommand.CreateFromTask(BackupGameDataAsync);
        RestoreCommand = ReactiveCommand.CreateFromTask(RestoreGameDataAsync);
        GetApkCommand = ReactiveCommand.CreateFromTask(GetApkAsync);
        UninstallAppCommand = ReactiveCommand.CreateFromTask(UninstallAppAsync);
        PullAppToDesktopCommand = ReactiveCommand.CreateFromTask(PullAppToDesktopAsync);
        CopyBulkObbCommand = ReactiveCommand.CreateFromTask(CopyBulkObbAsync);
        AboutCommand = ReactiveCommand.CreateFromTask(ShowAboutAsync);
        SettingsCommand = ReactiveCommand.CreateFromTask(ShowSettingsAsync);
        QuestOptionsCommand = ReactiveCommand.CreateFromTask(ShowQuestOptionsAsync);
        OpenDownloadsCommand = ReactiveCommand.CreateFromTask(OpenDownloadsFolderAsync);
        DisableSideloadingCommand = ReactiveCommand.CreateFromTask(DisableSideloadingAsync);
        GamesListCommand = ReactiveCommand.CreateFromTask(ShowGamesListAsync);
        RunAdbCommand = ReactiveCommand.CreateFromTask(RunAdbCommandAsync);
        AdbWirelessDisableCommand = ReactiveCommand.CreateFromTask(AdbWirelessDisableAsync);
        AdbWirelessEnableCommand = ReactiveCommand.CreateFromTask(AdbWirelessEnableAsync);
        UpdateGamesCommand = ReactiveCommand.CreateFromTask(UpdateGamesAsync);
        ListApkCommand = ReactiveCommand.CreateFromTask(ListApkAsync);
        DownloadInstallGameCommand = ReactiveCommand.CreateFromTask(DownloadInstallGameAsync);
        MountCommand = ReactiveCommand.CreateFromTask(MountRcloneAsync);
        FavoriteCommand = ReactiveCommand.CreateFromTask(ToggleFavoriteAsync);
        FilterUpToDateCommand = ReactiveCommand.Create(FilterUpToDate);
        FilterUpdateAvailableCommand = ReactiveCommand.Create(FilterUpdateAvailable);
        FilterNewerThanListCommand = ReactiveCommand.Create(FilterNewerThanList);
        ProcessUploadQueueCommand = ReactiveCommand.CreateFromTask(ProcessUploadQueueAsync);
        CancelDownloadCommand = ReactiveCommand.Create(CancelDownload);

        this.WhenAnyValue(x => x.SearchText)
            .Throttle(TimeSpan.FromSeconds(1))
            .Select(x => x ?? string.Empty) // Ensure searchText is never null
            .InvokeCommand(SearchCommand);

        // Automatically update game details when selection changes
        this.WhenAnyValue(x => x.SelectedGame)
            .Subscribe(_ => OnSelectedGameChanged());

        // Initialize device list with placeholder
        Devices.Add("Select your device");

        // Initialize installed apps list with placeholder
        InstalledAppNames.Add("No device connected...");

        CheckCommandLineArguments(); // Call the new method
        SetCurrentLogPath(); // Call the new method
        Logger.Initialize(); // Call Logger.Initialize()

        // Set version text for display from version file
        VersionText = $"Rookie v{Updater.LocalVersion}";
        Logger.Log($"Application version: {VersionText}");

        // Initial population of Games (fire-and-forget to avoid blocking UI thread)
        _ = UpdateGamesList();

        // Load remotes and public config (unless --noappcheck is specified)
        if (!NoAppCheck)
        {
            InitGames();
        }
        else
        {
            Logger.Log("NoAppCheck flag enabled - skipping game list initialization");
            // Use dummy data when app check is disabled
            _ = UpdateGamesList();
        }

        // Try to detect devices on startup (fire-and-forget to avoid blocking UI thread)
        _ = RefreshDeviceListAsync();

        // Start device wakeup timer to prevent Quest from sleeping during long operations
        // Sends KEYCODE_WAKEUP every 14 minutes (matching original behavior)
        StartDeviceWakeupTimer();
    }

    /// <summary>
    /// Starts a timer that sends wakeup command to device every 14 minutes
    /// to prevent Quest from sleeping during long download/install operations
    /// </summary>
    private void StartDeviceWakeupTimer()
    {
        var wakeupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(14)
        };

        wakeupTimer.Tick += (_, _) =>
        {
            // Only send wakeup if device is connected
            if (!string.IsNullOrEmpty(Adb.DeviceId))
            {
                Logger.Log("Sending wakeup signal to device", LogLevel.Debug);
                _ = Adb.RunAdbCommandToString("shell input keyevent KEYCODE_WAKEUP");
            }
        };

        wakeupTimer.Start();
        Logger.Log("Device wakeup timer started (14-minute interval)");
    }

    private void CheckCommandLineArguments()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            switch (arg.ToLower())
            {
                case "--offline":
                    IsOffline = true;
                    break;
                case "--noappcheck":
                    NoAppCheck = true;
                    break;
            }
        }
    }

    private static void SetCurrentLogPath()
    {
        if (string.IsNullOrEmpty(SettingsManager.Instance.CurrentLogPath))
        {
            // Set to full file path, not just directory
            SettingsManager.Instance.CurrentLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debuglog.txt");
            SettingsManager.Instance.Save();
        }
    }

    private async Task RunSearch(string searchText)
    {
        Logger.Log($"Searching for: {searchText}");
        await ApplyFilters();
    }

    private async Task ApplyFilters()
    {
        var searchText = SearchText ?? string.Empty;
        Logger.Log($"ApplyFilters called - _allGames.Count={_allGames.Count}, SearchText='{searchText}', ShowFavoritesOnly={ShowFavoritesOnly}");

        // Apply search and favorites filter
        var filteredGames = await Task.Run(() =>
        {
            var games = _allGames.AsEnumerable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                games = games.Where(g =>
                    (g.GameName ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (g.ReleaseName ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (g.PackageName ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase)
                );
            }

            // Apply favorites filter
            if (ShowFavoritesOnly)
            {
                games = games.Where(g => g.IsFavorite);
            }

            // Apply status filters (mutually exclusive)
            if (ShowUpToDateOnly)
            {
                games = games.Where(g => g.IsInstalled && !g.HasUpdate);
            }
            else if (ShowUpdateAvailableOnly)
            {
                games = games.Where(g => g.HasUpdate);
            }
            else if (ShowNewerThanListOnly)
            {
                games = games.Where(g => g.IsInstalled && g.InstalledVersionCode > g.AvailableVersionCode && g.AvailableVersionCode > 0);
            }

            return games.ToList();
        });

        Logger.Log($"Filtered to {filteredGames.Count} games");

        // Update the observable collection on the UI thread
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Games.Clear();
            foreach (var game in filteredGames)
            {
                Games.Add(game);
            }
            Logger.Log($"Updated Games ObservableCollection - now has {Games.Count} items");
        });
    }

    private async Task UpdateGamesList()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Games.Clear();
            foreach (var game in _allGames)
            {
                Games.Add(game);
            }
        });
    }

    private async void InitGames()
    {
        try
        {
            // Set offline mode flag for RCLONE
            Rclone.IsOffline = IsOffline;

            // Detect if running in Debug mode (#if DEBUG)
#if DEBUG
            Rclone.DebugMode = true;
#endif

            // Initialize rclone password if using custom config
            if (!IsOffline)
            {
                Rclone.Init();
                Logger.Log("Initialized rclone with custom password (if present)");
            }

            await GetDependencies.DownloadRclone();
            await GetDependencies.Download7Zip();

            // Download rclone configs
            await SideloaderRclone.UpdateDownloadConfig();
            await SideloaderRclone.UpdateUploadConfig();

            // Populate mirror list for automatic failover
            await SideloaderRclone.RefreshRemotes();

            await Rclone.DownloadPublicConfig();

            // Parse the downloaded public config
            var publicConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vrp-public.json");
            if (FileSystemUtilities.FileExistsAndNotEmpty(publicConfigPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(publicConfigPath);
                    var publicConfig = System.Text.Json.JsonSerializer.Deserialize<PublicConfig>(json);
                    if (publicConfig != null && !string.IsNullOrEmpty(publicConfig.BaseUri))
                    {
                        // Store in static location for global access
                        Rclone.PublicConfigFile = publicConfig;
                        Rclone.HasPublicConfig = true;
                        Logger.Log($"Loaded public config with BaseUri: {publicConfig.BaseUri}");
                    }
                    else
                    {
                        Logger.Log("Public config loaded but missing BaseUri", LogLevel.Warning);
                        Rclone.HasPublicConfig = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to parse vrp-public.json: {ex.Message}", LogLevel.Error);
                    Rclone.HasPublicConfig = false;
                }
            }
            else
            {
                Logger.Log("vrp-public.json not found after download", LogLevel.Warning);
                Rclone.HasPublicConfig = false;
            }

            // Check if we have public config available
            if (Rclone.HasPublicConfig && Rclone.PublicConfigFile != null)
            {
                Logger.Log("Using public mirror for game list");

                // Add public mirror as a "remote"
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Remotes.Clear();
                    Remotes.Add("VRP Public Mirror");
                    SelectedRemote = "VRP Public Mirror";
                });

                // Download and process metadata from public mirror
                await LoadGamesFromPublicMirrorAsync();
            }
            else
            {
                Logger.Log("No public config available, trying to list configured remotes");

                // Try to list remotes from download config
                var remotes = await Rclone.ListRemotes();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Remotes.Clear();
                    if (remotes is { Count: > 0 })
                    {
                        foreach (var remote in remotes)
                        {
                            Remotes.Add(remote);
                        }
                        SelectedRemote = Remotes.FirstOrDefault();
                    }
                });

                // Load games from the first remote or use dummy data
                if (!string.IsNullOrEmpty(SelectedRemote))
                {
                    await LoadGamesFromRemoteAsync(SelectedRemote);
                }
                else
                {
                    Logger.Log("No remotes available, using dummy data", LogLevel.Warning);
                    await UpdateGamesList();
                }
            }
            // After game list is loaded, check for contributor/donor apps
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Small delay to let UI settle
                    await CheckForDonorAppsAsync();
                }
                catch (Exception donorEx)
                {
                    Logger.Log($"Error checking for donor apps: {donorEx.Message}", LogLevel.Error);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error initializing games: {ex.Message}", LogLevel.Error);
            // Fall back to dummy data on error
            await UpdateGamesList();
        }
    }

    /// <summary>
    /// Checks for apps that can be contributed to VRPirates and shows donor dialog if found
    /// </summary>
    private async Task CheckForDonorAppsAsync()
    {
        try
        {
            // Detect donor apps
            var donorApps = DonorAppDetector.DetectDonorAppsAsync(_allGames.ToList(), NoAppCheck);

            if (donorApps.Count == 0)
            {
                Logger.Log("No donor apps detected");
                return;
            }

            Logger.Log($"Found {donorApps.Count} donor apps - showing dialog");

            // Show donor apps dialog on UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var donorWindow = new Views.DonorsListWindow(donorApps)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var desktop = Avalonia.Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (desktop?.MainWindow != null)
                {
                    await donorWindow.ShowDialog(desktop.MainWindow);
                }

                // Handle user selection
                if (donorWindow.UserClickedDonate && donorWindow.SelectedApps.Count > 0)
                {
                    Logger.Log($"User selected {donorWindow.SelectedApps.Count} apps to donate");

                    // Add selected apps to upload queue
                    foreach (var app in donorWindow.SelectedApps)
                    {
                        _uploadQueue.Add(new UploadGame
                        {
                            GameName = app.GameName,
                            PackageName = app.PackageName,
                            VersionCode = app.VersionCode
                        });
                    }

                    // Show info dialog
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"{donorWindow.SelectedApps.Count} app(s) added to upload queue.\n\n" +
                            "Use 'Process Upload Queue' to contribute them to VRPirates.",
                            "Apps Queued for Upload");
                    }
                }

                // Show NewApps categorization dialog if there are unselected new apps
                if (donorWindow.UnselectedNewApps.Count > 0)
                {
                    Logger.Log($"Showing NewApps dialog for {donorWindow.UnselectedNewApps.Count} apps");

                    var newAppsWindow = new Views.NewAppsWindow(donorWindow.UnselectedNewApps)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    };

                    var desktop2 = Avalonia.Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    if (desktop2?.MainWindow != null)
                    {
                        await newAppsWindow.ShowDialog(desktop2.MainWindow);
                    }

                    Logger.Log("NewApps categorization complete");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error in CheckForDonorAppsAsync: {ex.Message}", LogLevel.Error);
        }
    }

    private async Task LoadGamesFromPublicMirrorAsync()
    {
        try
        {
            ShowProgress("Loading games from public mirror...");
            Logger.Log("Downloading metadata from public mirror");

            // Download metadata archive from public mirror
            await SideloaderRclone.UpdateMetadataFromPublic();

            // Process the downloaded metadata
            await SideloaderRclone.ProcessMetadataFromPublic();

            // Convert the games list to GameItems
            var gamesList = new List<GameItem>();

            foreach (var game in SideloaderRclone.Games)
            {
                if (game.Length > SideloaderRclone.VersionNameIndex)
                {
                    var packageName = game[SideloaderRclone.PackageNameIndex];
                    var gameItem = new GameItem
                    {
                        GameName = game[SideloaderRclone.GameNameIndex],
                        ReleaseName = game[SideloaderRclone.ReleaseNameIndex],
                        PackageName = packageName,
                        Version = game[SideloaderRclone.VersionCodeIndex],
                        LastUpdated = game.Length > SideloaderRclone.ReleaseApkPathIndex ? game[SideloaderRclone.ReleaseApkPathIndex] : "Unknown",
                        SizeMb = game.Length > SideloaderRclone.VersionNameIndex ? game[SideloaderRclone.VersionNameIndex] : "Unknown",
                        Popularity = game.Length > SideloaderRclone.DownloadsIndex ? game[SideloaderRclone.DownloadsIndex] : "Unknown",
                        IsFavorite = SettingsManager.Instance.FavoriteGames.Contains(packageName)
                    };

                    gamesList.Add(gameItem);
                }
            }

            // Update the games collection
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allGames.Clear();
                _allGames.AddRange(gamesList);
                Logger.Log($"_allGames now contains {_allGames.Count} games");
            });

            await ApplyFilters();

            // Log after filtering
            Logger.Log($"Games ObservableCollection now contains {Games.Count} games");

            HideProgress();

            Logger.Log($"Loaded {gamesList.Count} games from public mirror");
            ProgressStatusText = $"Loaded {gamesList.Count} games from public mirror";
        }
        catch (Exception ex)
        {
            HideProgress();
            Logger.Log($"Error loading games from public mirror: {ex.Message}", LogLevel.Error);
            ProgressStatusText = "Error loading games from public mirror";

            // Fall back to dummy data on error
            await UpdateGamesList();
        }
    }

    private async Task LoadGamesFromRemoteAsync(string remoteName)
    {
        try
        {
            ShowProgress("Loading games from remote...");
            Logger.Log($"Loading games from remote: {remoteName}");

            // Use the original approach: download game list from remote using InitGames()
            UpdateProgress(10, "Downloading game list...");
            await Task.Run(() => SideloaderRclone.InitGames(remoteName));
            Logger.Log($"Downloaded game list with {SideloaderRclone.Games.Count} games");

            // Download metadata from remote (nouns, thumbnails, notes)
            UpdateProgress(30, "Syncing game notes...");
            await SideloaderRclone.UpdateGameNotes(remoteName);
            Logger.Log("Game notes synced");

            UpdateProgress(50, "Syncing thumbnails (this may take a minute)...");
            await SideloaderRclone.UpdateGamePhotos(remoteName);
            Logger.Log("Thumbnails synced");

            UpdateProgress(80, "Syncing nouns...");
            await SideloaderRclone.UpdateNouns(remoteName);
            Logger.Log("Nouns synced");

            UpdateProgress(90, "Processing game data...");

            // Convert the games list to GameItems
            var gamesList = SideloaderRclone.Games
                .Where(game => game.Length > SideloaderRclone.VersionNameIndex)
                .Select(game => new
                {
                    game, 
                    packageName = game[SideloaderRclone.PackageNameIndex]
                })
                .Select(g => new GameItem
                {
                    GameName = g.game[SideloaderRclone.GameNameIndex],
                    ReleaseName = g.game[SideloaderRclone.ReleaseNameIndex],
                    PackageName = g.packageName,
                    Version = g.game[SideloaderRclone.VersionCodeIndex],
                    LastUpdated = g.game.Length > SideloaderRclone.ReleaseApkPathIndex
                        ? g.game[SideloaderRclone.ReleaseApkPathIndex]
                        : "Unknown",
                    SizeMb = g.game.Length > SideloaderRclone.VersionNameIndex
                        ? g.game[SideloaderRclone.VersionNameIndex]
                        : "Unknown",
                    Popularity = g.game.Length > SideloaderRclone.DownloadsIndex
                        ? g.game[SideloaderRclone.DownloadsIndex]
                        : "Unknown",
                    IsFavorite = SettingsManager.Instance.FavoriteGames.Contains(g.packageName)
                }).ToList();

            // Update the games collection
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allGames.Clear();
                _allGames.AddRange(gamesList);
            });

            // Load cached images for games
            await LoadCachedImagesAsync();

            await ApplyFilters();
            HideProgress();

            Logger.Log($"Loaded {gamesList.Count} games from remote {remoteName}");
            ProgressStatusText = $"Loaded {gamesList.Count} games from {remoteName}";
        }
        catch (Exception ex)
        {
            HideProgress();
            Logger.Log($"Error loading games from remote: {ex.Message}", LogLevel.Error);
            ProgressStatusText = "Error loading games from remote";

            // Fall back to dummy data on error
            await UpdateGamesList();
        }
    }

    /// <summary>
    /// Load cached images for all games in the list
    /// </summary>
    private async Task LoadCachedImagesAsync()
    {
        try
        {
            Logger.Log("Loading cached images for games...");

            await Task.Run(() =>
            {
                foreach (var game in _allGames)
                {
                    // Check if image is already cached
                    var cachedPath = ImageCache.GetCachedImagePath(game.GameName);
                    if (!string.IsNullOrEmpty(cachedPath))
                    {
                        game.CachedImagePath = cachedPath;
                    }
                }
            });

            Logger.Log($"Loaded cached images for {_allGames.Count(g => g.HasImage)} games");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error loading cached images: {ex.Message}", LogLevel.Error);
        }
    }

    // Command Implementations

    private async Task SideloadApkAsync()
    {
        await ExecuteCommandAsync("Sideload APK", async () =>
        {
            if (!await EnsureDeviceConnectedAsync("No device connected. Please connect your device first using the 'RECONNECT DEVICE' button."))
                return;

            // Open file dialog to select APK
            var fileDialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select APK File",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Android Package")
                    {
                        Patterns = ["*.apk"]
                    }
                ]
            };

            var window = GetMainWindow();
            if (window == null)
            {
                Logger.Log("Cannot get main window for file dialog", LogLevel.Error);
                return;
            }

            var result = await window.StorageProvider.OpenFilePickerAsync(fileDialog);

            if (result is { Count: > 0 })
            {
                var apkPath = result[0].Path.LocalPath;
                Logger.Log($"Selected APK: {apkPath}");

                // Validate APK and extract metadata using aapt (before installation)
                ProgressStatusText = "Validating APK...";
                try
                {
                    var apkInfo = await Aapt.ValidateAndGetInfo(apkPath);
                    Logger.Log($"APK validated: {apkInfo.PackageName} v{apkInfo.VersionName} ({apkInfo.VersionCode})");

                    // Show validation info to user
                    if (_dialogService != null)
                    {
                        var confirmed = await _dialogService.ShowConfirmationAsync(
                            $"APK Information:\n\n" +
                            $"Package: {apkInfo.PackageName}\n" +
                            $"Name: {apkInfo.AppLabel}\n" +
                            $"Version: {apkInfo.VersionName} ({apkInfo.VersionCode})\n" +
                            $"Target SDK: {apkInfo.TargetSdkVersion}\n\n" +
                            $"Proceed with installation?",
                            "Confirm APK Installation");

                        if (!confirmed)
                        {
                            ProgressStatusText = "Installation cancelled";
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"APK validation failed: {ex.Message}", LogLevel.Warning);
                    if (_dialogService != null)
                    {
                        var proceed = await _dialogService.ShowConfirmationAsync(
                            $"Could not validate APK using aapt:\n\n{ex.Message}\n\n" +
                            "The APK may be invalid or corrupted.\n\n" +
                            "Proceed with installation anyway?",
                            "Validation Failed");

                        if (!proceed)
                        {
                            ProgressStatusText = "Installation cancelled";
                            return;
                        }
                    }
                }

                ProgressStatusText = "Installing APK...";

                // Install the APK with reinstall support
                var installResult = await Adb.SideloadAsync(apkPath, "", _dialogService);

                if (installResult.Output.Contains("Success"))
                {
                    ProgressStatusText = "APK installed successfully!";
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"APK installed successfully!\n\nFile: {Path.GetFileName(apkPath)}",
                            "Installation Complete");
                    }
                }
                else
                {
                    ProgressStatusText = "APK installation failed";
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Failed to install APK.\n\n{installResult.Error}",
                            "Installation Failed");
                    }
                }
            }
        });
    }

    private async Task ReconnectDeviceAsync()
    {
        await ExecuteCommandAsync("Reconnect Device", async () =>
        {
            ProgressStatusText = "Reconnecting to device...";

            // Refresh device list
            await RefreshDeviceListAsync();

            if (Devices.Count == 0 || (Devices.Count == 1 && Devices[0] == "Select your device"))
            {
                // No devices found
                if (_dialogService != null)
                {
                    await _dialogService.ShowWarningAsync(
                        "No devices detected. Please:\n\n" +
                        "1. Connect your Quest device via USB\n" +
                        "2. Enable USB debugging on your device\n" +
                        "3. Accept the debugging authorization prompt on your device\n" +
                        "4. Try again",
                        "No Device Found");
                }
                ProgressStatusText = "No device detected";
                Logger.Log("No devices detected", LogLevel.Warning);
            }
            else
            {
                // Select first device if only one available
                if (Devices.Count == 2) // "Select your device" + one device
                {
                    SelectedDevice = Devices[1];
                }

                ProgressStatusText = $"Found {Devices.Count - 1} device(s)";

                if (_dialogService != null)
                {
                    var deviceList = string.Join("\n", Devices.Skip(1));
                    await _dialogService.ShowInfoAsync(
                        $"Successfully detected device(s)!\n\n" +
                        $"{deviceList}\n\n" +
                        $"Storage: {DiskSpaceText}",
                        "Device Connected");
                }
            }
        });
    }

    private async Task RefreshDeviceListAsync()
    {
        try
        {
            // Run ADB devices command to detect connected devices
            var result = Adb.RunAdbCommandToString("devices -l");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Devices.Clear();
                Devices.Add("Select your device");

                // Parse device IDs from output
                var lines = result.Output.Split('\n');
                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("List of devices")))
                {
                    var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && !parts[0].Equals("*") && parts[0] != "adb")
                    {
                        // Add device ID (format: "deviceid   device")
                        var deviceId = parts[0];
                        Devices.Add(deviceId);
                        Logger.Log($"Found device: {deviceId}");
                    }
                }

                // Auto-select first device if one was found (matching original behavior)
                if (Devices.Count > 1)
                {
                    SelectedDevice = Devices[1]; // Select first real device (skip "Select your device")
                    Logger.Log($"Auto-selected device: {SelectedDevice}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error refreshing device list: {ex.Message}", LogLevel.Error);
        }
    }

    private async Task RefreshDeviceInfoAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(Adb.DeviceId))
            {
                // Get device storage info
                var spaceInfo = Adb.GetAvailableSpace();
                DiskSpaceText = spaceInfo;

                // Get battery info
                var batteryInfo = Adb.GetBatteryInfo();
                if (batteryInfo is { Level: > 0 })
                {
                    BatteryLevelText = $"{batteryInfo.Level}%";
                }

                // Get device model
                var modelResult = Adb.RunAdbCommandToString("shell getprop ro.product.model");
                var deviceModel = modelResult.Output.Trim();
                if (!string.IsNullOrEmpty(deviceModel))
                {
                    Logger.Log($"Device model: {deviceModel}");
                }

                Logger.Log($"Device info refreshed for: {Adb.DeviceId}");

                // Automatically refresh installed apps status to populate counts
                await RefreshInstalledAppsStatusAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error refreshing device info: {ex.Message}", LogLevel.Error);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Refresh installed apps status and update game list with version comparison
    /// </summary>
    private async Task RefreshInstalledAppsStatusAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Adb.DeviceId))
            {
                Logger.Log("Cannot refresh installed apps - no device connected", LogLevel.Warning);
                return;
            }

            Logger.Log("Refreshing installed apps status...");
            ShowProgress("Detecting installed apps...");

            // Get all installed packages with version info
            var installedPackages = await Task.Run(Adb.GetAllInstalledPackagesWithVersions);

            Logger.Log($"Found {installedPackages.Count} installed packages");

            // Populate InstalledAppNames dropdown (matches original m_combo behavior)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstalledAppNames.Clear();

                // Build list of installed game names (matching package names to game names)
                var installedGameNames = new List<string>();

                foreach (var packageName in installedPackages.Keys)
                {
                    // Try to find the game name from our game list
                    var matchingGame = _allGames.FirstOrDefault(g =>
                        g.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase));

                    if (matchingGame != null)
                    {
                        // Use game name if found in our list
                        installedGameNames.Add(matchingGame.GameName);
                    }
                    else
                    {
                        // Use package name if no match (for games not in VRPirates list)
                        installedGameNames.Add(packageName);
                    }
                }

                // Sort alphabetically
                installedGameNames.Sort();

                // Add to observable collection
                foreach (var gameName in installedGameNames)
                {
                    InstalledAppNames.Add(gameName);
                }

                Logger.Log($"Populated InstalledAppNames dropdown with {InstalledAppNames.Count} items");
            });

            // Update game list with install status
            var updatesAvailable = 0;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var game in _allGames)
                {
                    if (installedPackages.TryGetValue(game.PackageName, out var versionInfo))
                    {
                        game.IsInstalled = true;
                        game.InstalledVersionCode = versionInfo.versionCode;

                        // Parse available version code from game database
                        if (long.TryParse(game.Version, out var availableVersionCode))
                        {
                            game.AvailableVersionCode = availableVersionCode;
                        }

                        if (game.HasUpdate)
                        {
                            updatesAvailable++;
                            Logger.Log($"Update available for {game.GameName}: v{game.InstalledVersionCode} → v{game.AvailableVersionCode}");
                        }
                    }
                    else
                    {
                        game.IsInstalled = false;
                        game.InstalledVersionCode = 0;
                    }
                }
            });

            // Re-apply filters to refresh UI (outside dispatcher to avoid blocking)
            await ApplyFilters();

            // Notify UI that status counts have changed
            NotifyStatusCountsChanged();

            ProgressStatusText = updatesAvailable > 0
                ? $"Found {updatesAvailable} game(s) with updates available"
                : "All installed games are up to date";

            HideProgress();
            Logger.Log("Installed apps status refreshed");
        }
        catch (Exception ex)
        {
            HideProgress();
            Logger.Log($"Error refreshing installed apps status: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Update progress bar with percentage and status
    /// </summary>
    private void UpdateProgress(double percentage, string status, bool clearSpeedAndEta = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProgressPercentage = percentage;
            ProgressStatusText = status;
            IsProgressVisible = true;

            if (clearSpeedAndEta)
            {
                SpeedText = "";
                EtaText = "";
            }
        });
    }

    /// <summary>
    /// Show progress bar for operation
    /// </summary>
    private void ShowProgress(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsProgressVisible = true;
            ProgressStatusText = status;
            ProgressPercentage = 0;
        });
    }

    /// <summary>
    /// Hide progress bar after operation completes
    /// </summary>
    private void HideProgress()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsProgressVisible = false;
            ProgressPercentage = 0;
            SpeedText = "";
            EtaText = "";
            ProgressStatusText = "";
        });
    }

    #region Helper Methods

    /// <summary>
    /// Get the main application window
    /// </summary>
    private static Window GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
    }

    /// <summary>
    /// Ensure a device is connected, showing a warning dialog if not
    /// </summary>
    /// <param name="customMessage">Optional custom message to display</param>
    /// <returns>True if device is connected, false otherwise</returns>
    private async Task<bool> EnsureDeviceConnectedAsync(string customMessage = null)
    {
        if (!string.IsNullOrEmpty(Adb.DeviceId))
        {
            return true;
        }

        if (_dialogService != null)
        {
            await _dialogService.ShowWarningAsync(
                customMessage ?? "No device connected. Please connect your device first.",
                "No Device");
        }

        return false;
    }

    /// <summary>
    /// Ensure a game is selected, showing a warning dialog if not
    /// </summary>
    /// <param name="action">Description of the action requiring a selected game</param>
    /// <returns>True if a game is selected, false otherwise</returns>
    private async Task<bool> EnsureGameSelectedAsync(string action = "perform this action")
    {
        if (SelectedGame != null)
        {
            return true;
        }

        if (_dialogService != null)
        {
            await _dialogService.ShowWarningAsync(
                $"Please select a game to {action}.",
                "No Game Selected");
        }

        return false;
    }

    /// <summary>
    /// Handle an exception by logging it and optionally showing an error dialog
    /// </summary>
    private async Task HandleErrorAsync(Exception ex, string operationName, string userMessage = null)
    {
        Logger.Log($"Error during {operationName}: {ex.Message}", LogLevel.Error);
        ProgressStatusText = $"Error: {operationName}";

        if (_dialogService != null)
        {
            await _dialogService.ShowErrorAsync(
                userMessage ?? $"Error during {operationName}:\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Execute a command with automatic error handling and progress tracking
    /// </summary>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="operation">The async operation to execute</param>
    /// <param name="progressStartText">Optional progress text to show when starting</param>
    /// <param name="progressCompleteText">Optional progress text to show when complete</param>
    private async Task ExecuteCommandAsync(
        string operationName,
        Func<Task> operation,
        string progressStartText = null,
        string progressCompleteText = null)
    {
        try
        {
            Logger.Log($"{operationName} command triggered");

            if (progressStartText != null)
            {
                ProgressStatusText = progressStartText;
            }

            await operation();

            if (progressCompleteText != null)
            {
                ProgressStatusText = progressCompleteText;
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, operationName);
        }
    }

    #endregion

    private async Task CopyObbAsync()
    {
        await ExecuteCommandAsync("Copy OBB", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Open folder picker to select OBB folder
            var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select OBB Folder",
                AllowMultiple = false
            };

            var window = GetMainWindow();
            if (window == null) return;

            var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

            if (result is { Count: > 0 })
            {
                var obbPath = result[0].Path.LocalPath;
                Logger.Log($"Selected OBB folder: {obbPath}");

                ProgressStatusText = "Copying OBB files...";

                var copyResult = Adb.CopyObb(obbPath);

                if (copyResult.Output.Contains("Success") || !copyResult.Output.Contains("error"))
                {
                    ProgressStatusText = "OBB files copied successfully!";
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"OBB files copied successfully!\n\nFolder: {Path.GetFileName(obbPath)}",
                            "Copy Complete");
                    }
                }
                else
                {
                    ProgressStatusText = "OBB copy failed";
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Failed to copy OBB files.\n\n{copyResult.Error}",
                            "Copy Failed");
                    }
                }
            }
        });
    }

    private async Task UninstallAppAsync()
    {
        await ExecuteCommandAsync("Uninstall App", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Get package name and game name from either SelectedGame or SelectedInstalledApp
            string packageName;
            string gameName;

            // Try SelectedGame first (from main game list), then SelectedInstalledApp (from dropdown)
            if (SelectedGame != null)
            {
                packageName = SelectedGame.PackageName;
                gameName = SelectedGame.GameName;
            }
            else if (!string.IsNullOrEmpty(SelectedInstalledApp))
            {
                // User selected from installed apps dropdown
                var matchingGame = _allGames.FirstOrDefault(g =>
                    g.GameName.Equals(SelectedInstalledApp, StringComparison.OrdinalIgnoreCase));

                if (matchingGame != null)
                {
                    packageName = matchingGame.PackageName;
                    gameName = matchingGame.GameName;
                }
                else
                {
                    // If not in our game list, assume the dropdown entry is the package name
                    packageName = SelectedInstalledApp;
                    gameName = SelectedInstalledApp;
                }
            }
            else
            {
                // Nothing selected - show error
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Please select an installed app from the dropdown or select a game from the list.",
                        "No App Selected");
                }
                return;
            }

            // Confirm uninstall
            if (_dialogService != null)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    $"Are you sure you want to uninstall?\n\n{gameName}\n\nPackage: {packageName}",
                    "Confirm Uninstall");

                if (!confirmed) return;
            }

            ProgressStatusText = $"Uninstalling {gameName}...";

            // Use UninstallGame which also cleans up OBB and data folders
            var result = SideloaderUtilities.UninstallGame(packageName);

            if (result.Output.Contains("Success"))
            {
                ProgressStatusText = "App uninstalled successfully!";
                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        $"{gameName} has been uninstalled.",
                        "Uninstall Complete");
                }
            }
            else
            {
                ProgressStatusText = "Uninstall failed";
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        $"Failed to uninstall app.\n\n{result.Error}",
                        "Uninstall Failed");
                }
            }
        });
    }

    private async Task GetApkAsync()
    {
        await ExecuteCommandAsync("Upload Game", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // For "Share Selected App", use installed app dropdown selection
            string packageName;
            string gameName;

            // Try SelectedGame first (from main game list), then SelectedInstalledApp (from dropdown)
            if (SelectedGame != null)
            {
                packageName = SelectedGame.PackageName;
                gameName = SelectedGame.GameName;
            }
            else if (!string.IsNullOrEmpty(SelectedInstalledApp))
            {
                // User selected from installed apps dropdown
                // Find the matching game in our list to get the package name
                var matchingGame = _allGames.FirstOrDefault(g =>
                    g.GameName.Equals(SelectedInstalledApp, StringComparison.OrdinalIgnoreCase));

                if (matchingGame != null)
                {
                    packageName = matchingGame.PackageName;
                    gameName = matchingGame.GameName;
                }
                else
                {
                    // If not in our game list, assume the dropdown entry is the package name
                    packageName = SelectedInstalledApp;
                    gameName = SelectedInstalledApp;
                }
            }
            else
            {
                // Nothing selected - show error
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Please select an installed app from the dropdown to share/upload.",
                        "No App Selected");
                }
                return;
            }

            // Verify device is Meta Quest (check codename)
            ProgressStatusText = "Verifying device...";
            var deviceCodeName = Adb.RunAdbCommandToString("shell getprop ro.product.device").Output.ToLower().Trim();
            Logger.Log($"Device codename: {deviceCodeName}");

            // Fetch codenames list from GitHub
            const string codeNamesUrl = "https://raw.githubusercontent.com/VRPirates/rookie/master/codenames";
            bool isMetaDevice;

            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                var codenames = await httpClient.GetStringAsync(codeNamesUrl);
                isMetaDevice = codenames.Contains(deviceCodeName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error downloading codenames file: {ex.Message}", LogLevel.Error);
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Error verifying device type.\n\nCould not download codenames list from GitHub.",
                        "Verification Error");
                }
                return;
            }

            if (!isMetaDevice)
            {
                Logger.Log($"Device {deviceCodeName} is not a recognized Meta Quest device", LogLevel.Warning);
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        "You are attempting to upload from an unknown device.\n\nPlease connect a Meta Quest device to upload.",
                        "Unknown Device");
                }
                return;
            }

            // Get installed version
            var versionResult = Adb.RunAdbCommandToString($"shell \"dumpsys package {packageName} | grep versionCode -F\"");
            var versionCodeStr = versionResult.Output;
            if (versionCodeStr.Contains("versionCode="))
            {
                versionCodeStr = versionCodeStr.Substring(versionCodeStr.IndexOf("versionCode=", StringComparison.Ordinal) + "versionCode=".Length);
                var spaceIndex = versionCodeStr.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    versionCodeStr = versionCodeStr.Substring(0, spaceIndex);
                }
            }

            if (!long.TryParse(versionCodeStr.Trim(), out var versionCode))
            {
                Logger.Log($"Could not parse version code: {versionCodeStr}", LogLevel.Warning);
                versionCode = 0;
            }

            // Ask user for confirmation
            if (_dialogService != null)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    $"Upload {gameName} (v{versionCode}) to VRPirates servers?\n\n" +
                    $"Package: {packageName}\n" +
                    $"Device: {deviceCodeName}\n\n" +
                    "This will extract the APK and OBB files, compress them, and upload to the community mirrors.\n\n" +
                    "Continue?",
                    "Confirm Upload");

                if (!confirmed)
                {
                    return;
                }
            }

            var settings = SettingsManager.Instance;
            var mainDir = settings.MainDir;
            var packageFolder = Path.Combine(mainDir, packageName);
            var uuid = SideloaderUtilities.Uuid();

            try
            {
                // Create package folder
                Directory.CreateDirectory(packageFolder);

                // 1. Extract APK
                ProgressStatusText = $"Extracting APK for {gameName}...";
                Logger.Log($"Extracting APK for {packageName}");

                var apkPath = Adb.GetPackageApkPath(packageName);
                if (string.IsNullOrEmpty(apkPath))
                {
                    throw new Exception($"Could not find APK path for package: {packageName}");
                }

                var pullApkResult = Adb.RunAdbCommandToString($"pull \"{apkPath}\" \"{packageFolder}\"");
                if (!string.IsNullOrEmpty(pullApkResult.Error) && !pullApkResult.Error.Contains("skipping"))
                {
                    Logger.Log($"APK pull warning: {pullApkResult.Error}", LogLevel.Warning);
                }

                // 2. Pull OBB folder
                ProgressStatusText = $"Extracting OBB for {gameName}...";
                Logger.Log($"Pulling OBB folder for {packageName}");

                var pullObbResult = Adb.RunAdbCommandToString($"pull \"/sdcard/Android/obb/{packageName}\" \"{packageFolder}\"");

                // OBB may not exist for all games, so don't treat as error
                if (!string.IsNullOrEmpty(pullObbResult.Error) && !pullObbResult.Error.Contains("does not exist"))
                {
                    Logger.Log($"OBB pull warning: {pullObbResult.Error}", LogLevel.Warning);
                }
                else if (pullObbResult.Error.Contains("does not exist"))
                {
                    Logger.Log($"No OBB folder found for {packageName} (this is normal for many games)");
                }

                // 3. Write metadata files
                var hwidPath = Path.Combine(packageFolder, "HWID.txt");
                await File.WriteAllTextAsync(hwidPath, uuid);

                var uploadMethodPath = Path.Combine(packageFolder, "uploadMethod.txt");
                await File.WriteAllTextAsync(uploadMethodPath, "manual");

                Logger.Log("Wrote metadata files: HWID.txt, uploadMethod.txt");

                // 4. Compress with 7z
                ProgressStatusText = $"Compressing {gameName}...";
                var zipName = $"{gameName} v{versionCode} {packageName} {uuid.Substring(0, 1)} {deviceCodeName}.zip";
                var zipPath = Path.Combine(mainDir, zipName);

                // Delete existing zip if present
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                Logger.Log($"Compressing to {zipName}");

                // Compress the package folder contents (use wildcard to match original: .\{packagename}\*)
                var packageFolderContents = Path.Combine(packageFolder, "*");
                await Zip.CreateArchive(zipPath, packageFolderContents);

                // 5. Upload to rclone
                ProgressStatusText = $"Uploading {gameName} to servers...";
                Logger.Log($"Uploading {zipName} to RSL-gameuploads");

                // Get zip size
                var zipInfo = new FileInfo(zipPath);
                var zipSize = zipInfo.Length;

                // Write size metadata file
                var sizeFileName = $"{gameName} v{versionCode} {packageName} {uuid.Substring(0, 1)} {deviceCodeName}.txt";
                var sizePath = Path.Combine(mainDir, sizeFileName);
                await File.WriteAllTextAsync(sizePath, zipSize.ToString());

                // Upload size file
                await Rclone.runRcloneCommand_UploadConfig($"copy \"{sizePath}\" RSL-gameuploads:");

                // Upload zip
                await Rclone.runRcloneCommand_UploadConfig($"copy \"{zipPath}\" RSL-gameuploads:");

                Logger.Log($"Upload completed: {zipName}");

                // 6. Cleanup
                ProgressStatusText = "Cleaning up...";
                if (File.Exists(sizePath))
                {
                    File.Delete(sizePath);
                }
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                if (Directory.Exists(packageFolder))
                {
                    Directory.Delete(packageFolder, true);
                }

                Logger.Log("Cleanup complete");

                // 7. Show success message
                ProgressStatusText = "Upload complete!";
                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        $"Upload of {gameName} is complete!\n\n" +
                        $"Thank you for your contribution to the VRPirates community!",
                        "Upload Complete");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Upload failed: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Upload failed";

                // Cleanup on error
                try
                {
                    if (Directory.Exists(packageFolder))
                    {
                        Directory.Delete(packageFolder, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        $"Failed to upload game.\n\nError: {ex.Message}",
                        "Upload Failed");
                }
            }
        });
    }

    private async Task BackupAdbAsync()
    {
        await ExecuteCommandAsync("Backup ADB", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Get package name and game name from either SelectedGame or SelectedInstalledApp
            string packageName;
            string gameName;

            // Try SelectedGame first (from main game list), then SelectedInstalledApp (from dropdown)
            if (SelectedGame != null)
            {
                packageName = SelectedGame.PackageName;
                gameName = SelectedGame.GameName;
            }
            else if (!string.IsNullOrEmpty(SelectedInstalledApp))
            {
                // User selected from installed apps dropdown
                var matchingGame = _allGames.FirstOrDefault(g =>
                    g.GameName.Equals(SelectedInstalledApp, StringComparison.OrdinalIgnoreCase));

                if (matchingGame != null)
                {
                    packageName = matchingGame.PackageName;
                    gameName = matchingGame.GameName;
                }
                else
                {
                    // If not in our game list, assume the dropdown entry is the package name
                    packageName = SelectedInstalledApp;
                    gameName = SelectedInstalledApp;
                }
            }
            else
            {
                // Nothing selected - show error
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Please select an installed app from the dropdown or select a game from the list.",
                        "No App Selected");
                }
                return;
            }

            // Select backup location
            var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select Backup Location",
                AllowMultiple = false
            };

            var window = GetMainWindow();
            if (window == null) return;

            var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

            if (result is { Count: > 0 })
            {
                var backupPath = result[0].Path.LocalPath;
                var backupFile = Path.Combine(backupPath, $"{packageName}.ab");

                ProgressStatusText = $"Backing up {gameName}...";

                // Show info dialog that user needs to confirm on device
                if (_dialogService != null)
                {
                    _ = _dialogService.ShowInfoAsync(
                        "Please unlock your device and confirm the backup operation when prompted.\n\n" +
                        "The backup will start after you approve it on your device.",
                        "Confirm Backup on Device");
                }

                // Run backup command in background thread
                var (success, errorMessage) = await Task.Run(() =>
                {
                    var backupResult = Adb.RunAdbCommandToString($"backup -f \"{backupFile}\" {packageName}");

                    // Check for command errors first
                    if (!string.IsNullOrEmpty(backupResult.Error) &&
                        !backupResult.Error.Contains("deprecated"))  // Ignore deprecation warning
                    {
                        Logger.Log($"Backup command error: {backupResult.Error}", LogLevel.Warning);
                    }

                    // Check if user confirmation is needed (output contains the message)
                    if (backupResult.Output.Contains("confirm the backup operation"))
                    {
                        Logger.Log("Waiting for user to confirm backup on device...");

                        // Poll for backup file with timeout (2 minutes)
                        var timeout = DateTime.Now.AddMinutes(2);
                        while (DateTime.Now < timeout)
                        {
                            if (FileSystemUtilities.FileExistsAndNotEmpty(backupFile))
                            {
                                return (true, "");
                            }
                            System.Threading.Thread.Sleep(1000); // Check every second
                        }

                        // Timeout - user didn't confirm or backup failed
                        return (false, "Backup timed out. User may have cancelled the backup on device.");
                    }

                    // No confirmation needed - check immediately
                    if (FileSystemUtilities.FileExistsAndNotEmpty(backupFile))
                    {
                        return (true, "");
                    }

                    return (false, backupResult.Error ?? "Backup file not created");
                });

                if (success)
                {
                    ProgressStatusText = "Backup completed successfully!";
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"Backup saved to:\n\n{backupFile}",
                            "Backup Complete");
                    }
                }
                else
                {
                    ProgressStatusText = "Backup failed or cancelled";
                    Logger.Log($"Backup failed: {errorMessage}", LogLevel.Error);
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            $"Backup was not completed.\n\n{errorMessage}\n\nNote: ADB backup may have been cancelled on the device.",
                            "Backup Not Completed");
                    }
                }
            }
        });
    }

    private async Task OpenDownloadsFolderAsync()
    {
        await ExecuteCommandAsync("Open Downloads Folder", async () =>
        {
            var downloadsPath = SettingsManager.Instance.DownloadDir;

            if (!Directory.Exists(downloadsPath))
            {
                Logger.Log($"Download directory does not exist: {downloadsPath}, falling back to Desktop", LogLevel.Warning);
                downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            // Open folder in file explorer (cross-platform)
            if (PlatformHelper.IsWindows)
            {
                Process.Start("explorer.exe", downloadsPath);
            }
            else if (PlatformHelper.IsMacOs)
            {
                Process.Start("open", downloadsPath);
            }
            else if (PlatformHelper.IsLinux)
            {
                Process.Start("xdg-open", downloadsPath);
            }

            Logger.Log($"Opened downloads folder: {downloadsPath}");
            await Task.CompletedTask;
        });
    }

    private async Task RunAdbCommandAsync()
    {
        await ExecuteCommandAsync("Run ADB Command", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Show ADB command overlay
            AdbCommandBoxLabel = "Enter ADB Command";
            AdbCommandText = "";
            IsAdbCommandBoxVisible = true;

            await Task.CompletedTask;
        });
    }

    private async Task AdbWirelessDisableAsync()
    {
        await ExecuteCommandAsync("Disable Wireless ADB", async () =>
        {
            if (!await EnsureDeviceConnectedAsync("No device connected. Please connect via USB first."))
                return;

            ProgressStatusText = "Disabling wireless ADB...";

            // Use the dedicated ADB method
            Adb.DisableWirelessAdb();

            ProgressStatusText = "Wireless ADB disabled";

            if (_dialogService != null)
            {
                await _dialogService.ShowInfoAsync(
                    "Wireless ADB has been disabled. Device will only connect via USB.",
                    "Wireless ADB Disabled");
            }

            Logger.Log("Wireless ADB disabled successfully");
        });
    }

    private async Task AdbWirelessEnableAsync()
    {
        await ExecuteCommandAsync("Enable Wireless ADB", async () =>
        {
            // Ask user: Automatic or Manual?
            var useAutomatic = await _dialogService.ShowConfirmationAsync(
                "Use automatic wireless setup?\n\nYes = Automatic (USB device required)\nNo = Manual (Enter IP address)",
                "Wireless ADB Setup");

            if (useAutomatic)
            {
                // Automatic mode - requires USB device
                if (!await EnsureDeviceConnectedAsync("No device connected. Please connect via USB first."))
                    return;

                ProgressStatusText = "Enabling wireless ADB...";

                // Use the dedicated ADB method
                var (success, ipAddress) = await Adb.EnableWirelessAdb(_dialogService);

                if (success)
                {
                    ProgressStatusText = $"Wireless ADB enabled at {ipAddress}";
                    Logger.Log($"Wireless ADB enabled successfully: {ipAddress}");
                }
                else
                {
                    ProgressStatusText = "Failed to enable wireless ADB";
                    Logger.Log("Wireless ADB enable failed", LogLevel.Warning);
                }
            }
            else
            {
                // Manual mode - show IP entry dialog
                AdbCommandBoxLabel = "Enter Device IP Address (e.g., 192.168.1.100)";
                AdbCommandBoxCurrentMode = AdbCommandBoxMode.WirelessIpEntry;
                AdbCommandText = "";
                IsAdbCommandBoxVisible = true;
            }
        });
    }

    private async Task UpdateGamesAsync()
    {
        await ExecuteCommandAsync("Refresh All", async () =>
        {
            ProgressStatusText = "Refreshing game list from remotes...";

            if (!string.IsNullOrEmpty(SelectedRemote))
            {
                // Check if using public config mode
                if (Rclone.HasPublicConfig && SelectedRemote == "VRP Public Mirror")
                {
                    Logger.Log("Refreshing from public mirror (HTTP backend)");
                    await LoadGamesFromPublicMirrorAsync();
                }
                else
                {
                    // Load full game list from named remote (doesn't require device)
                    await LoadGamesFromRemoteAsync(SelectedRemote);
                }
            }
            else
            {
                // No remote selected, refresh dummy data
                await UpdateGamesList();
                ProgressStatusText = "Game list refreshed (dummy data)";
            }

            // If device is connected, also refresh installed apps status
            if (!string.IsNullOrEmpty(Adb.DeviceId))
            {
                await RefreshInstalledAppsStatusAsync();
            }

            if (_dialogService != null)
            {
                await _dialogService.ShowInfoAsync(
                    $"Game list refreshed!\n\nTotal games: {Games.Count}\nRemote: {SelectedRemote ?? "None"}",
                    "Refresh Complete");
            }
        });
    }

    private async Task ListApkAsync()
    {
        await ExecuteCommandAsync("Refresh Update List", async () =>
        {
            if (!await EnsureDeviceConnectedAsync("No device connected. Please connect your device first.\n\nThis feature checks which installed games have updates available."))
                return;

            // Warn user this may take time
            if (_dialogService != null)
            {
                var proceed = await _dialogService.ShowConfirmationAsync(
                    "This will check your installed games for updates.\n\nNOTE: THIS MAY TAKE UP TO 60 SECONDS.",
                    "Check for Updates?");

                if (!proceed) return;
            }

            ProgressStatusText = "Checking installed games for updates...";

            // Refresh installed apps status to detect updates
            await RefreshInstalledAppsStatusAsync();

            // Count games with updates
            var gamesWithUpdates = _allGames.Count(g => g.HasUpdate);
            var upToDate = _allGames.Count(g => g.IsInstalled && !g.HasUpdate);

            ProgressStatusText = "Update check complete";

            if (_dialogService != null)
            {
                var message = gamesWithUpdates > 0
                    ? $"Updates available for {gamesWithUpdates} game(s)!\n\n{upToDate} game(s) are up to date."
                    : $"All your games are up to date!\n\n{upToDate} game(s) checked.";

                await _dialogService.ShowInfoAsync(message, "Update Check Complete");
            }

            Logger.Log($"Update check complete: {gamesWithUpdates} updates available, {upToDate} up to date");
        });
    }

    private async Task PullAppToDesktopAsync()
    {
        await ExecuteCommandAsync("Pull App To Desktop", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Get package name and game name from either SelectedGame or SelectedInstalledApp
            string packageName;
            string gameName;
            long versionCode = 0;

            // Try SelectedGame first (from main game list), then SelectedInstalledApp (from dropdown)
            if (SelectedGame != null)
            {
                packageName = SelectedGame.PackageName;
                gameName = SelectedGame.GameName;
                versionCode = SelectedGame.InstalledVersionCode;
            }
            else if (!string.IsNullOrEmpty(SelectedInstalledApp))
            {
                // User selected from installed apps dropdown
                var matchingGame = _allGames.FirstOrDefault(g =>
                    g.GameName.Equals(SelectedInstalledApp, StringComparison.OrdinalIgnoreCase));

                if (matchingGame != null)
                {
                    packageName = matchingGame.PackageName;
                    gameName = matchingGame.GameName;
                    versionCode = matchingGame.InstalledVersionCode;
                }
                else
                {
                    // If not in our game list, assume the dropdown entry is the package name
                    packageName = SelectedInstalledApp;
                    gameName = SelectedInstalledApp;
                }
            }
            else
            {
                // Nothing selected - show error
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Please select an installed app from the dropdown or select a game from the list.",
                        "No App Selected");
                }
                return;
            }

            ProgressStatusText = $"Pulling {gameName} data to desktop...";

            // Run all blocking operations in background thread
            var (success, zipPath, zipFileName, errorMessage) = await Task.Run(async () =>
            {
                try
                {
                    // Get the APK path on device
                    var apkPath = Adb.GetPackageApkPath(packageName);

                    if (string.IsNullOrEmpty(apkPath))
                    {
                        return (false, "", "", $"Could not find package {packageName} on device.");
                    }

                    // Create output directory on desktop
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    var outputDir = Path.Combine(desktopPath, packageName);
                    FileSystemUtilities.EnsureDirectoryExists(outputDir);

                    // Pull APK
                    var apkOutput = Path.Combine(outputDir, "base.apk");
                    var pullApkResult = Adb.RunAdbCommandToString($"pull \"{apkPath}\" \"{apkOutput}\"");

                    if (!string.IsNullOrEmpty(pullApkResult.Error) && !pullApkResult.Error.Contains("skipping"))
                    {
                        Logger.Log($"APK pull error: {pullApkResult.Error}", LogLevel.Warning);
                    }
                    else
                    {
                        Logger.Log($"APK pulled successfully to {apkOutput}");
                    }

                    // Pull OBB files if they exist
                    var obbPath = $"/sdcard/Android/obb/{packageName}/";
                    var obbCheckResult = Adb.RunAdbCommandToString($"shell ls \"{obbPath}\"");

                    if (!obbCheckResult.Output.Contains("No such file"))
                    {
                        var obbOutputDir = Path.Combine(outputDir, "obb");
                        FileSystemUtilities.EnsureDirectoryExists(obbOutputDir);
                        var pullObbResult = Adb.RunAdbCommandToString($"pull \"{obbPath}\" \"{obbOutputDir}\"");

                        if (!string.IsNullOrEmpty(pullObbResult.Error) && !pullObbResult.Error.Contains("skipping"))
                        {
                            Logger.Log($"OBB pull error: {pullObbResult.Error}", LogLevel.Warning);
                        }
                        else
                        {
                            Logger.Log("OBB files pulled successfully");
                        }
                    }

                    // Pull app data if accessible (may require root)
                    var dataPath = $"/sdcard/Android/data/{packageName}/";
                    var dataCheckResult = Adb.RunAdbCommandToString($"shell ls \"{dataPath}\"");

                    if (!dataCheckResult.Output.Contains("No such file"))
                    {
                        var dataOutputDir = Path.Combine(outputDir, "data");
                        FileSystemUtilities.EnsureDirectoryExists(dataOutputDir);
                        var pullDataResult = Adb.RunAdbCommandToString($"pull \"{dataPath}\" \"{dataOutputDir}\"");

                        if (!string.IsNullOrEmpty(pullDataResult.Error) && !pullDataResult.Error.Contains("skipping"))
                        {
                            Logger.Log($"Data pull error: {pullDataResult.Error}", LogLevel.Warning);
                        }
                        else
                        {
                            Logger.Log("App data pulled successfully");
                        }
                    }

                    // Create zip archive of the pulled app (matching original behavior)
                    var versionCodeStr = versionCode > 0 ? versionCode.ToString() : "unknown";
                    var zipFileName = $"{gameName} v{versionCodeStr} {packageName}.zip";
                    var zipPath = Path.Combine(desktopPath, zipFileName);

                    // Remove existing zip if it exists
                    FileSystemUtilities.DeleteFileIfExists(zipPath);

                    // Create archive of the output directory
                    await Zip.CreateArchive(zipPath, $"{outputDir}/*");

                    // Delete the temporary folder now that we have the zip
                    Directory.Delete(outputDir, true);

                    return (true, zipPath, zipFileName, "");
                }
                catch (Exception ex)
                {
                    return (false, "", "", ex.Message);
                }
            });

            if (!success)
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(errorMessage);
                }
                return;
            }

            ProgressStatusText = "App pulled to desktop successfully!";

            if (_dialogService != null)
            {
                await _dialogService.ShowInfoAsync(
                    $"{gameName} pulled to:\n\n{zipFileName}\n\nOn your desktop!",
                    "Pull Complete");
            }

            Logger.Log($"Pulled app to desktop: {zipPath}");
        });
    }

    private async Task CopyBulkObbAsync()
    {
        await ExecuteCommandAsync("Copy Bulk OBB", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Open folder picker to select source OBB folder
            var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select OBB Folder (with multiple game folders inside)",
                AllowMultiple = false
            };

            var window = GetMainWindow();
            if (window == null) return;

            var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

            if (result is { Count: > 0 })
            {
                var sourcePath = result[0].Path.LocalPath;
                Logger.Log($"Selected bulk OBB folder: {sourcePath}");

                // Get all subdirectories (each should be a package name) - in background
                var packageFolders = await Task.Run(() => Directory.GetDirectories(sourcePath));

                if (packageFolders.Length == 0)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No package folders found in the selected directory.",
                            "No Folders");
                    }
                    return;
                }

                // Confirm bulk copy
                if (_dialogService != null)
                {
                    var confirmed = await _dialogService.ShowConfirmationAsync(
                        $"Found {packageFolders.Length} package folder(s).\n\nThis will recursively copy all OBB files to the device.\n\nContinue?",
                        "Confirm Bulk Copy");

                    if (!confirmed) return;
                }

                ShowProgress($"Recursively copying OBB files from {packageFolders.Length} folders...");

                // Use recursive copy which handles nested folder structures - in background
                // This matches the original behavior and is more robust
                var copyResult = await Task.Run(() => SideloaderUtilities.RecursiveCopyObb(sourcePath));

                HideProgress();

                var hasErrors = !string.IsNullOrEmpty(copyResult.Error);
                ProgressStatusText = hasErrors ? "Bulk copy completed with errors" : "Bulk copy complete!";

                if (_dialogService != null)
                {
                    var message = $"Bulk OBB copy complete!\n\n" +
                                  $"Found {packageFolders.Length} package folder(s).\n\n";

                    if (hasErrors)
                    {
                        message += $"Errors encountered:\n{copyResult.Error}\n\n";
                    }

                    message += "Check logs for details.";

                    if (hasErrors)
                    {
                        await _dialogService.ShowWarningAsync(message, "Bulk Copy Complete");
                    }
                    else
                    {
                        await _dialogService.ShowInfoAsync(message, "Bulk Copy Complete");
                    }
                }

                Logger.Log($"Bulk OBB recursive copy complete. Errors: {(hasErrors ? "Yes" : "None")}");
            }
        });
    }

    private async Task BackupGameDataAsync()
    {
        await ExecuteCommandAsync("Backup Gamedata", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Get package name and game name from either SelectedGame or SelectedInstalledApp
            string packageName;
            string gameName;

            // Try SelectedGame first (from main game list), then SelectedInstalledApp (from dropdown)
            if (SelectedGame != null)
            {
                packageName = SelectedGame.PackageName;
                gameName = SelectedGame.GameName;
            }
            else if (!string.IsNullOrEmpty(SelectedInstalledApp))
            {
                // User selected from installed apps dropdown
                var matchingGame = _allGames.FirstOrDefault(g =>
                    g.GameName.Equals(SelectedInstalledApp, StringComparison.OrdinalIgnoreCase));

                if (matchingGame != null)
                {
                    packageName = matchingGame.PackageName;
                    gameName = matchingGame.GameName;
                }
                else
                {
                    // If not in our game list, assume the dropdown entry is the package name
                    packageName = SelectedInstalledApp;
                    gameName = SelectedInstalledApp;
                }
            }
            else
            {
                // Nothing selected - show error
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Please select an installed app from the dropdown or select a game from the list.",
                        "No App Selected");
                }
                return;
            }

            // Select backup location
            var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select Backup Location",
                AllowMultiple = false
            };

            var window = GetMainWindow();
            if (window == null) return;

            var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

            if (result is { Count: > 0 })
            {
                var backupPath = result[0].Path.LocalPath;
                ProgressStatusText = $"Backing up {gameName} data...";

                // Run all blocking operations in background thread
                var gameBackupDir = await Task.Run(() =>
                {
                    var gameBackupDir = Path.Combine(backupPath, packageName);
                    FileSystemUtilities.EnsureDirectoryExists(gameBackupDir);

                    // Backup OBB files
                    var obbSourcePath = $"/sdcard/Android/obb/{packageName}/";
                    var obbCheckResult = Adb.RunAdbCommandToString($"shell ls \"{obbSourcePath}\"");

                    if (!obbCheckResult.Output.Contains("No such file"))
                    {
                        var obbBackupDir = Path.Combine(gameBackupDir, "obb");
                        FileSystemUtilities.EnsureDirectoryExists(obbBackupDir);
                        var pullObbResult = Adb.RunAdbCommandToString($"pull \"{obbSourcePath}\" \"{obbBackupDir}\"");

                        if (!string.IsNullOrEmpty(pullObbResult.Error) && !pullObbResult.Error.Contains("skipping"))
                        {
                            Logger.Log($"OBB backup error: {pullObbResult.Error}", LogLevel.Warning);
                        }
                        else
                        {
                            Logger.Log($"OBB backup successful: {pullObbResult.Output}");
                        }
                    }

                    // Backup app data from /sdcard/Android/data/
                    var dataSourcePath = $"/sdcard/Android/data/{packageName}/";
                    var dataCheckResult = Adb.RunAdbCommandToString($"shell ls \"{dataSourcePath}\"");

                    if (!dataCheckResult.Output.Contains("No such file"))
                    {
                        var dataBackupDir = Path.Combine(gameBackupDir, "data");
                        FileSystemUtilities.EnsureDirectoryExists(dataBackupDir);
                        var pullDataResult = Adb.RunAdbCommandToString($"pull \"{dataSourcePath}\" \"{dataBackupDir}\"");

                        if (!string.IsNullOrEmpty(pullDataResult.Error) && !pullDataResult.Error.Contains("skipping"))
                        {
                            Logger.Log($"Data backup error: {pullDataResult.Error}", LogLevel.Warning);
                        }
                        else
                        {
                            Logger.Log($"Data backup successful: {pullDataResult.Output}");
                        }
                    }

                    return gameBackupDir;
                });

                ProgressStatusText = "Gamedata backup complete!";

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        $"Gamedata backed up successfully!\n\nLocation:\n{gameBackupDir}\n\nContents:\n- obb/ (if exists)\n- data/ (if exists)",
                        "Backup Complete");
                }

                Logger.Log($"Gamedata backed up to: {gameBackupDir}");
            }
        });
    }

    private async Task RestoreGameDataAsync()
    {
        await ExecuteCommandAsync("Restore Gamedata", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Select backup folder to restore from
            var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select Backup Folder (should contain obb/ and/or data/ folders)",
                AllowMultiple = false
            };

            var window = GetMainWindow();
            if (window == null) return;

            var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

            if (result is { Count: > 0 })
            {
                var backupPath = result[0].Path.LocalPath;
                var packageName = Path.GetFileName(backupPath);

                Logger.Log($"Restoring gamedata from: {backupPath}");

                // Check for obb and data folders (quick check, not blocking)
                var obbBackupPath = Path.Combine(backupPath, "obb");
                var dataBackupPath = Path.Combine(backupPath, "data");

                var (hasObb, hasData) = await Task.Run(() =>
                    (Directory.Exists(obbBackupPath), Directory.Exists(dataBackupPath)));

                if (!hasObb && !hasData)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No 'obb' or 'data' folders found in the selected backup.\n\nPlease select a valid backup folder.",
                            "Invalid Backup");
                    }
                    return;
                }

                // Confirm restore
                if (_dialogService != null)
                {
                    var contents = hasObb && hasData ? "OBB and Data" : hasObb ? "OBB only" : "Data only";
                    var confirmed = await _dialogService.ShowConfirmationAsync(
                        $"Restore gamedata for:\n{packageName}\n\nContents: {contents}\n\nThis will overwrite existing data on the device.\n\nContinue?",
                        "Confirm Restore");

                    if (!confirmed) return;
                }

                ProgressStatusText = $"Restoring gamedata for {packageName}...";

                // Run all blocking operations in background thread
                await Task.Run(() =>
                {
                    // Restore OBB files
                    if (hasObb)
                    {
                        var obbDestPath = $"/sdcard/Android/obb/{packageName}/";

                        // Create OBB directory on device
                        var mkdirObbResult = Adb.RunAdbCommandToString($"shell mkdir -p \"{obbDestPath}\"");
                        if (!string.IsNullOrEmpty(mkdirObbResult.Error))
                        {
                            Logger.Log($"Failed to create OBB directory: {mkdirObbResult.Error}", LogLevel.Error);
                        }

                        // Push all files in obb backup folder
                        var obbFiles = Directory.GetFiles(obbBackupPath, "*", SearchOption.AllDirectories);
                        foreach (var obbFile in obbFiles)
                        {
                            var fileName = Path.GetFileName(obbFile);
                            var pushResult = Adb.RunAdbCommandToString($"push \"{obbFile}\" \"{obbDestPath}{fileName}\"");

                            if (!string.IsNullOrEmpty(pushResult.Error) && !pushResult.Error.Contains("skipping"))
                            {
                                Logger.Log($"Failed to push OBB file {fileName}: {pushResult.Error}", LogLevel.Warning);
                            }
                            else
                            {
                                Logger.Log($"Pushed OBB file: {fileName}");
                            }
                        }
                    }

                    // Restore data files
                    if (hasData)
                    {
                        var dataDestPath = $"/sdcard/Android/data/{packageName}/";

                        // Create data directory on device
                        var mkdirDataResult = Adb.RunAdbCommandToString($"shell mkdir -p \"{dataDestPath}\"");
                        if (!string.IsNullOrEmpty(mkdirDataResult.Error))
                        {
                            Logger.Log($"Failed to create data directory: {mkdirDataResult.Error}", LogLevel.Error);
                        }

                        // Push data folder recursively
                        var pushDataResult = Adb.RunAdbCommandToString($"push \"{dataBackupPath}/.\" \"{dataDestPath}\"");

                        if (!string.IsNullOrEmpty(pushDataResult.Error) && !pushDataResult.Error.Contains("skipping"))
                        {
                            Logger.Log($"Data restore error: {pushDataResult.Error}", LogLevel.Warning);
                        }
                        else
                        {
                            Logger.Log($"Data restore successful: {pushDataResult.Output}");
                        }
                    }
                });

                ProgressStatusText = "Gamedata restore complete!";

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        $"Gamedata restored successfully!\n\nPackage: {packageName}",
                        "Restore Complete");
                }

                Logger.Log($"Gamedata restored for: {packageName}");
            }
        });
    }


    private async Task MountRcloneAsync()
    {
        await ExecuteCommandAsync("Mount Rclone", async () =>
        {
            if (string.IsNullOrEmpty(SelectedRemote))
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowWarningAsync(
                        "Please select a remote from the dropdown first.",
                        "No Remote Selected");
                }
                return;
            }

            // Check if trying to mount public mirror
            if (Rclone.HasPublicConfig && SelectedRemote == "VRP Public Mirror")
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowWarningAsync(
                        "Cannot mount the VRP Public Mirror.\n\n" +
                        "The public mirror uses HTTP protocol and cannot be mounted as a filesystem.\n" +
                        "Files are downloaded directly when you sideload games.",
                        "Mount Not Supported");
                }
                return;
            }

            // Create mount directory - in background
            var mountPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mnt", SelectedRemote);
            await Task.Run(() => FileSystemUtilities.EnsureDirectoryExists(mountPath));
            Logger.Log($"Mount directory ready: {mountPath}");

            // Check if rclone mount is supported on this platform
            var platformNote = "";
            if (PlatformHelper.IsWindows)
            {
                platformNote = "Note: Mounting on Windows requires WinFSP to be installed.\n" +
                               "Download from: https://winfsp.dev/rel/\n\n";
            }
            else if (PlatformHelper.IsMacOs)
            {
                platformNote = "Note: Mounting on macOS requires macFUSE to be installed.\n" +
                               "Download from: https://osxfuse.github.io/\n\n";
            }
            else if (PlatformHelper.IsLinux)
            {
                platformNote = "Note: Mounting on Linux requires FUSE to be installed.\n" +
                               "Install with: sudo apt-get install fuse (Ubuntu/Debian)\n\n";
            }

            // Warn user about prerequisites
            if (_dialogService != null)
            {
                var shouldContinue = await _dialogService.ShowConfirmationAsync(
                    $"{platformNote}" +
                    $"This will mount remote '{SelectedRemote}' to:\n{mountPath}\n\n" +
                    $"The mount will run in the background until you close Rookie or unmount it manually.\n\n" +
                    $"Continue with mount?",
                    "Mount Remote");

                if (!shouldContinue)
                {
                    Logger.Log("User cancelled mount operation");
                    return;
                }
            }

            ProgressStatusText = $"Mounting {SelectedRemote}...";
            Logger.Log($"Mounting {SelectedRemote} to {mountPath}");

            try
            {
                // Attempt to mount the remote
                await Rclone.MountRemote(SelectedRemote, mountPath);

                // Give mount a moment to initialize
                await Task.Delay(2000);

                // Verify mount worked by checking if directory is accessible
                var mountSuccess = Directory.Exists(mountPath);

                if (mountSuccess)
                {
                    ProgressStatusText = $"{SelectedRemote} mounted successfully";
                    Logger.Log($"Successfully mounted {SelectedRemote} at {mountPath}");

                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"Remote mounted successfully!\n\n" +
                            $"Remote: {SelectedRemote}\n" +
                            $"Mount point: {mountPath}\n\n" +
                            $"You can now access the remote files at the mount point.\n" +
                            $"The mount will remain active until you close Rookie.",
                            "Mount Successful");
                    }

                    // Optionally open the mount directory
                    if (PlatformHelper.IsWindows)
                    {
                        Process.Start("explorer.exe", mountPath);
                    }
                    else if (PlatformHelper.IsMacOs)
                    {
                        Process.Start("open", mountPath);
                    }
                    else if (PlatformHelper.IsLinux)
                    {
                        Process.Start("xdg-open", mountPath);
                    }
                }
                else
                {
                    throw new Exception("Mount command completed but directory is not accessible");
                }
            }
            catch (Exception mountEx)
            {
                Logger.Log($"Mount failed: {mountEx.Message}", LogLevel.Error);

                // Check for common FUSE/WinFSP missing errors
                var errorMessage = mountEx.Message.ToLower();
                if (errorMessage.Contains("fuse") || errorMessage.Contains("winfsp") || errorMessage.Contains("not found"))
                {
                    var requiredSoftware = PlatformHelper.IsWindows ? "WinFSP" :
                        PlatformHelper.IsMacOs ? "macFUSE" : "FUSE";

                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Mount failed - {requiredSoftware} not installed\n\n" +
                            $"{platformNote}" +
                            $"Error details:\n{mountEx.Message}",
                            "Mount Failed");
                    }
                }
                else
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Failed to mount remote.\n\n" +
                            $"Error: {mountEx.Message}\n\n" +
                            $"{platformNote}",
                            "Mount Failed");
                    }
                }

                ProgressStatusText = "Mount failed";
            }
        });
    }

    private async Task DownloadInstallGameAsync()
    {
        Logger.Log("Download and Install Game command triggered");

        try
        {
            // Check if a game is selected
            if (SelectedGame == null)
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowWarningAsync(
                        "Please select a game to download and install.",
                        "No Game Selected");
                }
                return;
            }

            // If already downloading, add to queue instead of starting immediately
            if (_isDownloading)
            {
                Logger.Log($"Download in progress, adding {SelectedGame.GameName} to queue");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!GamesQueue.Contains(SelectedGame.GameName))
                    {
                        GamesQueue.Add(SelectedGame.GameName);
                        Logger.Log($"Added to queue: {SelectedGame.GameName}");
                    }
                    else
                    {
                        Logger.Log($"Game already in queue: {SelectedGame.GameName}");
                    }
                });
                return;
            }

            // IMPORTANT: Capture the game to download NOW, before SelectedGame can change
            // If user clicks another game during download, SelectedGame changes but this won't
            var gameToDownload = SelectedGame;

            // Set downloading flag and add current game to queue display
            // BUT: if it's already at the front of the queue, we're being called from ProcessNextQueueItem
            // and shouldn't add it again (to avoid infinite loop)
            _isDownloading = true;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Only add to queue if it's not already the first item
                // (if it is, we're processing from the queue and it will be removed when done)
                if (GamesQueue.Count == 0 || GamesQueue[0] != gameToDownload.GameName)
                {
                    GamesQueue.Add(gameToDownload.GameName);
                    Logger.Log($"Started download: {gameToDownload.GameName}");
                }
                else
                {
                    Logger.Log($"Processing from queue: {gameToDownload.GameName}");
                }
            });

            // Check device connection unless in NoDevice mode
            if (!SettingsManager.Instance.NodeviceMode && string.IsNullOrEmpty(Adb.DeviceId))
            {
                if (_dialogService != null)
                {
                    var enableNoDevice = await _dialogService.ShowConfirmationAsync(
                        "No device connected.\n\nDo you want to enable No Device Mode to download without installing?",
                        "No Device");

                    if (enableNoDevice)
                    {
                        SettingsManager.Instance.NodeviceMode = true;
                        SettingsManager.Instance.Save();
                    }
                    else
                    {
                        // User cancelled - clear flag and process next
                        await CompleteCurrentDownloadAndProcessNextAsync();
                        return;
                    }
                }
                else
                {
                    // No dialog service - clear flag and process next
                    await CompleteCurrentDownloadAndProcessNextAsync();
                    return;
                }
            }

            // Check if remote is selected
            if (string.IsNullOrEmpty(SelectedRemote))
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowWarningAsync(
                        "Please select a remote from the dropdown.",
                        "No Remote Selected");
                }
                // User error - clear flag and process next
                await CompleteCurrentDownloadAndProcessNextAsync();
                return;
            }

            ShowProgress($"Downloading {SelectedGame.GameName}...");
            Logger.Log($"Starting download for {SelectedGame.GameName} from {SelectedRemote}");

            // Get downloads folder - matches original: settings.DownloadDir
            var downloadsPath = SettingsManager.Instance.DownloadDir;
            Logger.Log($"Using download directory: {downloadsPath}");

            // Ensure download directory exists and calculate hash - in background
            var (hashDownloadPath, gameNameHash) = await Task.Run(() =>
            {
                if (!Directory.Exists(downloadsPath))
                {
                    Directory.CreateDirectory(downloadsPath!);
                    Logger.Log($"Created download directory: {downloadsPath}");
                }

                // Calculate MD5 hash of game name (matching original behavior)
                var bytes = Encoding.UTF8.GetBytes(gameToDownload.ReleaseName + "\n");
                var hash = MD5.HashData(bytes);
                var sb = new StringBuilder();
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                var gameNameHash = sb.ToString();

                Logger.Log($"Public mirror MD5 hash for {gameToDownload.ReleaseName}: {gameNameHash}");

                // Download to hash folder
                var hashDownloadPath = Path.Combine(downloadsPath, gameNameHash);
                Directory.CreateDirectory(hashDownloadPath);
                return (hashDownloadPath, gameNameHash);
            });

            // Use ReleaseName for folder (matches original which uses gameName)
            var gameDownloadPath = Path.Combine(downloadsPath, SelectedGame.ReleaseName);

            string localApkPath;

            // PUBLIC MIRROR: Uses 7z archives, different download flow
            if (SelectedRemote == "VRP Public Mirror")
            {

                try
                {
                    // Download entire game folder from public mirror (contains 7z archive parts)
                    // Don't call ShowProgress here - let OnProgress callback handle all updates from the start
                    // Just ensure progress is visible (on UI thread to avoid racing with OnProgress)
                    await Dispatcher.UIThread.InvokeAsync(() => IsProgressVisible = true);
                    var remotePath = $":http:/{gameNameHash}/";

                    // Set game name for progress display
                    Rclone.CurrentGameName = gameToDownload.GameName;

                    // Progress updates will come from OnProgress callback via stderr parsing
                    // Using --log-level INFO --stats=500ms to get progress as separate log lines
                    // (--progress/-P uses carriage returns which don't work with BeginErrorReadLine)
                    var downloadResult = await Rclone.runRcloneCommand_PublicConfig(
                        $"copy \"{remotePath}\" \"{hashDownloadPath}\" --log-level INFO --stats=500ms");

                    // Check for actual download errors (rclone writes INFO/progress to stderr, not errors)
                    // Only treat as error if stderr contains actual error keywords
                    if (!string.IsNullOrEmpty(downloadResult.Error))
                    {
                        // Check for real error indicators (ignore INFO, NOTICE, Transferred, etc.)
                        var hasActualError = downloadResult.Error.Contains("ERROR:") ||
                                            downloadResult.Error.Contains("Failed to") ||
                                            downloadResult.Error.Contains("error:") ||
                                            (downloadResult.Error.Contains("NOTICE:") &&
                                             !downloadResult.Error.Contains("Config file") &&
                                             !downloadResult.Error.Contains("not found - using defaults"));

                        if (hasActualError)
                        {
                            Logger.Log($"Download error: {downloadResult.Error}", LogLevel.Error);
                            throw new Exception($"Failed to download game: {downloadResult.Error}");
                        }

                        // If stderr only contains INFO/progress messages, that's normal - log but don't fail
                        Logger.Log($"Rclone stderr (not an error): {downloadResult.Error.Substring(0, Math.Min(200, downloadResult.Error.Length))}...", LogLevel.Debug);
                    }

                    Logger.Log("Download complete, extracting archive...");

                    // Brief delay to let final progress update be visible
                    await Task.Delay(500);

                    // Switch to extraction progress (clear speed/ETA since extraction doesn't use rclone)
                    UpdateProgress(60, "Extracting archive...", clearSpeedAndEta: true);

                    // Find the .7z.001 file (multi-part archive)
                    var archiveFile = Path.Combine(hashDownloadPath, $"{gameNameHash}.7z.001");
                    if (!File.Exists(archiveFile))
                    {
                        // Try single-part archive
                        archiveFile = Path.Combine(hashDownloadPath, $"{gameNameHash}.7z");
                    }

                    if (!File.Exists(archiveFile))
                    {
                        throw new FileNotFoundException($"Archive file not found in {hashDownloadPath}");
                    }

                    // Extract with password from PublicConfig (extract to Downloads/, not subfolder)
                    var password = Rclone.PublicConfigFile?.Password;
                    if (string.IsNullOrEmpty(password))
                    {
                        Logger.Log("Warning: No password found in PublicConfig, trying without password", LogLevel.Warning);
                    }

                    // Extract to Downloads/ folder - the archive contains the game folder structure
                    await Zip.ExtractArchive(archiveFile, downloadsPath, password);

                    Logger.Log($"Extraction complete to {downloadsPath}");
                    UpdateProgress(80, "Extraction complete");

                    // Clean up hash folder and find APK - run in background
                    localApkPath = await Task.Run(() =>
                    {
                        if (Directory.Exists(hashDownloadPath))
                        {
                            Directory.Delete(hashDownloadPath, true);
                            Logger.Log($"Cleaned up temporary download folder: {hashDownloadPath}");
                        }

                        // The archive should have created a folder with the game name (ReleaseName)
                        // Update gameDownloadPath to point to the extracted folder
                        var extractedPath = Path.Combine(downloadsPath, gameToDownload.ReleaseName);

                        // Find the APK file in extracted content
                        var apkFiles = Directory.GetFiles(extractedPath, "*.apk", SearchOption.AllDirectories);
                        if (apkFiles.Length > 0)
                        {
                            Logger.Log($"Found APK: {Path.GetFileName(apkFiles[0])}");
                            return apkFiles[0];
                        }

                        return null;
                    });

                    gameDownloadPath = Path.Combine(downloadsPath, gameToDownload.ReleaseName);
                }
                catch (Exception ex)
                {
                    HideProgress();
                    Rclone.CurrentGameName = null; // Clear game name on error
                    Logger.Log($"Public mirror download failed: {ex.Message}", LogLevel.Error);
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Failed to download from public mirror:\n{ex.Message}",
                            "Download Error");
                    }
                    // Download failed - clear flag and process next
                    await CompleteCurrentDownloadAndProcessNextAsync();
                    return;
                }
            }
            // REGULAR REMOTES: Download entire game folder (matching original behavior)
            else
            {
                await Task.Run(() => Directory.CreateDirectory(gameDownloadPath));

                // Build remote path - matches original: "{currentRemote}:{RcloneGamesFolder}/{gameName}"
                var remotePath = $"{SelectedRemote}:{SideloaderRclone.RcloneGamesFolder}/{gameToDownload.ReleaseName}";

                // Don't call ShowProgress here - let OnProgress callback handle all updates from the start
                // Just ensure progress is visible (on UI thread to avoid racing with OnProgress)
                await Dispatcher.UIThread.InvokeAsync(() => IsProgressVisible = true);
                Logger.Log($"rclone copy \"{remotePath}\" \"{gameDownloadPath}\"");

                // Set game name for progress display
                Rclone.CurrentGameName = gameToDownload.GameName;

                // Download entire folder - matches original command with all flags
                // Progress updates will come from OnProgress callback via stderr parsing
                // Using --log-level INFO --stats=500ms to get progress as separate log lines
                // (--progress/-P uses carriage returns which don't work with BeginErrorReadLine)
                await Rclone.runRcloneCommand_DownloadConfig(
                    $"copy \"{remotePath}\" \"{gameDownloadPath}\" --log-level INFO --stats=500ms --retries 2 --low-level-retries 1 --check-first");

                // Find APK file in downloaded folder (matching original) - in background
                localApkPath = await Task.Run(() =>
                {
                    var downloadedFiles = Directory.GetFiles(gameDownloadPath);
                    var apkFile = downloadedFiles.FirstOrDefault(file => Path.GetExtension(file).Equals(".apk", StringComparison.OrdinalIgnoreCase));

                    if (apkFile != null)
                    {
                        Logger.Log($"Found APK: {Path.GetFileName(apkFile)}");
                        return apkFile;
                    }

                    Logger.Log("Warning: No APK file found in downloaded folder", LogLevel.Warning);
                    return null;
                });
            }

            // Install if device connected and not in NoDevice mode
            if (!SettingsManager.Instance.NodeviceMode && !string.IsNullOrEmpty(Adb.DeviceId))
            {
                UpdateProgress(85, "Installing APK...");

                if (localApkPath != null && FileSystemUtilities.FileExistsAndNotEmpty(localApkPath))
                {
                    var installResult = await Adb.SideloadAsync(localApkPath, gameToDownload.PackageName, _dialogService);
                    if (installResult.Output.Contains("Success"))
                    {
                        Logger.Log($"APK installed successfully: {Path.GetFileName(localApkPath)}");
                    }
                }

                // Check for and execute custom install script (for games with non-standard install requirements)
                var customScriptPath = SideloaderUtilities.FindCustomInstallScript(gameDownloadPath);
                if (!string.IsNullOrEmpty(customScriptPath))
                {
                    UpdateProgress(87, "Running custom install script...");
                    Logger.Log($"Found custom install script - executing: {Path.GetFileName(customScriptPath)}");

                    try
                    {
                        var scriptResult = await SideloaderUtilities.RunADBCommandsFromFileAsync(customScriptPath);

                        if (!string.IsNullOrEmpty(scriptResult.Error) &&
                            !scriptResult.Error.Contains("mkdir") &&
                            !scriptResult.Error.Contains("already exists"))
                        {
                            Logger.Log($"Custom install script completed with warnings: {scriptResult.Error}", LogLevel.Warning);

                            if (_dialogService != null)
                            {
                                await _dialogService.ShowWarningAsync(
                                    $"Custom install script completed with warnings:\n\n{scriptResult.Error}\n\n" +
                                    $"The game may still work correctly.",
                                    "Custom Install Warning");
                            }
                        }
                        else
                        {
                            Logger.Log("Custom install script completed successfully");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error running custom install script: {ex.Message}", LogLevel.Error);

                        if (_dialogService != null)
                        {
                            await _dialogService.ShowWarningAsync(
                                $"Failed to run custom install script:\n\n{ex.Message}\n\n" +
                                $"Standard installation will continue.",
                                "Custom Install Error");
                        }
                    }
                }

                // Copy OBB files (checks the gameDownloadPath for any .obb files) - in background
                var obbFilesInFolder = await Task.Run(() => Directory.GetFiles(gameDownloadPath, "*.obb", SearchOption.AllDirectories));
                if (obbFilesInFolder.Length > 0)
                {
                    UpdateProgress(90, "Copying OBB files...");
                    var obbResult = Adb.CopyObb(gameDownloadPath);

                    // Check for OBB copy errors
                    if (!string.IsNullOrEmpty(obbResult.Error) && !obbResult.Error.Contains("skipping"))
                    {
                        Logger.Log($"OBB copy error: {obbResult.Error}", LogLevel.Warning);
                    }
                    else
                    {
                        Logger.Log($"OBB files copied: {obbFilesInFolder.Length} files");
                    }

                    // Verify OBB copy completed successfully
                    UpdateProgress(95, "Verifying OBB files...");
                    var (matches, localSize, remoteSize) = await SideloaderUtilities.CompareObbSizes(
                        gameToDownload.PackageName,
                        gameDownloadPath);

                    if (!matches && remoteSize > 0)
                    {
                        Logger.Log($"OBB size mismatch detected: Local={localSize}MB, Device={remoteSize}MB", LogLevel.Warning);

                        if (_dialogService != null)
                        {
                            var retry = await _dialogService.ShowConfirmationAsync(
                                $"Warning! OBB files may not have copied correctly.\n\n" +
                                $"Expected: {localSize} MB\n" +
                                $"On Device: {remoteSize} MB\n\n" +
                                $"The game may not launch correctly. Retry OBB copy?",
                                "OBB Size Mismatch");

                            if (retry)
                            {
                                Logger.Log("Retrying OBB copy...");
                                UpdateProgress(93, "Retrying OBB copy...");
                                var retryResult = Adb.CopyObb(gameDownloadPath);

                                if (!string.IsNullOrEmpty(retryResult.Error) && !retryResult.Error.Contains("skipping"))
                                {
                                    Logger.Log($"OBB copy retry failed: {retryResult.Error}", LogLevel.Error);
                                }
                                else
                                {
                                    Logger.Log("OBB copy retried successfully");
                                }
                            }
                        }
                    }
                    else if (matches)
                    {
                        Logger.Log($"OBB verification successful: {remoteSize}MB on device");
                    }
                }

                // Push user JSON files (required for some games like Rec Room, Horizon Worlds)
                UpdateProgress(97, "Pushing user authentication files...");
                SideloaderUtilities.PushUserJsons();
                Logger.Log("User JSON files pushed to device");
            }

            HideProgress();
            ProgressStatusText = "Download complete!";

            // Track download metrics anonymously
            Metrics.CountDownload(gameToDownload.PackageName, gameToDownload.AvailableVersionCode.ToString());

            Logger.Log($"Download and install completed for {gameToDownload.GameName}");

            // Clear retry count for this game (successful download)
            if (_downloadRetryCount.Remove(gameToDownload.GameName))
            {
                Logger.Log($"Reset retry count for {gameToDownload.GameName}");
            }

            // Clear game name from progress display
            Rclone.CurrentGameName = null;

            // Download completed successfully - clear flag and process next
            // Do this BEFORE showing the dialog so queue processing isn't blocked
            await CompleteCurrentDownloadAndProcessNextAsync();

            // Show completion dialog AFTER starting next queue item (fire-and-forget so it doesn't block)
            if (_dialogService != null)
            {
                // Capture local variables for the dialog (next queue item might change these)
                var completedGameName = gameToDownload.GameName;
                var completedGamePath = gameDownloadPath;

                // Count downloaded files - in background
                var (apkCount, obbCount) = await Task.Run(() =>
                {
                    var apkCount = Directory.GetFiles(completedGamePath, "*.apk", SearchOption.AllDirectories).Length;
                    var obbCount = Directory.GetFiles(completedGamePath, "*.obb", SearchOption.AllDirectories).Length;
                    return (apkCount, obbCount);
                });
                var totalDownloaded = apkCount + obbCount;

                var message = $"Download complete!\n\nGame: {completedGameName}\n" +
                              $"Files: {totalDownloaded} (APK: {apkCount}, OBB: {obbCount})\n" +
                              $"Location: {completedGamePath}";

                if (!SettingsManager.Instance.NodeviceMode && !string.IsNullOrEmpty(Adb.DeviceId))
                {
                    message += "\n\nGame has been installed to device.";
                }

                // Fire-and-forget - don't await so queue processing continues
                _ = _dialogService.ShowInfoAsync(message, "Download Complete");
            }
        }
        catch (Exception ex)
        {
            HideProgress();
            Rclone.CurrentGameName = null; // Clear game name on error
            Logger.Log($"Error downloading game: {ex.Message}", LogLevel.Error);
            ProgressStatusText = "Error downloading game";

            // Auto-retry logic
            var gameName = GamesQueue.Count > 0 ? GamesQueue[0] : SelectedGame?.GameName ?? "Unknown";

            // Get current retry count for this game
            _downloadRetryCount.TryAdd(gameName, 0);

            _downloadRetryCount[gameName]++;
            var retryCount = _downloadRetryCount[gameName];

            if (retryCount < MaxDownloadRetries)
            {
                // Retry the download
                Logger.Log($"Download failed for '{gameName}'. Retry attempt {retryCount}/{MaxDownloadRetries}", LogLevel.Warning);
                ProgressStatusText = $"Retrying download ({retryCount}/{MaxDownloadRetries})...";

                // Wait a moment before retrying (exponential backoff: 2, 4, 8 seconds)
                var delaySeconds = Math.Pow(2, retryCount);
                Logger.Log($"Waiting {delaySeconds} seconds before retry...");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                // Clear downloading flag temporarily
                _isDownloading = false;

                // Retry the download
                await DownloadInstallGameAsync();
            }
            else
            {
                // Max retries reached - show error and move to next
                Logger.Log($"Download failed for '{gameName}' after {retryCount} attempts. Moving to next item in queue.", LogLevel.Error);
                _downloadRetryCount.Remove(gameName); // Reset retry count for this game

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        $"Error downloading '{gameName}' after {retryCount} attempts:\n\n{ex.Message}\n\nMoving to next item in queue.",
                        "Download Failed");
                }

                // Download failed after retries - clear flag and process next
                await CompleteCurrentDownloadAndProcessNextAsync();
            }
        }
    }

    /// <summary>
    /// Complete the current download and process the next queued item
    /// </summary>
    private async Task CompleteCurrentDownloadAndProcessNextAsync()
    {
        _isDownloading = false;
        await ProcessNextQueueItem();
    }

    /// <summary>
    /// Process the next game in the download queue
    /// </summary>
    private async Task ProcessNextQueueItem()
    {
        // Remove the completed game from queue (first item)
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (GamesQueue.Count > 0)
            {
                var completed = GamesQueue[0];
                GamesQueue.RemoveAt(0);
                Logger.Log($"Removed completed game from queue: {completed}");
            }
        });

        // Check if there are more games to download
        if (GamesQueue.Count > 0)
        {
            var nextGameName = GamesQueue[0];
            Logger.Log($"Processing next queued game: {nextGameName}");

            // Find the game in the games list
            var nextGame = _allGames.FirstOrDefault(g => g.GameName == nextGameName);
            if (nextGame != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SelectedGame = nextGame;
                    Logger.Log($"Found game in list: {nextGame.GameName}");
                });

                // Start downloading the next game
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await DownloadInstallGameAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error processing queued game: {ex.Message}", LogLevel.Error);
                    }
                });
            }
            else
            {
                Logger.Log($"Could not find game in list: {nextGameName}", LogLevel.Warning);
                // Remove from queue if not found and try next
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    GamesQueue.RemoveAt(0);
                });
                // Recursively process next item without blocking
                await ProcessNextQueueItem();
            }
        }
        else
        {
            Logger.Log("Queue is empty, no more games to download");
        }
    }

    private async Task DisableSideloadingAsync()
    {
        await ExecuteCommandAsync("Disable Sideloading", async () =>
        {
            if (!await EnsureDeviceConnectedAsync()) return;

            // Confirm with user
            if (_dialogService != null)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "This will disable developer mode and sideloading on your device.\n\n" +
                    "You will need to re-enable it in your Quest settings to sideload apps again.\n\n" +
                    "Continue?",
                    "Disable Sideloading");

                if (!confirmed)
                {
                    return;
                }
            }

            ProgressStatusText = "Disabling sideloading...";

            // Disable USB debugging (this requires user confirmation on device)
            var result = Adb.RunAdbCommandToString("shell settings put global adb_enabled 0");

            if (!string.IsNullOrEmpty(result.Error))
            {
                Logger.Log($"Disable sideloading error: {result.Error}", LogLevel.Warning);
                ProgressStatusText = "Failed to disable sideloading";
            }
            else
            {
                Logger.Log("Sideloading disabled successfully");
                ProgressStatusText = "Sideloading disabled";
            }

            if (_dialogService != null)
            {
                await _dialogService.ShowInfoAsync(
                    "Sideloading has been disabled on your device.\n\n" +
                    "To re-enable, go to Settings > Developer on your Quest.",
                    "Sideloading Disabled");
            }

            Logger.Log("Sideloading disabled on device");
        });
    }

    private async Task ShowGamesListAsync()
    {
        await ExecuteCommandAsync("Games List toggle", async () =>
        {
            // Toggle between showing all games and showing only favorites
            ShowFavoritesOnly = !ShowFavoritesOnly;

            Logger.Log($"Games list filter toggled - ShowFavoritesOnly: {ShowFavoritesOnly}");

            // ApplyFilters() is automatically called by the ShowFavoritesOnly property setter
            await Task.CompletedTask;
        });
    }

    private async Task ShowAboutAsync()
    {
        await ExecuteCommandAsync("Show About", async () =>
        {
            var aboutMessage = $"Version: {Updater.LocalVersion}\n\n" +
                               " - Software originally coded by rookie.wtf\n" +
                               " - Thanks to the VRP Mod Staff, data team, and anyone else we missed!\n" +
                               " - Thanks to VRP staff of the present and past: fenopy, Maxine, JarJarBlinkz\n" +
                               "        pmow, SytheZN, Roma/Rookie, Flow, Ivan, Kaladin, HarryEffinPotter, John, Sam Hoque\n\n" +
                               " - Additional Thanks and Credits:\n" +
                               " - -- rclone https://rclone.org/\n" +
                               " - -- 7zip https://www.7-zip.org/\n" +
                               " - -- ErikE: https://stackoverflow.com/users/57611/erike\n" +
                               " - -- Serge Weinstock (SergeUtils)\n" +
                               " - -- Mike Gold https://www.c-sharpcorner.com/members/mike-gold2\n";

            if (_dialogService != null)
            {
                await _dialogService.ShowInfoAsync(aboutMessage, "About Rookie Sideloader");
            }

            Logger.Log("About dialog displayed");
            await Task.CompletedTask;
        });
    }

    private async Task ShowSettingsAsync()
    {
        await ExecuteCommandAsync("Show Settings", async () =>
        {
            var window = GetMainWindow();
            if (window == null)
            {
                Logger.Log("Cannot get main window for settings dialog", LogLevel.Error);
                return;
            }

            // Create and show settings window
            var settingsWindow = new Views.SettingsWindow();
            await settingsWindow.ShowDialog(window);

            // Check if settings were saved
            if (settingsWindow.SettingsSaved)
            {
                Logger.Log("Settings saved successfully");

                // Refresh TrailersOn property from settings
                TrailersOn = SettingsManager.Instance.TrailersOn;
                Logger.Log($"TrailersOn refreshed: {TrailersOn}");

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        "Settings saved successfully!\n\nChanges will take effect immediately.",
                        "Settings Saved");
                }
            }
            else
            {
                Logger.Log("Settings dialog cancelled");
            }
        });
    }

    private async Task ShowQuestOptionsAsync()
    {
        await ExecuteCommandAsync("Show Quest Options", async () =>
        {
            var window = GetMainWindow();
            if (window == null)
            {
                Logger.Log("Cannot get main window for Quest Options dialog", LogLevel.Error);
                return;
            }

            // Create and show Quest Options window
            var questOptionsWindow = new Views.QuestOptionsWindow();
            await questOptionsWindow.ShowDialog(window);

            Logger.Log("Quest Options dialog closed");
        });
    }

    /// <summary>
    /// Handle files dropped onto the window
    /// </summary>
    public async Task HandleDroppedFilesAsync(List<string> filePaths)
    {
        Logger.Log($"Files dropped: {filePaths.Count}");

        try
        {
            // Check device connection
            if (string.IsNullOrEmpty(Adb.DeviceId) && !SettingsManager.Instance.NodeviceMode)
            {
                if (_dialogService != null)
                {
                    var enableNoDevice = await _dialogService.ShowConfirmationAsync(
                        "No device connected.\n\nDo you want to enable No Device Mode?",
                        "No Device");

                    if (enableNoDevice)
                    {
                        SettingsManager.Instance.NodeviceMode = true;
                        SettingsManager.Instance.Save();
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            ProgressStatusText = "Processing dropped files...";

            // Categorize files by extension and type - run in background thread
            var (apkFiles, obbFolders, installTxtFiles, beatSaberSongZips) = await Task.Run(() =>
            {
                var apkFiles = new List<string>();
                var obbFolders = new List<string>();
                var installTxtFiles = new List<string>();
                var beatSaberSongZips = new List<string>();

                foreach (var path in filePaths)
                {
                    if (File.Exists(path))
                    {
                        // It's a file
                        var ext = Path.GetExtension(path).ToLower();
                        var fileName = Path.GetFileName(path).ToLower();

                        switch (ext)
                        {
                            case ".apk":
                                apkFiles.Add(path);
                                break;
                                
                            case ".obb":
                                // OBB file - add its parent directory
                                var parentDir = Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(parentDir) && !obbFolders.Contains(parentDir))
                                {
                                    obbFolders.Add(parentDir);
                                }
                                break;
                                
                            case ".zip":
                                // Could be Beat Saber custom song - we'll check if Beat Saber is selected/installed later
                                beatSaberSongZips.Add(path);
                                break;
                                
                            default:
                                if (fileName == "install.txt")
                                {
                                    installTxtFiles.Add(path);
                                }
                                break;
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        // Check for install.txt in the directory (special install instructions)
                        var installTxtPath = Path.Combine(path, "install.txt");
                        if (File.Exists(installTxtPath))
                        {
                            installTxtFiles.Add(installTxtPath);
                            continue; // Skip further processing - install.txt will handle it
                        }

                        // Check for APK files in directory
                        var dirApkFiles = Directory.GetFiles(path, "*.apk", SearchOption.TopDirectoryOnly);
                        apkFiles.AddRange(dirApkFiles);

                        // Check for OBB files or subdirectories with package name pattern
                        var obbFiles = Directory.GetFiles(path, "*.obb", SearchOption.AllDirectories);
                        if (obbFiles.Length > 0)
                        {
                            // Add each unique directory containing OBB files
                            foreach (var obbFile in obbFiles)
                            {
                                var obbDir = Path.GetDirectoryName(obbFile);
                                if (!string.IsNullOrEmpty(obbDir) && !obbFolders.Contains(obbDir))
                                {
                                    obbFolders.Add(obbDir);
                                }
                            }
                        }

                        // Check for subdirectories that look like package names (com.*.*)
                        var subDirs = Directory.GetDirectories(path);
                        foreach (var subDir in subDirs)
                        {
                            var dirName = Path.GetFileName(subDir);
                            if (dirName.StartsWith("com.") && dirName.Contains('.'))
                            {
                                // Likely a package name directory with OBB files
                                obbFolders.Add(subDir);
                            }
                        }
                    }
                }

                return (apkFiles, obbFolders, installTxtFiles, beatSaberSongZips);
            });

            // Check if Beat Saber is installed (for song installation)
            var beatSaberInstalled = false;
            if (beatSaberSongZips.Count > 0)
            {
                var installedPackages = Adb.GetInstalledPackages();
                beatSaberInstalled = installedPackages.Contains("com.beatgames.beatsaber");
            }

            // Show summary and confirmation
            var summary = "Files dropped:\n\n";
            if (installTxtFiles.Count > 0)
            {
                summary += $"Custom install scripts: {installTxtFiles.Count}\n";
            }
            if (apkFiles.Count > 0)
            {
                summary += $"APK files: {apkFiles.Count}\n";
            }
            if (beatSaberSongZips.Count > 0)
            {
                if (beatSaberInstalled)
                {
                    summary += $"Beat Saber custom songs: {beatSaberSongZips.Count}\n";
                }
                else
                {
                    summary += $"ZIP files (Beat Saber not detected): {beatSaberSongZips.Count}\n";
                }
            }
            if (obbFolders.Count > 0)
            {
                summary += $"OBB folders: {obbFolders.Count}\n";
            }

            if (installTxtFiles.Count == 0 && apkFiles.Count == 0 && obbFolders.Count == 0 && beatSaberSongZips.Count == 0)
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowWarningAsync(
                        "No APK, OBB, ZIP, or install.txt files detected in dropped items.",
                        "No Valid Files");
                }
                ProgressStatusText = string.Empty;
                return;
            }

            summary += $"\nDevice: {(string.IsNullOrEmpty(Adb.DeviceId) ? "None (No Device Mode)" : Adb.DeviceId)}\n\n";
            summary += "Do you want to install these files?";

            if (_dialogService != null)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(summary, "Install Files");
                if (!confirmed)
                {
                    ProgressStatusText = "Installation cancelled";
                    return;
                }
            }

            var totalOperations = installTxtFiles.Count + apkFiles.Count + obbFolders.Count;
            var currentOperation = 0;

            ShowProgress("Starting installation...");

            // Process install.txt files first (custom install scripts)
            foreach (var installTxtPath in installTxtFiles)
            {
                currentOperation++;
                var progressPercent = (double)currentOperation / totalOperations * 100;
                UpdateProgress(progressPercent, $"Running custom install script ({currentOperation}/{totalOperations})...");
                Logger.Log($"Running install.txt: {installTxtPath}");

                if (!SettingsManager.Instance.NodeviceMode)
                {
                    if (_dialogService != null)
                    {
                        var runScript = await _dialogService.ShowConfirmationAsync(
                            $"Special installation instructions found in:\n\n{Path.GetDirectoryName(installTxtPath)}\n\nRun custom install script?",
                            "Custom Installation");

                        if (runScript)
                        {
                            var result = await SideloaderUtilities.RunAdbCommandsFromFile(installTxtPath);
                            Logger.Log($"Install script result: {result.Output}");
                        }
                    }
                }

                await Task.Delay(500);
            }

            // Process APK files
            foreach (var apkPath in apkFiles)
            {
                currentOperation++;
                var progressPercent = (double)currentOperation / totalOperations * 100;
                var apkFileName = Path.GetFileName(apkPath);
                UpdateProgress(progressPercent, $"Installing {apkFileName} ({currentOperation}/{totalOperations})...");
                Logger.Log($"Installing APK: {apkPath}");

                if (!SettingsManager.Instance.NodeviceMode)
                {
                    var result = await Adb.SideloadAsync(apkPath, "", _dialogService);
                    if (result.Output.Contains("Success"))
                    {
                        Logger.Log($"APK installed successfully: {apkFileName}");

                        // Try to extract package name from the result
                        // The ADB.Sideload method might return package info
                        // For now, we'll check if there's a matching OBB folder in the same directory
                        var apkDir = Path.GetDirectoryName(apkPath);
                        if (!string.IsNullOrEmpty(apkDir))
                        {
                            // Look for subdirectories with package name pattern - in background
                            var subDirs = await Task.Run(() => Directory.GetDirectories(apkDir));
                            foreach (var subDir in subDirs)
                            {
                                var dirName = Path.GetFileName(subDir);
                                if (dirName.StartsWith("com.") && dirName.Contains('.'))
                                {
                                    // Found potential OBB folder - copy it
                                    Logger.Log($"Found matching OBB folder: {dirName}");
                                    UpdateProgress(progressPercent, $"Copying OBB for {dirName}...");
                                    var obbResult = Adb.CopyObb(subDir);
                                    if (obbResult.Output.Contains("Success"))
                                    {
                                        Logger.Log($"OBB copied successfully for {dirName}");
                                    }
                                    break; // Only copy the first matching OBB folder
                                }
                            }
                        }
                    }
                    else
                    {
                        Logger.Log($"APK install failed: {result.Error}", LogLevel.Warning);
                    }
                }

                await Task.Delay(500); // Small delay between installations
            }

            // Process OBB folders (that weren't already copied with APKs)
            foreach (var obbPath in obbFolders)
            {
                currentOperation++;
                var progressPercent = (double)currentOperation / totalOperations * 100;
                var obbFolderName = Path.GetFileName(obbPath);
                UpdateProgress(progressPercent, $"Copying OBB: {obbFolderName} ({currentOperation}/{totalOperations})...");
                Logger.Log($"Copying OBB folder: {obbPath}");

                if (!SettingsManager.Instance.NodeviceMode)
                {
                    var result = Adb.CopyObb(obbPath);
                    if (result.Output.Contains("Success"))
                    {
                        Logger.Log($"OBB copied successfully: {obbFolderName}");
                    }
                    else
                    {
                        Logger.Log($"OBB copy failed: {result.Error}", LogLevel.Warning);
                    }
                }

                await Task.Delay(500); // Small delay between operations
            }

            // Process Beat Saber custom songs if Beat Saber is installed
            var beatSaberSongsInstalled = 0;
            if (beatSaberInstalled && beatSaberSongZips.Count > 0)
            {
                Logger.Log($"Processing {beatSaberSongZips.Count} Beat Saber custom song(s)...");

                foreach (var songZipPath in beatSaberSongZips)
                {
                    try
                    {
                        var songFileName = Path.GetFileName(songZipPath);
                        UpdateProgress(50, $"Installing Beat Saber song: {songFileName}...");
                        Logger.Log($"Installing Beat Saber song: {songZipPath}");

                        // Create temporary extraction directory
                        var tempExtractPath = Path.Combine(Path.GetTempPath(), $"BeatSaberSong_{Guid.NewGuid()}");
                        Directory.CreateDirectory(tempExtractPath);

                        try
                        {
                            // Extract the song zip using 7-Zip
                            await Zip.ExtractArchive(songZipPath, tempExtractPath);

                            // Push extracted content to Beat Saber custom levels directory
                            var pushResult = Adb.RunAdbCommandToString(
                                $"push \"{tempExtractPath}\" \"/sdcard/ModData/com.beatgames.beatsaber/Mods/SongLoader/CustomLevels/\"");

                            if (!pushResult.Error.Contains("failed"))
                            {
                                Logger.Log($"Beat Saber song installed successfully: {songFileName}");
                                beatSaberSongsInstalled++;
                            }
                            else
                            {
                                Logger.Log($"Failed to push Beat Saber song: {pushResult.Error}", LogLevel.Warning);
                            }
                        }
                        finally
                        {
                            // Clean up temporary extraction directory
                            try
                            {
                                if (Directory.Exists(tempExtractPath))
                                {
                                    Directory.Delete(tempExtractPath, true);
                                }
                            }
                            catch (Exception cleanupEx)
                            {
                                Logger.Log($"Failed to clean up temp directory: {cleanupEx.Message}", LogLevel.Warning);
                            }
                        }

                        await Task.Delay(500);
                    }
                    catch (Exception songEx)
                    {
                        Logger.Log($"Error installing Beat Saber song: {songEx.Message}", LogLevel.Error);
                    }
                }
            }

            HideProgress();
            ProgressStatusText = "Installation complete!";

            // Show final summary
            var resultSummary = "Installation complete!\n\n";
            if (installTxtFiles.Count > 0)
            {
                resultSummary += $"Custom scripts: {installTxtFiles.Count}\n";
            }
            if (apkFiles.Count > 0)
            {
                resultSummary += $"APK files installed: {apkFiles.Count}\n";
            }
            if (obbFolders.Count > 0)
            {
                resultSummary += $"OBB folders copied: {obbFolders.Count}\n";
            }
            if (beatSaberSongsInstalled > 0)
            {
                resultSummary += $"Beat Saber songs installed: {beatSaberSongsInstalled}";
            }

            if (_dialogService != null)
            {
                await _dialogService.ShowInfoAsync(resultSummary, "Installation Complete");
            }

            // Refresh device info
            await RefreshDeviceListAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error handling dropped files: {ex.Message}", LogLevel.Error);
            HideProgress();
            ProgressStatusText = "Error processing dropped files";

            if (_dialogService != null)
            {
                await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}");
            }
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        await ExecuteCommandAsync("Toggle Favorite", async () =>
        {
            if (!await EnsureGameSelectedAsync("mark as favorite")) return;

            // Toggle favorite status
            SelectedGame.IsFavorite = !SelectedGame.IsFavorite;

            var status = SelectedGame.IsFavorite ? "added to" : "removed from";
            Logger.Log($"{SelectedGame.GameName} {status} favorites");

            ProgressStatusText = $"{SelectedGame.GameName} {status} favorites";

            // Save favorites to settings
            if (SelectedGame.IsFavorite)
            {
                SettingsManager.Instance.FavoriteGames.Add(SelectedGame.PackageName);
            }
            else
            {
                SettingsManager.Instance.FavoriteGames.Remove(SelectedGame.PackageName);
            }
            SettingsManager.Instance.Save();

            // Refresh display if showing favorites only
            if (ShowFavoritesOnly)
            {
                await ApplyFilters();
            }
        });
    }


    private void ApplyGameFilter(GameFilterType filterType)
    {
        Logger.Log($"Filter {filterType} command triggered");

        try
        {
            switch (filterType)
            {
                case GameFilterType.UpToDate:
                    if (ShowUpToDateOnly)
                    {
                        ShowUpToDateOnly = false;
                        ProgressStatusText = "Showing all games";
                        Logger.Log("Cleared Up To Date filter - showing all games");
                    }
                    else
                    {
                        ShowUpdateAvailableOnly = false;
                        ShowNewerThanListOnly = false;
                        ShowUpToDateOnly = true;

                        var upToDateCount = _allGames.Count(g => g.IsInstalled && !g.HasUpdate);
                        ProgressStatusText = $"Showing up-to-date games ({upToDateCount} games)";
                        Logger.Log($"Applied Up To Date filter - showing {upToDateCount} games");
                    }
                    break;

                case GameFilterType.UpdateAvailable:
                    if (ShowUpdateAvailableOnly)
                    {
                        ShowUpdateAvailableOnly = false;
                        ProgressStatusText = "Showing all games";
                        Logger.Log("Cleared Update Available filter - showing all games");
                    }
                    else
                    {
                        ShowUpToDateOnly = false;
                        ShowNewerThanListOnly = false;
                        ShowUpdateAvailableOnly = true;

                        var updateAvailableCount = _allGames.Count(g => g.HasUpdate);
                        ProgressStatusText = $"Showing games with updates ({updateAvailableCount} games)";
                        Logger.Log($"Applied Update Available filter - showing {updateAvailableCount} games");
                    }
                    break;

                case GameFilterType.NewerThanList:
                    if (ShowNewerThanListOnly)
                    {
                        ShowNewerThanListOnly = false;
                        ProgressStatusText = "Showing all games";
                        Logger.Log("Cleared Newer Than List filter - showing all games");
                    }
                    else
                    {
                        ShowUpToDateOnly = false;
                        ShowUpdateAvailableOnly = false;
                        ShowNewerThanListOnly = true;

                        var newerCount = _allGames.Count(g => g.IsInstalled && g.InstalledVersionCode > g.AvailableVersionCode && g.AvailableVersionCode > 0);
                        ProgressStatusText = $"Showing newer games ({newerCount} games)";
                        Logger.Log($"Applied Newer Than List filter - showing {newerCount} games");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error toggling {filterType} filter: {ex.Message}", LogLevel.Error);
        }
    }

    private void FilterUpToDate() => ApplyGameFilter(GameFilterType.UpToDate);
    private void FilterUpdateAvailable() => ApplyGameFilter(GameFilterType.UpdateAvailable);
    private void FilterNewerThanList() => ApplyGameFilter(GameFilterType.NewerThanList);

    /// <summary>
    /// Set the dialog service after construction (for MainWindow initialization)
    /// </summary>
    public void SetDialogService(IDialogService dialogService)
    {
        if (dialogService is IDialogService)
        {
            // Use reflection or just assign
            var field = GetType().GetField("_dialogService",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(this, dialogService);

            // Also set the dialog service for ADB and RCLONE classes
            Adb.SetDialogService(dialogService);
            Rclone.SetDialogService(dialogService);
        }
    }

    // ==================== UPLOAD FUNCTIONALITY ====================

    /// <summary>
    /// Process upload queue and upload games to VRPirates mirrors
    /// </summary>
    private async Task ProcessUploadQueueAsync()
    {
        await ExecuteCommandAsync("Process Upload Queue", async () =>
        {
            if (UploadQueue == null || UploadQueue.Count == 0)
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync("Upload queue is empty.\n\nUse 'Extract Game for Upload' to add games to the queue.", "No Uploads");
                }
                return;
            }

            // Count how many are queued
            var queuedGames = UploadQueue.Where(g => g.Status == UploadStatus.Queued).ToList();
            if (queuedGames.Count == 0)
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync("No games waiting in queue.\n\nAll uploads are either completed or failed.", "Nothing to Upload");
                }
                return;
            }

            if (_dialogService != null)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    $"Upload {queuedGames.Count} game(s) to VRPirates mirrors?\n\n" +
                    $"This will upload:\n" +
                    string.Join("\n", queuedGames.Take(5).Select(g => $"- {g.GameName}")) +
                    (queuedGames.Count > 5 ? $"\n... and {queuedGames.Count - 5} more" : "") +
                    "\n\nContinue?",
                    "Confirm Upload");

                if (!confirmed)
                {
                    return;
                }
            }

            ProgressStatusText = $"Uploading {queuedGames.Count} game(s)...";
            IsProgressVisible = true;
            ProgressPercentage = 0;

            var completed = 0;
            var failed = 0;

            foreach (var game in queuedGames)
            {
                try
                {
                    // Update status
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        game.Status = UploadStatus.Uploading;
                        game.StatusMessage = "Uploading...";
                    });

                    ProgressStatusText = $"Uploading {game.GameName}...";
                    Logger.Log($"Starting upload for {game.GameName} ({game.PackageName})");

                    // Determine upload remote (should be configured in settings)
                    var uploadRemote = "RSL-gameuploads"; // Default upload remote

                    // Get list of configured remotes from upload config
                    var uploadConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone", "vrp.upload.config");
                    if (File.Exists(uploadConfigPath))
                    {
                        // Parse config to find upload remote (simplified)
                        var config = await File.ReadAllTextAsync(uploadConfigPath);
                        if (config.Contains("[RSL-gameuploads]"))
                        {
                            uploadRemote = "RSL-gameuploads";
                        }
                    }

                    // Upload compressed archive (matches original workflow)
                    if (!string.IsNullOrEmpty(game.ZipPath) && File.Exists(game.ZipPath))
                    {
                        var zipFileName = Path.GetFileName(game.ZipPath);
                        Logger.Log($"Uploading archive: {zipFileName}");

                        var uploadResult = await Rclone.runRcloneCommand_UploadConfig(
                            $"copy \"{game.ZipPath}\" \"{uploadRemote}:Quest Games/\"");

                        if (uploadResult.Error.Contains("Failed") || uploadResult.Error.Contains("error"))
                        {
                            throw new Exception($"Archive upload failed: {uploadResult.Error}");
                        }

                        Logger.Log($"Archive uploaded successfully: {zipFileName}");
                    }
                    else
                    {
                        throw new Exception($"Archive file not found: {game.ZipPath}");
                    }

                    // Success
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        game.Status = UploadStatus.Completed;
                        game.StatusMessage = $"Uploaded successfully at {DateTime.Now:HH:mm:ss}";
                    });

                    Logger.Log($"Successfully uploaded {game.GameName}");
                    completed++;
                }
                catch (Exception gameEx)
                {
                    Logger.Log($"Failed to upload {game.GameName}: {gameEx.Message}", LogLevel.Error);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        game.Status = UploadStatus.Failed;
                        game.StatusMessage = $"Failed: {gameEx.Message}";
                    });

                    failed++;
                }

                // Update progress
                var processedCount = completed + failed;
                ProgressPercentage = (double)processedCount / queuedGames.Count * 100;
            }

            // Show summary
            ProgressStatusText = $"Upload complete: {completed} succeeded, {failed} failed";
            ProgressPercentage = 100;

            if (_dialogService != null)
            {
                var summary = $"Upload batch complete!\n\n" +
                              $"Successful: {completed}\n" +
                              $"Failed: {failed}\n\n";

                if (completed > 0)
                {
                    summary += "Successfully uploaded games will appear in VRPirates mirrors after processing.\n\n";
                }

                if (failed > 0)
                {
                    summary += "Check the log for details on failed uploads.";
                }

                if (completed > 0 && failed == 0)
                {
                    await _dialogService.ShowInfoAsync(summary, "Upload Successful");
                }
                else if (failed > 0)
                {
                    await _dialogService.ShowWarningAsync(summary, "Upload Complete (With Errors)");
                }
            }

            Logger.Log($"Upload queue processing complete: {completed} succeeded, {failed} failed");

            await Task.Delay(3000);
            IsProgressVisible = false;
            ProgressStatusText = "Ready";
        });
    }

    /// <summary>
    /// Cancel the current download by killing rclone processes
    /// </summary>
    private void CancelDownload()
    {
        try
        {
            Logger.Log("Canceling download...");

            // Kill all rclone processes (matches original behavior)
            Rclone.KillRclone();

            // Hide progress indicators
            HideProgress();
            ProgressStatusText = "Download canceled";

            Logger.Log("Download canceled successfully");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error canceling download: {ex.Message}", LogLevel.Error);
        }
    }

    #region UI Event Handlers

    /// <summary>
    /// Handles double-click on game list - triggers download/install
    /// </summary>
    private void MouseDoubleClick()
    {
        if (SelectedGame != null)
        {
            Logger.Log($"Double-clicked game: {SelectedGame.GameName}");
            DownloadInstallGameCommand.Execute().Subscribe();
        }
    }


    /// <summary>
    /// Handles game selection changed - loads game notes and thumbnail
    /// </summary>
    private void OnSelectedGameChanged()
    {
        if (SelectedGame == null)
        {
            GameNotesText = string.Empty;
            return;
        }

        // Clipboard copy must be done asynchronously
        _ = OnSelectedGameChangedAsync();
    }

    private async Task OnSelectedGameChangedAsync()
    {
        if (SelectedGame == null)
        {
            return;
        }

        try
        {
            Logger.Log($"Game selected: {SelectedGame.GameName}");

            // Copy package name to clipboard if setting is enabled
            if (SettingsManager.Instance.PackageNameToCb)
            {
                try
                {
                    // Use Avalonia clipboard through main window
                    var window = Avalonia.Application.Current?.ApplicationLifetime is
                        IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow : null;

                    if (window?.Clipboard != null)
                    {
                        await window.Clipboard.SetTextAsync(SelectedGame.PackageName);
                        Logger.Log($"Copied package name to clipboard: {SelectedGame.PackageName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to copy to clipboard: {ex.Message}", LogLevel.Warning);
                }
            }
            else
            {
                Logger.Log($"Package name: {SelectedGame.PackageName}");
            }

            // Load game notes from notes folder
            var notesPath = Path.Combine(SideloaderRclone.NotesFolder, $"{SelectedGame.ReleaseName}.txt");

            if (File.Exists(notesPath))
            {
                GameNotesText = await File.ReadAllTextAsync(notesPath);
                Logger.Log($"Loaded notes for {SelectedGame.ReleaseName}");
            }
            else
            {
                GameNotesText = "No notes available for this game.";
                Logger.Log($"No notes found at: {notesPath}", LogLevel.Debug);
            }

            // Load thumbnail image (check multiple filename patterns)
            SelectedGameImage = null; // Clear previous image

            // Try different filename patterns: PackageName first (most reliable), then GameName, then ReleaseName
            string[] possibleNames = [SelectedGame.PackageName, SelectedGame.GameName, SelectedGame.ReleaseName];

            Logger.Log($"Looking for thumbnails for: PackageName='{SelectedGame.PackageName}', GameName='{SelectedGame.GameName}', ReleaseName='{SelectedGame.ReleaseName}'", LogLevel.Debug);

            foreach (var name in possibleNames)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                Logger.Log($"Checking cache for name: '{name}'", LogLevel.Debug);

                // Use ImageCache.GetCachedImagePath which checks both .jpg and .png
                var cachedPath = ImageCache.GetCachedImagePath(name);
                if (!string.IsNullOrEmpty(cachedPath) && File.Exists(cachedPath))
                {
                    try
                    {
                        // Load the image as a Bitmap
                        SelectedGameImage = new Bitmap(cachedPath);
                        Logger.Log($"✓ Loaded thumbnail: {Path.GetFileName(cachedPath)}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"✗ Failed to load image {cachedPath}: {ex.Message}", LogLevel.Warning);
                    }
                }
                else
                {
                    Logger.Log($"✗ No cached image found for: '{name}'", LogLevel.Debug);
                }
            }

            if (SelectedGameImage == null)
            {
                Logger.Log($"No thumbnail found for {SelectedGame.GameName}", LogLevel.Debug);

                // Optionally trigger download if remote is selected (but not for public mirror)
                if (!string.IsNullOrEmpty(SelectedRemote) &&
                    !string.IsNullOrEmpty(SelectedGame.GameName) &&
                    !(Rclone.HasPublicConfig && SelectedRemote == "VRP Public Mirror"))
                {
                    // Capture the current game to check if selection changed during download
                    var gameToDownload = SelectedGame;

                    // Try to download the image in the background (don't await to avoid blocking)
                    await Task.Run(async () =>
                    {
                        var downloadedPath = await ImageCache.DownloadAndCacheImageAsync(
                            gameToDownload.GameName,
                            SelectedRemote);

                        // Only update image if the same game is still selected (user didn't click away)
                        if (!string.IsNullOrEmpty(downloadedPath) && SelectedGame == gameToDownload)
                        {
                            try
                            {
                                // Load and update UI on main thread
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    SelectedGameImage = new Bitmap(downloadedPath);
                                    Logger.Log($"Downloaded and loaded thumbnail for {SelectedGame.GameName}");
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"Failed to load downloaded image: {ex.Message}", LogLevel.Warning);
                            }
                        }
                    });
                }
            }

            // Load trailer video if TrailersOn is enabled (check settings directly to get latest value)
            var trailersEnabled = SettingsManager.Instance.TrailersOn;
            TrailersOn = trailersEnabled; // Update property for UI binding

            if (trailersEnabled && _youtubeService != null && _trailerPlayerService != null)
            {
                // Clear any currently loaded trailer
                _trailerPlayerService.Clear();

                // Reset video playing state so thumbnail shows while loading
                IsVideoPlaying = false;

                // Capture the current game to check if selection changed during search
                var gameForTrailer = SelectedGame;

                // Search for trailer in background (don't block UI)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Logger.Log($"Searching for trailer: {gameForTrailer.GameName}");
                        var youtubeUrl = await YouTubeTrailerService.SearchForTrailerAsync(gameForTrailer.GameName);

                        // Only play if same game is still selected
                        if (SelectedGame != gameForTrailer)
                        {
                            Logger.Log("Game selection changed, skipping trailer playback");
                            return;
                        }

                        if (!string.IsNullOrEmpty(youtubeUrl))
                        {
                            // Try to load the trailer in WebView
                            var loadResult = await _trailerPlayerService.LoadYouTubeVideoAsync(youtubeUrl);

                            if (loadResult)
                            {
                                IsVideoPlaying = true;
                                Logger.Log("WebView loaded successfully, showing video player");
                            }
                            else
                            {
                                Logger.Log("WebView loading failed, keeping thumbnail visible");
                            }
                        }
                        else
                        {
                            Logger.Log($"No trailer found for {gameForTrailer.GameName}, keeping thumbnail visible");
                        }
                    }
                    catch (Exception trailerEx)
                    {
                        Logger.Log($"Error loading trailer: {trailerEx.Message}", LogLevel.Error);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error loading game details: {ex.Message}", LogLevel.Error);
            GameNotesText = "Error loading game notes.";
        }
    }

    /// <summary>
    /// Handles mouse click on game list - intended for right-click context menu
    /// Note: Actual context menu needs to be defined in XAML
    /// The FavoriteCommand is already implemented and can be bound to context menu items
    /// </summary>
    private void OnMouseClick()
    {
        if (SelectedGame != null)
        {
            Logger.Log($"Mouse clicked on game: {SelectedGame.GameName}");
            // Context menu will be triggered from XAML
            // Menu item should bind to FavoriteCommand which is already implemented
        }
    }

    /// <summary>
    /// Handles click on download queue item
    /// - If first item (currently downloading): Cancel download
    /// - If other items: Remove from queue
    /// Note: Requires SelectedQueueItem to be bound in XAML
    /// </summary>
    private void OnGamesQueueClick()
    {
        if (string.IsNullOrEmpty(SelectedQueueItem))
        {
            Logger.Log("No queue item selected", LogLevel.Debug);
            return;
        }

        try
        {
            var selectedIndex = GamesQueue.IndexOf(SelectedQueueItem);

            switch (selectedIndex)
            {
                // If clicking the first item (currently downloading) and it's the only item
                case 0 when GamesQueue.Count == 1:
                    Logger.Log("Canceling current download from queue");
                    Rclone.KillRclone();
                    ProgressStatusText = "Download canceled from queue";
                    break;
                // If clicking any other item (not currently downloading)
                case > 0:
                    Logger.Log($"Removing '{SelectedQueueItem}' from download queue");
                    GamesQueue.Remove(SelectedQueueItem);
                    ProgressStatusText = "Removed game from queue";
                    break;
                default:
                    // First item but more in queue - user wants to skip current
                    Logger.Log("Skipping current download and moving to next in queue");
                    Rclone.KillRclone();
                    GamesQueue.RemoveAt(0);
                    ProgressStatusText = "Skipped current download";

                    // Auto-start next item in queue if available
                    if (GamesQueue.Count > 0)
                    {
                        var nextGameName = GamesQueue[0];
                        Logger.Log($"Auto-starting next game in queue: {nextGameName}");

                        // Find the game in the games list
                        var nextGame = _allGames.FirstOrDefault(g => g.GameName == nextGameName);
                        if (nextGame != null)
                        {
                            SelectedGame = nextGame;
                            // Fire and forget - start the download asynchronously
                            Task.Run(async () => await DownloadInstallGameAsync());
                        }
                        else
                        {
                            Logger.Log($"Could not find game '{nextGameName}' in games list", LogLevel.Warning);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error handling queue click: {ex.Message}", LogLevel.Error);
        }
    }

    #endregion
}