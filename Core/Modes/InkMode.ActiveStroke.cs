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
            public List<Stroke> Segments { get; } = new List<Stroke>(4);
            public SimulatedPressureState PressureState;

            public List<StylusPoint> PendingPoints { get; } = new List<StylusPoint>(256);
            public int PendingStartIndex { get; set; }
            public int PendingPointsCount => PendingPoints.Count - PendingStartIndex;
            public StylusPointCollection ScratchPoints { get; }

            public ActiveStroke(Stroke stroke, DrawingAttributes drawingAttributes, double logicalThicknessDip, RealtimeInkSmoother smoother, Point lastInputCanvasDip, long lastInputTicks, SimulatedPressureState pressureState)
            {
                Stroke = stroke;
                DrawingAttributes = drawingAttributes;
                LogicalThicknessDip = logicalThicknessDip;
                Smoother = smoother;
                LastInputCanvasDip = lastInputCanvasDip;
                LastInputTicks = lastInputTicks;
                PressureState = pressureState;
                ScratchPoints = new StylusPointCollection(stroke.StylusPoints.Description, 256);
            }
        }
    }
}

