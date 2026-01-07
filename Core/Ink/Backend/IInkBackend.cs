using System;
using WindBoard.Models.Ink;

namespace WindBoard.Core.Ink.Backend
{
    public interface IInkBackend : IDisposable
    {
        void BeginStroke(int pointerId, InkStrokeStyle style, InkPoint startPoint, double zoomAtStart);

        void AppendPoints(int pointerId, ReadOnlySpan<InkPoint> points);

        void StartNewSegment(int pointerId, InkPoint startPoint);

        void UpdateStrokeStyle(int pointerId, InkStrokeStyle style, double zoomAtStart);

        void EndStroke(int pointerId);

        void CancelStroke(int pointerId);

        void CancelAllStrokes();
    }
}
