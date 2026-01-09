using System;
using System.Collections.Generic;

namespace WindBoard.Models.InkV2
{
    public sealed class InkFragment
    {
        public Guid FragmentId { get; } = Guid.NewGuid();

        public List<InkPoint> Points { get; } = new List<InkPoint>(256);
    }
}

