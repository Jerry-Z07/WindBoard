namespace WindBoard.Models.Update
{
    public sealed class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }

        public UpdateInfo? LatestVersion { get; set; }

        public string? ErrorMessage { get; set; }
    }
}

