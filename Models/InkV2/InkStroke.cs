using System;
using System.Collections.Generic;

namespace WindBoard.Models.InkV2
{
    public sealed class InkStroke
    {
        public InkStroke(InkTool tool)
        {
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
        }

        public Guid StrokeId { get; } = Guid.NewGuid();

        public InkTool Tool { get; }

        public List<InkFragment> Fragments { get; } = new List<InkFragment>(1);
    }
}

