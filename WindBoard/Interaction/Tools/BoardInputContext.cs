using System;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 工具运行上下文：工具与外部环境（视口/文档/会话/渲染通知/绘制参数）的唯一通信面。
    /// </summary>
    /// <remarks>
    /// - 由 <see cref="WindBoard.Interaction.BoardInputController"/> 创建并持有，
    ///   与控制器生命周期一致（会话切换时随控制器重建）；
    /// - <see cref="PreviewItem"/>：预览项挂载点，活动笔迹等"未提交"条目经此暴露给渲染层，
    ///   语义与原 <c>BoardInputController.ActiveStroke</c> 等价；
    /// - 脏矩形：增量书写时的局部重绘区域经此暂存，由渲染层消费；
    /// - <see cref="ToolOptions"/>：绘制参数挂载点（design C 节），参数仅在工具 Begin 时读取一次。
    /// </remarks>
    internal sealed class BoardInputContext
    {
        private Rect? _pendingStrokeDirtyRect;

        public BoardInputContext(BoardViewport viewport, BoardSession session)
        {
            Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>视口（屏幕/世界坐标换算、缩放）。</summary>
        public BoardViewport Viewport { get; }

        /// <summary>编辑会话（命令提交入口：保证撤销/重做一致）。</summary>
        public BoardSession Session { get; }

        /// <summary>当前文档（工具直接修改的快照/选中操作仍须经命令栈，见规范约定）。</summary>
        public BoardDocument Document => Session.Document;

        /// <summary>
        /// 绘制参数（工具/颜色/粗细/压感）挂载点。
        /// </summary>
        /// <remarks>
        /// 仅影响后续新建笔迹：工具在 Begin 时读取一次并值拷贝进新建条目，Move 不重读。
        /// </remarks>
        public ToolOptions ToolOptions { get; set; } = new(
            BoardTool.Pen, new Color4(0f, 0f, 0f, 1f), 3.0f, true);

        /// <summary>
        /// 预览项挂载点：当前工具的未提交绘制条目（渲染层绘制"活动笔迹"的数据源）。
        /// </summary>
        /// <remarks>
        /// 工具在 <see cref="IBoardTool.Begin"/> 时写入、End/Cancel 时清除；
        /// 控制器在调度 Cancel 后兜底清除。
        /// </remarks>
        public IBoardInkItem? PreviewItem { get; set; }

        /// <summary>工具请求重绘（等价于原 FrameInvalidated 通知，经控制器转发到渲染层）。</summary>
        public event Action? FrameInvalidated;

        /// <summary>工具触发重绘（仅供工具/控制器调用）。</summary>
        internal void InvalidateFrame() => FrameInvalidated?.Invoke();

        /// <summary>读取当前暂存的增量脏矩形（屏幕 DIP 坐标）。</summary>
        public Rect? PeekStrokeDirtyRect() => _pendingStrokeDirtyRect;

        /// <summary>写入/覆盖增量脏矩形（屏幕 DIP 坐标；传 null 等价于清除）。</summary>
        public void RequestStrokeDirtyRect(Rect? rectDip) => _pendingStrokeDirtyRect = rectDip;

        /// <summary>清除增量脏矩形。</summary>
        public void ClearStrokeDirtyRect() => _pendingStrokeDirtyRect = null;

        /// <summary>
        /// 渲染层消费增量脏矩形（取走后即清空，返回 false 表示当前无请求）。
        /// </summary>
        public bool TryConsumeStrokeDirtyRect(out Rect rectDip)
        {
            if (_pendingStrokeDirtyRect is Rect rect)
            {
                _pendingStrokeDirtyRect = null;
                rectDip = rect;
                return true;
            }

            rectDip = default;
            return false;
        }
    }
}
