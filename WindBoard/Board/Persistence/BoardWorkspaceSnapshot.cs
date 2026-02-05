using System;
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

    /// <summary>
    /// 页面快照。
    /// </summary>
    /// <param name="Id">
    /// 页面 ID（与 <see cref="Editing.BoardPage.Id"/> 对齐）。
    /// 该字段用于后续导入/导出时维持页面身份稳定（例如资源引用、跨集合定位等）。
    /// </param>
    /// <param name="Strokes">笔迹数据。</param>
    internal sealed record BoardPageSnapshot(Guid Id, IReadOnlyList<StrokeSnapshot> Strokes);

    internal sealed record StrokeSnapshot(
        IReadOnlyList<StrokePointSnapshot> Points,
        Vector4 ColorRgba,
        float BaseSize,
        bool EnablePressure);

    internal readonly record struct StrokePointSnapshot(Vector2 Position, float Pressure);
}
