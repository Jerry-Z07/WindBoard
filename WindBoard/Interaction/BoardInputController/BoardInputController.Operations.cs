using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Board;
using WindBoard.Board.Elements;
using WindBoard.Board.Items;
using WindBoard.Interaction.Tools;
using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    /// <summary>
    /// 输入控制器：工具操作（笔迹/擦除/选择）与状态更新相关代码。
    /// </summary>
    internal sealed partial class BoardInputController
    {
        /// <summary>
        /// 取消当前工具会话（不提交命令，恢复会话前状态）。
        /// </summary>
        /// <remarks>
        /// 工具策略化调度：预览挂载点与脏矩形由控制器在调度 Cancel 后统一清理
        /// （<see cref="IBoardTool.Cancel"/> 无输入参数）。
        /// </remarks>
        private void DiscardActiveToolGesture()
        {
            if (ActiveItem is null && _routes.ActivePointerId is null && _routes.ActiveStrokeDeviceType is null)
            {
                return;
            }

            // 与按下路径统一经 ResolveActiveToolId 解析（Select 回退画笔语义一致）。
            if (_toolRegistry.TryGetTool(ResolveActiveToolId(), out IBoardTool? tool))
            {
                tool.Cancel();
            }

            _context.PreviewItem = null;
            _context.ClearStrokeDirtyRect();
            _routes.EndActiveStroke();
            FinalizeGestureState();
        }

        /// <summary>
        /// 提交当前工具会话（写入撤销栈）。
        /// </summary>
        /// <remarks>
        /// 画笔提交活动笔迹、橡皮提交擦除快照；End 的输入不携带指针信息（提交不依赖坐标），
        /// 传默认 ToolInput 即可。形状提交成功后在手势状态清理之后经 <see cref="ShapeCommitted"/> 抛出结果。
        /// </remarks>
        private void CommitActiveToolGesture()
        {
            // 与按下路径统一经 ResolveActiveToolId 解析（Select 回退画笔语义一致）。
            if (_toolRegistry.TryGetTool(ResolveActiveToolId(), out IBoardTool? tool))
            {
                tool.End(CreatePointerlessToolInput());
            }

            _routes.EndActiveStroke();
            FinalizeGestureState();

            // 形状提交结果在状态清理之后抛给宿主：宿主响应时会切到选择工具（内部经
            // CancelActiveToolOperation 重入），此时手势状态已清理，重入为 no-op，不会重复释放指针捕获。
            if (tool is ShapeTool shapeTool && shapeTool.LastCommittedShape is BoardShape shape)
            {
                ShapeCommitted?.Invoke(shape);
            }
        }

        /// <summary>构造不携带指针事件的输入（用于强制提交/取消等无 PointerPoint 的调度路径）。</summary>
        private ToolInput CreatePointerlessToolInput()
        {
            return new ToolInput(default, 0f, default, _context);
        }

        /// <summary>构造一次指针输入快照（压感在路由层归一化）。</summary>
        private ToolInput CreateToolInput(Pointer pointer, PointerPoint point)
        {
            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            float pressure = PointerRoutingDecisions.NormalizePressure(pointer.PointerDeviceType, point.Properties.Pressure);
            return new ToolInput(screen, pressure, pointer.PointerDeviceType, _context);
        }

        public void CancelActiveToolOperation()
        {
            // 外部操作（例如工具切换/撤销/重做/清空）前，用于安全结束当前工具动作，避免留下捕获/状态。
            // 画笔/橡皮会话互斥性保证不会与 pan/marquee/selection 同时存在，
            // 未命中的分支落到末尾的 DiscardActiveToolGesture（无会话时静默返回，保持原早退语义）。
            if (_routes.MarqueePointerId is not null)
            {
                CancelMarqueeSelectionGesture(releasePointerCaptures: true);
                return;
            }

            if (_routes.SelectionPointerId is not null || _routes.IsManipulatingSelection)
            {
                CancelSelectionGesture();
                return;
            }

            if (_routes.PanPointerId is not null)
            {
                CancelPanGesture();
                return;
            }

            DiscardActiveToolGesture();
        }

        private void CancelPanGesture()
        {
            _routes.CancelPan();
            _pendingPanScreenDelta = Vector2.Zero;
            FinalizeGestureState();
        }

        private void BeginMarqueeSelectionGesture(Pointer pointer, Vector2 startScreenDip)
        {
            _panel.CapturePointer(pointer);
            _routes.BeginMarquee(pointer.PointerId);

            // 框选几何状态由 SelectTool 内聚；这里只负责捕获与 pointerId 跟踪。
            _selectTool.Begin(new ToolInput(startScreenDip, 1.0f, pointer.PointerDeviceType, _context));

            NotifyInteractionUiChanged();
        }

        private void CommitMarqueeSelectionGesture(bool releasePointerCaptures)
        {
            uint? id = _routes.MarqueePointerId;
            if (id is null)
            {
                return;
            }

            _routes.EndMarquee();

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            // 框选命中/点击选中由 SelectTool 内聚处理（含点击阈值判定与命中测试）。
            _selectTool.End(CreatePointerlessToolInput());

            // 元素双击：外部打开（仅在“点击”路径触发，框选不会触发）。
            // 命中元素与点击起点坐标由 SelectTool.End 记录。
            if (_selectTool.LastMarqueeClickedElement is BoardElement clickedElement)
            {
                HandleElementClickForMaybeOpen(clickedElement, _selectTool.LastMarqueeClickScreenDip);
            }

            NotifyInteractionUiChanged();
        }

        private void CancelMarqueeSelectionGesture(bool releasePointerCaptures)
        {
            if (_routes.MarqueePointerId is null)
            {
                return;
            }

            _routes.EndMarquee();

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            _selectTool.Cancel();

            NotifyInteractionUiChanged();
        }

        private void CommitSelectionGesture(bool releasePointerCaptures)
        {
            _routes.EndSelectionMove();

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            // 快照清理与撤销命令提交由 SelectTool 内聚处理。
            _selectTool.CommitSelection();

            NotifyInteractionUiChanged();
        }

        private void CancelSelectionGesture(bool releasePointerCaptures = true)
        {
            // 先恢复快照（由 SelectTool 处理），再清理控制器手势状态。
            _selectTool.CancelSelection();

            _routes.CancelSelectionMove();

            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            NotifyInteractionUiChanged();
        }

        private void FinalizeGestureState(bool releasePointerCaptures = true, bool notifyStateChanged = true)
        {
            if (releasePointerCaptures)
            {
                _panel.ReleasePointerCaptures();
            }

            NotifyInteractionUiChanged(notifyStateChanged);
        }

        private void NotifyInteractionUiChanged(bool notifyStateChanged = true)
        {
            UpdateInteractionState();
            FrameInvalidated?.Invoke();

            if (notifyStateChanged)
            {
                StateChanged?.Invoke();
            }
        }

        private void UpdateInteractionState()
        {
            bool isInteracting = HasActiveToolInteraction || HasViewportGesture || HasSelectionGesture || _isWheelZooming;

            if (_isInteracting == isInteracting)
            {
                return;
            }

            _isInteracting = isInteracting;
            InteractionStateChanged?.Invoke(isInteracting);
        }
    }
}
