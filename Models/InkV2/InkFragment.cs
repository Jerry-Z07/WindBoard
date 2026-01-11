using System;
using System.Collections.Generic;

namespace WindBoard.Models.InkV2
{
    public sealed class InkFragment
    {
        public InkFragment()
            : this(Guid.NewGuid())
        {
        }

        public InkFragment(Guid fragmentId)
        {
            if (fragmentId == Guid.Empty) fragmentId = Guid.NewGuid();
            FragmentId = fragmentId;
        }

        public Guid FragmentId { get; }

        public List<InkPoint> Points { get; } = new List<InkPoint>(256);

        public int PointsVersion { get; internal set; }
    }
}
