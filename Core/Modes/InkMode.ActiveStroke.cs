using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using WindBoard.Core.Ink;
using StylusPoint = System.Windows.Input.StylusPoint;
using StylusPointCollection = System.Windows.Input.StylusPointCollection;

namespace WindBoard.Core.Modes
{
    public partial class InkMode
    {
        private sealed class ActiveStroke
        {
            public Stroke Stroke { get; set; }
            public DrawingAttributes DrawingAttributes { get; }
            public double LogicalThicknessDip { get; }
            public RealtimeInkSmoother Smoother { get; }
            public Point LastInputCanvasDip { get; set; }
            public long LastInputTicks { get; set; }
            public bool LiveTailEnabled { get; set; }
            public float LiveTailPressure { get; set; }
            public bool UsesRealPressure { get; set; }
            public float LastRealPressure { get; set; }
            public bool HasRealPressureCandidate { get; set; }
            public float RealPressureMin { get; set; }
            public float RealPressureMax { get; set; }
            public int RealPressureSamples { get; set; }
            public SimulatedPressure? SimulatedPressure { get; }
            public List<Stroke> Segments { get; } = new List<Stroke>(4);

            public List<StylusPoint> PendingPoints { get; } = new List<StylusPoint>(256);
            public int PendingStartIndex { get; set; }
            public int PendingPointsCount => PendingPoints.Count - PendingStartIndex;
            public StylusPointCollection ScratchPoints { get; }

            public ActiveStroke(Stroke stroke, DrawingAttributes drawingAttributes, double logicalThicknessDip, RealtimeInkSmoother smoother, Point lastInputCanvasDip, long lastInputTicks, bool usesRealPressure, float initialRealPressure, bool hasRealPressureCandidate, SimulatedPressure? simulatedPressure)
            {
                Stroke = stroke;
                DrawingAttributes = drawingAttributes;
                LogicalThicknessDip = logicalThicknessDip;
                Smoother = smoother;
                LastInputCanvasDip = lastInputCanvasDip;
                LastInputTicks = lastInputTicks;
                LiveTailEnabled = false;
                LiveTailPressure = initialRealPressure;
                UsesRealPressure = usesRealPressure;
                LastRealPressure = initialRealPressure;
                HasRealPressureCandidate = hasRealPressureCandidate;
                RealPressureMin = initialRealPressure;
                RealPressureMax = initialRealPressure;
                RealPressureSamples = hasRealPressureCandidate ? 1 : 0;
                SimulatedPressure = simulatedPressure;
                ScratchPoints = new StylusPointCollection(stroke.StylusPoints.Description, 256);
            }
        }
    }
}
