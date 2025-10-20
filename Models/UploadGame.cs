namespace AndroidSideloader.Models;

/// <summary>
/// Represents a game in the upload queue
/// </summary>
public class UploadGame
{
    public string GameName { get; set; }
    public string PackageName { get; init; }
    public long VersionCode { get; set; }
    public string ZipPath { get; set; }  // Path to compressed archive for upload
    public UploadStatus Status { get; set; } = UploadStatus.Queued;
    public string StatusMessage { get; set; } = "Waiting in queue...";
}

public enum UploadStatus
{
    Queued,
    Uploading,
    Completed,
    Failed
}