namespace AndroidSideloader.Models;

/// <summary>
/// Represents a new app that needs categorization (VR vs non-VR)
/// </summary>
public class NewApp
{
    public string GameName { get; set; }
    public string PackageName { get; set; }
    public bool IsVrApp { get; set; }

    public NewApp(string gameName, string packageName)
    {
        GameName = gameName;
        PackageName = packageName;
        IsVrApp = true; // Assume VR by default
    }
}
