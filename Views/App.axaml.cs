using System.Linq;
using AndroidSideloader.Sideloader;
using AndroidSideloader.Utilities;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AndroidSideloader.Views
{
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
                        var args = System.Environment.GetCommandLineArgs();
                        bool isOffline = args.Any(a => a.Equals("--offline", System.StringComparison.OrdinalIgnoreCase));

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

                        // Small delay to show final splash image
                        await System.Threading.Tasks.Task.Delay(500);
                    }
                    catch (System.Exception ex)
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
                    catch (System.Exception ex)
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

        private void OnApplicationExit(object sender, ControlledApplicationLifetimeExitEventArgs e)
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
}