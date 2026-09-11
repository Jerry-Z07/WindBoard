using System.Collections.Generic;
using Microsoft.UI.Input;

namespace WindBoard.Interaction
{
    /// <summary>
    /// 触摸手势的操纵目标：视口（多指拖动/捏合缩放画布）或已选中内容（拖动/变换选中集）。
    /// </summary>
    /// <remarks>
    /// 单指按下时由控制器依据命中位置判定；选择手势取消或触摸手势结束时复位为 Viewport。
    /// </remarks>
    internal enum TouchManipulationTarget
    {
        Viewport,
        Selection,
    }

    /// <summary>移动事件的路由结果（优先级与 OnCanvasPointerMoved 的分支顺序一致）。</summary>
    internal enum PointerMoveRoute
    {
        None,
        Pan,
        Selection,
        Marquee,
        Active,
    }

    /// <summary>
    /// 指针路由跟踪状态机：跟踪画线/擦除会话、右键平移、选择移动、框选四类手势的
    /// pointerId 分配/互斥/释放，以及触摸触点集合与触摸手势目标。
    /// </summary>
    /// <remarks>
    /// 纯状态容器（不依赖 WinUI 控件与事件参数，测试环境可直接构造），由
    /// <see cref="BoardInputController"/> 持有、事件处理器只做转发；独立成类使
    /// pointerId 的分配、互斥与释放时序可被单元测试覆盖。
    /// 各 Begin/End 方法与控制器原内联赋值一一对应，不做额外校验（互斥由控制器
    /// 在按下路径经 <see cref="HasActivePointerCapture"/> 闸门保证）。
    /// </remarks>
    internal sealed class PointerRouteState
    {
        /// <summary>画线/擦除会话的 pointerId（按下捕获时分配，提交/取消/捕获丢失共用同一清理转移）。</summary>
        public uint? ActivePointerId { get; private set; }

        /// <summary>活动会话的来源设备类型（与 <see cref="ActivePointerId"/> 同生命周期）。</summary>
        public PointerDeviceType? ActiveStrokeDeviceType { get; private set; }

        /// <summary>右键平移手势的 pointerId。</summary>
        public uint? PanPointerId { get; private set; }

        /// <summary>选择移动手势的 pointerId。</summary>
        public uint? SelectionPointerId { get; private set; }

        /// <summary>框选手势的 pointerId。</summary>
        public uint? MarqueePointerId { get; private set; }

        /// <summary>多指视口手势进行中（ManipulationStarting/Delta 置位，ManipulationCompleted 复位）。</summary>
        public bool IsManipulating { get; set; }

        /// <summary>多指选中内容变换手势进行中。</summary>
        public bool IsManipulatingSelection { get; set; }

        /// <summary>触摸手势目标（单指按下时判定；选择取消/触摸手势结束复位）。</summary>
        public TouchManipulationTarget TouchManipulationTarget { get; set; } = TouchManipulationTarget.Viewport;

        /// <summary>当前按下的触摸触点 pointerId 集合（达到 2 个即进入 Manipulation 通道）。</summary>
        public HashSet<uint> ActiveTouchPointers { get; } = new();

        /// <summary>是否存在任一 pointerId 手势（平移/选择移动/框选）。</summary>
        public bool HasPointerGesture => PanPointerId is not null || SelectionPointerId is not null || MarqueePointerId is not null;

        /// <summary>按下路径的互斥闸门：已有画线/擦除会话或任一 pointerId 手势时忽略新按下。</summary>
        public bool HasActivePointerCapture => ActivePointerId is not null || HasPointerGesture;

        /// <summary>视口连续手势进行中（右键平移或多指视口手势）。</summary>
        public bool HasViewportGesture => PanPointerId is not null || IsManipulating;

        /// <summary>选择连续手势进行中（选择移动/多指选中变换/框选）。</summary>
        public bool HasSelectionGesture => SelectionPointerId is not null || IsManipulatingSelection || MarqueePointerId is not null;

        /// <summary>开始画线/擦除会话（分配活动 pointerId 与设备类型）。</summary>
        public void BeginActiveStroke(uint pointerId, PointerDeviceType deviceType)
        {
            ActivePointerId = pointerId;
            ActiveStrokeDeviceType = deviceType;
        }

        /// <summary>结束画线/擦除会话（抬起提交/取消/捕获丢失共用；仅清理 pointerId 与设备类型）。</summary>
        public void EndActiveStroke()
        {
            ActivePointerId = null;
            ActiveStrokeDeviceType = null;
        }

        /// <summary>开始右键平移手势。</summary>
        public void BeginPan(uint pointerId)
        {
            PanPointerId = pointerId;
        }

        /// <summary>
        /// 结束平移手势：pointerId 匹配时清理并返回 true；不匹配返回 false 且状态不变
        /// （与原 TryHandlePanPointerEnded 的“尝试”语义一致）。
        /// </summary>
        public bool TryEndPan(uint pointerId)
        {
            if (PanPointerId != pointerId)
            {
                return false;
            }

            PanPointerId = null;
            return true;
        }

        /// <summary>外部取消平移手势（不校验 pointerId，与外部取消路径一致）。</summary>
        public void CancelPan()
        {
            PanPointerId = null;
        }

        /// <summary>开始选择移动手势。</summary>
        public void BeginSelectionMove(uint pointerId)
        {
            SelectionPointerId = pointerId;
        }

        /// <summary>提交选择移动（清理选择 pointerId 与选中变换标志）。</summary>
        public void EndSelectionMove()
        {
            SelectionPointerId = null;
            IsManipulatingSelection = false;
        }

        /// <summary>取消选择移动（在提交清理的基础上复位触摸手势目标）。</summary>
        public void CancelSelectionMove()
        {
            EndSelectionMove();
            TouchManipulationTarget = TouchManipulationTarget.Viewport;
        }

        /// <summary>开始框选手势。</summary>
        public void BeginMarquee(uint pointerId)
        {
            MarqueePointerId = pointerId;
        }

        /// <summary>结束/取消框选手势。</summary>
        public void EndMarquee()
        {
            MarqueePointerId = null;
        }

        /// <summary>
        /// 移动事件路由：优先级与原 OnCanvasPointerMoved 的分支顺序一致
        /// （平移 → 选择移动 → 框选 → 画线/擦除会话）。
        /// </summary>
        public PointerMoveRoute ResolveMoveRoute(uint pointerId)
        {
            if (PanPointerId == pointerId)
            {
                return PointerMoveRoute.Pan;
            }

            if (SelectionPointerId == pointerId)
            {
                return PointerMoveRoute.Selection;
            }

            if (MarqueePointerId == pointerId)
            {
                return PointerMoveRoute.Marquee;
            }

            if (ActivePointerId == pointerId)
            {
                return PointerMoveRoute.Active;
            }

            return PointerMoveRoute.None;
        }
    }
}
