using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using WindBoard;
using WindBoard.Core.Ink;
using WindBoard.Models.InkV2;
using StylusPoint = System.Windows.Input.StylusPoint;
using StylusPointCollection = System.Windows.Input.StylusPointCollection;

namespace WindBoard.Core.Modes
{
    public partial class InkMode
    {
        private sealed class ActiveStroke
        {
            public BoardPage Page { get; }
            public Guid StrokeId { get; }
            public InkTool Tool { get; set; }
            public InkFragment Fragment { get; }

            public Stroke Stroke { get; set; }
            public DrawingAttributes DrawingAttributes { get; }
            public double LogicalThicknessDip { get; }
            public Point LastInputCanvasDip { get; set; }
            public long LastInputTicks { get; set; }
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

            public ActiveStroke(
                BoardPage page,
                Guid strokeId,
                InkTool tool,
                InkFragment fragment,
                Stroke stroke,
                DrawingAttributes drawingAttributes,
                double logicalThicknessDip,
                Point lastInputCanvasDip,
                long lastInputTicks,
                bool usesRealPressure,
                float initialRealPressure,
                bool hasRealPressureCandidate,
                SimulatedPressure? simulatedPressure)
            {
                Page = page;
                StrokeId = strokeId;
                Tool = tool;
                Fragment = fragment;
                Stroke = stroke;
                DrawingAttributes = drawingAttributes;
                LogicalThicknessDip = logicalThicknessDip;
                LastInputCanvasDip = lastInputCanvasDip;
                LastInputTicks = lastInputTicks;
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
