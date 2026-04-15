using System;
using WindBoard.UI.Common;
using Xunit;

namespace WindBoard.Tests.UI.Common;

public sealed class WindowedDialogPresentationPlanBuilderTests
{
    [Fact]
    public void BuildImport_WithUsableOwner_UsesWindowedDialogAndImportSizing()
    {
        WindowedDialogPresentationPlan plan = WindowedDialogPresentationPlanBuilder.BuildImport(
            hasOwnerWindow: true,
            ownerHwnd: new IntPtr(1));

        Assert.Equal(DialogPresentationKind.WindowedContentDialog, plan.Kind);
        Assert.Equal(1320d, plan.InitialWidth);
        Assert.Equal(1040d, plan.MinimumWidth);
        Assert.Equal(620d, plan.MinimumHeight);
    }

    [Fact]
    public void BuildImport_WithoutUsableOwner_FallsBackToContentDialog()
    {
        WindowedDialogPresentationPlan plan = WindowedDialogPresentationPlanBuilder.BuildImport(
            hasOwnerWindow: false,
            ownerHwnd: IntPtr.Zero);

        Assert.Equal(DialogPresentationKind.ContentDialog, plan.Kind);
        Assert.Equal(1320d, plan.InitialWidth);
        Assert.Equal(1040d, plan.MinimumWidth);
        Assert.Equal(620d, plan.MinimumHeight);
    }

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
