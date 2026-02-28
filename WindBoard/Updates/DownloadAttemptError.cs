using WindBoard.Settings;

namespace WindBoard.Updates
{
    internal readonly struct DownloadAttemptError
    {
        internal DownloadSourceId SourceId { get; }

        internal string Message { get; }

        internal DownloadAttemptError(DownloadSourceId sourceId, string? message)
        {
            SourceId = sourceId;
            Message = message ?? string.Empty;
        }
    }
}

