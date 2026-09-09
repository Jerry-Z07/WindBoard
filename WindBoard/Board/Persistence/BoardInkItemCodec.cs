using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using WindBoard.Board.Items;
using WindBoard.Logging;
using Vortice.Mathematics;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 绘制条目"快照 ↔ 域对象"的单点转换器（Codec）。
    /// </summary>
    /// <remarks>
    /// 说明（阶段一"序列化收敛"重构，R3；阶段二形状扩展，design E）：
    /// - 快照到域的重建逻辑原先散落三处（BoardWorkspaceSnapshotApplier / BoardRasterExporter /
    ///   WbiWorkspaceImporter），其中的 Stroke 构造与 Bounds 重算完全同构；本类型把它们收敛为单点，
    ///   并作为"多态反序列化"的唯一 Kind 分发入口（命令栈的 <c>ReplaceStrokesCommand</c> 只持有
    ///   <see cref="IBoardInkItem"/> 对象引用，不引入新的类型标识来源）；
    /// - 序列化数据形态的兼容（v2 扁平 / v3 包装）由 <see cref="InkItemSnapshotJsonConverter"/> 负责，
    ///   本类型只处理 Kind 分发与值换算；形状 Kind 字符串（line/rect/ellipse/arrow）与
    ///   <see cref="BoardShapeKind"/> 的映射集中在本类型（单点）；
    /// - Bounds 重算单点：所有"快照/外部数据 → <see cref="WindBoard.Board.Stroke"/>"路径统一调用
    ///   <see cref="WindBoard.Board.Stroke.RecalculateBoundsFromPoints"/>，
    ///   形状路径统一调用 <see cref="WindBoard.Board.Items.BoardShape.SetGeometry"/>；
    /// - 未知 Kind：域→快照方向返回 null（调用方跳过），快照→域方向记 Warn 后跳过，
    ///   保持"单个条目异常不阻断整个导入流程"的容错约定。
    /// </remarks>
    internal static class BoardInkItemCodec
    {
        /// <summary>
        /// 折线笔迹的 Kind 标识（快照缺省类型）。
        /// </summary>
        internal const string StrokeKind = "stroke";

        /// <summary>直线形状的 Kind 标识（design E，v3 格式内的 kind 扩展）。</summary>
        internal const string LineKind = "line";

        /// <summary>矩形形状的 Kind 标识。</summary>
        internal const string RectKind = "rect";

        /// <summary>椭圆形状的 Kind 标识（宽高相等即圆）。</summary>
        internal const string EllipseKind = "ellipse";

        /// <summary>箭头形状的 Kind 标识。</summary>
        internal const string ArrowKind = "arrow";

        /// <summary>
        /// 判断给定 Kind 是否为形状类型，并输出对应的 <see cref="BoardShapeKind"/>（单点映射）。
        /// </summary>
        internal static bool TryGetShapeKind(string? kind, [NotNullWhen(true)] out BoardShapeKind shapeKind)
        {
            switch (kind?.Trim().ToLowerInvariant())
            {
                case LineKind:
                    shapeKind = BoardShapeKind.Line;
                    return true;

                case RectKind:
                    shapeKind = BoardShapeKind.Rectangle;
                    return true;

                case EllipseKind:
                    shapeKind = BoardShapeKind.Ellipse;
                    return true;

                case ArrowKind:
                    shapeKind = BoardShapeKind.Arrow;
                    return true;

                default:
                    shapeKind = default;
                    return false;
            }
        }

        /// <summary>
        /// <see cref="BoardShapeKind"/> → 快照 Kind 字符串（单点映射；未知值返回 null，由调用方处理）。
        /// </summary>
        internal static string? TryGetShapeKindText(BoardShapeKind kind)
        {
            return kind switch
            {
                BoardShapeKind.Line => LineKind,
                BoardShapeKind.Rectangle => RectKind,
                BoardShapeKind.Ellipse => EllipseKind,
                BoardShapeKind.Arrow => ArrowKind,
                _ => null,
            };
        }

        /// <summary>
        /// 域对象 → 快照（承载折线笔迹与形状；其它条目返回 null，由调用方跳过）。
        /// </summary>
        public static InkItemSnapshot? ToSnapshot(IBoardInkItem? item)
        {
            if (item is null)
            {
                return null;
            }

            if (item is BoardShape shape)
            {
                return new InkItemSnapshot
                {
                    Kind = TryGetShapeKindText(shape.Kind)
                        ?? throw new ArgumentException($"未知形状种类：{shape.Kind}", nameof(item)),
                    Shape = ToShapeSnapshot(shape),
                };
            }

            if (item is not Stroke stroke)
            {
                // 快照格式当前仅承载折线笔迹与形状；其它条目类型留待后续扩展。
                return null;
            }

            return new InkItemSnapshot
            {
                Kind = StrokeKind,
                Stroke = ToStrokeSnapshot(stroke),
            };
        }

        /// <summary>
        /// 域对象列表 → 快照列表（保持顺序即 z-order；非 Stroke 条目跳过）。
        /// </summary>
        public static List<InkItemSnapshot> ToSnapshotList(IReadOnlyList<IBoardInkItem> items)
        {
            var snapshots = new List<InkItemSnapshot>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                InkItemSnapshot? snapshot = ToSnapshot(items[i]);
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        /// <summary>
        /// 快照 → 域对象（按 Kind 单点分发；未知 Kind 记 Warn 后返回 null，由调用方跳过）。
        /// </summary>
        public static IBoardInkItem? ToItem(InkItemSnapshot? snapshot)
        {
            if (snapshot is null)
            {
                return null;
            }

            // 显式 null 防御：Kind 为 null/空一律归一为 stroke，不依赖 C# 初始化器缺省值。
            string kind = string.IsNullOrWhiteSpace(snapshot.Kind) ? StrokeKind : snapshot.Kind.Trim();

            if (TryGetShapeKind(kind, out BoardShapeKind shapeKind))
            {
                if (snapshot.Shape is null)
                {
                    throw new ArgumentException("Shape 快照缺少 shape 数据。", nameof(snapshot));
                }

                return BuildShape(shapeKind, snapshot.Shape);
            }

            switch (kind.ToLowerInvariant())
            {
                case StrokeKind:
                    if (snapshot.Stroke is null)
                    {
                        throw new ArgumentException("Stroke 快照缺少 stroke 数据。", nameof(snapshot));
                    }

                    return ToStrokeItem(snapshot.Stroke);

                default:
                    // 未知类型（含未来版本的形状 Kind）：跳过并告警，避免单条异常数据阻断整个导入流程。
                    AppLog.Warn("WBIX", $"未知笔迹条目类型，已跳过：kind='{kind}'");
                    return null;
            }
        }

        /// <summary>
        /// 快照列表 → 域对象列表（保持顺序即 z-order；未知 Kind 条目跳过）。
        /// </summary>
        public static List<IBoardInkItem> ToItemList(IReadOnlyList<InkItemSnapshot>? snapshots)
        {
            if (snapshots is null || snapshots.Count == 0)
            {
                return new List<IBoardInkItem>();
            }

            var items = new List<IBoardInkItem>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                IBoardInkItem? item = ToItem(snapshots[i]);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        /// <summary>
        /// 折线快照 → <see cref="WindBoard.Board.Stroke"/>（显式强制 stroke 路径）。
        /// </summary>
        /// <remarks>
        /// 用途：旧 Wbi 格式导入（ISF 解析结果无 Kind 概念，显式声明按折线处理）。
        /// </remarks>
        public static Stroke ToStrokeItem(StrokeSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            Color4 color = new(snapshot.ColorRgba.X, snapshot.ColorRgba.Y, snapshot.ColorRgba.Z, snapshot.ColorRgba.W);
            return BuildStroke(snapshot.Points, color, snapshot.BaseSize, snapshot.EnablePressure);
        }

        private static StrokeSnapshot ToStrokeSnapshot(Stroke stroke)
        {
            var points = new List<StrokePointSnapshot>(stroke.Points.Count);
            for (int i = 0; i < stroke.Points.Count; i++)
            {
                StrokePoint point = stroke.Points[i];
                points.Add(new StrokePointSnapshot(point.Position, point.Pressure));
            }

            Vector4 color = new(stroke.Color.R, stroke.Color.G, stroke.Color.B, stroke.Color.A);
            return new StrokeSnapshot(points, color, stroke.BaseSize, stroke.EnablePressure);
        }

        private static ShapeSnapshot ToShapeSnapshot(BoardShape shape)
        {
            Vector4 color = new(shape.Color.R, shape.Color.G, shape.Color.B, shape.Color.A);
            return new ShapeSnapshot(shape.Start, shape.End, color, shape.Width);
        }

        /// <summary>
        /// Stroke 构造单点：赋值 + 逐点填充 + Bounds 重算（原三处实现语义一致，收敛于此）。
        /// </summary>
        private static Stroke BuildStroke(IReadOnlyList<StrokePointSnapshot> points, Color4 color, float baseSize, bool enablePressure)
        {
            var stroke = new Stroke
            {
                Color = color,
                BaseSize = baseSize,
                EnablePressure = enablePressure,
            };

            for (int i = 0; i < points.Count; i++)
            {
                StrokePointSnapshot p = points[i];
                stroke.Points.Add(new StrokePoint(p.Position, p.Pressure));
            }

            // Bounds 重算单点：数据来自快照/外部输入时不信任持久化前的状态，统一重建一次，
            // 保证选择/变换/离屏导出裁剪等依赖 Bounds 的路径稳定。
            stroke.RecalculateBoundsFromPoints();
            return stroke;
        }

        /// <summary>
        /// BoardShape 构造单点：赋值 + SetGeometry 重算 Bounds（与 BuildStroke 对称，design E）。
        /// </summary>
        private static BoardShape BuildShape(BoardShapeKind kind, ShapeSnapshot snapshot)
        {
            var shape = new BoardShape(kind)
            {
                Color = new Color4(snapshot.ColorRgba.X, snapshot.ColorRgba.Y, snapshot.ColorRgba.Z, snapshot.ColorRgba.W),
                Width = snapshot.Width,
            };

            // SetGeometry 单点重算 Bounds：数据来自快照/外部输入时不信任持久化前的状态。
            shape.SetGeometry(snapshot.Start, snapshot.End);
            return shape;
        }
    }
}
