using System.Linq;
using AndroidSideloader.Services;
using AndroidSideloader.Utilities;
using AndroidSideloader.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace AndroidSideloader.Views
{
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
                    var files = e.DataTransfer.TryGetFiles();
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
                e.DragEffects = e.DataTransfer.Contains(DataFormat.File) 
                    ? DragDropEffects.Copy 
                    : DragDropEffects.None;
            });
        }
    }
}