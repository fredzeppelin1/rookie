using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using AndroidSideloader.Utilities;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace AndroidSideloader.Views
{
    public partial class SettingsWindow : Window
    {
        private static readonly SettingsManager Settings = SettingsManager.Instance;

        public bool SettingsSaved { get; private set; } = false;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Load;
        }

        private void SettingsWindow_Load(object sender, RoutedEventArgs e)
        {
            InitSettings();
            WireUpEvents();
        }

        private void InitSettings()
        {
            // Load all checkbox states from settings
            var checkForUpdates = this.FindControl<CheckBox>("CheckForUpdatesCheckBox");
            if (checkForUpdates != null) checkForUpdates.IsChecked = Settings.CheckForUpdates;

            var updateConfig = this.FindControl<CheckBox>("UpdateConfigCheckBox");
            if (updateConfig != null) updateConfig.IsChecked = Settings.AutoUpdateConfig;

            var noDeviceMode = this.FindControl<CheckBox>("NoDeviceModeBox");
            if (noDeviceMode != null) noDeviceMode.IsChecked = Settings.NodeviceMode;

            var deleteAfterInstall = this.FindControl<CheckBox>("DeleteAfterInstallCheckBox");
            if (deleteAfterInstall != null)
            {
                deleteAfterInstall.IsChecked = Settings.DeleteAllAfterInstall;
                // Disable if nodevice mode is on
                if (Settings.NodeviceMode)
                {
                    deleteAfterInstall.IsChecked = false;
                    deleteAfterInstall.IsEnabled = false;
                }
            }

            var trailersOn = this.FindControl<CheckBox>("TrailersOnCheckBox");
            if (trailersOn != null) trailersOn.IsChecked = Settings.TrailersOn;

            var singleThread = this.FindControl<CheckBox>("SingleThreadCheckBox");
            if (singleThread != null) singleThread.IsChecked = Settings.SingleThreadMode;

            var useDownloadedFiles = this.FindControl<CheckBox>("UseDownloadedFilesCheckBox");
            if (useDownloadedFiles != null) useDownloadedFiles.IsChecked = Settings.UseDownloadedFiles;

            var autoReinstall = this.FindControl<CheckBox>("AutoReinstallCheckBox");
            if (autoReinstall != null) autoReinstall.IsChecked = Settings.AutoReinstall;

            var enableMessageBoxes = this.FindControl<CheckBox>("EnableMessageBoxesCheckBox");
            if (enableMessageBoxes != null) enableMessageBoxes.IsChecked = Settings.EnableMessageBoxes;

            var userJsonOnGameInstall = this.FindControl<CheckBox>("UserJsonOnGameInstallCheckBox");
            if (userJsonOnGameInstall != null) userJsonOnGameInstall.IsChecked = Settings.UserJsonOnGameInstall;

            var bmbf = this.FindControl<CheckBox>("BMBFCheckBox");
            if (bmbf != null) bmbf.IsChecked = Settings.BmbfChecked;

            var virtualFilesystem = this.FindControl<CheckBox>("VirtualFilesystemCompatibilityCheckBox");
            if (virtualFilesystem != null) virtualFilesystem.IsChecked = Settings.VirtualFilesystemCompatibility;

            var hideAdultContent = this.FindControl<CheckBox>("HideAdultContentCheckBox");
            if (hideAdultContent != null) hideAdultContent.IsChecked = Settings.HideAdultContent;

            // Load bandwidth limit
            var bandwidthLimit = this.FindControl<TextBox>("BandwidthLimitTextBox");
            if (bandwidthLimit != null) bandwidthLimit.Text = Settings.BandwidthLimit.ToString();
        }

        private void WireUpEvents()
        {
            // Wire up checkbox events
            var checkForUpdates = this.FindControl<CheckBox>("CheckForUpdatesCheckBox");
            if (checkForUpdates != null)
                checkForUpdates.IsCheckedChanged += CheckForUpdatesCheckBox_CheckedChanged;

            var updateConfig = this.FindControl<CheckBox>("UpdateConfigCheckBox");
            if (updateConfig != null)
                updateConfig.IsCheckedChanged += UpdateConfigCheckBox_CheckedChanged;

            var noDeviceMode = this.FindControl<CheckBox>("NoDeviceModeBox");
            if (noDeviceMode != null)
                noDeviceMode.IsCheckedChanged += NoDeviceModeBox_CheckedChanged;

            var deleteAfterInstall = this.FindControl<CheckBox>("DeleteAfterInstallCheckBox");
            if (deleteAfterInstall != null)
                deleteAfterInstall.IsCheckedChanged += DeleteAfterInstallCheckBox_CheckedChanged;

            var trailersOn = this.FindControl<CheckBox>("TrailersOnCheckBox");
            if (trailersOn != null)
                trailersOn.IsCheckedChanged += TrailersOnCheckBox_CheckedChanged;

            var singleThread = this.FindControl<CheckBox>("SingleThreadCheckBox");
            if (singleThread != null)
                singleThread.IsCheckedChanged += SingleThreadCheckBox_CheckedChanged;

            var useDownloadedFiles = this.FindControl<CheckBox>("UseDownloadedFilesCheckBox");
            if (useDownloadedFiles != null)
                useDownloadedFiles.IsCheckedChanged += UseDownloadedFilesCheckBox_CheckedChanged;

            var autoReinstall = this.FindControl<CheckBox>("AutoReinstallCheckBox");
            if (autoReinstall != null)
                autoReinstall.IsCheckedChanged += AutoReinstallCheckBox_CheckedChanged;

            var enableMessageBoxes = this.FindControl<CheckBox>("EnableMessageBoxesCheckBox");
            if (enableMessageBoxes != null)
                enableMessageBoxes.IsCheckedChanged += EnableMessageBoxesCheckBox_CheckedChanged;

            var userJsonOnGameInstall = this.FindControl<CheckBox>("UserJsonOnGameInstallCheckBox");
            if (userJsonOnGameInstall != null)
                userJsonOnGameInstall.IsCheckedChanged += UserJsonOnGameInstallCheckBox_CheckedChanged;

            var bmbf = this.FindControl<CheckBox>("BMBFCheckBox");
            if (bmbf != null)
                bmbf.IsCheckedChanged += BMBFCheckBox_CheckedChanged;

            var virtualFilesystem = this.FindControl<CheckBox>("VirtualFilesystemCompatibilityCheckBox");
            if (virtualFilesystem != null)
                virtualFilesystem.IsCheckedChanged += VirtualFilesystemCompatibilityCheckBox_CheckedChanged;

            var hideAdultContent = this.FindControl<CheckBox>("HideAdultContentCheckBox");
            if (hideAdultContent != null)
                hideAdultContent.IsCheckedChanged += HideAdultContentCheckBox_CheckedChanged;

            // Wire up bandwidth limit textbox
            var bandwidthLimit = this.FindControl<TextBox>("BandwidthLimitTextBox");
            if (bandwidthLimit != null)
                bandwidthLimit.KeyDown += BandwidthLimitTextBox_KeyDown;

            // Wire up button events
            var applySettings = this.FindControl<Button>("ApplySettingsButton");
            if (applySettings != null)
                applySettings.Click += ApplySettingsButton_Click;

            var resetSettings = this.FindControl<Button>("ResetSettingsButton");
            if (resetSettings != null)
                resetSettings.Click += ResetSettingsButton_Click;

            var openDebugLog = this.FindControl<Button>("OpenDebugLogButton");
            if (openDebugLog != null)
                openDebugLog.Click += OpenDebugLogButton_Click;

            var resetDebugLog = this.FindControl<Button>("ResetDebugLogButton");
            if (resetDebugLog != null)
                resetDebugLog.Click += ResetDebugLogButton_Click;

            var uploadDebugLog = this.FindControl<Button>("UploadDebugLogButton");
            if (uploadDebugLog != null)
                uploadDebugLog.Click += UploadDebugLogButton_Click;

            var setDownloadDirectory = this.FindControl<Button>("SetDownloadDirectoryButton");
            if (setDownloadDirectory != null)
                setDownloadDirectory.Click += SetDownloadDirectoryButton_Click;

            var setBackupDirectory = this.FindControl<Button>("SetBackupDirectoryButton");
            if (setBackupDirectory != null)
                setBackupDirectory.Click += SetBackupDirectoryButton_Click;

            var openDownloadDirectory = this.FindControl<Button>("OpenDownloadDirectoryButton");
            if (openDownloadDirectory != null)
                openDownloadDirectory.Click += OpenDownloadDirectoryButton_Click;

            var openBackupDirectory = this.FindControl<Button>("OpenBackupDirectoryButton");
            if (openBackupDirectory != null)
                openBackupDirectory.Click += OpenBackupDirectoryButton_Click;
        }

        // Checkbox event handlers
        private void CheckForUpdatesCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.CheckForUpdates = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void UpdateConfigCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.AutoUpdateConfig = checkBox.IsChecked ?? false;
                if (Settings.AutoUpdateConfig)
                {
                    Settings.CreatePubMirrorFile = true;
                }
                Settings.Save();
            }
        }

        private void NoDeviceModeBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.NodeviceMode = checkBox.IsChecked ?? false;

                // Disable/enable delete after install checkbox
                var deleteAfterInstall = this.FindControl<CheckBox>("DeleteAfterInstallCheckBox");
                if (deleteAfterInstall != null)
                {
                    if (Settings.NodeviceMode)
                    {
                        deleteAfterInstall.IsChecked = false;
                        Settings.DeleteAllAfterInstall = false;
                        deleteAfterInstall.IsEnabled = false;
                    }
                    else
                    {
                        deleteAfterInstall.IsChecked = true;
                        Settings.DeleteAllAfterInstall = true;
                        deleteAfterInstall.IsEnabled = true;
                    }
                }

                Settings.Save();
            }
        }

        private void DeleteAfterInstallCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.DeleteAllAfterInstall = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void TrailersOnCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.TrailersOn = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void SingleThreadCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.SingleThreadMode = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void UseDownloadedFilesCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.UseDownloadedFiles = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void AutoReinstallCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.AutoReinstall = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void EnableMessageBoxesCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.EnableMessageBoxes = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void UserJsonOnGameInstallCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.UserJsonOnGameInstall = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void BMBFCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.BmbfChecked = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void VirtualFilesystemCompatibilityCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.VirtualFilesystemCompatibility = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        private void HideAdultContentCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                Settings.HideAdultContent = checkBox.IsChecked ?? false;
                Settings.Save();
            }
        }

        // Bandwidth limit validation
        private void BandwidthLimitTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Allow only digits, decimal point, backspace, delete, arrow keys
            if (sender is TextBox textBox)
            {
                var key = e.Key;
                var text = textBox.Text ?? "";

                // Allow control keys
                if (key == Key.Back || key == Key.Delete || key == Key.Left || key == Key.Right || key == Key.Tab)
                    return;

                // Allow digits
                if (key >= Key.D0 && key <= Key.D9)
                    return;

                // Allow numpad digits
                if (key >= Key.NumPad0 && key <= Key.NumPad9)
                    return;

                // Allow decimal point if not already present
                if (key == Key.OemPeriod || key == Key.Decimal)
                {
                    if (!text.Contains("."))
                        return;
                }

                // Block all other keys
                e.Handled = true;
            }
        }

        // Button event handlers
        private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bandwidthLimit = this.FindControl<TextBox>("BandwidthLimitTextBox");
                if (bandwidthLimit != null)
                {
                    string input = bandwidthLimit.Text ?? "0";
                    Regex regex = new Regex(@"^\d+(\.\d+)?$");

                    if (regex.IsMatch(input) && int.TryParse(input, out int limit))
                    {
                        Settings.BandwidthLimit = limit;
                        Settings.Save();
                        Logger.Log($"Settings applied: Bandwidth limit set to {limit} MB/s");
                        SettingsSaved = true;
                        Close();
                    }
                    else
                    {
                        Logger.Log("Please enter a valid number for the bandwidth limit.", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error applying settings: {ex.Message}", LogLevel.Error);
            }
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Reset specific properties
                Settings.CustomDownloadDir = false;
                Settings.CustomBackupDir = false;

                // Set backup folder and download directory to defaults
                Settings.BackupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rookie Backups");
                Settings.DownloadDir = AppDomain.CurrentDomain.BaseDirectory;
                Settings.CreatePubMirrorFile = true;

                // Reload settings in UI
                InitSettings();

                // Save the updated settings
                Settings.Save();

                Logger.Log("Settings reset to defaults");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error resetting settings: {ex.Message}", LogLevel.Error);
            }
        }

        private void OpenDebugLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var debugLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debuglog.txt");
                if (File.Exists(debugLogPath))
                {
                    if (PlatformHelper.IsWindows)
                    {
                        Process.Start(new ProcessStartInfo { FileName = debugLogPath, UseShellExecute = true });
                    }
                    else if (PlatformHelper.IsMacOs)
                    {
                        Process.Start("open", debugLogPath);
                    }
                    else if (PlatformHelper.IsLinux)
                    {
                        Process.Start("xdg-open", debugLogPath);
                    }

                    Logger.Log("Opened debug log");
                }
                else
                {
                    Logger.Log("Debug log file not found", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening debug log: {ex.Message}", LogLevel.Error);
            }
        }

        private void ResetDebugLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var debugLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debuglog.txt");
                if (File.Exists(debugLogPath))
                {
                    File.Delete(debugLogPath);
                    Logger.Log("Debug log reset");
                }

                if (File.Exists(Settings.CurrentLogPath))
                {
                    File.Delete(Settings.CurrentLogPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error resetting debug log: {ex.Message}", LogLevel.Error);
            }
        }

        private async void UploadDebugLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var debugLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debuglog.txt");
                if (!File.Exists(debugLogPath))
                {
                    Logger.Log("Debug log file not found", LogLevel.Warning);
                    return;
                }

                // Check if upload config is available
                if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload.rclone.conf")))
                {
                    Logger.Log("Upload configuration not found. Cannot upload debug log.", LogLevel.Warning);
                    return;
                }

                Logger.Log("Uploading debug log...");

                // Generate unique filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var remoteFileName = $"debuglog_{timestamp}.txt";

                // Upload using rclone (assumes VRP_UPLOAD remote is configured)
                var result = await Sideloader.Rclone.runRcloneCommand_UploadConfig(
                    $"copy \"{debugLogPath}\" \"VRP_UPLOAD:debuglogs/{remoteFileName}\"");

                if (string.IsNullOrEmpty(result.Error) || !result.Error.Contains("Failed"))
                {
                    Logger.Log($"Debug log uploaded successfully as {remoteFileName}");
                }
                else
                {
                    Logger.Log($"Failed to upload debug log: {result.Error}", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error uploading debug log: {ex.Message}", LogLevel.Error);
            }
        }

        private async void SetDownloadDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Download Directory",
                    AllowMultiple = false
                };

                var result = await StorageProvider.OpenFolderPickerAsync(folderDialog);

                if (result is { Count: > 0 })
                {
                    Settings.CustomDownloadDir = true;
                    Settings.DownloadDir = result[0].Path.LocalPath;
                    Settings.Save();
                    Logger.Log($"Download directory set to: {Settings.DownloadDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error setting download directory: {ex.Message}", LogLevel.Error);
            }
        }

        private async void SetBackupDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderDialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Backup Directory",
                    AllowMultiple = false
                };

                var result = await StorageProvider.OpenFolderPickerAsync(folderDialog);

                if (result is { Count: > 0 })
                {
                    Settings.CustomBackupDir = true;
                    Settings.BackupDir = result[0].Path.LocalPath;
                    Settings.Save();
                    Logger.Log($"Backup directory set to: {Settings.BackupDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error setting backup directory: {ex.Message}", LogLevel.Error);
            }
        }

        private void OpenDownloadDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string pathToOpen = Settings.CustomDownloadDir ? Settings.DownloadDir : AppDomain.CurrentDomain.BaseDirectory;
                OpenDirectory(pathToOpen);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening download directory: {ex.Message}", LogLevel.Error);
            }
        }

        private void OpenBackupDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string pathToOpen = Settings.CustomBackupDir
                    ? Path.Combine(Settings.BackupDir, "Rookie Backups")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rookie Backups");

                // Create directory if it doesn't exist
                if (!Directory.Exists(pathToOpen))
                {
                    Directory.CreateDirectory(pathToOpen);
                }

                OpenDirectory(pathToOpen);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening backup directory: {ex.Message}", LogLevel.Error);
            }
        }

        private void OpenDirectory(string path)
        {
            try
            {
                if (PlatformHelper.IsWindows)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = path,
                        UseShellExecute = true
                    });
                }
                else if (PlatformHelper.IsMacOs)
                {
                    Process.Start("open", path);
                }
                else if (PlatformHelper.IsLinux)
                {
                    Process.Start("xdg-open", path);
                }

                Logger.Log($"Opened directory: {path}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening directory: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
