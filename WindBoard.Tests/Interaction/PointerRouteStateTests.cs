using Microsoft.UI.Input;
using WindBoard.Interaction;
using Xunit;

namespace WindBoard.Tests.Interaction;

/// <summary>
/// 指针路由状态机用例：pointerId 的分配、互斥闸门、移动路由优先级与释放时序
/// （抬起/取消/捕获丢失共用同一套清理转移）。
/// </summary>
public sealed class PointerRouteStateTests
{
    [Fact]
    public void BeginActiveStroke_AssignsPointerIdAndDeviceType()
    {
        var routes = new PointerRouteState();

        routes.BeginActiveStroke(7, PointerDeviceType.Pen);

        Assert.Equal(7u, routes.ActivePointerId);
        Assert.Equal(PointerDeviceType.Pen, routes.ActiveStrokeDeviceType);
        Assert.Equal(PointerMoveRoute.Active, routes.ResolveMoveRoute(7));
        Assert.True(routes.HasActivePointerCapture);
    }

    [Fact]
    public void EndActiveStroke_ClearsSession_AllowingNextPointer()
    {
        var routes = new PointerRouteState();
        routes.BeginActiveStroke(7, PointerDeviceType.Pen);

        routes.EndActiveStroke();

        Assert.Null(routes.ActivePointerId);
        Assert.Null(routes.ActiveStrokeDeviceType);
        Assert.Equal(PointerMoveRoute.None, routes.ResolveMoveRoute(7));
        // 抬起/取消/捕获丢失共用该清理转移，之后互斥闸门重新放行。
        Assert.False(routes.HasActivePointerCapture);
    }

    [Fact]
    public void BeginPan_RoutesMoveToPan_UntilReleased()
    {
        var routes = new PointerRouteState();

        routes.BeginPan(3);

        Assert.Equal(PointerMoveRoute.Pan, routes.ResolveMoveRoute(3));
        Assert.True(routes.HasViewportGesture);
        Assert.True(routes.HasActivePointerCapture);

        Assert.True(routes.TryEndPan(3));

        Assert.Null(routes.PanPointerId);
        Assert.False(routes.HasViewportGesture);
        Assert.Equal(PointerMoveRoute.None, routes.ResolveMoveRoute(3));
    }

    [Fact]
    public void TryEndPan_WrongPointerId_KeepsState()
    {
        var routes = new PointerRouteState();
        routes.BeginPan(3);

        Assert.False(routes.TryEndPan(4));

        Assert.Equal(3u, routes.PanPointerId);
        Assert.True(routes.HasActivePointerCapture);
    }

    [Fact]
    public void CancelPan_ClearsPointerId()
    {
        var routes = new PointerRouteState();
        routes.BeginPan(3);

        routes.CancelPan();

        Assert.Null(routes.PanPointerId);
        Assert.False(routes.HasActivePointerCapture);
    }

    [Fact]
    public void ResolveMoveRoute_PrefersPanOverSelectionMarqueeAndActive()
    {
        var routes = new PointerRouteState();
        routes.BeginPan(1);
        routes.BeginSelectionMove(2);
        routes.BeginMarquee(3);
        routes.BeginActiveStroke(4, PointerDeviceType.Touch);

        // 路由优先级与 OnCanvasPointerMoved 原分支顺序一致（pan → selection → marquee → active）。
        Assert.Equal(PointerMoveRoute.Pan, routes.ResolveMoveRoute(1));
        Assert.Equal(PointerMoveRoute.Selection, routes.ResolveMoveRoute(2));
        Assert.Equal(PointerMoveRoute.Marquee, routes.ResolveMoveRoute(3));
        Assert.Equal(PointerMoveRoute.Active, routes.ResolveMoveRoute(4));
        Assert.Equal(PointerMoveRoute.None, routes.ResolveMoveRoute(99));
    }

    [Fact]
    public void HasActivePointerCapture_BlocksWhileAnyGestureActive()
    {
        var routes = new PointerRouteState();

        Assert.False(routes.HasActivePointerCapture);

        routes.BeginMarquee(1);
        Assert.True(routes.HasActivePointerCapture);
        routes.EndMarquee();
        Assert.False(routes.HasActivePointerCapture);

        routes.BeginSelectionMove(2);
        Assert.True(routes.HasActivePointerCapture);
        routes.EndSelectionMove();
        Assert.False(routes.HasActivePointerCapture);

        routes.BeginPan(3);
        Assert.True(routes.HasActivePointerCapture);
        routes.CancelPan();
        Assert.False(routes.HasActivePointerCapture);
    }

    [Fact]
    public void EndSelectionMove_ClearsPointerIdAndManipulatingFlag()
    {
        var routes = new PointerRouteState();
        routes.BeginSelectionMove(2);
        routes.IsManipulatingSelection = true;
        Assert.True(routes.HasSelectionGesture);

        routes.EndSelectionMove();

        Assert.Null(routes.SelectionPointerId);
        Assert.False(routes.IsManipulatingSelection);
        Assert.False(routes.HasSelectionGesture);
    }

    [Fact]
    public void CancelSelectionMove_ResetsTouchTargetToViewport()
    {
        var routes = new PointerRouteState();
        routes.BeginSelectionMove(2);
        routes.TouchManipulationTarget = TouchManipulationTarget.Selection;

        routes.CancelSelectionMove();

        Assert.Null(routes.SelectionPointerId);
        Assert.Equal(TouchManipulationTarget.Viewport, routes.TouchManipulationTarget);
    }

    [Fact]
    public void EndMarquee_ClearsMarqueePointerId()
    {
        var routes = new PointerRouteState();
        routes.BeginMarquee(1);
        Assert.True(routes.HasSelectionGesture);

        routes.EndMarquee();

        Assert.Null(routes.MarqueePointerId);
        Assert.False(routes.HasSelectionGesture);
    }

    [Fact]
    public void HasViewportGesture_TrueWhileManipulating()
    {
        var routes = new PointerRouteState();

        routes.IsManipulating = true;
        Assert.True(routes.HasViewportGesture);

        routes.IsManipulating = false;
        Assert.False(routes.HasViewportGesture);
    }

    [Fact]
    public void ActiveTouchPointers_TracksCountAcrossFingers()
    {
        var routes = new PointerRouteState();

        routes.ActiveTouchPointers.Add(10);
        Assert.Single(routes.ActiveTouchPointers);

        // 多指边界：第 2 个触点加入后进入 Manipulation 通道（≥2）。
        routes.ActiveTouchPointers.Add(11);
        Assert.Equal(2, routes.ActiveTouchPointers.Count);

        // 单指抬起后回落到单指。
        routes.ActiveTouchPointers.Remove(10);
        Assert.Single(routes.ActiveTouchPointers);

        // 三指以上手势结束可能收不到逐指释放事件，手势完成时统一清空。
        routes.ActiveTouchPointers.Clear();
        Assert.Empty(routes.ActiveTouchPointers);
    }
}
