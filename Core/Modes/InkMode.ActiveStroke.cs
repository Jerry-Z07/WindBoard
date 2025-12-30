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

            /// <summary>
            /// 临时移除 LiveTail 点（如果存在），返回被移除的点。
            /// 用于在插入新点之前暂时移除尾部跟随点。
            /// </summary>
            public StylusPoint? RemoveLiveTailTemporarily()
            {
                if (!LiveTailEnabled)
                {
                    return null;
                }

                var spc = Stroke.StylusPoints;
                if (spc.Count == 0)
                {
                    return null;
                }

                int lastIndex = spc.Count - 1;
                var tail = spc[lastIndex];
                spc.RemoveAt(lastIndex);
                return tail;
            }

            /// <summary>
            /// 恢复之前移除的 LiveTail 点。
            /// 如果 tail 为 null，则不执行任何操作。
            /// </summary>
            public void RestoreLiveTail(StylusPoint? tail)
            {
                if (!LiveTailEnabled || !tail.HasValue)
                {
                    return;
                }

                Stroke.StylusPoints.Add(tail.Value);
            }

            /// <summary>
            /// 更新 LiveTail 点的位置为当前原始输入位置。
            /// 确保 stroke 至少有 2 个点（倒数第二个是平滑后的点，最后一个是实时跟随点）。
            /// </summary>
            public void UpdateLiveTailPosition(Point rawCanvasDip)
            {
                if (!LiveTailEnabled)
                {
                    return;
                }

                var spc = Stroke.StylusPoints;
                if (spc.Count == 0)
                {
                    var p = new StylusPoint(rawCanvasDip.X, rawCanvasDip.Y, LiveTailPressure);
                    spc.Add(p);
                    spc.Add(p);
                    return;
                }

                if (spc.Count == 1)
                {
                    spc.Add(spc[0]);
                }

                spc.RemoveAt(spc.Count - 1);
                spc.Add(new StylusPoint(rawCanvasDip.X, rawCanvasDip.Y, LiveTailPressure));
            }
        }
    }
}
