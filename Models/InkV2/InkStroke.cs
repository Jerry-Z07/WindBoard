using System;
using System.Collections.Generic;

namespace WindBoard.Models.InkV2
{
    public sealed class InkStroke
    {
        public InkStroke(InkTool tool)
            : this(Guid.NewGuid(), tool)
        {
        }

        public InkStroke(Guid strokeId, InkTool tool)
        {
            if (strokeId == Guid.Empty) strokeId = Guid.NewGuid();
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            StrokeId = strokeId;
        }

        public Guid StrokeId { get; }

        public InkTool Tool { get; }

        public List<InkFragment> Fragments { get; } = new List<InkFragment>(1);
    }
}
