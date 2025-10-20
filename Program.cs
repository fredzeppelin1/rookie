using Avalonia;
using Avalonia.ReactiveUI;
using Avalonia.WebView.Desktop;
using AndroidSideloader.Views;
using System;

namespace AndroidSideloader;

internal class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseReactiveUI()
        .UseDesktopWebView()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args);
}