using System.Numerics;
using WindBoard.Board;

namespace WindBoard.Tests.Board.Editing;

/// <summary>
/// 测试用笔迹构造器：集中管理常用 Stroke 生成逻辑，避免用例之间复制粘贴。
/// </summary>
internal static class StrokeTestFactory
{
    internal static Stroke CreateStroke(Vector2 p0, Vector2 p1)
    {
        var stroke = new Stroke
        {
            BaseSize = 6.0f,
            EnablePressure = false,
        };

        stroke.Points.Add(new StrokePoint(p0, 1.0f));
        stroke.ExpandBounds(p0, 1.0f);

        stroke.Points.Add(new StrokePoint(p1, 1.0f));
        stroke.ExpandBounds(p1, 1.0f);

        return stroke;
    }
}

