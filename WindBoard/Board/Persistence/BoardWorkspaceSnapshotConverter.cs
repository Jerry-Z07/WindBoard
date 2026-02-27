using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using Vortice.Mathematics;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 工作区与快照之间的转换（用于导入/导出解耦）。
    /// </summary>
    internal static class BoardWorkspaceSnapshotConverter
    {
        public static BoardWorkspaceSnapshot CreateSnapshot(
            BoardWorkspace workspace,
            Vector2? viewportCameraWorld = null,
            float? viewportZoom = null,
            Vector2? viewportSizeDip = null)
        {
            if (workspace is null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            var pages = new List<BoardPageSnapshot>(workspace.Pages.Count);
            for (int i = 0; i < workspace.Pages.Count; i++)
            {
                BoardPage page = workspace.Pages[i];
                pages.Add(CreatePageSnapshot(page));
            }

            return new BoardWorkspaceSnapshot(
                pages,
                workspace.CurrentIndex,
                ViewportCameraWorld: viewportCameraWorld,
                ViewportZoom: viewportZoom,
                ViewportSizeDip: viewportSizeDip);
        }

        private static BoardPageSnapshot CreatePageSnapshot(BoardPage page)
        {
            var strokes = new List<StrokeSnapshot>(page.Session.Document.Strokes.Count);
            foreach (Stroke stroke in page.Session.Document.Strokes)
            {
                strokes.Add(CreateStrokeSnapshot(stroke));
            }

            IReadOnlyList<BoardElementSnapshot> below = CreateElementSnapshots(page.Session.Document.ElementsBelowInk);
            IReadOnlyList<BoardElementSnapshot> above = CreateElementSnapshots(page.Session.Document.ElementsAboveInk);

            return new BoardPageSnapshot(page.Id, strokes, below, above);
        }

        private static IReadOnlyList<BoardElementSnapshot> CreateElementSnapshots(IReadOnlyList<BoardElement> elements)
        {
            if (elements.Count == 0)
            {
                return Array.Empty<BoardElementSnapshot>();
            }

            var snapshots = new List<BoardElementSnapshot>(elements.Count);
            for (int i = 0; i < elements.Count; i++)
            {
                if (TryCreateElementSnapshot(elements[i], order: i, out BoardElementSnapshot? snapshot))
                {
                    snapshots.Add(snapshot!);
                }
            }

            return snapshots;
        }

        private static bool TryCreateElementSnapshot(BoardElement element, int order, out BoardElementSnapshot? snapshot)
        {
            snapshot = null;

            if (element is null)
            {
                return false;
            }

            switch (element)
            {
                case BoardTextElement text:
                    snapshot = new BoardTextElementSnapshot(
                        text.Id,
                        text.PositionWorld,
                        text.SizeWorld,
                        order,
                        Text: text.Text ?? string.Empty);
                    return true;

                case BoardLinkElement link:
                    snapshot = new BoardLinkElementSnapshot(
                        link.Id,
                        link.PositionWorld,
                        link.SizeWorld,
                        order,
                        Url: link.Url ?? string.Empty,
                        Title: link.Title);
                    return true;

                case BoardMediaElement media:
                    snapshot = new BoardMediaElementSnapshot(
                        media.Id,
                        media.PositionWorld,
                        media.SizeWorld,
                        order,
                        media.Kind,
                        SourcePath: media.SourcePath ?? string.Empty,
                        DisplayName: media.DisplayName ?? string.Empty);
                    return true;

                case BoardFileElement file:
                    snapshot = new BoardFileElementSnapshot(
                        file.Id,
                        file.PositionWorld,
                        file.SizeWorld,
                        order,
                        SourcePath: file.SourcePath ?? string.Empty,
                        DisplayName: file.DisplayName ?? string.Empty);
                    return true;

                default:
                    // 未知元素类型：当前快照不落盘该数据，避免破坏导出流程。
                    return false;
            }
        }

        private static StrokeSnapshot CreateStrokeSnapshot(Stroke stroke)
        {
            var points = new List<StrokePointSnapshot>(stroke.Points.Count);
            foreach (StrokePoint point in stroke.Points)
            {
                points.Add(new StrokePointSnapshot(point.Position, point.Pressure));
            }

            Vector4 color = ToVector4(stroke.Color);
            return new StrokeSnapshot(points, color, stroke.BaseSize, stroke.EnablePressure);
        }

        private static Vector4 ToVector4(Color4 color)
        {
            return new Vector4(color.R, color.G, color.B, color.A);
        }
    }
}
