using System;
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

        public Vector2 BoundsMin { get; private set; } = new(float.PositiveInfinity, float.PositiveInfinity);

        public Vector2 BoundsMax { get; private set; } = new(float.NegativeInfinity, float.NegativeInfinity);

        public bool HasBounds => BoundsMin.X <= BoundsMax.X && BoundsMin.Y <= BoundsMax.Y;

        internal void ExpandBounds(Vector2 position, float normalizedPressure)
        {
            float widthFactor = EnablePressure
                ? Math.Clamp(normalizedPressure, 0.1f, 1.0f)
                : 1.0f;

            float halfWidth = Math.Max(0.25f, BaseSize * widthFactor / 2.0f);
            ExpandBoundsWithPadding(position, halfWidth);
        }

        private void ExpandBoundsWithPadding(Vector2 position, float padding)
        {
            Vector2 min = position - new Vector2(padding, padding);
            Vector2 max = position + new Vector2(padding, padding);

            if (!HasBounds)
            {
                BoundsMin = min;
                BoundsMax = max;
                return;
            }

            BoundsMin = new Vector2(
                Math.Min(BoundsMin.X, min.X),
                Math.Min(BoundsMin.Y, min.Y));

            BoundsMax = new Vector2(
                Math.Max(BoundsMax.X, max.X),
                Math.Max(BoundsMax.Y, max.Y));
        }
    }

    internal readonly record struct StrokePoint(Vector2 Position, float Pressure);
}
