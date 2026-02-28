using WindBoard.Settings;

namespace WindBoard.Updates
{
    internal sealed class DownloadProgress
    {
        public required DownloadSourceId SourceId { get; init; }

        public required long DownloadedBytes { get; init; }

        public required long? TotalBytes { get; init; }

        public required DownloadProgressStatus Status { get; init; }
    }

    internal enum DownloadProgressStatus
    {
        Downloading,
        Completed,
    }
}

