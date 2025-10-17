using System;
using System.Linq;
using AndroidSideloader.Sideloader;
using AndroidSideloader.Utilities;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AndroidSideloader.Views;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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

            // Initialize dependencies in background
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    Logger.Log("Starting AndroidSideloader");

                    // Create and show splash on UI thread
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        splash = new Splash();
                        splash.Show();
                    });

                    // Check for offline mode
                    var args = Environment.GetCommandLineArgs();
                    var isOffline = args.Any(a => a.Equals("--offline", StringComparison.OrdinalIgnoreCase));

                    if (isOffline)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            splash?.UpdateBackgroundImage("splashimage_offline.png");
                        });
                        Logger.Log("Starting in Offline Mode");
                    }
                    else
                    {
                        // Download dependencies
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            splash?.UpdateBackgroundImage("splashimage_deps.png");
                        });

                        // Download rclone
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            splash?.UpdateBackgroundImage("splashimage_rclone.png");
                        });
                        await GetDependencies.DownloadRclone();

                        // Download 7-Zip
                        await GetDependencies.Download7Zip();

                        // Download ADB platform-tools
                        await GetDependencies.DownloadAdb();

                        // Show completion splash
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
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

                    // Handle crash logs if present
                    await HandleCrashLogAsync();

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
                    await System.Threading.Tasks.Task.Delay(500);
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
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        mainWindow = new MainWindow();
                    });

                    // Set as desktop main window and show it
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();
                    });

                    // Close splash screen
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        splash?.Close();
                    });

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
    /// </summary>
    private async System.Threading.Tasks.Task HandleCrashLogAsync()
    {
        try
        {
            var crashLogPath = System.IO.Path.Combine(Environment.CurrentDirectory, "crashlog.txt");

            // Check if previous crash log exists and should be cleaned up
            if (!string.IsNullOrEmpty(SettingsManager.Instance.CurrentCrashLogPath) &&
                System.IO.File.Exists(SettingsManager.Instance.CurrentCrashLogPath))
            {
                try
                {
                    System.IO.File.Delete(SettingsManager.Instance.CurrentCrashLogPath);
                    Logger.Log($"Deleted old crash log: {SettingsManager.Instance.CurrentCrashLogPath}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to delete old crash log: {ex.Message}", LogLevel.Warning);
                }
            }

            // Check if new crash log exists
            if (!System.IO.File.Exists(crashLogPath))
            {
                return; // No crash log to process
            }

            Logger.Log("Crash log detected - processing...", LogLevel.Warning);

            // Generate UUID for this crash log
            var crashId = SideloaderUtilities.Uuid();

            // Rename crashlog.txt to UUID.log
            var renamedPath = System.IO.Path.Combine(Environment.CurrentDirectory, $"{crashId}.log");

            try
            {
                System.IO.File.Move(crashLogPath, renamedPath);
                Logger.Log($"Renamed crash log to: {renamedPath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to rename crash log: {ex.Message}", LogLevel.Error);
                return;
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
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
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
            }
            else
            {
                Logger.Log($"Failed to upload crash log: {uploadResult.Error}", LogLevel.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error handling crash log: {ex.Message}", LogLevel.Error);
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