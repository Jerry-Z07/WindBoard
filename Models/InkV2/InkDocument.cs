using System.Collections.Generic;

namespace WindBoard.Models.InkV2
{
    public sealed class InkDocument
    {
        public List<InkStroke> Strokes { get; } = new List<InkStroke>(256);
    }
}

