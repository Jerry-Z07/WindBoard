using System;
using System.Collections.Generic;

namespace WindBoard.Models.Ink
{
    public sealed class InkStrokeModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public double ZoomAtCreation { get; set; } = 1.0;

        public InkStrokeStyle Style { get; set; } = new InkStrokeStyle(
            InkBrushKind.Pen,
            System.Windows.Media.Colors.White,
            LogicalThicknessDip: 1.0,
            UsesPressure: false);

        public List<InkPoint> Points { get; } = new List<InkPoint>(256);
    }
}
