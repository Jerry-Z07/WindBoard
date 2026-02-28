using System;
using System.Collections.Generic;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    internal sealed class DownloadResult
    {
        public bool Success { get; init; }

        public string FilePath { get; init; } = string.Empty;

        public DownloadSourceId SourceId { get; init; } = DownloadSourceId.Github;

        public string ErrorMessage { get; init; } = string.Empty;

        public Exception? Error { get; init; }

        public IReadOnlyList<DownloadAttemptError> AttemptErrors { get; init; } = Array.Empty<DownloadAttemptError>();

        public static DownloadResult SuccessResult(string filePath, DownloadSourceId sourceId)
        {
            return new DownloadResult
            {
                Success = true,
                FilePath = filePath ?? string.Empty,
                SourceId = sourceId,
            };
        }

        public static DownloadResult Fail(string errorMessage, Exception? error = null)
        {
            return new DownloadResult
            {
                Success = false,
                ErrorMessage = errorMessage ?? string.Empty,
                Error = error,
            };
        }

        public static DownloadResult Fail(string errorMessage, IReadOnlyList<DownloadAttemptError> attemptErrors, Exception? error = null)
        {
            return new DownloadResult
            {
                Success = false,
                ErrorMessage = errorMessage ?? string.Empty,
                AttemptErrors = attemptErrors ?? Array.Empty<DownloadAttemptError>(),
                Error = error,
            };
        }
    }
}

