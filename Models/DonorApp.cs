namespace AndroidSideloader.Models;

/// <summary>
/// Represents an app that can be donated/uploaded to VRPirates
/// </summary>
public class DonorApp
{
    public string GameName { get; set; }
    public string PackageName { get; set; }
    public long VersionCode { get; set; }
    public string Status { get; set; } // "Update" or "New App"
    public bool IsSelected { get; set; }

    public DonorApp(string gameName, string packageName, long versionCode, string status)
    {
        GameName = gameName;
        PackageName = packageName;
        VersionCode = versionCode;
        Status = status;
        IsSelected = true; // Selected by default
    }
}
