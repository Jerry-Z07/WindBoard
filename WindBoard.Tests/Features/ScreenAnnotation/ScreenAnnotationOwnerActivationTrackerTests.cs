using Microsoft.UI.Xaml;
using WindBoard.Features.ScreenAnnotation.Services;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationOwnerActivationTrackerTests
{
    [Fact]
    public void Observe_ActivatedBeforeAnyDeactivation_DoesNotRequestStop()
    {
        var tracker = new ScreenAnnotationOwnerActivationTracker();

        bool shouldStop = tracker.Observe(WindowActivationState.CodeActivated);

        Assert.False(shouldStop);
    }

    [Fact]
    public void Observe_DeactivatedThenReactivated_RequestsStop()
    {
        var tracker = new ScreenAnnotationOwnerActivationTracker();

        bool shouldStopWhileDeactivated = tracker.Observe(WindowActivationState.Deactivated);
        bool shouldStopWhenReactivated = tracker.Observe(WindowActivationState.PointerActivated);

        Assert.False(shouldStopWhileDeactivated);
        Assert.True(shouldStopWhenReactivated);
    }

    [Fact]
    public void Observe_MultipleDeactivatedEvents_WaitsForReactivation()
    {
        var tracker = new ScreenAnnotationOwnerActivationTracker();

        bool first = tracker.Observe(WindowActivationState.Deactivated);
        bool second = tracker.Observe(WindowActivationState.Deactivated);
        bool third = tracker.Observe(WindowActivationState.CodeActivated);

        Assert.False(first);
        Assert.False(second);
        Assert.True(third);
    }
}
