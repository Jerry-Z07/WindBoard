using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 橡皮工具：擦除运行态（进行中标记、上一次位置、擦除前快照）内聚，
    /// 内部继续使用 <see cref="IBoardEraser"/> 策略执行实际擦除（design B 节）。
    /// </summary>
    /// <remarks>
    /// 原 <c>BoardInputController</c> 的 Begin/Update/Commit/CancelEraserGesture 逻辑迁移至此；
    /// 指针捕获与活动 pointerId 跟踪仍由控制器负责。
    /// 提交经 <see cref="ReplaceStrokesCommand"/>（快照前后对比后单条命令），撤销/重做语义不变。
    /// </remarks>
    internal sealed class EraserTool : IBoardTool
    {
        /// <summary>相邻擦除采样点的最小距离（缩放为 1 时的世界坐标），低于该距离跳过。</summary>
        private const float MinMoveDistanceWorldBase = 0.75f;

        private IBoardEraser _eraser;
        private Vector2? _lastEraserWorld;
        private List<IBoardInkItem>? _eraseBeforeSnapshot;

        // Begin 时缓存的文档引用：Cancel 无输入参数，恢复快照需要文档访问入口。
        private BoardDocument? _document;

        public BoardTool Id => BoardTool.Eraser;

        /// <summary>是否正在进行擦除会话。</summary>
        public bool IsErasing { get; private set; }

        /// <summary>
        /// 橡皮擦半径（DIP）：X/Y 分量分别表示水平/垂直半径。
        /// </summary>
        /// <remarks>
        /// 该值需要与擦除光标的视觉尺寸保持一致（由控制器从光标控件尺寸同步）。
        /// </remarks>
        public Vector2 RadiusDip { get; set; } = new(24.0f, 36.0f);

        public EraserTool(IBoardEraser? eraser = null)
        {
            // 默认使用“像素级擦除”（局部擦除），更符合常见橡皮擦体验。
            _eraser = eraser ?? new PixelStrokeEraser();
        }

        /// <summary>擦除策略（控制器在 UI 切换整笔/像素擦除时替换）。</summary>
        public IBoardEraser Eraser
        {
            get => _eraser;
            set => _eraser = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void Begin(in ToolInput input)
        {
            BoardInputContext context = input.Context;

            // 记录擦除前的快照：整笔擦除与未来局部擦除都可以复用这套“前后快照 + 单条命令”机制。
            _document = context.Document;
            _eraseBeforeSnapshot = new List<IBoardInkItem>(context.Document.InkItems);
            IsErasing = true;
            context.ClearStrokeDirtyRect();

            Vector2 world = context.Viewport.ScreenToWorld(input.PositionScreenDip);
            _lastEraserWorld = world;

            ApplyEraserSegment(context, world, world);
        }

        public void Move(in ToolInput input)
        {
            if (!IsErasing)
            {
                return;
            }

            Vector2 currentWorld = input.Context.Viewport.ScreenToWorld(input.PositionScreenDip);

            if (_lastEraserWorld is not Vector2 lastWorld)
            {
                _lastEraserWorld = currentWorld;
                ApplyEraserSegment(input.Context, currentWorld, currentWorld);
                return;
            }

            float minDistWorld = MinMoveDistanceWorldBase / Math.Max(0.0001f, input.Context.Viewport.Zoom);
            if (Vector2.DistanceSquared(lastWorld, currentWorld) < minDistWorld * minDistWorld)
            {
                return;
            }

            _lastEraserWorld = currentWorld;
            ApplyEraserSegment(input.Context, lastWorld, currentWorld);
        }

        public void End(in ToolInput input)
        {
            if (!IsErasing)
            {
                return;
            }

            List<IBoardInkItem>? before = _eraseBeforeSnapshot;
            _eraseBeforeSnapshot = null;
            _document = null;
            IsErasing = false;
            _lastEraserWorld = null;

            if (before is not null)
            {
                var after = new List<IBoardInkItem>(input.Context.Document.InkItems);
                if (!InkItemListComparer.IsSameList(before, after))
                {
                    input.Context.Session.Execute(new ReplaceStrokesCommand(before, after));
                }
            }
        }

        public void Cancel()
        {
            if (!IsErasing)
            {
                return;
            }

            // 系统取消/外部打断时：恢复擦除前快照，不写入撤销栈，避免产生“半截”历史。
            if (_eraseBeforeSnapshot is not null && _document is not null)
            {
                _document.InkItems.Clear();
                _document.InkItems.AddRange(_eraseBeforeSnapshot);
            }

            _eraseBeforeSnapshot = null;
            _document = null;
            IsErasing = false;
            _lastEraserWorld = null;
        }

        /// <summary>对“橡皮擦轨迹线段”执行一次擦除；文档变化时请求重绘。</summary>
        private void ApplyEraserSegment(BoardInputContext context, Vector2 fromWorld, Vector2 toWorld)
        {
            float zoom = Math.Max(0.0001f, context.Viewport.Zoom);
            Vector2 radiusWorld = RadiusDip / zoom;

            if (_eraser.Erase(context.Document, fromWorld, toWorld, radiusWorld))
            {
                context.InvalidateFrame();
            }
        }
    }
}
