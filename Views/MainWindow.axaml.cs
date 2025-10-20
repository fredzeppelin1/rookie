using System.Linq;
using System.Threading.Tasks;
using AndroidSideloader.Services;
using AndroidSideloader.Utilities;
using AndroidSideloader.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace AndroidSideloader.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set window title with version from file
        Title = $"Rookie Sideloader v{Updater.LocalVersion}";

        var viewModel = new MainViewModel(showUpdateAvailableOnly: false);
        DataContext = viewModel;

        // Initialize dialog service after window is created
        var dialogService = new AvaloniaDialogService(this);
        viewModel.SetDialogService(dialogService);

        // Initialize WebView for trailer playback when window opens
        Opened += (_, _) =>
        {
            Logger.Log("MainWindow opened - initializing WebView for trailers");

            var trailerWebView = this.FindControl<AvaloniaWebView.WebView>("TrailerWebView");
            if (trailerWebView != null && viewModel.TrailerPlayerService != null)
            {
                viewModel.TrailerPlayerService.Initialize(trailerWebView);
                Logger.Log("WebView trailer player initialized");
            }
            else
            {
                Logger.Log("Warning: TrailerWebView or TrailerPlayerService not found", LogLevel.Warning);
            }
        };

        // Subscribe to the Closing event
        Closing += (_, _) =>
        {
            // Directly call Save() method
            SettingsManager.Instance.Save();
            Logger.Log("Saving settings on close.");
        };

        // Subscribe to DragDrop events
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                var files = e.Data.GetFiles();
                if (files != null)
                {
                    var filePaths = files
                        .Select(file => file.Path.LocalPath)
                        .ToList();
                    await vm.HandleDroppedFilesAsync(filePaths);
                }
            }
        });

        AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            // Only allow file drops
            e.DragEffects = e.Data.Contains(DataFormats.Files)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        });

        // Global keyboard shortcut handling
        KeyDown += MainWindow_KeyDown;
    }

    private async void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Key)
        {
            // Handle Ctrl+R to open ADB command box
            case Key.R when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                e.Handled = true;
                if (!vm.IsAdbCommandBoxVisible)
                {
                    // Show ADB command overlay
                    vm.AdbCommandBoxLabel = "Enter ADB Command";
                    vm.AdbCommandText = "";
                    vm.IsAdbCommandBoxVisible = true;
                }
                return;
                
            // Handle Esc to close ADB command box
            case Key.Escape when vm.IsAdbCommandBoxVisible:
                e.Handled = true;
                vm.IsAdbCommandBoxVisible = false;
                vm.AdbCommandText = "";
                return;
                
            // Handle Enter key in ADB command box
            case Key.Enter when vm.IsAdbCommandBoxVisible:
                e.Handled = true;
                var input = vm.AdbCommandText?.Trim();

                if (!string.IsNullOrEmpty(input))
                {
                    // Hide overlay first
                    vm.IsAdbCommandBoxVisible = false;

                    // Handle based on current mode
                    if (vm.AdbCommandBoxCurrentMode == MainViewModel.AdbCommandBoxMode.WirelessIpEntry)
                    {
                        await ConnectToWirelessDevice(vm, input);
                    }
                    else
                    {
                        // Execute the ADB command
                        await ExecuteAdbCommand(input);
                    }

                    // Clear the text and reset mode
                    vm.AdbCommandText = "";
                }
                else
                {
                    // Just close if empty
                    vm.IsAdbCommandBoxVisible = false;
                }

                vm.AdbCommandBoxCurrentMode = MainViewModel.AdbCommandBoxMode.AdbCommand;
                return;
        }
    }

    private static async Task ExecuteAdbCommand(string command)
    {
        try
        {
            Logger.Log($"Executing ADB command: {command}");

            // Run the ADB command
            var result = Adb.RunAdbCommandToString(command);

            // Show result to user
            if (!string.IsNullOrEmpty(result.Output))
            {
                Logger.Log($"ADB command output:\n{result.Output}");
                // TODO: Show result in a dialog or output panel
                await Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                Logger.Log($"ADB command error: {result.Error}", LogLevel.Error);
            }
        }
        catch (System.Exception ex)
        {
            Logger.Log($"Failed to execute ADB command: {ex.Message}", LogLevel.Error);
        }
    }

    private static Task ConnectToWirelessDevice(MainViewModel vm, string ipAddress)
    {
        try
        {
            Logger.Log($"Attempting to connect to wireless device: {ipAddress}");
            vm.ProgressStatusText = $"Connecting to {ipAddress}...";

            // Ensure IP has port (default to 5555)
            var connectionAddress = ipAddress.Contains(':') ? ipAddress : $"{ipAddress}:5555";

            // Try to connect
            var result = Adb.RunAdbCommandToString($"connect {connectionAddress}");

            if (!string.IsNullOrEmpty(result.Output))
            {
                Logger.Log($"ADB connect output: {result.Output}");

                // Check if connection was successful
                if (result.Output.Contains("connected") && !result.Output.Contains("unable to connect"))
                {
                    vm.ProgressStatusText = $"Connected to {connectionAddress} - Click 'Reconnect Device' to refresh";
                    Logger.Log($"Successfully connected to wireless device: {connectionAddress}");
                }
                else
                {
                    vm.ProgressStatusText = "Failed to connect to device";
                    Logger.Log($"Failed to connect to {connectionAddress}", LogLevel.Warning);
                }
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                Logger.Log($"ADB connect error: {result.Error}", LogLevel.Error);
                vm.ProgressStatusText = "Connection failed";
            }
        }
        catch (System.Exception ex)
        {
            Logger.Log($"Exception connecting to wireless device: {ex.Message}", LogLevel.Error);
            vm.ProgressStatusText = "Connection failed";
        }

        return Task.CompletedTask;
    }
}
