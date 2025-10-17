using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AndroidSideloader.Sideloader;

namespace AndroidSideloader.Utilities;

/// <summary>
/// Wrapper for Android Asset Packaging Tool (aapt) - extracts APK metadata without installation
/// </summary>
public static class Aapt
{
    private static string GetAaptExecutablePath()
    {
        var platformToolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools");
        var aaptExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "aapt.exe" : "aapt";
        return Path.Combine(platformToolsPath, aaptExeName);
    }

    private static async Task<ProcessOutput> RunAaptCommand(string arguments)
    {
        var aaptPath = GetAaptExecutablePath();

        if (!File.Exists(aaptPath))
        {
            throw new FileNotFoundException(
                $"aapt executable not found at: {aaptPath}\n\n" +
                "Please ensure Android platform-tools are downloaded.",
                aaptPath);
        }

        var process = new Process();
        process.StartInfo.FileName = aaptPath;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        var output = string.Empty;
        var error = string.Empty;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                output += e.Data + "\n";
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                error += e.Data + "\n";
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Run(() => process.WaitForExit());

        return new ProcessOutput
        {
            Output = output,
            Error = error,
            ExitCode = process.ExitCode
        };
    }

    /// <summary>
    /// Validate APK file and get basic info
    /// </summary>
    public static async Task<ApkInfo> ValidateAndGetInfo(string apkPath)
    {
        if (!File.Exists(apkPath))
        {
            throw new FileNotFoundException($"APK file not found: {apkPath}");
        }

        var result = await RunAaptCommand($"dump badging \"{apkPath}\"");

        if (result.ExitCode != 0)
        {
            throw new Exception($"Invalid APK or aapt command failed: {result.Error}");
        }

        // Parse all relevant info
        var packageMatch = Regex.Match(result.Output, @"package:\s*name='([^']+)'");
        var versionCodeMatch = Regex.Match(result.Output, "versionCode='([^']+)'");
        var versionNameMatch = Regex.Match(result.Output, "versionName='([^']+)'");
        var labelMatch = Regex.Match(result.Output, "application-label:'([^']+)'");
        var targetSdkVersionMatch = Regex.Match(result.Output, "targetSdkVersion:'([^']+)'");

        return new ApkInfo
        {
            PackageName = packageMatch.Success ? packageMatch.Groups[1].Value : "Unknown",
            VersionCode = versionCodeMatch.Success ? versionCodeMatch.Groups[1].Value : "Unknown",
            VersionName = versionNameMatch.Success ? versionNameMatch.Groups[1].Value : "Unknown",
            AppLabel = labelMatch.Success ? labelMatch.Groups[1].Value : "Unknown",
            TargetSdkVersion = targetSdkVersionMatch.Success ? targetSdkVersionMatch.Groups[1].Value : "Unknown"
        };
    }
}

/// <summary>
/// APK information extracted from aapt
/// </summary>
public class ApkInfo
{
    public string PackageName { get; init; }
    public string VersionCode { get; init; }
    public string VersionName { get; init; }
    public string AppLabel { get; init; }
    public string TargetSdkVersion { get; init; }
}