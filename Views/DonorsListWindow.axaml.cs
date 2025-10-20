using System.Collections.Generic;
using System.Linq;
using AndroidSideloader.Models;
using AndroidSideloader.Utilities;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;

namespace AndroidSideloader.Views;

public partial class DonorsListWindow : Window
{
    public ObservableCollection<DonorApp> DonorApps { get; set; }
    public List<DonorApp> SelectedApps { get; private set; }
    public List<DonorApp> UnselectedNewApps { get; private set; }
    public bool UserClickedDonate { get; private set; }

    public DonorsListWindow()
    {
        InitializeComponent();
        DonorApps = new ObservableCollection<DonorApp>();
        SelectedApps = new List<DonorApp>();
        UnselectedNewApps = new List<DonorApp>();
    }

    public DonorsListWindow(List<DonorApp> apps) : this()
    {
        foreach (var app in apps)
        {
            DonorApps.Add(app);
        }

        // Set data context and wire up events
        var dataGrid = this.FindControl<DataGrid>("DonorsDataGrid");
        if (dataGrid != null)
        {
            dataGrid.ItemsSource = DonorApps;
        }

        var donateButton = this.FindControl<Button>("DonateButton");
        if (donateButton != null)
        {
            donateButton.Click += DonateButton_Click;
        }

        var skipButton = this.FindControl<Button>("SkipButton");
        if (skipButton != null)
        {
            skipButton.Click += SkipButton_Click;
        }

        Logger.Log($"DonorsListWindow opened with {apps.Count} apps");
    }

    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        // Get selected apps
        SelectedApps = DonorApps.Where(a => a.IsSelected).ToList();

        // Get unselected NEW apps (not updates) for the NewApps dialog
        UnselectedNewApps = DonorApps
            .Where(a => !a.IsSelected && a.Status.Contains("New"))
            .ToList();

        Logger.Log($"User selected {SelectedApps.Count} apps to donate");
        Logger.Log($"Found {UnselectedNewApps.Count} unselected new apps");

        UserClickedDonate = true;
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        // Get all unselected NEW apps for the NewApps dialog
        UnselectedNewApps = DonorApps
            .Where(a => !a.IsSelected && a.Status.Contains("New"))
            .ToList();

        Logger.Log($"User clicked Skip. Found {UnselectedNewApps.Count} unselected new apps");

        UserClickedDonate = false;
        Close();
    }
}
