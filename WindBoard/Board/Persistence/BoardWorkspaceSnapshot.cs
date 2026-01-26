using System.Collections.Generic;
using System.Numerics;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 工作区快照（用于多页面导入/导出）。
    /// 
    /// 说明：
    /// - 快照是“数据态”，不包含运行态对象（渲染器、输入控制器等）
    /// - 当前仅覆盖笔迹；后续可扩展：背景、网格、视口、图层、元素等
    /// </summary>
    internal sealed record BoardWorkspaceSnapshot(IReadOnlyList<BoardPageSnapshot> Pages, int CurrentIndex);

    internal sealed record BoardPageSnapshot(IReadOnlyList<StrokeSnapshot> Strokes);

    internal sealed record StrokeSnapshot(
        IReadOnlyList<StrokePointSnapshot> Points,
        Vector4 ColorRgba,
        float BaseSize,
        bool EnablePressure);

    internal readonly record struct StrokePointSnapshot(Vector2 Position, float Pressure);
}

