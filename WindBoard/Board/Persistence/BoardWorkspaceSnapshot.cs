using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using WindBoard.Board.Elements;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 工作区快照（用于多页面导入/导出）。
    /// 
    /// 说明：
    /// - 快照是“数据态”，不包含运行态对象（渲染器、输入控制器等）
    /// - 当前覆盖：笔迹 + 页面元素；并预留视口元数据（仅记录，可用于导出等）
    /// </summary>
    internal sealed record BoardWorkspaceSnapshot(
        IReadOnlyList<BoardPageSnapshot> Pages,
        int CurrentIndex,
        Vector2? ViewportCameraWorld = null,
        float? ViewportZoom = null,
        Vector2? ViewportSizeDip = null);

    /// <summary>
    /// 页面快照。
    /// </summary>
    /// <param name="Id">
    /// 页面 ID（与 <see cref="Editing.BoardPage.Id"/> 对齐）。
    /// 该字段用于后续导入/导出时维持页面身份稳定（例如资源引用、跨集合定位等）。
    /// </param>
    /// <param name="Strokes">笔迹层数据（带类型标识的绘制条目快照；属性名保持 <c>strokes</c> 以兼容 v1/v2 文件）。</param>
    /// <param name="ElementsBelowInk">元素（笔迹下方层）。顺序即绘制/命中测试顺序。</param>
    /// <param name="ElementsAboveInk">元素（笔迹上方层）。顺序即绘制/命中测试顺序。</param>
    internal sealed record BoardPageSnapshot(
        Guid Id,
        IReadOnlyList<InkItemSnapshot> Strokes,
        IReadOnlyList<BoardElementSnapshot>? ElementsBelowInk = null,
        IReadOnlyList<BoardElementSnapshot>? ElementsAboveInk = null);

    /// <summary>
    /// 绘制条目快照（扁平 + Kind 判别，与 Wbix 元素的 <c>{kind, data}</c> 模式一致）。
    /// </summary>
    /// <remarks>
    /// 说明：
    /// - <see cref="Kind"/> 为类型标识；JSON 中缺省/为 null 时按 <c>"stroke"</c> 处理，
    ///   使 v1/v2 旧文件（无 Kind 字段）天然可读；
    /// - <see cref="Stroke"/> 为折线笔迹数据；<see cref="Shape"/> 为形状数据（design E，
    ///   v3 格式内的 kind 扩展，版本不升级）；
    /// - 序列化兼容（v2 扁平形态与 v3 包装形态）由 <see cref="InkItemSnapshotJsonConverter"/> 单点处理，
    ///   域对象与快照之间的转换由 <see cref="BoardInkItemCodec"/> 单点处理。
    /// </remarks>
    [JsonConverter(typeof(InkItemSnapshotJsonConverter))]
    internal sealed class InkItemSnapshot
    {
        /// <summary>
        /// 绘制条目类型标识（缺省 <c>"stroke"</c>；旧文件缺省/为 null 时归一为该值）。
        /// </summary>
        public string Kind { get; set; } = BoardInkItemCodec.StrokeKind;

        /// <summary>
        /// 折线笔迹数据（Kind 为 <c>"stroke"</c> 时的数据载荷）。
        /// </summary>
        public StrokeSnapshot? Stroke { get; set; }

        /// <summary>
        /// 形状数据（Kind 为形状类型 <c>"line"/"rect"/"ellipse"/"arrow"</c> 时的数据载荷）。
        /// </summary>
        public ShapeSnapshot? Shape { get; set; }
    }

    internal sealed record StrokeSnapshot(
        IReadOnlyList<StrokePointSnapshot> Points,
        Vector4 ColorRgba,
        float BaseSize,
        bool EnablePressure);

    internal readonly record struct StrokePointSnapshot(Vector2 Position, float Pressure);

    /// <summary>
    /// 两点式形状快照（design E）。
    /// </summary>
    /// <remarks>
    /// Start/End 为世界坐标端点（矩形/椭圆按对角点存储，读侧规范化为 Min/Max）；
    /// Width 为线宽（世界坐标直径，不支持压感）。
    /// </remarks>
    internal sealed record ShapeSnapshot(
        Vector2 Start,
        Vector2 End,
        Vector4 ColorRgba,
        float Width);

    /// <summary>
    /// 页面元素快照（非笔迹对象）。
    /// </summary>
    /// <remarks>
    /// 说明：
    /// - 目前覆盖文本/链接/媒体/文件四类，满足 WBI 与 WBIX 的互通需求；
    /// - 更复杂的元素（便签/图形等）可在后续扩展。
    /// </remarks>
    internal abstract record BoardElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order);

    internal sealed record BoardTextElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        string Text)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);

    internal sealed record BoardLinkElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        string Url,
        string? Title)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);

    internal sealed record BoardMediaElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        BoardMediaKind Kind,
        string SourcePath,
        string DisplayName)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);

    internal sealed record BoardFileElementSnapshot(
        Guid Id,
        Vector2 PositionWorld,
        Vector2 SizeWorld,
        int Order,
        string SourcePath,
        string DisplayName)
        : BoardElementSnapshot(Id, PositionWorld, SizeWorld, Order);
}
