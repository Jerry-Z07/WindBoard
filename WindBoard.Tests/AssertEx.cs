using System;
using System.Numerics;
using Xunit;

namespace WindBoard.Tests;

internal static class AssertEx
{
    public static void Equal(float expected, float actual, float tolerance = 0.0001f)
    {
        // 浮点比较使用容差，避免因为累计误差导致测试不稳定。
        Assert.True(
            MathF.Abs(expected - actual) <= tolerance,
            $"期望: {expected}，实际: {actual}，容差: {tolerance}");
    }

    public static void Equal(Vector2 expected, Vector2 actual, float tolerance = 0.0001f)
    {
        Equal(expected.X, actual.X, tolerance);
        Equal(expected.Y, actual.Y, tolerance);
    }
}

