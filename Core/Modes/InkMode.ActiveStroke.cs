using System.Collections.Generic;
using System.Windows;
using WindBoard.Core.Ink;
using WindBoard.Models.Ink;

namespace WindBoard.Core.Modes
{
    public partial class InkMode
    {
        private sealed class ActiveStroke
        {
            public int PointerId { get; }
            public InkStrokeStyle Style { get; set; }
            public double ZoomAtStart { get; }
            public DetailPreservingSmoother? DetailSmoother { get; }

            public Point LastInputCanvasDip { get; set; }
            public long LastInputTicks { get; set; }

            public bool UsesRealPressure { get; set; }
            public float LastRealPressure { get; set; }
            public bool HasRealPressureCandidate { get; set; }
            public float RealPressureMin { get; set; }
            public float RealPressureMax { get; set; }
            public int RealPressureSamples { get; set; }
            public SimulatedPressure? SimulatedPressure { get; }

            public List<InkPoint> PendingPoints { get; } = new List<InkPoint>(256);
            public int PendingStartIndex { get; set; }
            public int PendingPointsCount => PendingPoints.Count - PendingStartIndex;
            public List<DetailPreservingSample> SmoothingScratch { get; } = new List<DetailPreservingSample>(4);

            public int SegmentPointCount { get; set; }
            public InkPoint LastCommittedPoint { get; set; }

            public ActiveStroke(
                int pointerId,
                InkStrokeStyle style,
                double zoomAtStart,
                DetailPreservingSmoother? detailSmoother,
                Point lastInputCanvasDip,
                long lastInputTicks,
                bool usesRealPressure,
                float initialRealPressure,
                bool hasRealPressureCandidate,
                SimulatedPressure? simulatedPressure,
                InkPoint initialCommittedPoint)
            {
                PointerId = pointerId;
                Style = style;
                ZoomAtStart = zoomAtStart;
                DetailSmoother = detailSmoother;
                LastInputCanvasDip = lastInputCanvasDip;
                LastInputTicks = lastInputTicks;
                UsesRealPressure = usesRealPressure;
                LastRealPressure = initialRealPressure;
                HasRealPressureCandidate = hasRealPressureCandidate;
                RealPressureMin = initialRealPressure;
                RealPressureMax = initialRealPressure;
                RealPressureSamples = hasRealPressureCandidate ? 1 : 0;
                SimulatedPressure = simulatedPressure;
                SegmentPointCount = 1;
                LastCommittedPoint = initialCommittedPoint;
            }
        }
    }
}
