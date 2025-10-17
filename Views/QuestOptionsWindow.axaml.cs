using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Threading.Tasks;
using AndroidSideloader.Utilities;

namespace AndroidSideloader.Views;

public partial class QuestOptionsWindow : Window
{
    public QuestOptionsWindow()
    {
        InitializeComponent();
        WireUpEvents();
    }

    private void WireUpEvents()
    {
        var applyUsernameButton = this.FindControl<Button>("ApplyUsernameButton");
        var transferScreenshotsButton = this.FindControl<Button>("TransferScreenshotsButton");
        var transferRecordingsButton = this.FindControl<Button>("TransferRecordingsButton");
        var applyResolutionButton = this.FindControl<Button>("ApplyResolutionButton");
        var refreshRateComboBox = this.FindControl<ComboBox>("RefreshRateComboBox");
        var gpuLevelComboBox = this.FindControl<ComboBox>("GpuLevelComboBox");
        var cpuLevelComboBox = this.FindControl<ComboBox>("CpuLevelComboBox");

        if (applyUsernameButton != null)
        {
            applyUsernameButton.Click += ApplyUsernameButton_Click;
        }

        if (transferScreenshotsButton != null)
        {
            transferScreenshotsButton.Click += TransferScreenshotsButton_Click;
        }

        if (transferRecordingsButton != null)
        {
            transferRecordingsButton.Click += TransferRecordingsButton_Click;
        }

        if (applyResolutionButton != null)
        {
            applyResolutionButton.Click += ApplyResolutionButton_Click;
        }

        // Wire up combo box selection changed to immediately apply settings
        if (refreshRateComboBox != null)
        {
            refreshRateComboBox.SelectionChanged += RefreshRateComboBox_SelectionChanged;
        }

        if (gpuLevelComboBox != null)
        {
            gpuLevelComboBox.SelectionChanged += GpuLevelComboBox_SelectionChanged;
        }

        if (cpuLevelComboBox != null)
        {
            cpuLevelComboBox.SelectionChanged += CpuLevelComboBox_SelectionChanged;
        }
    }

    private async void ApplyUsernameButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var usernameTextBox = this.FindControl<TextBox>("UsernameTextBox");
            if (usernameTextBox == null || string.IsNullOrWhiteSpace(usernameTextBox.Text))
            {
                await ShowMessageAsync("Please enter a username.", "No Username");
                return;
            }

            if (string.IsNullOrEmpty(Adb.DeviceId))
            {
                await ShowMessageAsync("No device connected. Please connect your device first.", "No Device");
                return;
            }

            var username = usernameTextBox.Text.Trim();
            Logger.Log($"Setting username to: {username}");

            // Set username in Quest settings
            Adb.RunAdbCommandToString($"shell settings put secure user_account_name \"{username}\"");

            await ShowMessageAsync($"Username set to: {username}", "Success");
            Logger.Log("Username set successfully");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting username: {ex.Message}", LogLevel.Error);
            await ShowMessageAsync($"Error: {ex.Message}", "Error");
        }
    }

    private async void TransferScreenshotsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(Adb.DeviceId))
            {
                await ShowMessageAsync("No device connected. Please connect your device first.", "No Device");
                return;
            }

            Logger.Log("Transferring screenshots from Quest");

            // Create destination folder
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var screenshotsPath = Path.Combine(desktopPath, "Quest Screenshots");
            await Task.Run(() => Directory.CreateDirectory(screenshotsPath));

            // Pull screenshots from device
            const string sourcePath = "/sdcard/Oculus/Screenshots/";
            await Task.Run(() => Adb.RunAdbCommandToString($"pull \"{sourcePath}\" \"{screenshotsPath}\""));

            // Check if we should delete after transfer
            var deleteCheckBox = this.FindControl<CheckBox>("DeleteAfterTransferCheckBox");
            if (deleteCheckBox?.IsChecked == true)
            {
                Logger.Log("Deleting screenshots from device");
                await Task.Run(() => Adb.RunAdbCommandToString($"shell rm -rf \"{sourcePath}*\""));
            }

            await ShowMessageAsync($"Screenshots transferred to:\n{screenshotsPath}", "Success");
            Logger.Log("Screenshots transferred successfully");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error transferring screenshots: {ex.Message}", LogLevel.Error);
            await ShowMessageAsync($"Error: {ex.Message}", "Error");
        }
    }

    private async void TransferRecordingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(Adb.DeviceId))
            {
                await ShowMessageAsync("No device connected. Please connect your device first.", "No Device");
                return;
            }

            Logger.Log("Transferring recordings from Quest");

            // Create destination folder
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var recordingsPath = Path.Combine(desktopPath, "Quest Recordings");
            await Task.Run(() => Directory.CreateDirectory(recordingsPath));

            // Pull recordings from device
            const string sourcePath = "/sdcard/Oculus/VideoShots/";
            await Task.Run(() => Adb.RunAdbCommandToString($"pull \"{sourcePath}\" \"{recordingsPath}\""));

            // Check if we should delete after transfer
            var deleteCheckBox = this.FindControl<CheckBox>("DeleteAfterTransferCheckBox");
            if (deleteCheckBox?.IsChecked == true)
            {
                Logger.Log("Deleting recordings from device");
                await Task.Run(() => Adb.RunAdbCommandToString($"shell rm -rf \"{sourcePath}*\""));
            }

            await ShowMessageAsync($"Recordings transferred to:\n{recordingsPath}", "Success");
            Logger.Log("Recordings transferred successfully");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error transferring recordings: {ex.Message}", LogLevel.Error);
            await ShowMessageAsync($"Error: {ex.Message}", "Error");
        }
    }

    private static void RefreshRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: > 0 } comboBox || string.IsNullOrEmpty(Adb.DeviceId))
        {
            return;
        }

        try
        {
            // Map: 0=placeholder, 1=72Hz, 2=80Hz, 3=90Hz, 4=120Hz
            int[] refreshRates = [72, 80, 90, 120];
            var rate = refreshRates[comboBox.SelectedIndex - 1];

            Logger.Log($"Setting refresh rate to {rate}Hz");
            Adb.RunAdbCommandToString($"shell setprop debug.oculus.refreshRate {rate}");

            // Set additional properties for 90Hz and 120Hz
            if (rate >= 90)
            {
                Adb.RunAdbCommandToString("shell setprop debug.oculus.forceChroma 1");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting refresh rate: {ex.Message}", LogLevel.Error);
        }
    }

    private static void GpuLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: > 0 } comboBox || string.IsNullOrEmpty(Adb.DeviceId))
        {
            return;
        }

        try
        {
            var gpuLevel = comboBox.SelectedIndex - 1; // 0=placeholder, 1-5 map to 0-4
            Logger.Log($"Setting GPU level to {gpuLevel}");
            Adb.RunAdbCommandToString($"shell setprop debug.oculus.gpuLevel {gpuLevel}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting GPU level: {ex.Message}", LogLevel.Error);
        }
    }

    private static void CpuLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: > 0 } comboBox || string.IsNullOrEmpty(Adb.DeviceId))
        {
            return;
        }

        try
        {
            var cpuLevel = comboBox.SelectedIndex - 1; // 0=placeholder, 1-5 map to 0-4
            Logger.Log($"Setting CPU level to {cpuLevel}");
            Adb.RunAdbCommandToString($"shell setprop debug.oculus.cpuLevel {cpuLevel}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting CPU level: {ex.Message}", LogLevel.Error);
        }
    }

    private async void ApplyResolutionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var resolutionTextBox = this.FindControl<TextBox>("ResolutionTextBox");
            if (resolutionTextBox == null || string.IsNullOrWhiteSpace(resolutionTextBox.Text))
            {
                await ShowMessageAsync("Please enter a resolution value (0 for default).", "No Resolution");
                return;
            }

            if (string.IsNullOrEmpty(Adb.DeviceId))
            {
                await ShowMessageAsync("No device connected. Please connect your device first.", "No Device");
                return;
            }

            var resolution = resolutionTextBox.Text.Trim();
            if (!int.TryParse(resolution, out var res))
            {
                await ShowMessageAsync("Invalid resolution. Please enter a number (0 for default).", "Invalid Input");
                return;
            }

            Logger.Log($"Setting resolution to {resolution}");

            if (res == 0)
            {
                // Reset to default
                Adb.RunAdbCommandToString("shell setprop debug.oculus.textureWidth 0");
                Adb.RunAdbCommandToString("shell setprop debug.oculus.textureHeight 0");
                await ShowMessageAsync("Resolution reset to default.", "Success");
            }
            else
            {
                Adb.RunAdbCommandToString($"shell setprop debug.oculus.textureWidth {resolution}");
                Adb.RunAdbCommandToString($"shell setprop debug.oculus.textureHeight {resolution}");
                await ShowMessageAsync($"Resolution set to {resolution}.\n\nNote: Reboot Quest to apply.", "Success");
            }

            Logger.Log("Resolution setting applied");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting resolution: {ex.Message}", LogLevel.Error);
            await ShowMessageAsync($"Error: {ex.Message}", "Error");
        }
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        var messageBox = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2d2d2d"))
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 20),
            Foreground = Avalonia.Media.Brushes.White
        });

        var okButton = new Button
        {
            Content = "OK",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        okButton.Click += (_, _) => messageBox.Close();
        panel.Children.Add(okButton);

        messageBox.Content = panel;
        await messageBox.ShowDialog(this);
    }
}