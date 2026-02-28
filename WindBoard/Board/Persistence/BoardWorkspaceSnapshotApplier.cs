using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
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

                // 导入页面元素（文本/链接/媒体/文件等）。
                ApplyElements(session, page.ElementsBelowInk, aboveInk: false);
                ApplyElements(session, page.ElementsAboveInk, aboveInk: true);

                pages.Add(new BoardPage(page.Id, session));
            }

            if (pages.Count == 0)
            {
                pages.Add(new BoardPage());
            }

            return pages;
        }

        private static void ApplyElements(BoardSession session, IReadOnlyList<BoardElementSnapshot>? elements, bool aboveInk)
        {
            if (elements is null || elements.Count == 0)
            {
                return;
            }

            List<BoardElement> target = aboveInk
                ? session.Document.ElementsAboveInk
                : session.Document.ElementsBelowInk;

            for (int i = 0; i < elements.Count; i++)
            {
                BoardElement? element = CreateElement(elements[i]);
                if (element is null)
                {
                    continue;
                }

                target.Add(element);
            }
        }

        private static BoardElement? CreateElement(BoardElementSnapshot snapshot)
        {
            if (snapshot is null)
            {
                return null;
            }

            BoardElement? element = snapshot switch
            {
                BoardTextElementSnapshot text => new BoardTextElement { Text = text.Text ?? string.Empty },
                BoardLinkElementSnapshot link => new BoardLinkElement { Url = link.Url ?? string.Empty, Title = link.Title },
                BoardMediaElementSnapshot media => new BoardMediaElement
                {
                    Kind = media.Kind,
                    SourcePath = media.SourcePath ?? string.Empty,
                    DisplayName = media.DisplayName ?? string.Empty,
                },
                BoardFileElementSnapshot file => new BoardFileElement
                {
                    SourcePath = file.SourcePath ?? string.Empty,
                    DisplayName = file.DisplayName ?? string.Empty,
                },
                _ => null,
            };

            if (element is null)
            {
                return null;
            }

            // 位置/尺寸：属于外部输入，做基础兜底，避免出现 NaN/Infinity 导致渲染异常。
            element.PositionWorld = ToFiniteVector2(snapshot.PositionWorld, fallback: Vector2.Zero);

            Vector2 size = ToFiniteVector2(snapshot.SizeWorld, fallback: new Vector2(320.0f, 180.0f));
            element.SizeWorld = new Vector2(
                Math.Max(1.0f, size.X),
                Math.Max(1.0f, size.Y));

            return element;
        }

        private static Vector2 ToFiniteVector2(Vector2 value, Vector2 fallback)
        {
            float x = ToFiniteFloat(value.X, fallback.X);
            float y = ToFiniteFloat(value.Y, fallback.Y);
            return new Vector2(x, y);
        }

        private static float ToFiniteFloat(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return value;
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
