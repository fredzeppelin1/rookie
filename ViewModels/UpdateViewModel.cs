using System;
using System.Reactive;
using ReactiveUI;
using AndroidSideloader.Utilities;
using Avalonia.Controls;

namespace AndroidSideloader.ViewModels;

public class UpdateViewModel : ReactiveObject
{
    private readonly Window _window;

    public string CurrentVersion { get; }
    public string NewVersion { get; }
    public string Changelog { get; }

    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipCommand { get; }

    public UpdateViewModel(string currentVersion, string newVersion, string changelog, Window window)
    {
        CurrentVersion = currentVersion;
        NewVersion = newVersion;
        Changelog = changelog;
        _window = window;

        UpdateCommand = ReactiveCommand.Create(OnUpdate);
        SkipCommand = ReactiveCommand.Create(OnSkip);
    }

    private async void OnUpdate()
    {
        try
        {
            Logger.Log("User chose to update application");

            // Close the dialog
            _window.Close(true);

            // Perform the update
            await Updater.DownloadAndInstallUpdateAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during update: {ex.Message}", LogLevel.Error);
            _window.Close(false);
        }
    }

    private void OnSkip()
    {
        Logger.Log("User skipped update");
        _window.Close(false);
    }
}