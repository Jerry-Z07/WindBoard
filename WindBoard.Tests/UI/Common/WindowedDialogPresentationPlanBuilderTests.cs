using System;
using WindBoard.UI.Common;
using Xunit;

namespace WindBoard.Tests.UI.Common;

public sealed class WindowedDialogPresentationPlanBuilderTests
{
    [Fact]
    public void BuildUpdateResult_TwoColumnWithUsableOwner_UsesWindowedDialogAndWideSizing()
    {
        WindowedDialogPresentationPlan plan = WindowedDialogPresentationPlanBuilder.BuildUpdateResult(
            hasOwnerWindow: true,
            ownerHwnd: new IntPtr(1),
            useTwoColumnLayout: true);

        Assert.Equal(DialogPresentationKind.WindowedContentDialog, plan.Kind);
        Assert.Equal(1240d, plan.InitialWidth);
        Assert.Equal(980d, plan.MinimumWidth);
    }

    [Fact]
    public void BuildUpdateResult_SingleColumnWithoutUsableOwner_FallsBackToContentDialog()
    {
        WindowedDialogPresentationPlan plan = WindowedDialogPresentationPlanBuilder.BuildUpdateResult(
            hasOwnerWindow: true,
            ownerHwnd: IntPtr.Zero,
            useTwoColumnLayout: false);

        Assert.Equal(DialogPresentationKind.ContentDialog, plan.Kind);
        Assert.Equal(760d, plan.InitialWidth);
        Assert.Equal(560d, plan.MinimumWidth);
    }
}
