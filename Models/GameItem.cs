namespace AndroidSideloader.Models;

public class GameItem
{
    public string GameName { get; set; }
    public string ReleaseName { get; set; }
    public string PackageName { get; set; }

    // Version as string for display
    private string _version;
    public string Version
    {
        get => _version;
        set
        {
            _version = value;
            // Parse numeric value for sorting (unparseable values sort to -1, i.e., bottom)
            if (long.TryParse(value, out var ver))
            {
                VersionNumeric = ver;
            }
            else
            {
                VersionNumeric = -1;
            }
        }
    }

    // Numeric version for proper sorting
    public long VersionNumeric { get; private set; } = -1;

    public string LastUpdated { get; set; }

    // Size as string for display
    private string _sizeMb;
    public string SizeMb
    {
        get => _sizeMb;
        set
        {
            _sizeMb = value;
            // Parse numeric value for sorting (unparseable values sort to -1, i.e., bottom)
            if (double.TryParse(value, out var size))
            {
                SizeMbNumeric = size;
            }
            else
            {
                SizeMbNumeric = -1;
            }
        }
    }

    // Numeric size for proper sorting
    public double SizeMbNumeric { get; private set; } = -1;

    // Popularity - has leading zeros so text sorting works fine
    public string Popularity { get; set; }

    public bool IsFavorite { get; set; }

    // Image/Thumbnail properties
    public string CachedImagePath { get; set; }
    public bool HasImage => !string.IsNullOrEmpty(CachedImagePath) && System.IO.File.Exists(CachedImagePath);

    // Installed status properties
    public bool IsInstalled { get; set; }
    public long InstalledVersionCode { get; set; }
    public long AvailableVersionCode { get; set; }

    /// <summary>
    /// True if this game is installed and has an available update
    /// </summary>
    public bool HasUpdate => IsInstalled && AvailableVersionCode > InstalledVersionCode && InstalledVersionCode > 0;
}