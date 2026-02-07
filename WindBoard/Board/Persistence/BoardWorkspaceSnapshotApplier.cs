using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Editing;
using Vortice.Mathematics;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 将 <see cref="BoardWorkspaceSnapshot"/> 应用为运行态对象（页面/会话/文档）。
    /// </summary>
    internal static class BoardWorkspaceSnapshotApplier
    {
        public static List<BoardPage> CreatePages(BoardWorkspaceSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var pages = new List<BoardPage>(snapshot.Pages.Count);

            for (int i = 0; i < snapshot.Pages.Count; i++)
            {
                BoardPageSnapshot page = snapshot.Pages[i];
                var session = new BoardSession();

                // 导入属于“重建初始状态”，这里直接填充 Document，
                // 避免污染撤销/重做栈（导入后应视为新的起点）。
                foreach (StrokeSnapshot strokeSnap in page.Strokes)
                {
                    session.Document.Strokes.Add(CreateStroke(strokeSnap));
                }

                pages.Add(new BoardPage(page.Id, session));
            }

            if (pages.Count == 0)
            {
                pages.Add(new BoardPage());
            }

            return pages;
        }

        private static Stroke CreateStroke(StrokeSnapshot snapshot)
        {
            var stroke = new Stroke
            {
                Color = FromVector4(snapshot.ColorRgba),
                BaseSize = snapshot.BaseSize,
                EnablePressure = snapshot.EnablePressure,
            };

            foreach (StrokePointSnapshot p in snapshot.Points)
            {
                stroke.Points.Add(new StrokePoint(p.Position, p.Pressure));
            }

            stroke.RecalculateBoundsFromPoints();
            return stroke;
        }

        private static Color4 FromVector4(Vector4 color)
        {
            return new Color4(color.X, color.Y, color.Z, color.W);
        }
    }
}

