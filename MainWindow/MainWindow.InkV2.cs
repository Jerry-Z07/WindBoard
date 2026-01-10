using System.Windows;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;

namespace WindBoard
{
    public partial class MainWindow
    {
        private uint _inkColorArgb = 0xFFFFFFFF;
        private InkThicknessSemantics _inkThicknessSemantics = InkThicknessSemantics.ViewInvariant;

        private InkTool CreateCurrentInkToolSnapshot()
        {
            return new InkTool(
                ColorArgb: _inkColorArgb,
                BaseThicknessDip: _baseThickness,
                ThicknessSemantics: _inkThicknessSemantics,
                BrushKind: InkBrushKind.Pen,
                UsesPressure: false,
                PressureNominal: 1.0f);
        }

        private void EraseInkV2ByRect(Rect rect)
        {
            BoardPage? page = _pageService?.CurrentPage;
            if (page == null) return;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            bool changed = InkEraserEngine.EraseRect(
                page.Ink,
                page.InkSpatialIndex,
                page.InkUndoHistory,
                new InkRectDip(rect.X, rect.Y, rect.Width, rect.Height));

            if (!changed)
            {
                return;
            }

            page.InkSpatialIndex.Rebuild(page.Ink);
            page.ContentVersion++;
            InvalidateInkSurface();
        }
    }
}

