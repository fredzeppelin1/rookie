using System;

namespace AndroidSideloader.Models
{
    /// <summary>
    /// Represents a game in the upload queue
    /// </summary>
    public class UploadGame
    {
        public string GameName { get; set; }
        public string PackageName { get; set; }
        public string VersionName { get; set; }
        public string VersionCode { get; set; }
        public string ApkPath { get; set; }
        public string ObbPath { get; set; }
        public long TotalSize { get; set; }
        public long UploadedSize { get; set; }
        public double ProgressPercentage => TotalSize > 0 ? (double)UploadedSize / TotalSize * 100 : 0;
        public UploadStatus Status { get; set; }
        public string StatusMessage { get; set; }
        public DateTime QueuedAt { get; set; }

        public UploadGame()
        {
            Status = UploadStatus.Queued;
            QueuedAt = DateTime.Now;
            StatusMessage = "Waiting in queue...";
        }
    }

    public enum UploadStatus
    {
        Queued,
        Extracting,
        Packaging,
        Uploading,
        Completed,
        Failed,
        Cancelled
    }
}
