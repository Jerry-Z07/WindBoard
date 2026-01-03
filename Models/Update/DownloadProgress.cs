namespace WindBoard.Models.Update
{
    public sealed class DownloadProgress
    {
        public long BytesReceived { get; set; }

        public long TotalBytes { get; set; }

        public double ProgressPercentage => TotalBytes > 0
            ? (double)BytesReceived / TotalBytes * 100.0
            : 0.0;
    }
}

