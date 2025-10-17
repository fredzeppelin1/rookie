using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AndroidSideloader.Utilities;

public class SettingsManager
{
    public static SettingsManager Instance { get; } = new();

    // Logging
    public string CurrentLogPath { get; set; }
    public string CurrentCrashLogPath { get; set; }
    public bool LogRclone { get; private set; }
    public string Uuid { get; set; }

    // ADB Configuration
    public string AdbFolder { get; private set; }
    public string AdbPath { get; private set; }
    public bool AdbDebugWarned { get; set; }
    public bool NodeviceMode { get; set; }
    public bool AutoReinstall { get; set; }
    public bool Wired { get; set; }
    public string IpAddress { get; set; }

    // Download Configuration
    public string DownloadDir { get; set; }
    public bool CustomDownloadDir { get; set; }
    public string MainDir { get; set; }
    public string BackupDir { get; set; }
    public bool CustomBackupDir { get; set; }

    // RCLONE Configuration
    public int BandwidthLimit { get; set; } // In MB/s, 0 = unlimited
    public bool CheckForUpdates { get; set; }
    public bool EnableMessageBoxes { get; set; }
    public bool DeleteAllAfterInstall { get; set; }
    public bool AutoUpdateConfig { get; set; }
    public bool CreatePubMirrorFile { get; set; }
    public bool UserJsonOnGameInstall { get; set; }
    public bool UseDownloadedFiles { get; set; }
    public bool TrailersOn { get; set; }
    public bool SingleThreadMode { get; set; }
    public bool BmbfChecked { get; set; }
    public bool VirtualFilesystemCompatibility { get; set; }
    public bool HideAdultContent { get; set; }

    // UI Configuration
    public bool PackageNameToCb { get; private set; } // Copy package name to clipboard on game selection

    // First Run and Upgrade
    private bool FirstRun { get; set; }
    private bool UpdateSettings { get; set; }

    // Upload/Share Configuration
    private string GlobalUsername { get; set; }
    private DateTime? LastTimeShared { get; set; }

    // Wireless ADB
    public bool WirelessAdb { get; set; }

    // Advanced Features (not yet implemented in migration)
    private bool SpoofGames { get; set; }
    private bool ResignApks { get; set; }

    // Favorites - stored as package names
    public HashSet<string> FavoriteGames { get; private set; }

    // Telemetry
    public DateTime? LastLaunch { get; set; }

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    // Constructor with defaults
    private SettingsManager()
    {
        // Set default paths
        CurrentLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debuglog.txt");
        CurrentCrashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crashlog.txt");
        LogRclone = false;
        AdbFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools");
        AdbPath = Path.Combine(AdbFolder, PlatformHelper.GetAdbExecutableName());
        Uuid = Guid.NewGuid().ToString();
        LastLaunch = null;
        AdbDebugWarned = false;
        NodeviceMode = false;
        AutoReinstall = false;
        Wired = true;
        IpAddress = string.Empty;

        // Download defaults - matches original behavior (portable app style)
        DownloadDir = AppDomain.CurrentDomain.BaseDirectory;
        CustomDownloadDir = false;
        MainDir = AppDomain.CurrentDomain.BaseDirectory;
        BackupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rookie Backups");
        CustomBackupDir = false;

        // RCLONE defaults
        BandwidthLimit = 0; // Unlimited
        CheckForUpdates = true;
        EnableMessageBoxes = true;
        DeleteAllAfterInstall = false;
        AutoUpdateConfig = true;
        CreatePubMirrorFile = true;
        UserJsonOnGameInstall = false;
        UseDownloadedFiles = false;
        TrailersOn = false;
        SingleThreadMode = false;
        BmbfChecked = true;
        VirtualFilesystemCompatibility = false;
        HideAdultContent = false;

        // UI defaults
        PackageNameToCb = false; // Don't copy package name by default

        // First run and upgrade defaults
        FirstRun = true;
        UpdateSettings = true;

        // Upload/Share defaults
        GlobalUsername = string.Empty;
        LastTimeShared = null;

        // Wireless ADB default
        WirelessAdb = false;

        // Advanced features defaults
        SpoofGames = false;
        ResignApks = false;

        // Favorites
        FavoriteGames = [];

        // Load saved settings
        Load();
    }

    private void Load()
    {
        try
        {
            var settingsPath = GetSettingsPath();
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var loadedSettings = JsonSerializer.Deserialize<SettingsData>(json);

                if (loadedSettings != null)
                {
                    // Apply loaded settings
                    if (!string.IsNullOrEmpty(loadedSettings.CurrentLogPath))
                    {
                        CurrentLogPath = loadedSettings.CurrentLogPath;
                    }

                    if (!string.IsNullOrEmpty(loadedSettings.CurrentCrashLogPath))
                    {
                        CurrentCrashLogPath = loadedSettings.CurrentCrashLogPath;
                    }

                    LogRclone = loadedSettings.LogRclone;
                    if (!string.IsNullOrEmpty(loadedSettings.Uuid))
                    {
                        Uuid = loadedSettings.Uuid;
                    }

                    LastLaunch = loadedSettings.LastLaunch;
                    if (!string.IsNullOrEmpty(loadedSettings.AdbFolder))
                    {
                        AdbFolder = loadedSettings.AdbFolder;
                    }

                    if (!string.IsNullOrEmpty(loadedSettings.AdbPath))
                    {
                        AdbPath = loadedSettings.AdbPath;
                    }

                    if (!string.IsNullOrEmpty(loadedSettings.DownloadDir))
                    {
                        DownloadDir = loadedSettings.DownloadDir;
                    }

                    if (!string.IsNullOrEmpty(loadedSettings.MainDir))
                    {
                        MainDir = loadedSettings.MainDir;
                    }

                    if (!string.IsNullOrEmpty(loadedSettings.BackupDir))
                    {
                        BackupDir = loadedSettings.BackupDir;
                    }

                    if (!string.IsNullOrEmpty(loadedSettings.IpAddress))
                    {
                        IpAddress = loadedSettings.IpAddress;
                    }

                    AdbDebugWarned = loadedSettings.AdbDebugWarned;
                    NodeviceMode = loadedSettings.NodeviceMode;
                    AutoReinstall = loadedSettings.AutoReinstall;
                    Wired = loadedSettings.Wired;
                    CustomDownloadDir = loadedSettings.CustomDownloadDir;
                    CustomBackupDir = loadedSettings.CustomBackupDir;
                    BandwidthLimit = loadedSettings.BandwidthLimit;
                    CheckForUpdates = loadedSettings.CheckForUpdates;
                    EnableMessageBoxes = loadedSettings.EnableMessageBoxes;
                    DeleteAllAfterInstall = loadedSettings.DeleteAllAfterInstall;
                    AutoUpdateConfig = loadedSettings.AutoUpdateConfig;
                    CreatePubMirrorFile = loadedSettings.CreatePubMirrorFile;
                    UserJsonOnGameInstall = loadedSettings.UserJsonOnGameInstall;
                    UseDownloadedFiles = loadedSettings.UseDownloadedFiles;
                    TrailersOn = loadedSettings.TrailersOn;
                    SingleThreadMode = loadedSettings.SingleThreadMode;
                    BmbfChecked = loadedSettings.BmbfChecked;
                    VirtualFilesystemCompatibility = loadedSettings.VirtualFilesystemCompatibility;
                    HideAdultContent = loadedSettings.HideAdultContent;
                    PackageNameToCb = loadedSettings.PackageNameToCb;

                    FirstRun = loadedSettings.FirstRun;
                    UpdateSettings = loadedSettings.UpdateSettings;
                    if (!string.IsNullOrEmpty(loadedSettings.GlobalUsername))
                    {
                        GlobalUsername = loadedSettings.GlobalUsername;
                    }

                    LastTimeShared = loadedSettings.LastTimeShared;
                    WirelessAdb = loadedSettings.WirelessAdb;
                    SpoofGames = loadedSettings.SpoofGames;
                    ResignApks = loadedSettings.ResignApks;

                    if (loadedSettings.FavoriteGames != null)
                    {
                        FavoriteGames = [..loadedSettings.FavoriteGames];
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load settings: {ex.Message}");
            // Continue with defaults
        }
    }

    public void Save()
    {
        try
        {
            var settingsData = new SettingsData
            {
                CurrentLogPath = CurrentLogPath,
                CurrentCrashLogPath = CurrentCrashLogPath,
                LogRclone = LogRclone,
                Uuid = Uuid,
                LastLaunch = LastLaunch,
                AdbFolder = AdbFolder,
                AdbPath = AdbPath,
                AdbDebugWarned = AdbDebugWarned,
                NodeviceMode = NodeviceMode,
                AutoReinstall = AutoReinstall,
                Wired = Wired,
                IpAddress = IpAddress,
                DownloadDir = DownloadDir,
                CustomDownloadDir = CustomDownloadDir,
                MainDir = MainDir,
                BackupDir = BackupDir,
                CustomBackupDir = CustomBackupDir,
                BandwidthLimit = BandwidthLimit,
                CheckForUpdates = CheckForUpdates,
                EnableMessageBoxes = EnableMessageBoxes,
                DeleteAllAfterInstall = DeleteAllAfterInstall,
                AutoUpdateConfig = AutoUpdateConfig,
                CreatePubMirrorFile = CreatePubMirrorFile,
                UserJsonOnGameInstall = UserJsonOnGameInstall,
                UseDownloadedFiles = UseDownloadedFiles,
                TrailersOn = TrailersOn,
                SingleThreadMode = SingleThreadMode,
                BmbfChecked = BmbfChecked,
                VirtualFilesystemCompatibility = VirtualFilesystemCompatibility,
                HideAdultContent = HideAdultContent,
                PackageNameToCb = PackageNameToCb,
                FirstRun = FirstRun,
                UpdateSettings = UpdateSettings,
                GlobalUsername = GlobalUsername,
                LastTimeShared = LastTimeShared,
                WirelessAdb = WirelessAdb,
                SpoofGames = SpoofGames,
                ResignApks = ResignApks,
                FavoriteGames = new List<string>(FavoriteGames)
            };

            var settingsPath = GetSettingsPath();
            var settingsDir = Path.GetDirectoryName(settingsPath);
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }

            var json = JsonSerializer.Serialize(settingsData, _jsonSerializerOptions);
            File.WriteAllText(settingsPath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save settings: {ex.Message}");
        }
    }

    private static string GetSettingsPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var rookieDataPath = Path.Combine(appDataPath, "Rookie");
        return Path.Combine(rookieDataPath, "settings.json");
    }

    // Data class for JSON serialization
    private class SettingsData
    {
        public string CurrentLogPath { get; init; }
        public string CurrentCrashLogPath { get; init; }
        public bool LogRclone { get; init; }
        public string Uuid { get; init; }
        public DateTime? LastLaunch { get; init; }
        public string AdbFolder { get; init; }
        public string AdbPath { get; init; }
        public bool AdbDebugWarned { get; init; }
        public bool NodeviceMode { get; init; }
        public bool AutoReinstall { get; init; }
        public bool Wired { get; init; }
        public string IpAddress { get; init; }
        public string DownloadDir { get; init; }
        public bool CustomDownloadDir { get; init; }
        public string MainDir { get; init; }
        public string BackupDir { get; init; }
        public bool CustomBackupDir { get; init; }
        public int BandwidthLimit { get; init; }
        public bool CheckForUpdates { get; init; }
        public bool EnableMessageBoxes { get; init; }
        public bool DeleteAllAfterInstall { get; init; }
        public bool AutoUpdateConfig { get; init; }
        public bool CreatePubMirrorFile { get; init; }
        public bool UserJsonOnGameInstall { get; init; }
        public bool UseDownloadedFiles { get; init; }
        public bool TrailersOn { get; init; }
        public bool SingleThreadMode { get; init; }
        public bool BmbfChecked { get; init; }
        public bool VirtualFilesystemCompatibility { get; init; }
        public bool HideAdultContent { get; init; }
        public bool PackageNameToCb { get; init; }
        public bool FirstRun { get; init; }
        public bool UpdateSettings { get; init; }
        public string GlobalUsername { get; init; }
        public DateTime? LastTimeShared { get; init; }
        public bool WirelessAdb { get; init; }
        public bool SpoofGames { get; init; }
        public bool ResignApks { get; init; }
        public List<string> FavoriteGames { get; init; }
    }
}