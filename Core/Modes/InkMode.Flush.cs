using System;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Threading;
using WindBoard.Core.Ink;

namespace WindBoard.Core.Modes
{
    public partial class InkMode
    {
        private void EnsureFlushTimer()
        {
            if (_flushTimer == null)
            {
                _flushTimer = new DispatcherTimer(DispatcherPriority.Render, _canvas.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _flushTimer.Tick += (_, __) =>
                {
                    foreach (var active in _activeStrokes.Values)
                    {
                        FlushPendingPoints(active);
                    }
                    StopFlushTimerIfIdle();
                };
            }

            if (!_flushTimer.IsEnabled)
            {
                _flushTimer.Start();
            }
        }

        private void StopFlushTimerIfIdle()
        {
            if (_flushTimer == null) return;
            if (_activeStrokes.Count != 0) return;
            _flushTimer.Stop();
        }

        private void FlushPendingPoints(ActiveStroke active)
        {
            if (active.PendingPointsCount == 0) return;

            while (active.PendingPointsCount > 0)
            {
                EnsureSegmentCapacity(active, active.PendingPointsCount);

                int curCount = active.Stroke.StylusPoints.Count;
                int remain = MaxStylusPointsPerSegment - curCount;
                if (remain <= 0) continue;

                int take = Math.Min(remain, active.PendingPointsCount);
                AppendPendingBatch(active, take);
            }
        }

        private void EnsureSegmentCapacity(ActiveStroke active, int pointsToAppend)
        {
            int curCount = active.Stroke.StylusPoints.Count;
            if (curCount + pointsToAppend <= MaxStylusPointsPerSegment) return;

            // 分段：避免单个 Stroke 无限增长导致增量更新越来越慢（单笔越画越卡）。
            var last = curCount > 0
                ? active.Stroke.StylusPoints[^1]
                : new System.Windows.Input.StylusPoint(active.LastInputCanvasDip.X, active.LastInputCanvasDip.Y);

            var next = new Stroke(new System.Windows.Input.StylusPointCollection { last })
            {
                DrawingAttributes = active.DrawingAttributes
            };
            StrokeThicknessMetadata.SetLogicalThicknessDip(next, active.LogicalThicknessDip);
            _canvas.Strokes.Add(next);
            active.Segments.Add(next);
            active.Stroke = next;
        }

        private void AppendPendingBatch(ActiveStroke active, int take)
        {
            var scratch = active.ScratchPoints;
            scratch.Clear();

            int start = active.PendingStartIndex;
            for (int i = 0; i < take; i++)
            {
                scratch.Add(active.PendingPoints[start + i]);
            }

            active.Stroke.StylusPoints.Add(scratch);
            active.PendingStartIndex += take;

            if (active.PendingStartIndex >= 2048 && active.PendingStartIndex >= active.PendingPoints.Count / 2)
            {
                active.PendingPoints.RemoveRange(0, active.PendingStartIndex);
                active.PendingStartIndex = 0;
            }
        }
    }
}

