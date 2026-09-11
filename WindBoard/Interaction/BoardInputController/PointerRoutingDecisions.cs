using System;
using Microsoft.UI.Input;

namespace WindBoard.Interaction
{
    /// <summary>触摸按下事件的路由决策（分支顺序与 HandleTouchPointerPressed 一致）。</summary>
    internal enum TouchPressRoute
    {
        /// <summary>多指：结束单指操作（若允许）并交给 Manipulation 通道处理缩放/拖动。</summary>
        MultiFinger,

        /// <summary>选择模式单指：框选，或对已选中内容做手势（目标判定由控制器完成）。</summary>
        SelectGesture,

        /// <summary>已有画线/擦除会话或手势：忽略本次按下（不置 Handled，与原早退一致）。</summary>
        Ignore,

        /// <summary>单指画线/擦除。</summary>
        PenOrEraser,
    }

    /// <summary>多指手势打断单指操作时的处置决策（与 EndTouchSingleFingerToolOperationForManipulation 一致）。</summary>
    internal enum TouchGestureEndAction
    {
        /// <summary>无需要处置的触摸来源操作（框选取消由控制器按 MarqueePointerId 另行处理）。</summary>
        None,

        /// <summary>单点笔迹（≤1 个点）：丢弃，避免手势留下点状笔迹。</summary>
        DiscardDotStroke,

        /// <summary>多段笔迹：提交，避免已画轨迹丢失。</summary>
        CommitStroke,

        /// <summary>形状预览被打断：半截形状无意义，直接丢弃（不提交）。</summary>
        DiscardShape,

        /// <summary>擦除会话：提交擦除结果。</summary>
        CommitEraser,
    }

    /// <summary>滚轮缩放空闲检测定时器 tick 的决策。</summary>
    internal enum WheelZoomTickDecision
    {
        /// <summary>未处于滚轮缩放状态：停止定时器（空闲保护）。</summary>
        StopIdle,

        /// <summary>距最后一次滚轮事件不足空闲超时：继续等待（延迟合并连续滚动）。</summary>
        Wait,

        /// <summary>滚轮缩放空闲超时：结束缩放状态。</summary>
        End,
    }

    /// <summary>
    /// 指针路由与输入判定的纯决策函数集合：仅接收原始数据（设备类型/按键/触点数/时间戳），
    /// 不依赖 WinUI 控件与事件参数（PointerRoutedEventArgs 等在测试中不可构造），
    /// 由 <see cref="BoardInputController"/> 的事件处理器提取原始数据后转发调用。
    /// </summary>
    internal static class PointerRoutingDecisions
    {
        /// <summary>滚轮缩放空闲超时（毫秒）：超过该时长无滚轮事件则结束缩放状态。</summary>
        internal const int WheelZoomIdleTimeoutMs = 150;

        /// <summary>滚轮缩放空闲检测定时器间隔（毫秒）。</summary>
        internal const int WheelZoomTimerIntervalMs = 50;

        /// <summary>
        /// 是否开始绘制笔迹：鼠标要求左键按下；触控笔/触摸默认允许。
        /// </summary>
        internal static bool ShouldStartStroke(PointerDeviceType deviceType, bool isLeftButtonPressed)
        {
            if (deviceType == PointerDeviceType.Mouse)
            {
                return isLeftButtonPressed;
            }

            // 触控笔 / 触摸：默认允许
            return true;
        }

        /// <summary>
        /// 是否开始右键平移：仅鼠标右键且允许视口操作。
        /// </summary>
        internal static bool ShouldStartPan(bool allowViewportManipulation, PointerDeviceType deviceType, bool isRightButtonPressed)
        {
            if (!allowViewportManipulation)
            {
                return false;
            }

            if (deviceType != PointerDeviceType.Mouse)
            {
                return false;
            }

            return isRightButtonPressed;
        }

        /// <summary>
        /// 压感归一化：触控笔收敛到 [0.1, 1]（避免零压力导致笔迹消失）；鼠标/触摸固定 1.0。
        /// </summary>
        internal static float NormalizePressure(PointerDeviceType deviceType, float pressure)
        {
            if (deviceType != PointerDeviceType.Pen)
            {
                return 1.0f;
            }

            return Math.Clamp(pressure, 0.1f, 1.0f);
        }

        /// <summary>
        /// 触摸按下路由（分支顺序与 HandleTouchPointerPressed 一致；触点计数为本次按下加入后的数量）。
        /// </summary>
        internal static TouchPressRoute ResolveTouchPressRoute(
            int activeTouchPointerCount,
            bool isSelectTool,
            bool allowSelectionInteractions,
            bool hasActivePointerCapture)
        {
            // 多指触摸：交给 Manipulation 处理缩放/拖动（单指操作是否结束由控制器按视口开关决定）。
            if (activeTouchPointerCount >= 2)
            {
                return TouchPressRoute.MultiFinger;
            }

            if (isSelectTool && allowSelectionInteractions)
            {
                // 选择模式：单指用于“框选”；双指/多指用于视口手势或对已选中笔迹做变换。
                return TouchPressRoute.SelectGesture;
            }

            if (hasActivePointerCapture)
            {
                return TouchPressRoute.Ignore;
            }

            return TouchPressRoute.PenOrEraser;
        }

        /// <summary>
        /// 多指手势打断单指操作的处置决策（分支顺序与 EndTouchSingleFingerToolOperationForManipulation 一致；
        /// 框选取消不在此决策内，由控制器按 MarqueePointerId 另行处理）。
        /// </summary>
        /// <param name="isStrokeActiveItem">活动预览条目是否为笔迹。</param>
        /// <param name="strokePointCount">笔迹点数（仅 isStrokeActiveItem 时有效）。</param>
        /// <param name="hasOtherActiveItem">活动预览条目是否为其它类型（形状）。</param>
        /// <param name="isErasing">擦除会话是否进行中。</param>
        /// <param name="isTouchOrigin">活动会话是否为触摸来源（ActiveStrokeDeviceType == Touch）。</param>
        internal static TouchGestureEndAction ResolveTouchGestureEnd(
            bool isStrokeActiveItem,
            int strokePointCount,
            bool hasOtherActiveItem,
            bool isErasing,
            bool isTouchOrigin)
        {
            // 非触摸来源（鼠标/触控笔）的会话不被触摸手势打断。
            if (!isTouchOrigin)
            {
                return TouchGestureEndAction.None;
            }

            if (isStrokeActiveItem)
            {
                // 两指及以上时视为手势：如果只是按下的“单点”，不要留下点状笔迹。
                return strokePointCount <= 1
                    ? TouchGestureEndAction.DiscardDotStroke
                    : TouchGestureEndAction.CommitStroke;
            }

            if (hasOtherActiveItem)
            {
                // 形状预览被手势打断：半截形状无意义，直接丢弃（不提交）。
                return TouchGestureEndAction.DiscardShape;
            }

            if (isErasing)
            {
                return TouchGestureEndAction.CommitEraser;
            }

            return TouchGestureEndAction.None;
        }

        /// <summary>
        /// 滚轮缩放空闲检测 tick 决策（判定顺序与 OnWheelZoomTimerTick 一致）：
        /// 未缩放时停表；距最后一次滚轮不足空闲超时时继续等待，否则结束缩放状态。
        /// </summary>
        internal static WheelZoomTickDecision EvaluateWheelZoomTick(
            bool isWheelZooming,
            DateTimeOffset now,
            DateTimeOffset lastWheelZoomAt)
        {
            if (!isWheelZooming)
            {
                return WheelZoomTickDecision.StopIdle;
            }

            if ((now - lastWheelZoomAt).TotalMilliseconds < WheelZoomIdleTimeoutMs)
            {
                return WheelZoomTickDecision.Wait;
            }

            return WheelZoomTickDecision.End;
        }
    }
}
