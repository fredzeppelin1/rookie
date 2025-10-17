using System.Runtime.InteropServices;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Provides cross-platform helper methods for OS-specific operations
/// </summary>
public static class PlatformHelper
{
    /// <summary>
    /// Gets the ADB executable name based on the current platform
    /// </summary>
    /// <returns>"adb.exe" on Windows, "adb" on macOS/Linux</returns>
    public static string GetAdbExecutableName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "adb.exe"
            : "adb";
    }

    /// <summary>
    /// Gets the shell path for command execution
    /// </summary>
    /// <returns>Path to cmd.exe on Windows, /bin/bash on Unix systems</returns>
    public static string GetShellPath()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? "cmd.exe" :
            "/bin/bash";
    }

    /// <summary>
    /// Gets the shell argument prefix for executing commands
    /// </summary>
    /// <returns>"/c" for Windows cmd.exe, "-c" for Unix shells</returns>
    public static string GetShellCommandPrefix()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "/c"
            : "-c";
    }

    /// <summary>
    /// Checks if running on Windows
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Checks if running on macOS
    /// </summary>
    public static bool IsMacOs => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Checks if running on Linux
    /// </summary>
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}