using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AndroidSideloader.ViewModels;

namespace AndroidSideloader.Views;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
    }

    public UpdateWindow(string currentVersion, string newVersion, string changelog) : this()
    {
        DataContext = new UpdateViewModel(currentVersion, newVersion, changelog, this);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}