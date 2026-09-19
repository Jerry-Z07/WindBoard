using System;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using Vortice.Mathematics;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 两点式形状工具（阶段二 R5，design B）：一个类按 (BoardTool id, BoardShapeKind kind) 注册 4 个实例。
    /// </summary>
    /// <remarks>
    /// - Begin：读取 <see cref="ToolOptions"/> 快照（颜色/粗细；压感忽略，R6），创建
    ///   <see cref="BoardShape"/> 挂 <see cref="BoardInputContext.PreviewItem"/>（语义同 PenTool）；
    /// - Move：更新 End → <see cref="BoardShape.SetGeometry"/> → 请求"旧 ∪ 新几何 AABB"的增量脏矩形
    ///   （屏幕 DIP，机制与 PenTool 等价）；
    /// - End：退化几何（起点终点几乎重合，即点击未拖动）丢弃不提交；否则经
    ///   <see cref="AddInkItemCommand"/> 提交（撤销/重做语义不变），并把结果记入
    ///   <see cref="LastCommittedShape"/> 供控制器抛事件（本类不切工具、不设置选中，保持无 UI 依赖）；
    /// - Cancel：清空（预览挂载点与脏矩形由控制器统一清理）；
    /// - 高频路径（指针事件内）禁止日志。
    /// </remarks>
    internal sealed class ShapeTool : IBoardTool
    {
        /// <summary>增量脏矩形的额外扩展（DIP），与 PenTool 常量保持一致。</summary>
        private const float DirtyRectExtraDip = 2.0f;

        /// <summary>退化几何判定阈值（世界坐标长度）：低于该值视为"点击未拖动"，丢弃不提交。</summary>
        private const float DegenerateLengthWorld = 0.001f;

        private readonly BoardShapeKind _kind;

        public ShapeTool(BoardShapeKind kind)
        {
            _kind = kind;
            // 工具身份与形状种类的单点绑定：注册表按 Id 解析工具，Kind 决定预览/提交的形状种类。
            Id = kind switch
            {
                BoardShapeKind.Line => BoardTool.Line,
                BoardShapeKind.Rectangle => BoardTool.Rectangle,
                BoardShapeKind.Ellipse => BoardTool.Ellipse,
                BoardShapeKind.Arrow => BoardTool.Arrow,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知形状种类。"),
            };
        }

        public BoardTool Id { get; }

        /// <summary>当前活动形状（未提交）；无会话时为 null。</summary>
        public BoardShape? ActiveShape { get; private set; }

        /// <summary>
        /// 最近一次 <see cref="End"/> 提交成功的形状（退化几何被丢弃时为 null）。
        /// </summary>
        /// <remarks>
        /// 供控制器在提交后把结果抛给宿主（控件/主窗口），工具自身不切工具、不设置选中。
        /// </remarks>
        public BoardShape? LastCommittedShape { get; private set; }

        public void Begin(in ToolInput input)
        {
            BoardInputContext context = input.Context;
            // 防御：清理上一会话可能残留的增量脏矩形（正常路径 End/Cancel 已清理）。
            context.ClearStrokeDirtyRect();

            // 参数快照：仅在 Begin 读取一次并值拷贝进形状（"仅影响后续新建形状"）；
            // 压感忽略（R6）：不读取 input.Pressure。
            ToolOptions options = context.ToolOptions;
            var shape = new BoardShape(_kind)
            {
                Color = options.PenColor,
                Width = options.PenBaseSize,
            };

            Vector2 startWorld = context.Viewport.ScreenToWorld(input.PositionScreenDip);
            shape.SetGeometry(startWorld, startWorld);

            ActiveShape = shape;
            context.PreviewItem = shape;
            context.InvalidateFrame();
        }

        public void Move(in ToolInput input)
        {
            if (ActiveShape is null)
            {
                return;
            }

            BoardInputContext context = input.Context;
            Vector2 posWorld = context.Viewport.ScreenToWorld(input.PositionScreenDip);

            Rect oldBoundsWorld = ActiveShape.BoundsWorld;
            ActiveShape.SetGeometry(ActiveShape.Start, posWorld);

            UpdatePendingDirtyRect(context, oldBoundsWorld, ActiveShape.BoundsWorld);
            context.InvalidateFrame();
        }

        public void End(in ToolInput input)
        {
            // 每次提交会话先清空上一次结果：退化几何走丢弃分支时保持 null（宿主据此不自动选中）。
            LastCommittedShape = null;

            BoardShape? shape = ActiveShape;
            if (shape is not null && !IsDegenerate(shape))
            {
                input.Context.Session.Execute(new AddInkItemCommand(shape));
                LastCommittedShape = shape;
            }

            ActiveShape = null;
            input.Context.PreviewItem = null;
            input.Context.ClearStrokeDirtyRect();
        }

        public void Cancel()
        {
            // 预览挂载点与脏矩形由控制器在调度 Cancel 后统一清理（Cancel 无输入参数）。
            ActiveShape = null;
        }

        private static bool IsDegenerate(BoardShape shape)
        {
            return (shape.End - shape.Start).LengthSquared() < DegenerateLengthWorld * DegenerateLengthWorld;
        }

        /// <summary>
        /// 请求"旧 ∪ 新几何 AABB"的增量脏矩形（屏幕 DIP）。
        /// </summary>
        /// <remarks>
        /// 形状 Move 是"整体几何替换"而非逐点追加（与 PenTool 的线段增量不同）：
        /// 端点可能回退，因此新矩形必须与旧矩形取并集，避免旧位置残影。
        /// BoundsWorld 已含线宽半宽外扩，屏幕化后仅再叠加额外 padding。
        /// </remarks>
        private static void UpdatePendingDirtyRect(BoardInputContext context, Rect oldBoundsWorld, Rect newBoundsWorld)
        {
            float zoom = context.Viewport.Zoom;
            if (zoom <= 0.0001f)
            {
                return;
            }

            Rect? updated = context.PeekStrokeDirtyRect();
            updated = UnionScreenRect(updated, ToScreenRect(oldBoundsWorld, context.Viewport));
            updated = UnionScreenRect(updated, ToScreenRect(newBoundsWorld, context.Viewport));
            context.RequestStrokeDirtyRect(updated);
        }

        private static Rect ToScreenRect(Rect boundsWorld, BoardViewport viewport)
        {
            Matrix3x2 worldToScreen = viewport.GetWorldToScreenTransform();
            Vector2 topLeft = Vector2.Transform(new Vector2(boundsWorld.Left, boundsWorld.Top), worldToScreen);
            Vector2 bottomRight = Vector2.Transform(new Vector2(boundsWorld.Right, boundsWorld.Bottom), worldToScreen);
            return Rect.FromLTRB(
                Math.Min(topLeft.X, bottomRight.X),
                Math.Min(topLeft.Y, bottomRight.Y),
                Math.Max(topLeft.X, bottomRight.X),
                Math.Max(topLeft.Y, bottomRight.Y));
        }

        private static Rect? UnionScreenRect(Rect? existing, Rect rectDip)
        {
            Rect padded = Rect.FromLTRB(
                rectDip.Left - DirtyRectExtraDip,
                rectDip.Top - DirtyRectExtraDip,
                rectDip.Right + DirtyRectExtraDip,
                rectDip.Bottom + DirtyRectExtraDip);

            if (existing is Rect e)
            {
                return Rect.FromLTRB(
                    Math.Min(e.Left, padded.Left),
                    Math.Min(e.Top, padded.Top),
                    Math.Max(e.Right, padded.Right),
                    Math.Max(e.Bottom, padded.Bottom));
            }

            return padded;
        }
    }
}
