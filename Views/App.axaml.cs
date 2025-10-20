using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AndroidSideloader.Sideloader;
using AndroidSideloader.Utilities;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaWebView;

namespace AndroidSideloader.Views;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();

        // Initialize WebView (uses native WKWebView on macOS, WebView2 on Windows)
        AvaloniaWebViewBuilder.Initialize(null);
        Logger.Log("WebView initialized (native platform WebView)");
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        // Enable design-time tools (previewer, inspector)
        this.AttachDevTools();
#endif

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Splash splash = null;
            string crashId = null; // Declare outside Task.Run so it's accessible later

            // Initialize dependencies in background
            Task.Run(async () =>
            {
                try
                {
                    Logger.Log("Starting AndroidSideloader");

                    // Create and show splash on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        splash = new Splash();
                        splash.Show();
                    });

                    // Check for offline mode
                    var args = Environment.GetCommandLineArgs();
                    var isOffline = args.Any(a => a.Equals("--offline", StringComparison.OrdinalIgnoreCase));

                    if (isOffline)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            splash?.UpdateBackgroundImage("splashimage_offline.png");
                        });
                        Logger.Log("Starting in Offline Mode");
                    }
                    else
                    {
                        // Download dependencies
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            splash?.UpdateBackgroundImage("splashimage_deps.png");
                        });

                        // Download rclone
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            splash?.UpdateBackgroundImage("splashimage_rclone.png");
                        });
                        await GetDependencies.DownloadRclone();

                        // Download 7-Zip
                        await GetDependencies.Download7Zip();

                        // Download ADB platform-tools
                        await GetDependencies.DownloadAdb();

                        // Show completion splash
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            splash?.UpdateBackgroundImage("splashimage.png");
                        });
                    }

                    // Attempt to reconnect wireless ADB if previously enabled
                    if (SettingsManager.Instance.WirelessAdb && !string.IsNullOrEmpty(SettingsManager.Instance.IpAddress))
                    {
                        Logger.Log("Attempting wireless ADB reconnection...");
                        var reconnected = await Adb.ReconnectWirelessAdb();
                        if (reconnected)
                        {
                            Logger.Log("Wireless ADB reconnected successfully");
                        }
                    }

                    // Handle crash logs if present (returns crash ID if found)
                    crashId = await HandleCrashLogAsync();

                    // Update LastLaunch timestamp
                    SettingsManager.Instance.LastLaunch = DateTime.Now;
                    SettingsManager.Instance.Save();
                    Logger.Log($"Updated LastLaunch: {DateTime.Now}");

                    // Check for updates (only if enabled in settings)
                    if (SettingsManager.Instance.CheckForUpdates)
                    {
                        Logger.Log("Checking for updates...");
                        Updater.AppName = "Rookie";
                        await Updater.CheckForUpdatesAsync();
                    }
                    else
                    {
                        Logger.Log("Update checking disabled in settings");
                    }

                    // Small delay to show final splash image
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error during initialization: {ex.Message}", LogLevel.Error);
                }

                // Show main window on UI thread
                try
                {
                    MainWindow mainWindow = null;

                    // Create main window on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        mainWindow = new MainWindow();
                    });

                    // Set as desktop main window and show it
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();
                    });

                    // Close splash screen
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        splash?.Close();
                    });

                    // If a crash log was uploaded, show dialog to user
                    if (!string.IsNullOrEmpty(crashId))
                    {
                        await ShowCrashLogDialogAsync(mainWindow, crashId);
                    }

                    Logger.Log("Application started successfully");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error showing main window: {ex.Message}", LogLevel.Error);
                    if (ex.InnerException != null)
                    {
                        Logger.Log($"Inner exception: {ex.InnerException.Message}", LogLevel.Error);
                    }
                }
            });

            // Clean shutdown: Kill rclone processes and save settings
            desktop.Exit += OnApplicationExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Check for crash logs and upload them if found
    /// Returns crash ID if a crash log was uploaded, null otherwise
    /// </summary>
    private async Task<string> HandleCrashLogAsync()
    {
        try
        {
            var crashLogPath = Path.Combine(Environment.CurrentDirectory, "crashlog.txt");

            // Check if previous crash log exists and should be cleaned up
            if (!string.IsNullOrEmpty(SettingsManager.Instance.CurrentCrashLogPath) &&
                File.Exists(SettingsManager.Instance.CurrentCrashLogPath))
            {
                try
                {
                    File.Delete(SettingsManager.Instance.CurrentCrashLogPath);
                    Logger.Log($"Deleted old crash log: {SettingsManager.Instance.CurrentCrashLogPath}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to delete old crash log: {ex.Message}", LogLevel.Warning);
                }
            }

            // Check if new crash log exists
            if (!File.Exists(crashLogPath))
            {
                return null; // No crash log to process
            }

            Logger.Log("Crash log detected - processing...", LogLevel.Warning);

            // Generate UUID for this crash log
            var crashId = SideloaderUtilities.Uuid();

            // Rename crashlog.txt to UUID.log
            var renamedPath = Path.Combine(Environment.CurrentDirectory, $"{crashId}.log");

            try
            {
                File.Move(crashLogPath, renamedPath);
                Logger.Log($"Renamed crash log to: {renamedPath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to rename crash log: {ex.Message}", LogLevel.Error);
                return null;
            }

            // Save crash log info to settings
            SettingsManager.Instance.CurrentCrashLogPath = renamedPath;
            SettingsManager.Instance.Save();

            // Upload crash log to remote server via rclone
            Logger.Log("Uploading crash log to server...");
            var uploadResult = await Rclone.runRcloneCommand_UploadConfig($"copy \"{renamedPath}\" RSL-gameuploads:CrashLogs");

            if (string.IsNullOrEmpty(uploadResult.Error))
            {
                Logger.Log("Crash log uploaded successfully");

                // Copy crash ID to clipboard
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        // Get clipboard from the main window (TopLevel)
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktop)
                        {
                            var clipboard = desktop.MainWindow.Clipboard;
                            if (clipboard != null)
                            {
                                await clipboard.SetTextAsync(crashId);
                                Logger.Log($"Crash ID copied to clipboard: {crashId}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to copy crash ID to clipboard: {ex.Message}", LogLevel.Warning);
                    }

                    // TODO: Show message to user with crash ID
                    // This requires the dialog service to be set up
                    // For now, just log it prominently
                    Logger.Log("===========================================", LogLevel.Error);
                    Logger.Log($"CRASH LOG UPLOADED - CRASH ID: {crashId}", LogLevel.Error);
                    Logger.Log("Please provide this ID to support staff", LogLevel.Error);
                    Logger.Log("===========================================", LogLevel.Error);
                });

                return crashId; // Return the crash ID to show dialog later
            }

            Logger.Log($"Failed to upload crash log: {uploadResult.Error}", LogLevel.Error);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error handling crash log: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// Shows a dialog to the user informing them that a crash log was uploaded
    /// </summary>
    private static async Task ShowCrashLogDialogAsync(MainWindow mainWindow, string crashId)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Get the dialog service from the main window's view model
                if (mainWindow.DataContext is ViewModels.MainViewModel)
                {
                    var message = $"Sideloader crashed during your last use.\n\n" +
                                $"Your crash log has been uploaded to the server.\n\n" +
                                $"Crash Log ID: {crashId}\n\n" +
                                $"The ID has been copied to your clipboard.\n" +
                                $"Please mention this ID to the support team.\n\n" +
                                $"NOTE: Upload can take up to 30 seconds to complete.";

                    // Show the crash dialog using the dialog service
                    var dialogService = new Services.AvaloniaDialogService(mainWindow);
                    await dialogService.ShowInfoAsync(message, "Crash Log Uploaded");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to show crash log dialog: {ex.Message}", LogLevel.Warning);
        }
    }

    private static void OnApplicationExit(object sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Logger.Log("Application shutting down");

        // Kill any running rclone processes
        Rclone.KillRclone();
        Logger.Log("Killed rclone processes");

        // Save settings
        SettingsManager.Instance.Save();
        Logger.Log("Settings saved");
    }
}