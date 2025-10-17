using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace AndroidSideloader.Services;

/// <summary>
/// Avalonia implementation of IDialogService using MessageBox.Avalonia
/// All dialog operations are dispatched to the UI thread to ensure thread safety
/// </summary>
public class AvaloniaDialogService : IDialogService
{
    private readonly Window _owner;

    public AvaloniaDialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task ShowInfoAsync(string message, string title = "Information")
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Info);

            await messageBoxStandardWindow.ShowWindowDialogAsync(_owner);
        });
    }

    public async Task ShowErrorAsync(string message, string title = "Error")
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Error);

            await messageBoxStandardWindow.ShowWindowDialogAsync(_owner);
        });
    }

    public async Task ShowWarningAsync(string message, string title = "Warning")
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Warning);

            await messageBoxStandardWindow.ShowWindowDialogAsync(_owner);
        });
    }

    public async Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Question);

            var result = await messageBoxStandardWindow.ShowWindowDialogAsync(_owner);
            return result == ButtonResult.Yes;
        });
    }

    public async Task<bool> ShowOkCancelAsync(string message, string title = "Confirm")
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(title, message, ButtonEnum.OkCancel, Icon.Question);

            var result = await messageBoxStandardWindow.ShowWindowDialogAsync(_owner);
            return result == ButtonResult.Ok;
        });
    }
}