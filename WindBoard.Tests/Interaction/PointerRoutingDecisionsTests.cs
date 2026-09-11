using System;
using Microsoft.UI.Input;
using WindBoard.Interaction;
using Xunit;

namespace WindBoard.Tests.Interaction;

/// <summary>
/// 指针路由纯决策函数用例：按键判定、压感归一化、触摸按下与打断路由、滚轮缩放节流。
/// </summary>
public sealed class PointerRoutingDecisionsTests
{
    [Fact]
    public void ShouldStartStroke_Mouse_RequiresLeftButton()
    {
        Assert.True(PointerRoutingDecisions.ShouldStartStroke(PointerDeviceType.Mouse, isLeftButtonPressed: true));
        Assert.False(PointerRoutingDecisions.ShouldStartStroke(PointerDeviceType.Mouse, isLeftButtonPressed: false));
    }

    [Fact]
    public void ShouldStartStroke_PenAndTouch_AllowWithoutButton()
    {
        // 触控笔/触摸不依赖按键状态，默认允许落笔。
        Assert.True(PointerRoutingDecisions.ShouldStartStroke(PointerDeviceType.Pen, isLeftButtonPressed: false));
        Assert.True(PointerRoutingDecisions.ShouldStartStroke(PointerDeviceType.Touch, isLeftButtonPressed: false));
    }

    [Fact]
    public void ShouldStartPan_MouseRightButton_StartsPan()
    {
        Assert.True(PointerRoutingDecisions.ShouldStartPan(allowViewportManipulation: true, PointerDeviceType.Mouse, isRightButtonPressed: true));
    }

    [Fact]
    public void ShouldStartPan_BoundaryConditions_RejectNonPanStarts()
    {
        // 视口操作被禁用时，即使右键也不平移。
        Assert.False(PointerRoutingDecisions.ShouldStartPan(allowViewportManipulation: false, PointerDeviceType.Mouse, isRightButtonPressed: true));
        // 非右键不平移。
        Assert.False(PointerRoutingDecisions.ShouldStartPan(allowViewportManipulation: true, PointerDeviceType.Mouse, isRightButtonPressed: false));
        // 平移仅限鼠标：触控笔/触摸交给画线与触摸手势通道。
        Assert.False(PointerRoutingDecisions.ShouldStartPan(allowViewportManipulation: true, PointerDeviceType.Pen, isRightButtonPressed: true));
        Assert.False(PointerRoutingDecisions.ShouldStartPan(allowViewportManipulation: true, PointerDeviceType.Touch, isRightButtonPressed: true));
    }

    [Fact]
    public void NormalizePressure_NonPen_ReturnsOne()
    {
        AssertEx.Equal(1.0f, PointerRoutingDecisions.NormalizePressure(PointerDeviceType.Mouse, pressure: 0.0f));
        AssertEx.Equal(1.0f, PointerRoutingDecisions.NormalizePressure(PointerDeviceType.Touch, pressure: 0.7f));
    }

    [Fact]
    public void NormalizePressure_Pen_ClampsToUsableRange()
    {
        AssertEx.Equal(0.1f, PointerRoutingDecisions.NormalizePressure(PointerDeviceType.Pen, pressure: 0.0f));
        AssertEx.Equal(0.1f, PointerRoutingDecisions.NormalizePressure(PointerDeviceType.Pen, pressure: 0.05f));
        AssertEx.Equal(0.5f, PointerRoutingDecisions.NormalizePressure(PointerDeviceType.Pen, pressure: 0.5f));
        AssertEx.Equal(1.0f, PointerRoutingDecisions.NormalizePressure(PointerDeviceType.Pen, pressure: 1.0f));
        AssertEx.Equal(1.0f, PointerRoutingDecisions.NormalizePressure(PointerDeviceType.Pen, pressure: 1.5f));
    }

    [Fact]
    public void ResolveTouchPressRoute_SecondFinger_RoutesToManipulation()
    {
        Assert.Equal(
            TouchPressRoute.PenOrEraser,
            PointerRoutingDecisions.ResolveTouchPressRoute(1, isSelectTool: false, allowSelectionInteractions: true, hasActivePointerCapture: false));

        // 多指边界：自第 2 个触点起交给 Manipulation 通道（与工具/选择开关无关）。
        Assert.Equal(
            TouchPressRoute.MultiFinger,
            PointerRoutingDecisions.ResolveTouchPressRoute(2, isSelectTool: false, allowSelectionInteractions: true, hasActivePointerCapture: false));
        Assert.Equal(
            TouchPressRoute.MultiFinger,
            PointerRoutingDecisions.ResolveTouchPressRoute(3, isSelectTool: true, allowSelectionInteractions: true, hasActivePointerCapture: false));
    }

    [Fact]
    public void ResolveTouchPressRoute_SelectTool_SingleFingerUsesSelectGesture()
    {
        Assert.Equal(
            TouchPressRoute.SelectGesture,
            PointerRoutingDecisions.ResolveTouchPressRoute(1, isSelectTool: true, allowSelectionInteractions: true, hasActivePointerCapture: false));
    }

    [Fact]
    public void ResolveTouchPressRoute_SelectionDisabled_FallsThroughToCaptureGate()
    {
        // 禁用选择时单指回退为画线/擦除。
        Assert.Equal(
            TouchPressRoute.PenOrEraser,
            PointerRoutingDecisions.ResolveTouchPressRoute(1, isSelectTool: true, allowSelectionInteractions: false, hasActivePointerCapture: false));
        // 已有会话时被闸门拦截。
        Assert.Equal(
            TouchPressRoute.Ignore,
            PointerRoutingDecisions.ResolveTouchPressRoute(1, isSelectTool: true, allowSelectionInteractions: false, hasActivePointerCapture: true));
    }

    [Fact]
    public void ResolveTouchPressRoute_WhileOtherGestureActive_IgnoresPress()
    {
        Assert.Equal(
            TouchPressRoute.Ignore,
            PointerRoutingDecisions.ResolveTouchPressRoute(1, isSelectTool: false, allowSelectionInteractions: true, hasActivePointerCapture: true));
    }

    [Fact]
    public void ResolveTouchGestureEnd_SinglePointStroke_DiscardsDot()
    {
        // 边界：仅按下的“单点”（≤1 个点）不留点状笔迹。
        Assert.Equal(
            TouchGestureEndAction.DiscardDotStroke,
            PointerRoutingDecisions.ResolveTouchGestureEnd(isStrokeActiveItem: true, strokePointCount: 1, hasOtherActiveItem: false, isErasing: false, isTouchOrigin: true));
        Assert.Equal(
            TouchGestureEndAction.DiscardDotStroke,
            PointerRoutingDecisions.ResolveTouchGestureEnd(isStrokeActiveItem: true, strokePointCount: 0, hasOtherActiveItem: false, isErasing: false, isTouchOrigin: true));
    }

    [Fact]
    public void ResolveTouchGestureEnd_MultiPointStroke_Commits()
    {
        Assert.Equal(
            TouchGestureEndAction.CommitStroke,
            PointerRoutingDecisions.ResolveTouchGestureEnd(isStrokeActiveItem: true, strokePointCount: 2, hasOtherActiveItem: false, isErasing: false, isTouchOrigin: true));
    }

    [Fact]
    public void ResolveTouchGestureEnd_ShapePreview_Discards()
    {
        // 半截形状无意义，直接丢弃（不提交）。
        Assert.Equal(
            TouchGestureEndAction.DiscardShape,
            PointerRoutingDecisions.ResolveTouchGestureEnd(isStrokeActiveItem: false, strokePointCount: 0, hasOtherActiveItem: true, isErasing: false, isTouchOrigin: true));
    }

    [Fact]
    public void ResolveTouchGestureEnd_Erasing_Commits()
    {
        Assert.Equal(
            TouchGestureEndAction.CommitEraser,
            PointerRoutingDecisions.ResolveTouchGestureEnd(isStrokeActiveItem: false, strokePointCount: 0, hasOtherActiveItem: false, isErasing: true, isTouchOrigin: true));
    }

    [Fact]
    public void ResolveTouchGestureEnd_NonTouchOriginOrIdle_ReturnsNone()
    {
        // 非触摸来源（鼠标/触控笔）的会话不被触摸手势打断。
        Assert.Equal(
            TouchGestureEndAction.None,
            PointerRoutingDecisions.ResolveTouchGestureEnd(isStrokeActiveItem: true, strokePointCount: 2, hasOtherActiveItem: false, isErasing: false, isTouchOrigin: false));
        // 无任何活动操作。
        Assert.Equal(
            TouchGestureEndAction.None,
            PointerRoutingDecisions.ResolveTouchGestureEnd(isStrokeActiveItem: false, strokePointCount: 0, hasOtherActiveItem: false, isErasing: false, isTouchOrigin: true));
    }

    [Fact]
    public void EvaluateWheelZoomTick_NotZooming_StopsIdleTimer()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.Equal(WheelZoomTickDecision.StopIdle, PointerRoutingDecisions.EvaluateWheelZoomTick(isWheelZooming: false, now, now));
    }

    [Fact]
    public void EvaluateWheelZoomTick_WaitsWithinIdleTimeout_AndEndsAtBoundary()
    {
        DateTimeOffset lastWheel = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        // 空闲超时（150ms）内继续等待：连续滚动经 lastWheelZoomAt 刷新被合并为一次缩放交互。
        Assert.Equal(WheelZoomTickDecision.Wait, PointerRoutingDecisions.EvaluateWheelZoomTick(isWheelZooming: true, lastWheel.AddMilliseconds(149), lastWheel));

        // 边界：判定为“小于 150ms 才等待”，达到 150ms 即结束缩放状态。
        Assert.Equal(WheelZoomTickDecision.End, PointerRoutingDecisions.EvaluateWheelZoomTick(isWheelZooming: true, lastWheel.AddMilliseconds(150), lastWheel));
        Assert.Equal(WheelZoomTickDecision.End, PointerRoutingDecisions.EvaluateWheelZoomTick(isWheelZooming: true, lastWheel.AddMilliseconds(1000), lastWheel));
    }

    [Fact]
    public void EvaluateWheelZoomTick_ClockSkewBeforeLastWheel_Waits()
    {
        DateTimeOffset lastWheel = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        // 时钟回拨（now 早于 lastWheelZoomAt）按未超时处理，等待下一 tick。
        Assert.Equal(WheelZoomTickDecision.Wait, PointerRoutingDecisions.EvaluateWheelZoomTick(isWheelZooming: true, lastWheel.AddMilliseconds(-50), lastWheel));
    }

    [Fact]
    public void WheelZoomConstants_MatchThrottleContract()
    {
        // 延迟缩放合并的契约常量：空闲超时 150ms、空闲检测间隔 50ms。
        Assert.Equal(150, PointerRoutingDecisions.WheelZoomIdleTimeoutMs);
        Assert.Equal(50, PointerRoutingDecisions.WheelZoomTimerIntervalMs);
    }
}
