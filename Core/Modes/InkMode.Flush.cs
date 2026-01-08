using System;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Threading;
using WindBoard.Models.Ink;

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

                int remain = MaxStylusPointsPerSegment - active.SegmentPointCount;
                if (remain <= 0) continue;

                int take = Math.Min(remain, active.PendingPointsCount);
                AppendPendingBatch(active, take);
            }
        }

        private void EnsureSegmentCapacity(ActiveStroke active, int pointsToAppend)
        {
            if (active.SegmentPointCount + pointsToAppend <= MaxStylusPointsPerSegment) return;

            // 分段：避免单个 Stroke 无限增长导致增量更新越来越慢（单笔越画越卡）。
            _backend.StartNewSegment(active.PointerId, active.LastCommittedPoint);
            active.SegmentPointCount = 1;
        }

        private void AppendPendingBatch(ActiveStroke active, int take)
        {
            int start = active.PendingStartIndex;
            ReadOnlySpan<InkPoint> span = CollectionsMarshal.AsSpan(active.PendingPoints).Slice(start, take);
            _backend.AppendPoints(active.PointerId, span);

            active.SegmentPointCount += take;
            active.LastCommittedPoint = span[^1];

            active.PendingStartIndex += take;

            if (active.PendingStartIndex >= 2048 && active.PendingStartIndex >= active.PendingPoints.Count / 2)
            {
                active.PendingPoints.RemoveRange(0, active.PendingStartIndex);
                active.PendingStartIndex = 0;
            }
        }
    }
}
