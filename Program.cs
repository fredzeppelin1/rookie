using Avalonia;
using Avalonia.ReactiveUI;
using AndroidSideloader.Views;
using System;

namespace AndroidSideloader;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before BuildAvaloniaApp is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => 
        AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseReactiveUI()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args);
}