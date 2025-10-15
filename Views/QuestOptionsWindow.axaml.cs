using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AndroidSideloader.Utilities;

namespace AndroidSideloader.Views
{
    public partial class QuestOptionsWindow : Window
    {
        public QuestOptionsWindow()
        {
            InitializeComponent();
            LoadDeviceInfo();
            WireUpEvents();
        }

        private void LoadDeviceInfo()
        {
            // Find controls
            var deviceIdTextBox = this.FindControl<TextBox>("DeviceIdTextBox");
            var wirelessStatusTextBox = this.FindControl<TextBox>("WirelessStatusTextBox");
            var storageTextBox = this.FindControl<TextBox>("StorageTextBox");
            var batteryTextBox = this.FindControl<TextBox>("BatteryTextBox");
            var deviceModelTextBox = this.FindControl<TextBox>("DeviceModelTextBox");
            var androidVersionTextBox = this.FindControl<TextBox>("AndroidVersionTextBox");

            // Load device info
            if (deviceIdTextBox != null)
            {
                deviceIdTextBox.Text = string.IsNullOrEmpty(Adb.DeviceId) ? "Not connected" : Adb.DeviceId;
            }

            if (wirelessStatusTextBox != null)
            {
                wirelessStatusTextBox.Text = Adb.WirelessadbOn ? "Enabled" : "Disabled";
            }

            if (storageTextBox != null)
            {
                try
                {
                    storageTextBox.Text = !string.IsNullOrEmpty(Adb.DeviceId) 
                        ? Adb.GetAvailableSpace() 
                        : "Device not connected";
                }
                catch (Exception ex)
                {
                    storageTextBox.Text = $"Error: {ex.Message}";
                }
            }

            if (batteryTextBox != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(Adb.DeviceId))
                    {
                        var batteryResult = Adb.RunAdbCommandToString("shell dumpsys battery | grep level");
                        if (!string.IsNullOrEmpty(batteryResult.Output))
                        {
                            // Parse battery level
                            var lines = batteryResult.Output.Split('\n');
                            foreach (var line in lines)
                            {
                                if (line.Contains("level:"))
                                {
                                    var parts = line.Split(':');
                                    if (parts.Length > 1)
                                    {
                                        batteryTextBox.Text = $"{parts[1].Trim()}%";
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            batteryTextBox.Text = "Unknown";
                        }
                    }
                    else
                    {
                        batteryTextBox.Text = "Device not connected";
                    }
                }
                catch (Exception ex)
                {
                    batteryTextBox.Text = $"Error: {ex.Message}";
                }
            }

            if (deviceModelTextBox != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(Adb.DeviceId))
                    {
                        var modelResult = Adb.RunAdbCommandToString("shell getprop ro.product.model");
                        deviceModelTextBox.Text = !string.IsNullOrEmpty(modelResult.Output)
                            ? modelResult.Output.Trim()
                            : "Unknown";
                    }
                    else
                    {
                        deviceModelTextBox.Text = "Device not connected";
                    }
                }
                catch (Exception ex)
                {
                    deviceModelTextBox.Text = $"Error: {ex.Message}";
                }
            }

            if (androidVersionTextBox != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(Adb.DeviceId))
                    {
                        var versionResult = Adb.RunAdbCommandToString("shell getprop ro.build.version.release");
                        androidVersionTextBox.Text = !string.IsNullOrEmpty(versionResult.Output)
                            ? $"Android {versionResult.Output.Trim()}"
                            : "Unknown";
                    }
                    else
                    {
                        androidVersionTextBox.Text = "Device not connected";
                    }
                }
                catch (Exception ex)
                {
                    androidVersionTextBox.Text = $"Error: {ex.Message}";
                }
            }
        }

        private void WireUpEvents()
        {
            var closeButton = this.FindControl<Button>("CloseButton");
            var refreshButton = this.FindControl<Button>("RefreshButton");
            var enableWirelessButton = this.FindControl<Button>("EnableWirelessButton");
            var disableWirelessButton = this.FindControl<Button>("DisableWirelessButton");
            var toggleUpdatesButton = this.FindControl<Button>("ToggleUpdatesButton");
            var listAppsButton = this.FindControl<Button>("ListAppsButton");
            var rebootButton = this.FindControl<Button>("RebootButton");
            var rebootRecoveryButton = this.FindControl<Button>("RebootRecoveryButton");
            var applyPerformanceButton = this.FindControl<Button>("ApplyPerformanceButton");

            if (closeButton != null)
            {
                closeButton.Click += CloseButton_Click;
            }

            if (refreshButton != null)
            {
                refreshButton.Click += RefreshButton_Click;
            }

            if (enableWirelessButton != null)
            {
                enableWirelessButton.Click += EnableWirelessButton_Click;
            }

            if (disableWirelessButton != null)
            {
                disableWirelessButton.Click += DisableWirelessButton_Click;
            }

            if (toggleUpdatesButton != null)
            {
                toggleUpdatesButton.Click += ToggleUpdatesButton_Click;
            }

            if (listAppsButton != null)
            {
                listAppsButton.Click += ListAppsButton_Click;
            }

            if (rebootButton != null)
            {
                rebootButton.Click += RebootButton_Click;
            }

            if (rebootRecoveryButton != null)
            {
                rebootRecoveryButton.Click += RebootRecoveryButton_Click;
            }

            if (applyPerformanceButton != null)
            {
                applyPerformanceButton.Click += ApplyPerformanceButton_Click;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Log("Refreshing device info in Quest Options");
                LoadDeviceInfo();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error refreshing device info: {ex.Message}", LogLevel.Error);
            }
        }

        private async void EnableWirelessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    await ShowMessageAsync("No device connected. Please connect via USB first.", "No Device");
                    return;
                }

                Logger.Log("Enabling wireless ADB from Quest Options");

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
                    await ShowMessageAsync("Could not detect device IP address. Make sure WiFi is enabled on the device.", "IP Detection Failed");
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
                    await ShowMessageAsync($"Wireless ADB enabled!\n\nIP Address: {deviceIp}:5555\n\nYou can now disconnect the USB cable.", "Success");
                    LoadDeviceInfo();
                }
                else
                {
                    await ShowMessageAsync($"Failed to connect wirelessly.\n\n{connectResult.Error}", "Connection Failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error enabling wireless ADB: {ex.Message}", LogLevel.Error);
                await ShowMessageAsync($"Error: {ex.Message}", "Error");
            }
        }

        private async void DisableWirelessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    await ShowMessageAsync("No device connected.", "No Device");
                    return;
                }

                Logger.Log("Disabling wireless ADB from Quest Options");

                var disconnectResult = Adb.RunAdbCommandToString("disconnect");
                Adb.WirelessadbOn = false;

                await ShowMessageAsync("Wireless ADB has been disabled. Device will only connect via USB.", "Success");
                LoadDeviceInfo();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error disabling wireless ADB: {ex.Message}", LogLevel.Error);
                await ShowMessageAsync($"Error: {ex.Message}", "Error");
            }
        }

        private async void ToggleUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    await ShowMessageAsync("No device connected.", "No Device");
                    return;
                }

                Logger.Log("Checking Quest system updates status...");

                // Check current status
                var result = Adb.RunAdbCommandToString("shell pm list packages -d");
                var isUpdatesDisabled = result.Output.Contains("com.oculus.updater");

                string command;
                string statusText;
                string confirmMessage;

                if (isUpdatesDisabled)
                {
                    // Updates are disabled, enable them
                    command = "shell pm enable com.oculus.updater";
                    statusText = "enabled";
                    confirmMessage = "Quest system updates are currently DISABLED.\n\nDo you want to ENABLE automatic updates?";
                }
                else
                {
                    // Updates are enabled, disable them
                    command = "shell pm disable-user com.oculus.updater";
                    statusText = "disabled";
                    confirmMessage = "Quest system updates are currently ENABLED.\n\nDo you want to DISABLE automatic updates?\n\nNote: This will prevent automatic system updates but you can still manually update.";
                }

                // Ask for confirmation
                var confirmed = await ShowConfirmationAsync(confirmMessage, "Toggle System Updates");
                if (!confirmed)
                {
                    return;
                }

                Logger.Log($"Toggling Quest system updates: {command}");
                var toggleResult = Adb.RunAdbCommandToString(command);

                if (string.IsNullOrEmpty(toggleResult.Error) || !toggleResult.Error.Contains("Error"))
                {
                    await ShowMessageAsync(
                        $"Quest system updates have been {statusText}.\n\n" +
                        $"Status: {statusText.ToUpper()}\n\n" +
                        (statusText == "disabled"
                            ? "Your Quest will no longer automatically update. You can manually update in Settings > System > Software Update."
                            : "Your Quest will automatically check for and install system updates."),
                        "Success");

                    Logger.Log($"Quest system updates {statusText}");
                }
                else
                {
                    await ShowMessageAsync($"Failed to toggle updates.\n\n{toggleResult.Error}", "Failed");
                    Logger.Log($"Failed to toggle updates: {toggleResult.Error}", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error toggling updates: {ex.Message}", LogLevel.Error);
                await ShowMessageAsync($"Error: {ex.Message}", "Error");
            }
        }

        private async void ListAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    await ShowMessageAsync("No device connected.", "No Device");
                    return;
                }

                Logger.Log("Listing installed packages from Quest Options");

                // Get list of all installed packages
                var result = Adb.RunAdbCommandToString("shell pm list packages -3");

                // Parse package names
                var packages = result.Output.Split('\n')
                    .Where(line => line.StartsWith("package:"))
                    .Select(line => line.Replace("package:", "").Trim())
                    .ToList();

                var packageList = string.Join("\n", packages.Count > 50 ? packages.GetRange(0, 50) : packages);
                if (packages.Count > 50)
                {
                    packageList += $"\n\n... and {packages.Count - 50} more";
                }

                await ShowMessageAsync($"Installed packages ({packages.Count} total):\n\n{packageList}", "Installed Apps");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error listing packages: {ex.Message}", LogLevel.Error);
                await ShowMessageAsync($"Error: {ex.Message}", "Error");
            }
        }

        private async void RebootButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    await ShowMessageAsync("No device connected.", "No Device");
                    return;
                }

                // Confirm reboot
                var confirmed = await ShowConfirmationAsync("Are you sure you want to reboot the device?", "Confirm Reboot");
                if (!confirmed)
                {
                    return;
                }

                Logger.Log("Rebooting device from Quest Options");
                Adb.RunAdbCommandToString("reboot");

                await ShowMessageAsync("Reboot command sent. Device will restart shortly.", "Success");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error rebooting device: {ex.Message}", LogLevel.Error);
                await ShowMessageAsync($"Error: {ex.Message}", "Error");
            }
        }

        private async void RebootRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    await ShowMessageAsync("No device connected.", "No Device");
                    return;
                }

                // Confirm reboot
                var confirmed = await ShowConfirmationAsync("Are you sure you want to reboot to recovery mode?", "Confirm Reboot to Recovery");
                if (!confirmed)
                {
                    return;
                }

                Logger.Log("Rebooting device to recovery from Quest Options");
                Adb.RunAdbCommandToString("reboot recovery");

                await ShowMessageAsync("Reboot to recovery command sent. Device will restart to recovery mode.", "Success");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error rebooting to recovery: {ex.Message}", LogLevel.Error);
                await ShowMessageAsync($"Error: {ex.Message}", "Error");
            }
        }

        private async void ApplyPerformanceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Adb.DeviceId))
                {
                    await ShowMessageAsync("No device connected.", "No Device");
                    return;
                }

                // Get control references
                var refreshRateComboBox = this.FindControl<ComboBox>("RefreshRateComboBox");
                var cpuLevelComboBox = this.FindControl<ComboBox>("CpuLevelComboBox");
                var gpuLevelComboBox = this.FindControl<ComboBox>("GpuLevelComboBox");
                var textureResolutionTextBox = this.FindControl<TextBox>("TextureResolutionTextBox");

                var changesMade = false;
                var appliedSettings = new List<string>();

                // Apply Refresh Rate
                if (refreshRateComboBox is { SelectedIndex: >= 0 })
                {
                    // Map combo box index to refresh rate value
                    int[] refreshRates = [72, 80, 90, 120];
                    var selectedRate = refreshRates[refreshRateComboBox.SelectedIndex];

                    Logger.Log($"Setting refresh rate to {selectedRate}Hz");
                    Adb.RunAdbCommandToString($"shell setprop debug.oculus.refreshRate {selectedRate}");

                    // Also set global settings for broader compatibility
                    if (selectedRate is 90 or 120)
                    {
                        Adb.RunAdbCommandToString($"shell settings put global 90hz_global 1");
                        Adb.RunAdbCommandToString($"shell settings put global 90hzglobal 1");
                    }
                    else
                    {
                        Adb.RunAdbCommandToString($"shell settings put global 90hz_global 0");
                        Adb.RunAdbCommandToString($"shell settings put global 90hzglobal 0");
                    }

                    appliedSettings.Add($"Refresh Rate: {selectedRate}Hz");
                    changesMade = true;
                }

                // Apply CPU Level
                if (cpuLevelComboBox is { SelectedIndex: >= 0 })
                {
                    var cpuLevel = cpuLevelComboBox.SelectedIndex;
                    Logger.Log($"Setting CPU level to {cpuLevel}");
                    Adb.RunAdbCommandToString($"shell setprop debug.oculus.cpuLevel {cpuLevel}");
                    appliedSettings.Add($"CPU Level: {cpuLevel}");
                    changesMade = true;
                }

                // Apply GPU Level
                if (gpuLevelComboBox is { SelectedIndex: >= 0 })
                {
                    var gpuLevel = gpuLevelComboBox.SelectedIndex;
                    Logger.Log($"Setting GPU level to {gpuLevel}");
                    Adb.RunAdbCommandToString($"shell setprop debug.oculus.gpuLevel {gpuLevel}");
                    appliedSettings.Add($"GPU Level: {gpuLevel}");
                    changesMade = true;
                }

                // Apply Texture Resolution
                if (textureResolutionTextBox != null && !string.IsNullOrWhiteSpace(textureResolutionTextBox.Text))
                {
                    var textureRes = textureResolutionTextBox.Text.Trim();
                    if (int.TryParse(textureRes, out var resolution))
                    {
                        Logger.Log($"Setting texture resolution to {resolution}");
                        Adb.RunAdbCommandToString($"shell settings put global texture_size_Global {resolution}");
                        Adb.RunAdbCommandToString($"shell setprop debug.oculus.textureWidth {resolution}");
                        Adb.RunAdbCommandToString($"shell setprop debug.oculus.textureHeight {resolution}");
                        appliedSettings.Add($"Texture Resolution: {resolution}");
                        changesMade = true;
                    }
                    else
                    {
                        await ShowMessageAsync("Invalid texture resolution. Please enter a valid number (e.g., 2048).", "Invalid Input");
                        return;
                    }
                }

                if (changesMade)
                {
                    var settingsList = string.Join("\n", appliedSettings);
                    await ShowMessageAsync(
                        $"Performance settings applied successfully!\n\n{settingsList}\n\n" +
                        "Note: Some changes may require restarting the app or device to take full effect.",
                        "Settings Applied");

                    Logger.Log("Performance settings applied successfully");
                }
                else
                {
                    await ShowMessageAsync("No settings were changed. Please select at least one option.", "No Changes");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error applying performance settings: {ex.Message}", LogLevel.Error);
                await ShowMessageAsync($"Error applying settings:\n\n{ex.Message}", "Error");
            }
        }

        private async Task ShowMessageAsync(string message, string title)
        {
            var messageBox = new Window
            {
                Title = title,
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 20)
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

        private async Task<bool> ShowConfirmationAsync(string message, string title)
        {
            var messageBox = new Window
            {
                Title = title,
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var result = false;

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 20)
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10
            };

            var yesButton = new Button { Content = "Yes", Width = 80 };
            yesButton.Click += (_, _) => { result = true; messageBox.Close(); };
            buttonPanel.Children.Add(yesButton);

            var noButton = new Button { Content = "No", Width = 80 };
            noButton.Click += (_, _) => { result = false; messageBox.Close(); };
            buttonPanel.Children.Add(noButton);

            panel.Children.Add(buttonPanel);

            messageBox.Content = panel;
            await messageBox.ShowDialog(this);

            return result;
        }
    }
}
