using System.Collections.Generic;
using System.Numerics;
using Vortice.Mathematics;

namespace WindBoard.Board
{
    internal sealed class BoardDocument
    {
        public List<Stroke> Strokes { get; } = new();
    }

    internal sealed class Stroke
    {
        public List<StrokePoint> Points { get; } = new();

        public Color4 Color { get; init; } = new(0, 0, 0, 1);

        public float BaseSize { get; init; } = 3.0f;

        public bool EnablePressure { get; init; } = true;
    }

    internal readonly record struct StrokePoint(Vector2 Position, float Pressure);
}

