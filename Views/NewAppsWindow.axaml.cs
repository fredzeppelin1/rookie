using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AndroidSideloader.Models;
using AndroidSideloader.Utilities;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AndroidSideloader.Views;

public partial class NewAppsWindow : Window
{
    public ObservableCollection<NewApp> NewApps { get; set; }
    public List<NewApp> VrApps { get; private set; }
    public List<NewApp> NonVrApps { get; private set; }

    public NewAppsWindow()
    {
        InitializeComponent();
        NewApps = new ObservableCollection<NewApp>();
        VrApps = new List<NewApp>();
        NonVrApps = new List<NewApp>();
    }

    public NewAppsWindow(List<DonorApp> newApps) : this()
    {
        // Convert DonorApp list to NewApp list
        foreach (var donorApp in newApps)
        {
            NewApps.Add(new NewApp(donorApp.GameName, donorApp.PackageName));
        }

        // Set data context and wire up events
        var dataGrid = this.FindControl<DataGrid>("NewAppsDataGrid");
        if (dataGrid != null)
        {
            dataGrid.ItemsSource = NewApps;
        }

        var submitButton = this.FindControl<Button>("SubmitButton");
        if (submitButton != null)
        {
            submitButton.Click += SubmitButton_Click;
        }

        Logger.Log($"NewAppsWindow opened with {newApps.Count} apps");
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        // Separate VR apps from non-VR apps
        VrApps = NewApps.Where(a => a.IsVrApp).ToList();
        NonVrApps = NewApps.Where(a => !a.IsVrApp).ToList();

        Logger.Log($"User categorized {VrApps.Count} VR apps and {NonVrApps.Count} non-VR apps");

        // Save to settings
        var hwid = SideloaderUtilities.Uuid();
        foreach (var nonVrApp in NonVrApps)
        {
            var existingNonApps = SettingsManager.Instance.NonAppPackages ?? "";
            if (!existingNonApps.Contains(nonVrApp.PackageName))
            {
                SettingsManager.Instance.NonAppPackages += $"{nonVrApp.PackageName};{hwid}\n";
            }
        }

        foreach (var vrApp in VrApps)
        {
            var existingAppPackages = SettingsManager.Instance.AppPackages ?? "";
            if (!existingAppPackages.Contains(vrApp.PackageName))
            {
                SettingsManager.Instance.AppPackages += $"{vrApp.PackageName}\n";
            }
        }

        SettingsManager.Instance.Save();
        Logger.Log("App categorization saved to settings");

        Close();
    }
}
