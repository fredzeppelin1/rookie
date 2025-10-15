using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AndroidSideloader.Models;
using AndroidSideloader.Services;
using AndroidSideloader.Sideloader;
using AndroidSideloader.Utilities;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;

namespace AndroidSideloader.ViewModels
{
    public enum GameFilterType
    {
        UpToDate,
        UpdateAvailable,
        NewerThanList
    }

    public class MainViewModel : ReactiveObject
    {
        private readonly IDialogService _dialogService;

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

        // Track if a download is currently in progress
        private bool _isDownloading;

        private string _selectedQueueItem;
        public string SelectedQueueItem
        {
            get => _selectedQueueItem;
            set => this.RaiseAndSetIfChanged(ref _selectedQueueItem, value);
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

        private double _progressPercentage = 0;
        public double ProgressPercentage
        {
            get => _progressPercentage;
            set => this.RaiseAndSetIfChanged(ref _progressPercentage, value);
        }

        private bool _isProgressVisible = false;
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

        private string _diskSpaceText = "";
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

        // Command line argument properties
        public bool IsOffline { get; private set; }
        public bool NoRcloneUpdating { get; private set; }
        public bool NoAppCheck { get; private set; }

        // Release notes URL
        private string _releaseNotesUrl = "https://github.com/VRPirates/rookie/releases";
        public string ReleaseNotesUrl
        {
            get => _releaseNotesUrl;
            set => this.RaiseAndSetIfChanged(ref _releaseNotesUrl, value);
        }

        // Command for opening URLs
        public ReactiveCommand<string, Unit> OpenUrlCommand { get; }

        // Private list to hold all games
        private readonly List<GameItem> _allGames;

        // Favorites filter flag
        private bool _showFavoritesOnly;
        public bool ShowFavoritesOnly
        {
            get => _showFavoritesOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _showFavoritesOnly, value);
                ApplyFilters().GetAwaiter().GetResult();
            }
        }

        // Status filter flags (mutually exclusive)
        private bool _showUpToDateOnly;
        public bool ShowUpToDateOnly
        {
            get => _showUpToDateOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _showUpToDateOnly, value);
                ApplyFilters().GetAwaiter().GetResult();
            }
        }

        private bool _showUpdateAvailableOnly;
        public bool ShowUpdateAvailableOnly
        {
            get => _showUpdateAvailableOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _showUpdateAvailableOnly, value);
                ApplyFilters().GetAwaiter().GetResult();
            }
        }

        private bool _showNewerThanListOnly;
        public bool ShowNewerThanListOnly
        {
            get => _showNewerThanListOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _showNewerThanListOnly, value);
                ApplyFilters().GetAwaiter().GetResult();
            }
        }

        public ReactiveCommand<string, Unit> SearchCommand { get; }
        public ReactiveCommand<Unit, Unit> MouseClickCommand { get; }
        public ReactiveCommand<Unit, Unit> MouseDoubleClickCommand { get; }
        public ReactiveCommand<Unit, Unit> GamesQueueMouseClickCommand { get; }
        public ReactiveCommand<Unit, Unit> DragDropCommand { get; }
        public ReactiveCommand<Unit, Unit> DragEnterCommand { get; }
        public ReactiveCommand<Unit, Unit> DragLeaveCommand { get; }

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
        public ReactiveCommand<Unit, Unit> ExtractGameForUploadCommand { get; }
        public ReactiveCommand<Unit, Unit> ProcessUploadQueueCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelDownloadCommand { get; }
        public ReactiveCommand<Unit, Unit> DisableSideloadingCommand { get; }
        public ReactiveCommand<Unit, Unit> GamesListCommand { get; }

        // Status counts - dynamically calculated from game list
        public int UpToDateCount => _allGames.Count(g => g.IsInstalled && !g.HasUpdate);
        public int UpdateAvailableCount => _allGames.Count(g => g.HasUpdate);
        public int NewerThanListCount => _allGames.Count(g => g.IsInstalled && g.InstalledVersionCode > g.AvailableVersionCode && g.AvailableVersionCode > 0);

        public MainViewModel(bool showUpdateAvailableOnly, IDialogService dialogService = null)
        {
            _showUpdateAvailableOnly = showUpdateAvailableOnly;
            _dialogService = dialogService; // Can be null initially, will be set later

            // Wire up RCLONE progress callback for real-time download updates
            Rclone.OnProgress = (progress) =>
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
            GamesQueue = [];
            Remotes = [];
            Devices = [];

            // Initialize _allGames with dummy data for now
            _allGames = [];

            SearchCommand = ReactiveCommand.CreateFromTask<string>(RunSearch);
            MouseClickCommand = ReactiveCommand.Create(OnMouseClick);
            MouseDoubleClickCommand = ReactiveCommand.Create(MouseDoubleClick);
            GamesQueueMouseClickCommand = ReactiveCommand.Create(OnGamesQueueClick);
            DragDropCommand = ReactiveCommand.Create(() => { Logger.Log("DragDrop event handled."); });
            DragEnterCommand = ReactiveCommand.Create(DragEnter);
            DragLeaveCommand = ReactiveCommand.Create(DragLeave);

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
            ExtractGameForUploadCommand = ReactiveCommand.CreateFromTask(ExtractGameForUploadAsync);
            ProcessUploadQueueCommand = ReactiveCommand.CreateFromTask(ProcessUploadQueueAsync);
            CancelDownloadCommand = ReactiveCommand.Create(CancelDownload);
            OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);

            this.WhenAnyValue(x => x.SearchText)
                .Throttle(TimeSpan.FromSeconds(1))
                .Select(x => x ?? string.Empty) // Ensure searchText is never null
                .InvokeCommand(SearchCommand);

            // Automatically update game details when selection changes
            this.WhenAnyValue(x => x.SelectedGame)
                .Subscribe(_ => OnSelectedGameChanged());

            // Initialize device list with placeholder
            Devices.Add("Select your device");

            CheckCommandLineArguments(); // Call the new method
            SetCurrentLogPath(); // Call the new method
            Logger.Initialize(); // Call Logger.Initialize()

            // Set version text for display from version file
            VersionText = $"Rookie v{Updater.LocalVersion}";
            Logger.Log($"Application version: {VersionText}");

            // Initial population of Games (fire-and-forget to avoid blocking UI thread)
            _ = UpdateGamesList();

            // Load remotes and public config
            InitGames();

            // Try to detect devices on startup (fire-and-forget to avoid blocking UI thread)
            _ = RefreshDeviceListAsync();
        }

        private void CheckCommandLineArguments()
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg.ToLower() == "--offline")
                {
                    IsOffline = true;
                }
                if (arg.ToLower() == "--norcloneupdate")
                {
                    NoRcloneUpdating = true;
                }
                if (arg.ToLower() == "--noappcheck")
                {
                    NoAppCheck = true;
                }
            }
        }

        private static void SetCurrentLogPath()
        {
            if (string.IsNullOrEmpty(SettingsManager.Instance.CurrentLogPath))
            {
                SettingsManager.Instance.CurrentLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
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

                await Rclone.DownloadPublicConfig();

                // Parse the downloaded public config
                var publicConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vrp-public.json");
                if (File.Exists(publicConfigPath))
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
                        if (remotes != null && remotes.Count > 0)
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error initializing games: {ex.Message}", LogLevel.Error);
                // Fall back to dummy data on error
                await UpdateGamesList();
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

        /// <summary>
        /// Download all game thumbnails from remote
        /// </summary>
        private async Task DownloadAllThumbnailsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedRemote))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Please select a remote first.",
                            "No Remote Selected");
                    }
                    return;
                }

                // Confirm with user
                if (_dialogService != null)
                {
                    var confirmed = await _dialogService.ShowConfirmationAsync(
                        $"Download all game thumbnails from {SelectedRemote}?\n\n" +
                        $"This may take several minutes depending on the number of games.",
                        "Download Thumbnails");

                    if (!confirmed) return;
                }

                ShowProgress("Downloading thumbnails...");
                Logger.Log($"Starting thumbnail batch download from {SelectedRemote}");

                var downloadedCount = await ImageCache.DownloadAllThumbnailsAsync(SelectedRemote);

                // Reload cached images
                await LoadCachedImagesAsync();

                // Refresh UI
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ApplyFilters();
                });

                HideProgress();

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        $"Successfully downloaded {downloadedCount} thumbnail(s)!\n\n" +
                        $"Cache size: {ImageCache.GetCacheSizeMb()} MB",
                        "Download Complete");
                }

                Logger.Log($"Thumbnail download complete: {downloadedCount} images");
            }
            catch (Exception ex)
            {
                HideProgress();
                Logger.Log($"Error downloading thumbnails: {ex.Message}", LogLevel.Error);

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        $"Error downloading thumbnails:\n\n{ex.Message}",
                        "Download Error");
                }
            }
        }

        // Command Implementations

        private async Task SideloadApkAsync()
        {
            Logger.Log("Sideload APK command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first using the 'RECONNECT DEVICE' button.",
                            "No Device");
                    }
                    return;
                }

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

                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (window == null)
                {
                    Logger.Log("Cannot get main window for file dialog", LogLevel.Error);
                    return;
                }

                var result = await window.StorageProvider.OpenFilePickerAsync(fileDialog);

                if (result != null && result.Count > 0)
                {
                    var apkPath = result[0].Path.LocalPath;
                    Logger.Log($"Selected APK: {apkPath}");

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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during APK sideload: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error during sideload";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        $"Error during sideload:\n\n{ex.Message}",
                        "Sideload Error");
                }
            }
        }

        private async Task ReconnectDeviceAsync()
        {
            Logger.Log("Reconnect Device command triggered");

            try
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reconnecting device: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error connecting to device";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        $"Error connecting to device:\n\n{ex.Message}",
                        "Connection Error");
                }
            }
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

                    // Get battery level
                    var batteryResult = Adb.RunAdbCommandToString("shell dumpsys battery");
                    var batteryLevel = ParseBatteryLevel(batteryResult.Output);
                    if (!string.IsNullOrEmpty(batteryLevel))
                    {
                        BatteryLevelText = $"{batteryLevel}%";
                    }

                    // Get device model
                    var modelResult = Adb.RunAdbCommandToString("shell getprop ro.product.model");
                    var deviceModel = modelResult.Output.Trim();
                    if (!string.IsNullOrEmpty(deviceModel))
                    {
                        Logger.Log($"Device model: {deviceModel}");
                    }

                    Logger.Log($"Device info refreshed for: {Adb.DeviceId}");
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
                var installedPackages = await Task.Run(() => Adb.GetAllInstalledPackagesWithVersions());

                Logger.Log($"Found {installedPackages.Count} installed packages");

                // Update game list with install status
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var updatesAvailable = 0;

                    foreach (var game in _allGames)
                    {
                        if (installedPackages.TryGetValue(game.PackageName, out var versionInfo))
                        {
                            game.IsInstalled = true;
                            game.InstalledVersion = versionInfo.versionName;
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
                            game.InstalledVersion = null;
                            game.InstalledVersionCode = 0;
                        }
                    }

                    // Re-apply filters to refresh UI
                    ApplyFilters().GetAwaiter().GetResult();

                    ProgressStatusText = updatesAvailable > 0 
                        ? $"Found {updatesAvailable} game(s) with updates available" 
                        : "All installed games are up to date";
                });

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

        private async Task CopyObbAsync()
        {
            Logger.Log("Copy OBB command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Open folder picker to select OBB folder
                var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select OBB Folder",
                    AllowMultiple = false
                };

                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (window == null) return;

                var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

                if (result != null && result.Count > 0)
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error copying OBB: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error copying OBB";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error copying OBB:\n\n{ex.Message}", "Copy Error");
                }
            }
        }

        private async Task UninstallAppAsync()
        {
            Logger.Log("Uninstall App command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Check if a game is selected
                if (SelectedGame == null)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Please select an app to uninstall from the list.",
                            "No App Selected");
                    }
                    return;
                }

                // Confirm uninstall
                if (_dialogService != null)
                {
                    var confirmed = await _dialogService.ShowConfirmationAsync(
                        $"Are you sure you want to uninstall?\n\n{SelectedGame.GameName}\n\nPackage: {SelectedGame.PackageName}",
                        "Confirm Uninstall");

                    if (!confirmed) return;
                }

                ProgressStatusText = $"Uninstalling {SelectedGame.GameName}...";

                // Use UninstallGame which also cleans up OBB and data folders
                var result = SideloaderUtilities.UninstallGame(SelectedGame.PackageName);

                if (result.Output.Contains("Success"))
                {
                    ProgressStatusText = "App uninstalled successfully!";
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"{SelectedGame.GameName} has been uninstalled.",
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error uninstalling app: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error uninstalling app";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error uninstalling:\n\n{ex.Message}", "Uninstall Error");
                }
            }
        }

        private async Task GetApkAsync()
        {
            Logger.Log("Get APK command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Check if a game is selected
                if (SelectedGame == null)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Please select an app to extract from the list.",
                            "No App Selected");
                    }
                    return;
                }

                ProgressStatusText = $"Extracting APK for {SelectedGame.GameName}...";

                // Get the APK path on device
                var pathResult = Adb.RunAdbCommandToString($"shell pm path {SelectedGame.PackageName}");

                if (pathResult.Output.Contains("package:"))
                {
                    var apkPath = pathResult.Output.Replace("package:", "").Trim();

                    // Pull APK to desktop
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    var outputPath = Path.Combine(desktopPath, $"{SelectedGame.PackageName}.apk");

                    var pullResult = Adb.RunAdbCommandToString($"pull \"{apkPath}\" \"{outputPath}\"");

                    if (File.Exists(outputPath))
                    {
                        ProgressStatusText = "APK extracted successfully!";
                        if (_dialogService != null)
                        {
                            await _dialogService.ShowInfoAsync(
                                $"APK extracted to:\n\n{outputPath}",
                                "Extraction Complete");
                        }
                    }
                    else
                    {
                        ProgressStatusText = "APK extraction failed";
                        if (_dialogService != null)
                        {
                            await _dialogService.ShowErrorAsync(
                                "Failed to extract APK from device.",
                                "Extraction Failed");
                        }
                    }
                }
                else
                {
                    ProgressStatusText = "Package not found";
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Could not find package {SelectedGame.PackageName} on device.",
                            "Package Not Found");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error extracting APK: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error extracting APK";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error extracting APK:\n\n{ex.Message}", "Extraction Error");
                }
            }
        }

        private async Task BackupAdbAsync()
        {
            Logger.Log("Backup ADB command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Check if a game is selected
                if (SelectedGame == null)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Please select an app to backup from the list.",
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

                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (window == null) return;

                var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

                if (result != null && result.Count > 0)
                {
                    var backupPath = result[0].Path.LocalPath;
                    var backupFile = Path.Combine(backupPath, $"{SelectedGame.PackageName}.ab");

                    ProgressStatusText = $"Backing up {SelectedGame.GameName}...";

                    var backupResult = Adb.RunAdbCommandToString($"backup -f \"{backupFile}\" {SelectedGame.PackageName}");

                    if (File.Exists(backupFile))
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
                        ProgressStatusText = "Backup failed";
                        if (_dialogService != null)
                        {
                            await _dialogService.ShowWarningAsync(
                                "Backup command executed but file not found. Check if backup was approved on device.",
                                "Backup Status Unknown");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during backup: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error during backup";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error during backup:\n\n{ex.Message}", "Backup Error");
                }
            }
        }

        private async Task OpenDownloadsFolderAsync()
        {
            Logger.Log("Open Downloads Folder command triggered");

            try
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening downloads folder: {ex.Message}", LogLevel.Error);
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error opening folder:\n\n{ex.Message}", "Error");
                }
            }
        }

        private async Task RunAdbCommandAsync()
        {
            Logger.Log("Run ADB Command triggered");

            // Check if device is connected
            if (string.IsNullOrEmpty(Adb.DeviceId))
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowWarningAsync(
                        "No device connected. Please connect your device first.",
                        "No Device");
                }
                return;
            }

            // Open ADB command dialog window
            var adbWindow = new Views.AdbCommandWindow();
            if (_dialogService is Services.AvaloniaDialogService avaloniaDialog)
            {
                await adbWindow.ShowDialog(avaloniaDialog.Owner);
            }
            else
            {
                adbWindow.Show();
            }
        }

        private async Task AdbWirelessDisableAsync()
        {
            Logger.Log("Disable Wireless ADB command triggered");

            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect via USB first.",
                            "No Device");
                    }
                    return;
                }

                ProgressStatusText = "Disabling wireless ADB...";

                // Disable wireless ADB
                var result = Adb.RunAdbCommandToString("tcpip 5555");
                await Task.Delay(1000);
                var disconnectResult = Adb.RunAdbCommandToString("disconnect");

                Adb.WirelessadbOn = false;
                ProgressStatusText = "Wireless ADB disabled";

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        "Wireless ADB has been disabled. Device will only connect via USB.",
                        "Wireless ADB Disabled");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error disabling wireless ADB: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error disabling wireless ADB";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}", "Error");
                }
            }
        }

        private async Task AdbWirelessEnableAsync()
        {
            Logger.Log("Enable Wireless ADB command triggered");

            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect via USB first.",
                            "No Device");
                    }
                    return;
                }

                ProgressStatusText = "Enabling wireless ADB...";

                // Get device IP address
                var ipResult = Adb.RunAdbCommandToString("shell ip addr show wlan0");

                // Parse IP from output (looking for inet line)
                var deviceIp = "";
                foreach (var line in ipResult.Output.Split('\n'))
                {
                    if (line.Contains("inet ") && !line.Contains("inet6"))
                    {
                        var parts = line.Trim().Split(' ');
                        if (parts.Length > 1)
                        {
                            deviceIp = parts[1].Split('/')[0];
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(deviceIp))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            "Could not detect device IP address. Make sure WiFi is enabled on the device.",
                            "IP Detection Failed");
                    }
                    return;
                }

                // Enable TCP/IP mode on port 5555
                var tcpipResult = Adb.RunAdbCommandToString("tcpip 5555");
                await Task.Delay(2000);

                // Connect wirelessly
                var connectResult = Adb.RunAdbCommandToString($"connect {deviceIp}:5555");

                if (connectResult.Output.Contains("connected"))
                {
                    Adb.WirelessadbOn = true;
                    ProgressStatusText = $"Wireless ADB enabled at {deviceIp}";

                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"Wireless ADB enabled!\n\nIP Address: {deviceIp}:5555\n\nYou can now disconnect the USB cable.",
                            "Wireless ADB Enabled");
                    }
                }
                else
                {
                    ProgressStatusText = "Failed to enable wireless ADB";

                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Failed to connect wirelessly.\n\n{connectResult.Error}",
                            "Connection Failed");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error enabling wireless ADB: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error enabling wireless ADB";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}", "Error");
                }
            }
        }

        private async Task UpdateGamesAsync()
        {
            Logger.Log("Refresh All command triggered");

            try
            {
                ProgressStatusText = "Refreshing game list from remotes...";

                if (!string.IsNullOrEmpty(SelectedRemote))
                {
                    // Load full game list from remote (doesn't require device)
                    await LoadGamesFromRemoteAsync(SelectedRemote);
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error refreshing games: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error refreshing games";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}", "Refresh Error");
                }
            }
        }

        private async Task ListApkAsync()
        {
            Logger.Log("Refresh Update List command triggered");

            try
            {
                // Check for device connection first
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.\n\nThis feature checks which installed games have updates available.",
                            "No Device Connected");
                    }
                    return;
                }

                // Warn user this may take time
                if (_dialogService != null)
                {
                    var proceed = await _dialogService.ShowConfirmationAsync(
                        "This will check your installed games for updates.\n\nNOTE: THIS MAY TAKE UP TO 60 SECONDS.",
                        "Check for Updates?");

                    if (!proceed)
                    {
                        return;
                    }
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error checking for updates: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error checking for updates";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}", "Update Check Error");
                }
            }
        }

        private async Task PullAppToDesktopAsync()
        {
            Logger.Log("Pull App To Desktop command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Check if a game is selected
                if (SelectedGame == null)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Please select an app to extract from the list.",
                            "No App Selected");
                    }
                    return;
                }

                ProgressStatusText = $"Pulling {SelectedGame.GameName} data to desktop...";

                // Get the APK path on device
                var pathResult = Adb.RunAdbCommandToString($"shell pm path {SelectedGame.PackageName}");

                if (!pathResult.Output.Contains("package:"))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync(
                            $"Could not find package {SelectedGame.PackageName} on device.",
                            "Package Not Found");
                    }
                    return;
                }

                var apkPath = pathResult.Output.Replace("package:", "").Trim();

                // Create output directory on desktop
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var outputDir = Path.Combine(desktopPath, SelectedGame.PackageName);
                Directory.CreateDirectory(outputDir);

                // Pull APK
                var apkOutput = Path.Combine(outputDir, "base.apk");
                var pullApkResult = Adb.RunAdbCommandToString($"pull \"{apkPath}\" \"{apkOutput}\"");

                // Pull OBB files if they exist
                var obbPath = $"/sdcard/Android/obb/{SelectedGame.PackageName}/";
                var obbCheckResult = Adb.RunAdbCommandToString($"shell ls \"{obbPath}\"");

                if (!obbCheckResult.Output.Contains("No such file"))
                {
                    var obbOutputDir = Path.Combine(outputDir, "obb");
                    Directory.CreateDirectory(obbOutputDir);
                    var pullObbResult = Adb.RunAdbCommandToString($"pull \"{obbPath}\" \"{obbOutputDir}\"");
                }

                // Pull app data if accessible (may require root)
                var dataPath = $"/sdcard/Android/data/{SelectedGame.PackageName}/";
                var dataCheckResult = Adb.RunAdbCommandToString($"shell ls \"{dataPath}\"");

                if (!dataCheckResult.Output.Contains("No such file"))
                {
                    var dataOutputDir = Path.Combine(outputDir, "data");
                    Directory.CreateDirectory(dataOutputDir);
                    var pullDataResult = Adb.RunAdbCommandToString($"pull \"{dataPath}\" \"{dataOutputDir}\"");
                }

                // Create zip archive of the pulled app (matching original behavior)
                ProgressStatusText = "Creating zip archive...";
                var versionCode = SelectedGame.InstalledVersionCode > 0 ? SelectedGame.InstalledVersionCode.ToString() : "unknown";
                var zipFileName = $"{SelectedGame.GameName} v{versionCode} {SelectedGame.PackageName}.zip";
                var zipPath = Path.Combine(desktopPath, zipFileName);

                // Remove existing zip if it exists
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                // Create archive of the output directory
                await SevenZip.CreateArchive(zipPath, $"{outputDir}/*");

                // Delete the temporary folder now that we have the zip
                Directory.Delete(outputDir, true);

                ProgressStatusText = "App pulled to desktop successfully!";

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        $"{SelectedGame.GameName} pulled to:\n\n{zipFileName}\n\nOn your desktop!",
                        "Pull Complete");
                }

                Logger.Log($"Pulled app to desktop: {zipPath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error pulling app to desktop: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error pulling app";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error pulling app:\n\n{ex.Message}", "Pull Error");
                }
            }
        }

        private async Task CopyBulkObbAsync()
        {
            Logger.Log("Copy Bulk OBB command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Open folder picker to select source OBB folder
                var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select OBB Folder (with multiple game folders inside)",
                    AllowMultiple = false
                };

                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (window == null) return;

                var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

                if (result != null && result.Count > 0)
                {
                    var sourcePath = result[0].Path.LocalPath;
                    Logger.Log($"Selected bulk OBB folder: {sourcePath}");

                    // Get all subdirectories (each should be a package name)
                    var packageFolders = Directory.GetDirectories(sourcePath);

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

                    // Use recursive copy which handles nested folder structures
                    // This matches the original behavior and is more robust
                    var copyResult = SideloaderUtilities.RecursiveCopyObb(sourcePath);

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

                        message += $"Check logs for details.";

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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during bulk OBB copy: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error during bulk copy";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error during bulk copy:\n\n{ex.Message}", "Copy Error");
                }
            }
        }

        private async Task BackupGameDataAsync()
        {
            Logger.Log("Backup Gamedata command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Check if a game is selected
                if (SelectedGame == null)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Please select a game to backup from the list.",
                            "No Game Selected");
                    }
                    return;
                }

                // Select backup location
                var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Backup Location",
                    AllowMultiple = false
                };

                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (window == null) return;

                var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

                if (result != null && result.Count > 0)
                {
                    var backupPath = result[0].Path.LocalPath;
                    var gameBackupDir = Path.Combine(backupPath, SelectedGame.PackageName);
                    Directory.CreateDirectory(gameBackupDir);

                    ProgressStatusText = $"Backing up {SelectedGame.GameName} data...";

                    // Backup OBB files
                    var obbSourcePath = $"/sdcard/Android/obb/{SelectedGame.PackageName}/";
                    var obbCheckResult = Adb.RunAdbCommandToString($"shell ls \"{obbSourcePath}\"");

                    if (!obbCheckResult.Output.Contains("No such file"))
                    {
                        var obbBackupDir = Path.Combine(gameBackupDir, "obb");
                        Directory.CreateDirectory(obbBackupDir);
                        var pullObbResult = Adb.RunAdbCommandToString($"pull \"{obbSourcePath}\" \"{obbBackupDir}\"");
                        Logger.Log($"OBB backup result: {pullObbResult.Output}");
                    }

                    // Backup app data from /sdcard/Android/data/
                    var dataSourcePath = $"/sdcard/Android/data/{SelectedGame.PackageName}/";
                    var dataCheckResult = Adb.RunAdbCommandToString($"shell ls \"{dataSourcePath}\"");

                    if (!dataCheckResult.Output.Contains("No such file"))
                    {
                        var dataBackupDir = Path.Combine(gameBackupDir, "data");
                        Directory.CreateDirectory(dataBackupDir);
                        var pullDataResult = Adb.RunAdbCommandToString($"pull \"{dataSourcePath}\" \"{dataBackupDir}\"");
                        Logger.Log($"Data backup result: {pullDataResult.Output}");
                    }

                    ProgressStatusText = "Gamedata backup complete!";

                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"Gamedata backed up successfully!\n\nLocation:\n{gameBackupDir}\n\nContents:\n- obb/ (if exists)\n- data/ (if exists)",
                            "Backup Complete");
                    }

                    Logger.Log($"Gamedata backed up to: {gameBackupDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error backing up gamedata: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error backing up gamedata";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error backing up gamedata:\n\n{ex.Message}", "Backup Error");
                }
            }
        }

        private async Task RestoreGameDataAsync()
        {
            Logger.Log("Restore Gamedata command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Select backup folder to restore from
                var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Backup Folder (should contain obb/ and/or data/ folders)",
                    AllowMultiple = false
                };

                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (window == null) return;

                var result = await window.StorageProvider.OpenFolderPickerAsync(folderDialog);

                if (result != null && result.Count > 0)
                {
                    var backupPath = result[0].Path.LocalPath;
                    var packageName = Path.GetFileName(backupPath);

                    Logger.Log($"Restoring gamedata from: {backupPath}");

                    // Check for obb and data folders
                    var obbBackupPath = Path.Combine(backupPath, "obb");
                    var dataBackupPath = Path.Combine(backupPath, "data");

                    var hasObb = Directory.Exists(obbBackupPath);
                    var hasData = Directory.Exists(dataBackupPath);

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

                    // Restore OBB files
                    if (hasObb)
                    {
                        var obbDestPath = $"/sdcard/Android/obb/{packageName}/";

                        // Create OBB directory on device
                        Adb.RunAdbCommandToString($"shell mkdir -p \"{obbDestPath}\"");

                        // Push all files in obb backup folder
                        var obbFiles = Directory.GetFiles(obbBackupPath, "*", SearchOption.AllDirectories);
                        foreach (var obbFile in obbFiles)
                        {
                            var fileName = Path.GetFileName(obbFile);
                            var pushResult = Adb.RunAdbCommandToString($"push \"{obbFile}\" \"{obbDestPath}{fileName}\"");
                            Logger.Log($"Pushed OBB file: {fileName}");
                        }
                    }

                    // Restore data files
                    if (hasData)
                    {
                        var dataDestPath = $"/sdcard/Android/data/{packageName}/";

                        // Create data directory on device
                        Adb.RunAdbCommandToString($"shell mkdir -p \"{dataDestPath}\"");

                        // Push data folder recursively
                        var pushDataResult = Adb.RunAdbCommandToString($"push \"{dataBackupPath}/.\" \"{dataDestPath}\"");
                        Logger.Log($"Data restore result: {pushDataResult.Output}");
                    }

                    ProgressStatusText = "Gamedata restore complete!";

                    if (_dialogService != null)
                    {
                        await _dialogService.ShowInfoAsync(
                            $"Gamedata restored successfully!\n\nPackage: {packageName}",
                            "Restore Complete");
                    }

                    Logger.Log($"Gamedata restored for: {packageName}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error restoring gamedata: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error restoring gamedata";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error restoring gamedata:\n\n{ex.Message}", "Restore Error");
                }
            }
        }


        private async Task MountRcloneAsync()
        {
            Logger.Log("Mount Rclone command triggered");

            try
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

                // Create mount directory
                var mountPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mnt", SelectedRemote);
                if (!Directory.Exists(mountPath))
                {
                    Directory.CreateDirectory(mountPath);
                    Logger.Log($"Created mount directory: {mountPath}");
                }

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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error mounting remote: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error mounting remote";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error mounting:\n\n{ex.Message}", "Mount Error");
                }
            }
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
                            // User cancelled - remove from queue and clear flag
                            _isDownloading = false;
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (GamesQueue.Count > 0) GamesQueue.RemoveAt(0);
                            });
                            await ProcessNextQueueItem();
                            return;
                        }
                    }
                    else
                    {
                        // No dialog service - remove from queue and clear flag
                        _isDownloading = false;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (GamesQueue.Count > 0) GamesQueue.RemoveAt(0);
                        });
                        await ProcessNextQueueItem();
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
                    // User error - remove from queue and clear flag
                    _isDownloading = false;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (GamesQueue.Count > 0) GamesQueue.RemoveAt(0);
                    });
                    await ProcessNextQueueItem();
                    return;
                }

                ShowProgress($"Downloading {SelectedGame.GameName}...");
                Logger.Log($"Starting download for {SelectedGame.GameName} from {SelectedRemote}");

                // Get downloads folder - matches original: settings.DownloadDir
                var downloadsPath = SettingsManager.Instance.DownloadDir;
                Logger.Log($"Using download directory: {downloadsPath}");

                // Ensure download directory exists
                if (!Directory.Exists(downloadsPath))
                {
                    Directory.CreateDirectory(downloadsPath);
                    Logger.Log($"Created download directory: {downloadsPath}");
                }

                // Use ReleaseName for folder (matches original which uses gameName)
                var gameDownloadPath = Path.Combine(downloadsPath, SelectedGame.ReleaseName);

                string localApkPath = null;

                // PUBLIC MIRROR: Uses 7z archives, different download flow
                if (SelectedRemote == "VRP Public Mirror")
                {
                    // Calculate MD5 hash of game name (matching original behavior)
                    string gameNameHash;
                    using (var md5 = MD5.Create())
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(gameToDownload.ReleaseName + "\n");
                        var hash = md5.ComputeHash(bytes);
                        var sb = new System.Text.StringBuilder();
                        foreach (var b in hash)
                        {
                            sb.Append(b.ToString("x2"));
                        }
                        gameNameHash = sb.ToString();
                    }

                    Logger.Log($"Public mirror MD5 hash for {gameToDownload.ReleaseName}: {gameNameHash}");

                    // Download to hash folder
                    var hashDownloadPath = Path.Combine(downloadsPath, gameNameHash);
                    Directory.CreateDirectory(hashDownloadPath);

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
                        await SevenZip.ExtractArchive(archiveFile, downloadsPath, password);

                        Logger.Log($"Extraction complete to {downloadsPath}");
                        UpdateProgress(80, "Extraction complete");

                        // Clean up hash folder
                        if (Directory.Exists(hashDownloadPath))
                        {
                            Directory.Delete(hashDownloadPath, true);
                            Logger.Log($"Cleaned up temporary download folder: {hashDownloadPath}");
                        }

                        // The archive should have created a folder with the game name (ReleaseName)
                        // Update gameDownloadPath to point to the extracted folder
                        gameDownloadPath = Path.Combine(downloadsPath, gameToDownload.ReleaseName);

                        // Find the APK file in extracted content
                        var apkFiles = Directory.GetFiles(gameDownloadPath, "*.apk", SearchOption.AllDirectories);
                        if (apkFiles.Length > 0)
                        {
                            localApkPath = apkFiles[0];
                            Logger.Log($"Found APK: {Path.GetFileName(localApkPath)}");
                        }
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
                        // Download failed - remove from queue and process next
                        _isDownloading = false;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (GamesQueue.Count > 0) GamesQueue.RemoveAt(0);
                        });
                        await ProcessNextQueueItem();
                        return;
                    }
                }
                // REGULAR REMOTES: Download entire game folder (matching original behavior)
                else
                {
                    Directory.CreateDirectory(gameDownloadPath);

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

                    // Find APK file in downloaded folder (matching original)
                    var downloadedFiles = Directory.GetFiles(gameDownloadPath);
                    var apkFile = downloadedFiles.FirstOrDefault(file => Path.GetExtension(file).Equals(".apk", StringComparison.OrdinalIgnoreCase));

                    if (apkFile != null)
                    {
                        localApkPath = apkFile;
                        Logger.Log($"Found APK: {Path.GetFileName(apkFile)}");
                    }
                    else
                    {
                        Logger.Log("Warning: No APK file found in downloaded folder", LogLevel.Warning);
                    }
                }

                // Install if device connected and not in NoDevice mode
                if (!SettingsManager.Instance.NodeviceMode && !string.IsNullOrEmpty(Adb.DeviceId))
                {
                    UpdateProgress(85, "Installing APK...");

                    if (localApkPath != null && File.Exists(localApkPath))
                    {
                        var installResult = await Adb.SideloadAsync(localApkPath, gameToDownload.PackageName, _dialogService);
                        if (installResult.Output.Contains("Success"))
                        {
                            Logger.Log($"APK installed successfully: {Path.GetFileName(localApkPath)}");
                        }
                    }

                    // Copy OBB files (checks the gameDownloadPath for any .obb files)
                    var obbFilesInFolder = Directory.GetFiles(gameDownloadPath, "*.obb", SearchOption.AllDirectories);
                    if (obbFilesInFolder.Length > 0)
                    {
                        UpdateProgress(90, "Copying OBB files...");
                        var obbResult = Adb.CopyObb(gameDownloadPath);
                        Logger.Log($"OBB files copied: {obbFilesInFolder.Length} files");

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
                                    Adb.CopyObb(gameDownloadPath);
                                    Logger.Log("OBB copy retried");
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

                // Clear game name from progress display
                Rclone.CurrentGameName = null;

                // Download completed successfully, process next in queue
                // Do this BEFORE showing the dialog so queue processing isn't blocked
                _isDownloading = false;
                await ProcessNextQueueItem();

                // Show completion dialog AFTER starting next queue item (fire-and-forget so it doesn't block)
                if (_dialogService != null)
                {
                    // Capture local variables for the dialog (next queue item might change these)
                    var completedGameName = gameToDownload.GameName;
                    var completedGamePath = gameDownloadPath;

                    // Count downloaded files
                    var apkCount = Directory.GetFiles(completedGamePath, "*.apk", SearchOption.AllDirectories).Length;
                    var obbCount = Directory.GetFiles(completedGamePath, "*.obb", SearchOption.AllDirectories).Length;
                    var totalDownloaded = apkCount + obbCount;

                    var message = $"Download complete!\n\nGame: {completedGameName}\n" +
                                  $"Files: {totalDownloaded} (APK: {apkCount}, OBB: {obbCount})\n" +
                                  $"Location: {completedGamePath}";

                    if (!SettingsManager.Instance.NodeviceMode && !string.IsNullOrEmpty(Adb.DeviceId))
                    {
                        message += "\n\nGame has been installed to device.";
                    }

                    // Fire-and-forget - don't await so queue processing continues
                    _dialogService.ShowInfoAsync(message, "Download Complete").GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                HideProgress();
                Rclone.CurrentGameName = null; // Clear game name on error
                Logger.Log($"Error downloading game: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error downloading game";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error downloading:\n\n{ex.Message}", "Download Error");
                }

                // Download failed, clear flag and process next in queue
                _isDownloading = false;
                await ProcessNextQueueItem();
            }
        }

        /// <summary>
        /// Process the next game in the download queue
        /// </summary>
        private async Task ProcessNextQueueItem()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Remove the completed game from queue (first item)
                if (GamesQueue.Count > 0)
                {
                    var completed = GamesQueue[0];
                    GamesQueue.RemoveAt(0);
                    Logger.Log($"Removed completed game from queue: {completed}");
                }

                // Check if there are more games to download
                if (GamesQueue.Count > 0)
                {
                    var nextGameName = GamesQueue[0];
                    Logger.Log($"Processing next queued game: {nextGameName}");

                    // Find the game in the games list
                    var nextGame = _allGames.FirstOrDefault(g => g.GameName == nextGameName);
                    if (nextGame != null)
                    {
                        SelectedGame = nextGame;
                        Logger.Log($"Found game in list: {nextGame.GameName}");

                        // Start downloading the next game
                        Task.Run(async () =>
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
                        GamesQueue.RemoveAt(0);
                        ProcessNextQueueItem().GetAwaiter().GetResult();
                    }
                }
                else
                {
                    Logger.Log("Queue is empty, no more games to download");
                }
            });
        }

        private async Task DisableSideloadingAsync()
        {
            Logger.Log("Disable Sideloading command triggered");

            try
            {
                // Check if device is connected
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No device connected. Please connect your device first.",
                            "No Device");
                    }
                    return;
                }

                // Confirm with user
                if (_dialogService != null)
                {
                    var confirmed = await _dialogService.ShowConfirmationAsync(
                        "This will disable developer mode and sideloading on your device.\n\n" +
                        "You will need to re-enable it in your Quest settings to sideload apps again.\n\n" +
                        "Continue?",
                        "Disable Sideloading");

                    if (!confirmed) return;
                }

                ProgressStatusText = "Disabling sideloading...";

                // Disable USB debugging (this requires user confirmation on device)
                var result = Adb.RunAdbCommandToString("shell settings put global adb_enabled 0");

                ProgressStatusText = "Sideloading disabled";

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        "Sideloading has been disabled on your device.\n\n" +
                        "To re-enable, go to Settings > Developer on your Quest.",
                        "Sideloading Disabled");
                }

                Logger.Log("Sideloading disabled on device");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error disabling sideloading: {ex.Message}", LogLevel.Error);
                ProgressStatusText = "Error disabling sideloading";

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}", "Error");
                }
            }
        }

        private async Task ShowGamesListAsync()
        {
            Logger.Log("Games List command triggered");

            try
            {
                // Open the VRP games list in browser
                var gamesListUrl = "https://wiki.vrpirates.club/en/gamelist";

                if (_dialogService != null)
                {
                    var confirmed = await _dialogService.ShowConfirmationAsync(
                        $"Open VR Pirates games list in your browser?\n\n{gamesListUrl}",
                        "Open Games List");

                    if (!confirmed) return;
                }

                OpenUrl(gamesListUrl);

                Logger.Log($"Opened games list URL: {gamesListUrl}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening games list: {ex.Message}", LogLevel.Error);

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error opening games list:\n\n{ex.Message}", "Error");
                }
            }
        }

        private async Task ShowAboutAsync()
        {
            Logger.Log("Show About command triggered");

            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
                var aboutMessage = $"Rookie Sideloader\n" +
                                   $"Version: {version}\n\n" +
                                   $"A cross-platform Android sideloading tool for VR headsets.\n\n" +
                                   $"Original project: VRPirates/rookie\n" +
                                   $"Migrated to .NET 9 + Avalonia UI\n\n" +
                                   $"Platform: {(PlatformHelper.IsWindows ? "Windows" : PlatformHelper.IsMacOs ? "macOS" : "Linux")}\n" +
                                   $"Framework: .NET 9.0\n" +
                                   $"UI Framework: Avalonia 11.2.0";

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(aboutMessage, "About Rookie Sideloader");
                }

                Logger.Log("About dialog displayed");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error showing about: {ex.Message}", LogLevel.Error);
            }
        }

        private async Task ShowSettingsAsync()
        {
            Logger.Log("Show Settings command triggered");

            try
            {
                // Get main window
                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error showing settings: {ex.Message}", LogLevel.Error);

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error showing settings:\n\n{ex.Message}", "Settings Error");
                }
            }
        }

        private async Task ShowQuestOptionsAsync()
        {
            Logger.Log("Show Quest Options command triggered");

            try
            {
                // Get main window
                var window = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (window == null)
                {
                    Logger.Log("Cannot get main window for Quest Options dialog", LogLevel.Error);
                    return;
                }

                // Create and show Quest Options window
                var questOptionsWindow = new Views.QuestOptionsWindow();
                await questOptionsWindow.ShowDialog(window);

                Logger.Log("Quest Options dialog closed");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error showing quest options: {ex.Message}", LogLevel.Error);

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error showing quest options:\n\n{ex.Message}", "Quest Options Error");
                }
            }
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

                // Categorize files by extension and type
                var apkFiles = new List<string>();
                var obbFolders = new List<string>();
                var installTxtFiles = new List<string>();

                foreach (var path in filePaths)
                {
                    if (File.Exists(path))
                    {
                        // It's a file
                        var ext = Path.GetExtension(path).ToLower();
                        var fileName = Path.GetFileName(path).ToLower();

                        if (ext == ".apk")
                        {
                            apkFiles.Add(path);
                        }
                        else if (ext == ".obb")
                        {
                            // OBB file - add its parent directory
                            var parentDir = Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(parentDir) && !obbFolders.Contains(parentDir))
                            {
                                obbFolders.Add(parentDir);
                            }
                        }
                        else if (fileName == "install.txt")
                        {
                            installTxtFiles.Add(path);
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
                            if (dirName.StartsWith("com.") && dirName.Contains("."))
                            {
                                // Likely a package name directory with OBB files
                                obbFolders.Add(subDir);
                            }
                        }
                    }
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
                if (obbFolders.Count > 0)
                {
                    summary += $"OBB folders: {obbFolders.Count}\n";
                }

                if (installTxtFiles.Count == 0 && apkFiles.Count == 0 && obbFolders.Count == 0)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "No APK, OBB, or install.txt files detected in dropped items.",
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
                    var progressPercent = ((double)currentOperation / totalOperations) * 100;
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
                    var progressPercent = ((double)currentOperation / totalOperations) * 100;
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
                                // Look for subdirectories with package name pattern
                                var subDirs = Directory.GetDirectories(apkDir);
                                foreach (var subDir in subDirs)
                                {
                                    var dirName = Path.GetFileName(subDir);
                                    if (dirName.StartsWith("com.") && dirName.Contains("."))
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
                    var progressPercent = ((double)currentOperation / totalOperations) * 100;
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
                    resultSummary += $"OBB folders copied: {obbFolders.Count}";
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
                    await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}", "Error");
                }
            }
        }

        private async Task ToggleFavoriteAsync()
        {
            Logger.Log("Toggle Favorite command triggered");

            try
            {
                if (SelectedGame == null)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowWarningAsync(
                            "Please select a game to mark as favorite.",
                            "No Game Selected");
                    }
                    return;
                }

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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error toggling favorite: {ex.Message}", LogLevel.Error);

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Error:\n\n{ex.Message}", "Error");
                }
            }
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

        private static void OpenUrl(string url)
        {
            try
            {
                Logger.Log($"Opening URL: {url}");

                // Open URL in default browser (cross-platform)
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening URL: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Parse battery level from dumpsys battery output
        /// </summary>
        private static string ParseBatteryLevel(string dumpsysOutput)
        {
            try
            {
                // Look for "level: XX" in the output
                var lines = dumpsysOutput.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("level:"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1)
                        {
                            return parts[1].Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error parsing battery level: {ex.Message}", LogLevel.Warning);
            }
            return null;
        }

        /// <summary>
        /// Set the dialog service after construction (for MainWindow initialization)
        /// </summary>
        public void SetDialogService(IDialogService dialogService)
        {
            if (dialogService is IDialogService service)
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
        /// Extract game from device and prepare for upload
        /// </summary>
        private async Task ExtractGameForUploadAsync()
        {
            try
            {
                ProgressStatusText = "Extracting game for upload...";
                IsProgressVisible = true;
                ProgressPercentage = 0;

                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync("No device connected. Please connect a device first.", "No Device");
                    }
                    IsProgressVisible = false;
                    return;
                }

                if (SelectedGame == null)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowErrorAsync("Please select a game from the list first.", "No Game Selected");
                    }
                    IsProgressVisible = false;
                    return;
                }

                var packageName = SelectedGame.PackageName;
                if (string.IsNullOrEmpty(packageName))
                {
                    packageName = SideloaderUtilities.GameNameToPackageName(SelectedGame.GameName);
                }

                // Check if game is installed on device
                var installedCheck = Adb.RunAdbCommandToString($"shell pm list packages {packageName}");
                if (!installedCheck.Output.Contains(packageName))
                {
                    if (_dialogService != null)
                    {
                        var shouldContinue = await _dialogService.ShowConfirmationAsync(
                            $"The game '{SelectedGame.GameName}' doesn't appear to be installed on the device.\n\n" +
                            $"Do you want to continue anyway?",
                            "Game Not Installed");

                        if (!shouldContinue)
                        {
                            IsProgressVisible = false;
                            return;
                        }
                    }
                }

                // Check if this is an update or new upload
                var isUpdate = SideloaderRclone.Games.Any(g =>
                    g[SideloaderRclone.PackageNameIndex] == packageName);

                if (_dialogService != null)
                {
                    var message = isUpdate
                        ? $"This will extract and prepare '{SelectedGame.GameName}' for upload as an UPDATE.\n\nContinue?"
                        : $"This will extract and prepare '{SelectedGame.GameName}' for upload as a NEW game.\n\nContinue?";

                    var confirmed = await _dialogService.ShowConfirmationAsync(message, "Confirm Upload Preparation");
                    if (!confirmed)
                    {
                        IsProgressVisible = false;
                        return;
                    }
                }

                // Step 1: Extract APK (20%)
                ProgressStatusText = "Extracting APK from device...";
                ProgressPercentage = 10;
                await Task.Run(() => SideloaderUtilities.GetApk(packageName));
                ProgressPercentage = 30;

                // Step 2: Pull OBB folder (40%)
                ProgressStatusText = "Pulling OBB files (if any)...";
                var gameDir = Path.Combine(SettingsManager.Instance.MainDir, packageName);
                Adb.RunAdbCommandToString($"pull \"/sdcard/Android/obb/{packageName}\" \"{gameDir}\"");
                ProgressPercentage = 70;

                // Step 3: Create HWID file
                var hwid = SideloaderUtilities.Uuid();
                await File.WriteAllTextAsync(Path.Combine(gameDir, "HWID.txt"), hwid);
                Logger.Log($"Created HWID: {hwid}");

                // Step 4: Get version info
                var versionOutput = Adb.RunAdbCommandToString($"shell dumpsys package {packageName} | grep versionCode");
                var versionCode = "0";
                if (versionOutput.Output.Contains("versionCode="))
                {
                    // Parse versionCode from output
                    var match = Regex.Match(versionOutput.Output, @"versionCode=(\d+)");
                    if (match.Success)
                    {
                        versionCode = match.Groups[1].Value;
                    }
                }

                // Step 5: Add to upload queue
                var uploadGame = new UploadGame
                {
                    GameName = SelectedGame.GameName,
                    PackageName = packageName,
                    VersionCode = versionCode,
                    ApkPath = Path.Combine(gameDir, $"{packageName}.apk"),
                    ObbPath = Path.Combine(gameDir, packageName),
                    Status = UploadStatus.Queued,
                    QueuedAt = DateTime.Now
                };

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UploadQueue.Add(uploadGame);
                });

                ProgressPercentage = 100;
                ProgressStatusText = $"Ready for upload: {SelectedGame.GameName}";

                if (_dialogService != null)
                {
                    await _dialogService.ShowInfoAsync(
                        $"Game extracted successfully!\n\n" +
                        $"Game: {SelectedGame.GameName}\n" +
                        $"Package: {packageName}\n" +
                        $"Version: {versionCode}\n" +
                        $"Location: {gameDir}\n\n" +
                        $"The game has been added to the upload queue.\n" +
                        $"Use 'Process Upload Queue' to upload to VRPirates mirrors.",
                        "Extraction Complete");
                }

                Logger.Log($"Game prepared for upload: {SelectedGame.GameName} (Queue: {UploadQueue.Count})");

                await Task.Delay(2000);
                IsProgressVisible = false;
                ProgressStatusText = "Ready";
            }
            catch (Exception ex)
            {
                Logger.Log($"Error extracting game for upload: {ex.Message}", LogLevel.Error);
                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync($"Failed to extract game: {ex.Message}", "Extraction Error");
                }
                IsProgressVisible = false;
                ProgressStatusText = "Ready";
            }
        }


        /// <summary>
        /// Process upload queue and upload games to VRPirates mirrors
        /// </summary>
        private async Task ProcessUploadQueueAsync()
        {
            try
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

                        // Upload APK
                        var remoteApkPath = $"{uploadRemote}:Quest Games/{game.GameName}/{Path.GetFileName(game.ApkPath)}";
                        Logger.Log($"Uploading APK to: {remoteApkPath}");

                        var apkUploadResult = await Rclone.runRcloneCommand_UploadConfig(
                            $"copy \"{game.ApkPath}\" \"{uploadRemote}:Quest Games/{game.GameName}/\"");

                        if (apkUploadResult.Error.Contains("Failed") || apkUploadResult.Error.Contains("error"))
                        {
                            throw new Exception($"APK upload failed: {apkUploadResult.Error}");
                        }

                        // Upload OBB if exists
                        if (Directory.Exists(game.ObbPath))
                        {
                            var obbFiles = Directory.GetFiles(game.ObbPath, "*.*", SearchOption.AllDirectories);
                            if (obbFiles.Length > 0)
                            {
                                Logger.Log($"Uploading {obbFiles.Length} OBB file(s)...");

                                var obbUploadResult = await Rclone.runRcloneCommand_UploadConfig(
                                    $"copy \"{game.ObbPath}\" \"{uploadRemote}:Quest Games/{game.GameName}/{game.PackageName}/\"");

                                if (obbUploadResult.Error.Contains("Failed") || obbUploadResult.Error.Contains("error"))
                                {
                                    Logger.Log($"OBB upload warning: {obbUploadResult.Error}", LogLevel.Warning);
                                }
                            }
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Error processing upload queue: {ex.Message}", LogLevel.Error);

                if (_dialogService != null)
                {
                    await _dialogService.ShowErrorAsync(
                        $"Upload queue processing failed:\n\n{ex.Message}",
                        "Upload Error");
                }

                ProgressStatusText = "Ready";
                IsProgressVisible = false;
            }
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
        /// Handles drag enter event - shows drag overlay
        /// </summary>
        private void DragEnter()
        {
            ProgressStatusText = "Drag apk or obb";
            Logger.Log("File dragged over window");
        }

        /// <summary>
        /// Handles drag leave event - hides drag overlay
        /// </summary>
        private void DragLeave()
        {
            ProgressStatusText = string.Empty;
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
                            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
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
                string[] possibleNames = { SelectedGame.PackageName, SelectedGame.GameName, SelectedGame.ReleaseName };

                Logger.Log($"Looking for thumbnails for: PackageName='{SelectedGame.PackageName}', GameName='{SelectedGame.GameName}', ReleaseName='{SelectedGame.ReleaseName}'", LogLevel.Debug);

                foreach (var name in possibleNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;

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

                    // Optionally trigger download if remote is selected
                    if (!string.IsNullOrEmpty(SelectedRemote) && !string.IsNullOrEmpty(SelectedGame.GameName))
                    {
                        // Try to download the image in the background (don't await to avoid blocking)
                        await Task.Run(async () =>
                        {
                            var downloadedPath = await ImageCache.DownloadAndCacheImageAsync(
                                SelectedGame.GameName,
                                SelectedRemote);

                            if (!string.IsNullOrEmpty(downloadedPath) && SelectedGame == SelectedGame)
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
}