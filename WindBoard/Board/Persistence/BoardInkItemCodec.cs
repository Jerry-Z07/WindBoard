using System;
using System.Collections.Generic;
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
    /// 说明（阶段一"序列化收敛"重构，R3）：
    /// - 快照到域的重建逻辑原先散落三处（BoardWorkspaceSnapshotApplier / BoardRasterExporter /
    ///   WbiWorkspaceImporter），其中的 Stroke 构造与 Bounds 重算完全同构；本类型把它们收敛为单点，
    ///   并作为"多态反序列化"的唯一 Kind 分发入口（命令栈的 <c>ReplaceStrokesCommand</c> 只持有
    ///   <see cref="IBoardInkItem"/> 对象引用，不引入新的类型标识来源）；
    /// - 序列化数据形态的兼容（v2 扁平 / v3 包装）由 <see cref="InkItemSnapshotJsonConverter"/> 负责，
    ///   本类型只处理 Kind 分发与值换算；
    /// - Bounds 重算单点：所有"快照/外部数据 → <see cref="WindBoard.Board.Stroke"/>"路径统一调用
    ///   <see cref="WindBoard.Board.Stroke.RecalculateBoundsFromPoints"/>，三处原实现语义一致
    ///   （按点集与压感重算；z-order 由调用方按条目顺序保持）；
    /// - 非折线条目：域→快照方向返回 null（调用方跳过），快照→域方向未知 Kind 记 Warn 后跳过，
    ///   保持"单个条目异常不阻断整个导入流程"的容错约定。
    /// </remarks>
    internal static class BoardInkItemCodec
    {
        /// <summary>
        /// 折线笔迹的 Kind 标识（快照缺省类型）。
        /// </summary>
        internal const string StrokeKind = "stroke";

        /// <summary>
        /// 域对象 → 快照（仅承载折线笔迹；非 Stroke 条目返回 null，由调用方跳过）。
        /// </summary>
        public static InkItemSnapshot? ToSnapshot(IBoardInkItem? item)
        {
            if (item is null)
            {
                return null;
            }

            if (item is not Stroke stroke)
            {
                // 快照格式当前仅承载折线笔迹；非 Stroke 条目（阶段二起引入）留待序列化扩展。
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

            switch (kind.ToLowerInvariant())
            {
                case StrokeKind:
                    if (snapshot.Stroke is null)
                    {
                        throw new ArgumentException("Stroke 快照缺少 stroke 数据。", nameof(snapshot));
                    }

                    return ToStrokeItem(snapshot.Stroke);

                default:
                    // 未知类型：跳过并告警，避免单条异常数据阻断整个导入流程。
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
    }
}
