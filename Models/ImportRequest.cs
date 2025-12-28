using System.Collections.Generic;

namespace WindBoard
{
    public sealed class ImportRequest
    {
        public List<string> ImagePaths { get; } = new();
        public List<string> VideoPaths { get; } = new();
        public List<string> TextFilePaths { get; } = new();
        public string? TextContent { get; set; }
        public List<string> Urls { get; } = new();
    }
}

