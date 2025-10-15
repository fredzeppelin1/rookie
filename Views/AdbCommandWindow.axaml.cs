using System;
using System.Threading.Tasks;
using AndroidSideloader.Utilities;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AndroidSideloader.Views
{
    public partial class AdbCommandWindow : Window
    {
        private readonly TextBox _commandTextBox;
        private readonly TextBox _outputTextBox;
        private readonly Button _sendButton;
        private readonly Button _toggleUpdatesButton;
        private readonly Button _clearButton;
        private readonly Button _closeButton;

        public AdbCommandWindow()
        {
            InitializeComponent();

            // Get controls
            _commandTextBox = this.FindControl<TextBox>("CommandTextBox");
            _outputTextBox = this.FindControl<TextBox>("OutputTextBox");
            _sendButton = this.FindControl<Button>("SendButton");
            _toggleUpdatesButton = this.FindControl<Button>("ToggleUpdatesButton");
            _clearButton = this.FindControl<Button>("ClearButton");
            _closeButton = this.FindControl<Button>("CloseButton");

            // Wire up events
            if (_sendButton != null)
                _sendButton.Click += SendButton_Click;

            if (_toggleUpdatesButton != null)
                _toggleUpdatesButton.Click += ToggleUpdatesButton_Click;

            if (_clearButton != null)
                _clearButton.Click += ClearButton_Click;

            if (_closeButton != null)
                _closeButton.Click += (_, _) => Close();

            // Allow Enter key to send command
            if (_commandTextBox != null)
            {
                _commandTextBox.KeyDown += CommandTextBox_KeyDown;
            }

            // Set focus to command textbox
            _commandTextBox?.Focus();
        }

        private void CommandTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = SendCommandAsync();
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendCommandAsync();
        }

        private async Task SendCommandAsync()
        {
            if (_commandTextBox == null || _outputTextBox == null)
                return;

            var command = _commandTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                AppendOutput("Error: Please enter a command.\n");
                return;
            }

            try
            {
                _sendButton.IsEnabled = false;
                AppendOutput($"$ {command}\n");
                Logger.Log($"Running ADB command: {command}");

                var result = await Task.Run(() => Adb.RunAdbCommandToString(command));

                if (!string.IsNullOrEmpty(result.Output))
                {
                    AppendOutput($"{result.Output}\n");
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    AppendOutput($"Error: {result.Error}\n");
                    Logger.Log($"ADB command error: {result.Error}", LogLevel.Warning);
                }

                AppendOutput("\n");
                Logger.Log($"ADB command completed");
            }
            catch (Exception ex)
            {
                AppendOutput($"Exception: {ex.Message}\n\n");
                Logger.Log($"ADB command exception: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _sendButton.IsEnabled = true;
            }
        }

        private async void ToggleUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _toggleUpdatesButton.IsEnabled = false;
                AppendOutput("Checking Quest system updates status...\n");
                Logger.Log("Checking Quest system updates status...");

                var result = await Task.Run(() => Adb.RunAdbCommandToString("shell pm list packages -d"));
                var isUpdatesDisabled = result.Output.Contains("com.oculus.updater");

                string command;
                string statusText;

                if (isUpdatesDisabled)
                {
                    // Updates are disabled, enable them
                    command = "shell pm enable com.oculus.updater";
                    statusText = "Enabling Quest system updates...";
                }
                else
                {
                    // Updates are enabled, disable them
                    command = "shell pm disable-user com.oculus.updater";
                    statusText = "Disabling Quest system updates...";
                }

                AppendOutput($"{statusText}\n");
                AppendOutput($"$ {command}\n");
                Logger.Log($"Running: {command}");

                var toggleResult = await Task.Run(() => Adb.RunAdbCommandToString(command));

                if (!string.IsNullOrEmpty(toggleResult.Output))
                {
                    AppendOutput($"{toggleResult.Output}\n");
                }

                if (!string.IsNullOrEmpty(toggleResult.Error))
                {
                    AppendOutput($"Error: {toggleResult.Error}\n");
                }

                var newStatus = isUpdatesDisabled ? "enabled" : "disabled";
                AppendOutput($"\nQuest system updates are now {newStatus}.\n\n");
                Logger.Log($"Quest system updates {newStatus}");
            }
            catch (Exception ex)
            {
                AppendOutput($"Exception: {ex.Message}\n\n");
                Logger.Log($"Toggle updates error: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _toggleUpdatesButton.IsEnabled = true;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_outputTextBox != null)
            {
                _outputTextBox.Text = string.Empty;
            }
        }

        private void AppendOutput(string text)
        {
            if (_outputTextBox != null)
            {
                _outputTextBox.Text += text;

                // Scroll to end
                if (_outputTextBox.Parent is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToEnd();
                }
            }
        }
    }
}
