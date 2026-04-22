using System;

namespace WindBoard.UI.Common
{
    internal enum DialogPresentationKind
    {
        ContentDialog,
        WindowedContentDialog,
    }

    internal sealed record WindowedDialogPresentationPlan(
        DialogPresentationKind Kind,
        double InitialWidth,
        double MinimumWidth,
        double MinimumHeight);

    internal static class WindowedDialogPresentationPlanBuilder
    {
        private const double UpdateTwoColumnInitialWidth = 1240d;
        private const double UpdateTwoColumnMinimumWidth = 980d;
        private const double UpdateSingleColumnInitialWidth = 760d;
        private const double UpdateSingleColumnMinimumWidth = 560d;
        private const double UpdateMinimumHeight = 0d;

        internal static WindowedDialogPresentationPlan BuildUpdateResult(bool hasOwnerWindow, IntPtr ownerHwnd, bool useTwoColumnLayout)
        {
            return new WindowedDialogPresentationPlan(
                ResolveKind(hasOwnerWindow, ownerHwnd),
                useTwoColumnLayout ? UpdateTwoColumnInitialWidth : UpdateSingleColumnInitialWidth,
                useTwoColumnLayout ? UpdateTwoColumnMinimumWidth : UpdateSingleColumnMinimumWidth,
                UpdateMinimumHeight);
        }

        private static DialogPresentationKind ResolveKind(bool hasOwnerWindow, IntPtr ownerHwnd)
        {
            return hasOwnerWindow && ownerHwnd != IntPtr.Zero
                ? DialogPresentationKind.WindowedContentDialog
                : DialogPresentationKind.ContentDialog;
        }
    }
}
