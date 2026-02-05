using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Editing;
using Vortice.Mathematics;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 工作区与快照之间的转换（用于导入/导出解耦）。
    /// </summary>
    internal static class BoardWorkspaceSnapshotConverter
    {
        public static BoardWorkspaceSnapshot CreateSnapshot(BoardWorkspace workspace)
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

            return new BoardWorkspaceSnapshot(pages, workspace.CurrentIndex);
        }

        private static BoardPageSnapshot CreatePageSnapshot(BoardPage page)
        {
            var strokes = new List<StrokeSnapshot>(page.Session.Document.Strokes.Count);
            foreach (Stroke stroke in page.Session.Document.Strokes)
            {
                strokes.Add(CreateStrokeSnapshot(stroke));
            }

            return new BoardPageSnapshot(page.Id, strokes);
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

