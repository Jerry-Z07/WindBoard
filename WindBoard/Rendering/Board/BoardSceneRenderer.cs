using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Board.Elements;
using WindBoard.Board.Viewport;
using WindBoard.Fonts;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard.Rendering.Board
{
    internal sealed class BoardSceneRenderer : IDisposable
    {
        private readonly struct VisibleWorldBounds
        {
            public VisibleWorldBounds(Vector2 min, Vector2 max)
            {
                Min = min;
                Max = max;
            }

            public Vector2 Min { get; }

            public Vector2 Max { get; }
        }

        private ID2D1Factory1? _factory;
        private ID2D1SolidColorBrush? _strokeBrush;
        private ID2D1StrokeStyle? _strokeStyle;
        private ID2D1InkStyle? _inkStyle;
        private readonly Dictionary<Stroke, StrokeInkCacheEntry> _inkCache = new();

        // 元素绘制资源
        private ID2D1SolidColorBrush? _elementFillBrush;
        private ID2D1SolidColorBrush? _elementBorderBrush;
        private ID2D1SolidColorBrush? _elementTextBrush;
        private ID2D1SolidColorBrush? _elementSecondaryTextBrush;
        private ID2D1SolidColorBrush? _elementIconTextBrush;

        private IDWriteFactory? _dwriteFactory;
        private IDWriteTextFormat? _elementTitleTextFormat;
        private IDWriteTextFormat? _elementBodyTextFormat;
        private IDWriteTextFormat? _elementIconTextFormat;

        private readonly Dictionary<BoardMediaElement, ID2D1Bitmap> _imageBitmapCache = new();
        private nint _imageBitmapCacheRenderTargetPtr;

        /// <summary>
        /// 元素卡片主题（深/浅）：影响导入的图片/文件/文本/链接等卡片外观。
        /// </summary>
        internal ElementCardTheme ElementCardTheme { get; set; } = ElementCardTheme.Dark;

        private readonly record struct ElementCardPalette(
            Color4 Background,
            Color4 Border,
            Color4 ImageBorder,
            Color4 Text,
            Color4 SecondaryText,
            Color4 IconText);

        private static readonly ElementCardPalette DarkPalette = new(
            Background: new Color4(0x20 / 255.0f, 0x21 / 255.0f, 0x26 / 255.0f, 1.0f), // #202126
            Border: new Color4(0x3A / 255.0f, 0x3B / 255.0f, 0x40 / 255.0f, 1.0f), // #3A3B40
            ImageBorder: new Color4(0x3A / 255.0f, 0x3B / 255.0f, 0x40 / 255.0f, 1.0f), // #3A3B40
            Text: new Color4(0xED / 255.0f, 0xED / 255.0f, 0xED / 255.0f, 1.0f), // #EDEDED
            SecondaryText: new Color4(0xBD / 255.0f, 0xBD / 255.0f, 0xBD / 255.0f, 1.0f), // #BDBDBD
            IconText: new Color4(0xED / 255.0f, 0xED / 255.0f, 0xED / 255.0f, 1.0f)); // #EDEDED

        private static readonly ElementCardPalette LightPalette = new(
            Background: new Color4(1, 1, 1, 0.92f),
            Border: new Color4(0, 0, 0, 0.28f),
            ImageBorder: new Color4(0, 0, 0, 0.35f),
            Text: new Color4(0, 0, 0, 0.90f),
            SecondaryText: new Color4(0, 0, 0, 0.60f),
            IconText: new Color4(1, 1, 1, 0.95f));

        private ElementCardPalette GetCardPalette()
        {
            return ElementCardTheme == ElementCardTheme.Light ? LightPalette : DarkPalette;
        }

        public void Draw(ID2D1RenderTarget ctx, BoardDocument document, Stroke? activeStroke, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, activeStroke);
                PruneElementCache(document);

                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
                    DrawSceneInWorldBounds(ctx, ctx2, document, activeStroke, new VisibleWorldBounds(visibleMinWorld, visibleMaxWorld));
                });
            });
        }

        public void DrawBackground(ID2D1RenderTarget ctx, BoardDocument document, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, null);
                PruneElementCache(document);

                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
                    DrawSceneInWorldBounds(ctx, ctx2, document, null, new VisibleWorldBounds(visibleMinWorld, visibleMaxWorld));
                });
            });
        }

        /// <summary>
        /// 绘制“笔迹层之下”的背景（用于活动笔迹叠加渲染）。
        /// </summary>
        /// <remarks>
        /// 该方法会绘制：
        /// - 元素（下层）
        /// - 文档笔迹
        /// 
        /// 不会绘制：
        /// - 元素（上层）
        /// - 活动笔迹
        /// 
        /// 目的：保证“活动笔迹”与“上层元素”的遮挡关系正确（上层元素应覆盖活动笔迹）。
        /// </remarks>
        public void DrawBackgroundUnderInk(ID2D1RenderTarget ctx, BoardDocument document, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, null);
                PruneElementCache(document);

                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
                    DrawSceneUnderInkInWorldBounds(ctx, ctx2, document, new VisibleWorldBounds(visibleMinWorld, visibleMaxWorld));
                });
            });
        }

        public void DrawBackgroundInScreenRect(ID2D1RenderTarget ctx, BoardDocument document, BoardViewport viewport, Rect screenRectDip)
        {
            EnsureStrokeBrush(ctx);

            GetVisibleWorldBoundsFromScreenRect(viewport, screenRectDip, out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, null);
                PruneElementCache(document);

                WithWorldTransform(ctx, viewport, () =>
                {
                    DrawSceneInWorldBounds(ctx, ctx2, document, null, new VisibleWorldBounds(visibleMinWorld, visibleMaxWorld));
                });
            });
        }

        public void DrawActiveStroke(ID2D1RenderTarget ctx, Stroke activeStroke, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
                    DrawStrokeIfVisible(ctx, ctx2, activeStroke, visibleMinWorld, visibleMaxWorld);
                });
            });
        }

        /// <summary>
        /// 绘制活动笔迹 + 上层元素（用于叠加渲染）。
        /// </summary>
        public void DrawOverlayAboveInk(ID2D1RenderTarget ctx, BoardDocument document, Stroke? activeStroke, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, activeStroke);
                PruneElementCache(document);

                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);

                    if (activeStroke is not null)
                    {
                        DrawStrokeIfVisible(ctx, ctx2, activeStroke, visibleMinWorld, visibleMaxWorld);
                    }

                    DrawElementsIfVisible(ctx, document.ElementsAboveInk, visibleMinWorld, visibleMaxWorld);
                });
            });
        }

        private void EnsureStrokeBrush(ID2D1RenderTarget ctx)
        {
            _strokeBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
        }

        private void WithOptionalDeviceContext2(ID2D1RenderTarget ctx, Action<ID2D1DeviceContext2?> action)
        {
            // 某些系统/驱动环境可能不支持 DeviceContext2，这里做一次安全降级。
            using ID2D1DeviceContext2? ctx2 = TryGetDeviceContext2(ctx);
            EnsureInkStyle(ctx2);
            action(ctx2);
        }

        private static void WithWorldTransform(ID2D1RenderTarget ctx, BoardViewport viewport, Action action)
        {
            Matrix3x2 oldTransform = ctx.Transform;
            ctx.Transform = viewport.GetWorldToScreenTransform();
            try
            {
                action();
            }
            finally
            {
                ctx.Transform = oldTransform;
            }
        }

        private static void GetVisibleWorldBoundsFromScreenRect(BoardViewport viewport, Rect screenRectDip, out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld)
        {
            // 局部重绘：把屏幕矩形转换为世界坐标 AABB，用于裁剪可见笔迹的计算。
            Vector2 worldTopLeft = viewport.ScreenToWorld(new Vector2(screenRectDip.Left, screenRectDip.Top));
            Vector2 worldBottomRight = viewport.ScreenToWorld(new Vector2(screenRectDip.Right, screenRectDip.Bottom));

            visibleMinWorld = new Vector2(
                Math.Min(worldTopLeft.X, worldBottomRight.X),
                Math.Min(worldTopLeft.Y, worldBottomRight.Y));

            visibleMaxWorld = new Vector2(
                Math.Max(worldTopLeft.X, worldBottomRight.X),
                Math.Max(worldTopLeft.Y, worldBottomRight.Y));
        }

        private void DrawSceneInWorldBounds(
            ID2D1RenderTarget ctx,
            ID2D1DeviceContext2? ctx2,
            BoardDocument document,
            Stroke? activeStroke,
            VisibleWorldBounds visibleWorldBounds)
        {
            // 绘制顺序：
            // 1) 元素（笔迹下层）
            // 2) 笔迹
            // 3) 元素（笔迹上层）
            // 4) 活动笔迹（可选）

            DrawElementsIfVisible(ctx, document.ElementsBelowInk, visibleWorldBounds.Min, visibleWorldBounds.Max);

            foreach (var stroke in document.Strokes)
            {
                if (!BoardSceneMath.IsStrokeVisible(stroke, visibleWorldBounds.Min, visibleWorldBounds.Max))
                {
                    continue;
                }

                DrawStroke(ctx, ctx2, stroke);
            }

            DrawElementsIfVisible(ctx, document.ElementsAboveInk, visibleWorldBounds.Min, visibleWorldBounds.Max);

            if (activeStroke is null)
            {
                return;
            }

            DrawStrokeIfVisible(ctx, ctx2, activeStroke, visibleWorldBounds.Min, visibleWorldBounds.Max);
        }

        private void DrawSceneUnderInkInWorldBounds(
            ID2D1RenderTarget ctx,
            ID2D1DeviceContext2? ctx2,
            BoardDocument document,
            VisibleWorldBounds visibleWorldBounds)
        {
            // 仅绘制“活动笔迹下方”的内容：下层元素 + 文档笔迹。
            DrawElementsIfVisible(ctx, document.ElementsBelowInk, visibleWorldBounds.Min, visibleWorldBounds.Max);

            foreach (var stroke in document.Strokes)
            {
                if (!BoardSceneMath.IsStrokeVisible(stroke, visibleWorldBounds.Min, visibleWorldBounds.Max))
                {
                    continue;
                }

                DrawStroke(ctx, ctx2, stroke);
            }
        }

        private void DrawStrokeIfVisible(ID2D1RenderTarget ctx, ID2D1DeviceContext2? ctx2, Stroke stroke, Vector2 visibleMinWorld, Vector2 visibleMaxWorld)
        {
            if (!BoardSceneMath.IsStrokeVisible(stroke, visibleMinWorld, visibleMaxWorld))
            {
                return;
            }

            DrawStroke(ctx, ctx2, stroke);
        }

        private void DrawStroke(ID2D1RenderTarget ctx, ID2D1DeviceContext2? ctx2, Stroke stroke)
        {
            if (_strokeBrush is null)
            {
                return;
            }

            if (stroke.Points.Count == 0)
            {
                return;
            }

            _strokeBrush.Color = stroke.Color;

            if (stroke.Points.Count == 1)
            {
                float radius = Math.Max(0.5f, stroke.BaseSize * BoardSceneMath.GetStrokeWidthFactor(stroke.Points[0].Pressure) / 2.0f);
                ctx.FillEllipse(new Ellipse(stroke.Points[0].Position, radius, radius), _strokeBrush);
                return;
            }

            if (ctx2 is not null && TryDrawInkStroke(ctx2, stroke))
            {
                StrokeInkCacheEntry entry = _inkCache[stroke];

                // Ink 缓存只会在“点数变化”时做增量追加；对于选择工具造成的“点坐标整体变化”，
                // 通过对 DeviceContext 临时叠加平移变换，避免每一帧都重建 Ink，提升拖动流畅度。
                Vector2 offsetWorld = entry.DrawOffsetWorld;
                if (offsetWorld.LengthSquared() <= 0.0000001f)
                {
                    ctx2.DrawInk(entry.Ink, _strokeBrush, _inkStyle);
                    return;
                }

                Matrix3x2 oldTransform = ctx2.Transform;
                ctx2.Transform = Matrix3x2.CreateTranslation(offsetWorld) * oldTransform;
                try
                {
                    ctx2.DrawInk(entry.Ink, _strokeBrush, _inkStyle);
                }
                finally
                {
                    ctx2.Transform = oldTransform;
                }
                return;
            }

            EnsureStrokeStyle(ctx);

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float widthFactor = stroke.EnablePressure
                    ? BoardSceneMath.GetStrokeWidthFactor((p0.Pressure + p1.Pressure) / 2.0f)
                    : 1.0f;

                float strokeWidth = Math.Max(0.5f, stroke.BaseSize * widthFactor);
                ctx.DrawLine(p0.Position, p1.Position, _strokeBrush, strokeWidth, _strokeStyle);
            }
        }

        private void DrawElementsIfVisible(ID2D1RenderTarget ctx, IReadOnlyList<BoardElement> elements, Vector2 visibleMinWorld, Vector2 visibleMaxWorld)
        {
            if (elements is null || elements.Count == 0)
            {
                return;
            }

            EnsureElementBrushes(ctx);
            EnsureElementTextFormats();

            if (_elementFillBrush is null
                || _elementBorderBrush is null
                || _elementTextBrush is null
                || _elementSecondaryTextBrush is null
                || _elementIconTextBrush is null
                || _elementTitleTextFormat is null
                || _elementBodyTextFormat is null
                || _elementIconTextFormat is null)
            {
                return;
            }

            for (int i = 0; i < elements.Count; i++)
            {
                BoardElement element = elements[i];
                Rect bounds = element.GetBoundsWorld();

                Vector2 minWorld = new(bounds.Left, bounds.Top);
                Vector2 maxWorld = new(bounds.Right, bounds.Bottom);
                if (!BoardSceneMath.IntersectsAabb(minWorld, maxWorld, visibleMinWorld, visibleMaxWorld))
                {
                    continue;
                }

                DrawElement(ctx, element, bounds);
            }
        }

        private void DrawElement(ID2D1RenderTarget ctx, BoardElement element, Rect boundsWorld)
        {
            // 图片元素：优先绘制位图；解码失败则降级为占位卡片。
            if (element is BoardMediaElement media
                && media.Kind == BoardMediaKind.Image
                && TryGetOrCreateImageBitmap(ctx, media, out ID2D1Bitmap? bitmap)
                && bitmap is not null)
            {
                DrawImageElement(ctx, media, bitmap, boundsWorld);
                return;
            }

            DrawCardElement(ctx, element, boundsWorld);
        }

        private void DrawCardElement(ID2D1RenderTarget ctx, BoardElement element, Rect boundsWorld)
        {
            if (_elementFillBrush is null
                || _elementBorderBrush is null
                || _elementTextBrush is null
                || _elementSecondaryTextBrush is null
                || _elementIconTextBrush is null
                || _elementTitleTextFormat is null
                || _elementBodyTextFormat is null
                || _elementIconTextFormat is null)
            {
                return;
            }

            const float corner = 14.0f;
            const float pad = 12.0f;
            const float iconSize = 32.0f;
            const float iconGap = 12.0f;
            const float titleHeight = 22.0f;
            const float titleGap = 4.0f;

            ElementCardPalette palette = GetCardPalette();
            _elementFillBrush.Color = palette.Background;
            _elementBorderBrush.Color = palette.Border;
            _elementTextBrush.Color = palette.Text;
            _elementSecondaryTextBrush.Color = palette.SecondaryText;
            _elementIconTextBrush.Color = palette.IconText;

            var rr = new RoundedRectangle(
                new RectangleF(boundsWorld.Left, boundsWorld.Top, boundsWorld.Width, boundsWorld.Height),
                corner,
                corner);
            ctx.FillRoundedRectangle(rr, _elementFillBrush);
            ctx.DrawRoundedRectangle(rr, _elementBorderBrush, strokeWidth: 1.5f);

            ElementCardVisual visual = GetElementCardVisual(element);

            // 图标区域：左上角圆形徽标（颜色区分类型）。
            Rect iconRect = Rect.FromLTRB(
                boundsWorld.Left + pad,
                boundsWorld.Top + pad,
                boundsWorld.Left + pad + iconSize,
                boundsWorld.Top + pad + iconSize);

            if (!string.IsNullOrWhiteSpace(visual.IconGlyph))
            {
                _elementFillBrush.Color = visual.AccentColor;
                ctx.FillEllipse(
                    new Ellipse(new Vector2(iconRect.Left + iconSize / 2.0f, iconRect.Top + iconSize / 2.0f), iconSize / 2.0f, iconSize / 2.0f),
                    _elementFillBrush);
                ctx.DrawText(visual.IconGlyph, _elementIconTextFormat, iconRect, _elementIconTextBrush, DrawTextOptions.None, MeasuringMode.Natural);
            }

            float textLeft = iconRect.Right + iconGap;
            float textRight = boundsWorld.Right - pad;

            // 标题：文件名/标题等；副标题：类型与“双击打开”提示。
            Rect titleRect = Rect.FromLTRB(
                textLeft,
                boundsWorld.Top + pad,
                textRight,
                boundsWorld.Top + pad + titleHeight);

            Rect bodyRect = Rect.FromLTRB(
                textLeft,
                titleRect.Bottom + titleGap,
                textRight,
                boundsWorld.Bottom - pad);

            if (!string.IsNullOrWhiteSpace(visual.Title))
            {
                ctx.DrawText(visual.Title, _elementTitleTextFormat, titleRect, _elementTextBrush, DrawTextOptions.None, MeasuringMode.Natural);
            }

            if (!string.IsNullOrWhiteSpace(visual.Secondary))
            {
                ctx.DrawText(visual.Secondary, _elementBodyTextFormat, bodyRect, _elementSecondaryTextBrush, DrawTextOptions.None, MeasuringMode.Natural);
            }
        }

        private void DrawImageElement(ID2D1RenderTarget ctx, BoardMediaElement element, ID2D1Bitmap bitmap, Rect boundsWorld)
        {
            if (_elementFillBrush is null || _elementBorderBrush is null)
            {
                return;
            }

            const float corner = 14.0f;
            const float pad = 6.0f;

            ElementCardPalette palette = GetCardPalette();
            _elementFillBrush.Color = palette.Background;
            _elementBorderBrush.Color = palette.ImageBorder;

            var rr = new RoundedRectangle(
                new RectangleF(boundsWorld.Left, boundsWorld.Top, boundsWorld.Width, boundsWorld.Height),
                corner,
                corner);
            ctx.FillRoundedRectangle(rr, _elementFillBrush);

            Rect inner = Rect.FromLTRB(
                boundsWorld.Left + pad,
                boundsWorld.Top + pad,
                boundsWorld.Right - pad,
                boundsWorld.Bottom - pad);

            // 为避免个别图片/异常尺寸下出现“图片溢出容器”的视觉问题，这里额外做一次裁剪兜底。
            ctx.PushAxisAlignedClip(new RectangleF(boundsWorld.Left, boundsWorld.Top, boundsWorld.Width, boundsWorld.Height), AntialiasMode.PerPrimitive);
            try
            {
                Rect dest = ComputeAspectFitRect(inner, element.PixelWidth, element.PixelHeight);

                // 注意：Vortice 的 ID2D1RenderTarget.DrawBitmap(bitmap, opacity, mode, rect) 这个重载的 rect 是“源矩形”，
                // 会导致把元素世界坐标误当成位图裁剪区域，从而出现“图片不在框内 / 移动框体能看到更多图片”的错位现象。
                // 这里显式使用 destination + source 的重载，source 传整张位图，destination 才是要绘制到的目标区域。
                Rect src = Rect.FromLTRB(0.0f, 0.0f, element.PixelWidth, element.PixelHeight);
                ctx.DrawBitmap(bitmap, dest, 1.0f, BitmapInterpolationMode.Linear, src);
            }
            finally
            {
                ctx.PopAxisAlignedClip();
            }

            ctx.DrawRoundedRectangle(rr, _elementBorderBrush, strokeWidth: 1.5f);
        }

        private static Rect ComputeAspectFitRect(Rect container, int pixelWidth, int pixelHeight)
        {
            // 兜底：保证容器为有效矩形，避免极端缩放/异常尺寸导致 L>R / T>B。
            float cl = Math.Min(container.Left, container.Right);
            float ct = Math.Min(container.Top, container.Bottom);
            float cr = Math.Max(container.Left, container.Right);
            float cb = Math.Max(container.Top, container.Bottom);
            container = Rect.FromLTRB(cl, ct, cr, cb);

            float w = Math.Max(1.0f, container.Width);
            float h = Math.Max(1.0f, container.Height);

            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                return container;
            }

            float iw = pixelWidth;
            float ih = pixelHeight;

            float scale = Math.Min(w / iw, h / ih);
            float drawW = iw * scale;
            float drawH = ih * scale;

            float left = container.Left + (w - drawW) / 2.0f;
            float top = container.Top + (h - drawH) / 2.0f;

            return Rect.FromLTRB(left, top, left + drawW, top + drawH);
        }

        private readonly record struct ElementCardVisual(Color4 AccentColor, string IconGlyph, string Title, string Secondary);

        private static string GetSymbolGlyph(Symbol symbol)
        {
            int code = (int)symbol;
            if (code <= 0)
            {
                return string.Empty;
            }

            try
            {
                return char.ConvertFromUtf32(code);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryGetSymbolGlyph(string symbolName, Symbol fallback)
        {
            if (Enum.TryParse(symbolName, ignoreCase: true, out Symbol symbol))
            {
                return GetSymbolGlyph(symbol);
            }

            return GetSymbolGlyph(fallback);
        }

        private static ElementCardVisual GetElementCardVisual(BoardElement element)
        {
            // 颜色参考 iOS Accent 风格，保证在深色画布上可读。
            static Color4 AccentBlue() => new(0.04f, 0.52f, 1.00f, 1.00f);
            static Color4 AccentOrange() => new(1.00f, 0.58f, 0.00f, 1.00f);
            static Color4 AccentPurple() => new(0.69f, 0.32f, 0.87f, 1.00f);
            static Color4 AccentGreen() => new(0.20f, 0.78f, 0.35f, 1.00f);
            static Color4 AccentGray() => new(0.55f, 0.55f, 0.58f, 1.00f);
            static Color4 AccentCyan() => new(0.35f, 0.78f, 0.98f, 1.00f);

            if (element is BoardLinkElement link)
            {
                string url = (link.Url ?? string.Empty).Trim();
                string title = string.IsNullOrWhiteSpace(link.Title) ? url : link.Title!.Trim();

                string openHint = L10n.Get("ElementCard_DoubleClickToOpenLink");
                string secondary = string.IsNullOrWhiteSpace(link.Title) || string.IsNullOrWhiteSpace(url)
                    ? openHint
                    : url + "\n" + openHint;

                return new ElementCardVisual(AccentBlue(), TryGetSymbolGlyph("Link", Symbol.OpenFile), title, secondary);
            }

            if (element is BoardMediaElement media)
            {
                string name = GetBestDisplayName(media.DisplayName, media.SourcePath);
                string openHint = L10n.Get("ElementCard_DoubleClickToOpenExternal");

                return media.Kind switch
                {
                    BoardMediaKind.Audio => new ElementCardVisual(AccentOrange(), TryGetSymbolGlyph("MusicInfo", Symbol.OpenFile), name, L10n.Format("ElementCard_Kind_OpenExternal_Fmt", L10n.Get("ElementCard_MediaKind_Audio"), openHint)),
                    BoardMediaKind.Video => new ElementCardVisual(AccentPurple(), TryGetSymbolGlyph("Video", Symbol.OpenFile), name, L10n.Format("ElementCard_Kind_OpenExternal_Fmt", L10n.Get("ElementCard_MediaKind_Video"), openHint)),
                    BoardMediaKind.Image => new ElementCardVisual(AccentCyan(), TryGetSymbolGlyph("Pictures", Symbol.OpenFile), name, L10n.Format("ElementCard_Kind_OpenExternal_Fmt", L10n.Get("ElementCard_MediaKind_ImageNoPreview"), openHint)),
                    _ => new ElementCardVisual(AccentGray(), GetSymbolGlyph(Symbol.OpenFile), name, L10n.Format("ElementCard_Kind_OpenExternal_Fmt", L10n.Get("ElementCard_MediaKind_Generic"), openHint)),
                };
            }

            if (element is BoardFileElement file)
            {
                string name = GetBestDisplayName(file.DisplayName, file.SourcePath);
                (string kindLabel, bool known) = GetFileKindLabel(name, file.SourcePath);
                string openHint = L10n.Get("ElementCard_DoubleClickToOpenExternal");
                string secondary = known ? kindLabel + "\n" + openHint : openHint;
                string icon = known
                    ? TryGetSymbolGlyph("Document", Symbol.OpenFile)
                    : TryGetSymbolGlyph("Page", Symbol.OpenFile);
                return new ElementCardVisual(known ? AccentGreen() : AccentGray(), icon, name, secondary);
            }

            if (element is BoardTextElement text)
            {
                string preview = (text.Text ?? string.Empty).Trim();
                if (preview.Length > 160)
                {
                    preview = preview.Substring(0, 160) + "…";
                }

                string secondary = string.IsNullOrWhiteSpace(preview) ? string.Empty : preview;
                return new ElementCardVisual(AccentGray(), GetSymbolGlyph(Symbol.Edit), L10n.Get("ElementCard_Text_Title"), secondary);
            }

            return new ElementCardVisual(AccentGray(), TryGetSymbolGlyph("Help", Symbol.More), string.Empty, string.Empty);
        }

        private static string GetBestDisplayName(string? displayName, string? sourcePath)
        {
            string name = displayName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            string path = sourcePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return L10n.Get("ElementCard_Unnamed");
            }

            try
            {
                return Path.GetFileName(path);
            }
            catch
            {
                return path;
            }
        }

        private static (string label, bool known) GetFileKindLabel(string? displayName, string? sourcePath)
        {
            string name = displayName ?? string.Empty;
            string path = sourcePath ?? string.Empty;

            string ext = string.Empty;
            try
            {
                ext = Path.GetExtension(!string.IsNullOrWhiteSpace(name) ? name : path).ToLowerInvariant();
            }
            catch
            {
                ext = string.Empty;
            }

            return ext switch
            {
                ".pdf" => (L10n.Get("ElementCard_FileKind_Pdf"), true),
                ".doc" or ".docx" or ".docm" => (L10n.Get("ElementCard_FileKind_Word"), true),
                ".ppt" or ".pptx" or ".pptm" => (L10n.Get("ElementCard_FileKind_PowerPoint"), true),
                ".xls" or ".xlsx" or ".xlsm" => (L10n.Get("ElementCard_FileKind_Excel"), true),
                ".csv" => (L10n.Get("ElementCard_FileKind_Csv"), true),
                ".rtf" => (L10n.Get("ElementCard_FileKind_Rtf"), true),
                ".epub" => (L10n.Get("ElementCard_FileKind_Epub"), true),
                ".zip" or ".7z" or ".rar" => (L10n.Get("ElementCard_FileKind_Archive"), true),
                _ => (string.IsNullOrWhiteSpace(ext) ? L10n.Get("Common_File") : L10n.Format("ElementCard_File_Ext_Fmt", ext), false),
            };
        }

        private void EnsureElementBrushes(ID2D1RenderTarget ctx)
        {
            _elementFillBrush ??= ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 0.92f));
            _elementBorderBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 0.35f));
            _elementTextBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 0.90f));
            _elementSecondaryTextBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 0.60f));
            _elementIconTextBrush ??= ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 0.95f));
        }

        private void EnsureElementTextFormats()
        {
            if (_elementTitleTextFormat is not null && _elementBodyTextFormat is not null && _elementIconTextFormat is not null)
            {
                return;
            }

            _dwriteFactory ??= DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);

            _elementTitleTextFormat = _dwriteFactory.CreateTextFormat(
                "Segoe UI",
                fontCollection: null,
                FontWeight.SemiBold,
                Vortice.DirectWrite.FontStyle.Normal,
                FontStretch.Normal,
                fontSize: 15.0f,
                localeName: "zh-CN");

            _elementTitleTextFormat.WordWrapping = WordWrapping.NoWrap;
            _elementTitleTextFormat.TextAlignment = TextAlignment.Leading;
            _elementTitleTextFormat.ParagraphAlignment = ParagraphAlignment.Near;

            _elementBodyTextFormat = _dwriteFactory.CreateTextFormat(
                "Segoe UI",
                fontCollection: null,
                FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                FontStretch.Normal,
                fontSize: 13.0f,
                localeName: "zh-CN");

            _elementBodyTextFormat.WordWrapping = WordWrapping.Wrap;
            _elementBodyTextFormat.TextAlignment = TextAlignment.Leading;
            _elementBodyTextFormat.ParagraphAlignment = ParagraphAlignment.Near;

            // 使用图标字体绘制类型徽标，避免用“链/音/图”等文字充当图标。
            // 说明：优先使用 Segoe Fluent Icons；若不可用或 DirectWrite 无法创建，则降级为 Segoe MDL2 Assets。
            string iconFontFamily = SegoeFluentIconsFontLoader.EffectiveIconFontFamilyName;
            try
            {
                _elementIconTextFormat = _dwriteFactory.CreateTextFormat(
                    iconFontFamily,
                    fontCollection: null,
                    FontWeight.Normal,
                    Vortice.DirectWrite.FontStyle.Normal,
                    FontStretch.Normal,
                    fontSize: 16.0f,
                    localeName: "zh-CN");
            }
            catch (Exception ex)
            {
                AppLog.Warn(
                    "Rendering",
                    $"创建元素图标字体失败，将降级为 '{SegoeFluentIconsFontLoader.FallbackFontFamilyName}'：family='{iconFontFamily}'",
                    ex);

                _elementIconTextFormat = _dwriteFactory.CreateTextFormat(
                    SegoeFluentIconsFontLoader.FallbackFontFamilyName,
                    fontCollection: null,
                    FontWeight.Normal,
                    Vortice.DirectWrite.FontStyle.Normal,
                    FontStretch.Normal,
                    fontSize: 16.0f,
                    localeName: "zh-CN");
            }

            _elementIconTextFormat.WordWrapping = WordWrapping.NoWrap;
            _elementIconTextFormat.TextAlignment = TextAlignment.Center;
            _elementIconTextFormat.ParagraphAlignment = ParagraphAlignment.Center;
        }

        private bool TryGetOrCreateImageBitmap(ID2D1RenderTarget ctx, BoardMediaElement element, out ID2D1Bitmap? bitmap)
        {
            bitmap = null;

            if (element.Bgra8PremulPixels is not byte[] bytes)
            {
                return false;
            }

            int w = element.PixelWidth;
            int h = element.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            int stride;
            int required;
            try
            {
                stride = checked(w * 4);
                required = checked(stride * h);
            }
            catch
            {
                return false;
            }

            if (bytes.Length < required)
            {
                return false;
            }

            EnsureImageBitmapCacheContext(ctx);

            if (_imageBitmapCache.TryGetValue(element, out ID2D1Bitmap? cached))
            {
                bitmap = cached;
                return true;
            }

            var props = new BitmapProperties(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                dpiX: 96.0f,
                dpiY: 96.0f);

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                ID2D1Bitmap created = ctx.CreateBitmap(new SizeI(w, h), ptr, (uint)stride, props);
                _imageBitmapCache[element] = created;
                bitmap = created;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                handle.Free();
            }
        }

        private void EnsureImageBitmapCacheContext(ID2D1RenderTarget ctx)
        {
            nint ptr = ctx.NativePointer;
            if (_imageBitmapCacheRenderTargetPtr == ptr)
            {
                return;
            }

            foreach (ID2D1Bitmap bmp in _imageBitmapCache.Values)
            {
                bmp.Dispose();
            }

            _imageBitmapCache.Clear();
            _imageBitmapCacheRenderTargetPtr = ptr;
        }

        private static ID2D1DeviceContext2? TryGetDeviceContext2(ID2D1RenderTarget ctx)
        {
            try
            {
                return ctx.QueryInterface<ID2D1DeviceContext2>();
            }
            catch
            {
                return null;
            }
        }

        private void EnsureInkStyle(ID2D1DeviceContext2? ctx2)
        {
            if (_inkStyle is not null || ctx2 is null)
            {
                return;
            }

            var props = new InkStyleProperties
            {
                NibShape = InkNibShape.Round,
                NibTransform = Matrix3x2.Identity,
            };
            _inkStyle = ctx2.CreateInkStyle(props);
        }

        private void EnsureStrokeStyle(ID2D1RenderTarget ctx)
        {
            if (_strokeStyle is not null)
            {
                return;
            }

            _factory ??= D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded, DebugLevel.None);

            var props = new StrokeStyleProperties
            {
                StartCap = CapStyle.Round,
                EndCap = CapStyle.Round,
                DashCap = CapStyle.Round,
                LineJoin = LineJoin.Round,
                MiterLimit = 2.0f,
                DashStyle = DashStyle.Solid,
                DashOffset = 0.0f,
            };

            _strokeStyle = _factory.CreateStrokeStyle(props);
        }

        private void PruneInkCache(BoardDocument document, Stroke? activeStroke)
        {
            if (_inkCache.Count == 0)
            {
                return;
            }

            var live = new HashSet<Stroke>(document.Strokes);
            if (activeStroke is not null)
            {
                live.Add(activeStroke);
            }

            List<Stroke>? toRemove = null;
            foreach (var kv in _inkCache)
            {
                if (!live.Contains(kv.Key))
                {
                    toRemove ??= new List<Stroke>();
                    toRemove.Add(kv.Key);
                }
            }

            if (toRemove is null)
            {
                return;
            }

            foreach (Stroke stroke in toRemove)
            {
                if (_inkCache.Remove(stroke, out StrokeInkCacheEntry? entry))
                {
                    entry.Dispose();
                }
            }
        }

        private void PruneElementCache(BoardDocument document)
        {
            if (_imageBitmapCache.Count == 0)
            {
                return;
            }

            var live = new HashSet<BoardMediaElement>();

            CollectLiveImageElements(document.ElementsBelowInk, live);
            CollectLiveImageElements(document.ElementsAboveInk, live);

            List<BoardMediaElement>? toRemove = null;
            foreach (var kv in _imageBitmapCache)
            {
                if (!live.Contains(kv.Key))
                {
                    toRemove ??= new List<BoardMediaElement>();
                    toRemove.Add(kv.Key);
                }
            }

            if (toRemove is null)
            {
                return;
            }

            foreach (BoardMediaElement key in toRemove)
            {
                if (_imageBitmapCache.Remove(key, out ID2D1Bitmap? bmp))
                {
                    bmp.Dispose();
                }
            }
        }

        private static void CollectLiveImageElements(IReadOnlyList<BoardElement> elements, HashSet<BoardMediaElement> live)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] is BoardMediaElement { Kind: BoardMediaKind.Image } img)
                {
                    live.Add(img);
                }
            }
        }

        private static bool NearlyEqual(Vector2 a, Vector2 b, float eps = 0.0001f)
        {
            // 这里的比较用于判断“是否为纯平移”：只需要一个很小的阈值即可。
            // eps 取值对应世界坐标下的绝对误差（默认 1e-4），对拖动/旋转/缩放的判别足够稳定。
            return Vector2.DistanceSquared(a, b) <= eps * eps;
        }

        private bool TryDrawInkStroke(ID2D1DeviceContext2 ctx2, Stroke stroke)
        {
            if (stroke.Points.Count < 2)
            {
                return false;
            }

            int pointCount = stroke.Points.Count;
            Vector2 firstPos = stroke.Points[0].Position;
            Vector2 lastPos = stroke.Points[^1].Position;

            if (!_inkCache.TryGetValue(stroke, out StrokeInkCacheEntry? entry))
            {
                _inkCache[stroke] = StrokeInkCacheEntry.Create(ctx2, stroke);
            }
            else if (pointCount == entry.PointCount)
            {
                // 点数没变但坐标变了（例如选择工具拖动/缩放/旋转）时，Ink 缓存会过期，导致笔迹渲染停在旧位置，
                // 从而出现“笔迹与选择框错位”。这里做一次轻量更新：
                // - 若首尾点位移一致：认为是纯平移，复用 Ink，并记录 DrawOffsetWorld
                // - 否则：重建 Ink（旋转/缩放会改变相对位置，无法用平移修正）
                if (!TryUpdateInkDrawOffset(entry, firstPos, lastPos))
                {
                    RebuildInkCacheEntry(ctx2, stroke, entry);
                }
            }
            else if (pointCount < entry.PointCount)
            {
                // 点数减少：通常来自撤销/重做或替换点集，直接重建更安全。
                RebuildInkCacheEntry(ctx2, stroke, entry);
            }
            else if (entry.DrawOffsetWorld.LengthSquared() > 0.0000001f)
            {
                // 如果缓存处于“偏移绘制”状态，为避免增量追加导致坐标空间混乱，直接重建。
                RebuildInkCacheEntry(ctx2, stroke, entry);
            }
            else
            {
                // 点数增加且没有偏移：走增量追加，提高实时书写性能。
                entry.AppendSegments(stroke, entry.PointCount);
                entry.PointCount = pointCount;
                entry.LastPosition = lastPos;
            }

            return true;
        }

        private static bool TryUpdateInkDrawOffset(StrokeInkCacheEntry entry, Vector2 firstPos, Vector2 lastPos)
        {
            Vector2 expectedFirst = entry.FirstPosition + entry.DrawOffsetWorld;
            Vector2 expectedLast = entry.LastPosition + entry.DrawOffsetWorld;

            if (NearlyEqual(firstPos, expectedFirst) && NearlyEqual(lastPos, expectedLast))
            {
                return true;
            }

            Vector2 deltaFirst = firstPos - entry.FirstPosition;
            Vector2 deltaLast = lastPos - entry.LastPosition;
            if (NearlyEqual(deltaFirst, deltaLast))
            {
                entry.DrawOffsetWorld = deltaFirst;
                return true;
            }

            return false;
        }

        private void RebuildInkCacheEntry(ID2D1DeviceContext2 ctx2, Stroke stroke, StrokeInkCacheEntry entry)
        {
            // Ink 资源与对应的缓存条目需要成对更新，避免内存泄漏或引用旧对象导致的错位。
            entry.Dispose();
            _inkCache.Remove(stroke);
            _inkCache[stroke] = StrokeInkCacheEntry.Create(ctx2, stroke);
        }

        private sealed class StrokeInkCacheEntry : IDisposable
        {
            public StrokeInkCacheEntry(ID2D1Ink ink, int pointCount, Vector2 firstPosition, Vector2 lastPosition)
            {
                Ink = ink;
                PointCount = pointCount;
                FirstPosition = firstPosition;
                LastPosition = lastPosition;
            }

            public ID2D1Ink Ink { get; }

            public int PointCount { get; set; }

            public Vector2 FirstPosition { get; }

            public Vector2 LastPosition { get; set; }

            /// <summary>
            /// 用于“纯平移”场景的绘制偏移（世界坐标）。
            /// </summary>
            public Vector2 DrawOffsetWorld { get; set; }

            public static StrokeInkCacheEntry Create(ID2D1DeviceContext2 ctx2, Stroke stroke)
            {
                InkPoint start = CreateInkPoint(stroke, stroke.Points[0]);
                ID2D1Ink ink = ctx2.CreateInk(start);

                Vector2 firstPos = stroke.Points[0].Position;
                Vector2 lastPos = stroke.Points[^1].Position;
                var entry = new StrokeInkCacheEntry(ink, stroke.Points.Count, firstPos, lastPos);

                if (stroke.Points.Count > 1)
                {
                    entry.AppendSegments(stroke, 1);
                }

                return entry;
            }

            public void AppendSegments(Stroke stroke, int startPointIndex)
            {
                int pointCount = stroke.Points.Count;
                int clampedStart = Math.Max(1, startPointIndex);
                if (clampedStart >= pointCount)
                {
                    return;
                }

                int segmentCount = pointCount - clampedStart;
                var segments = new InkBezierSegment[segmentCount];
                for (int i = clampedStart; i < pointCount; i++)
                {
                    StrokePoint p0 = stroke.Points[i - 1];
                    StrokePoint p1 = stroke.Points[i];
                    segments[i - clampedStart] = CreateInkSegment(stroke, p0, p1);
                }

                Ink.AddSegments(segments, (uint)segments.Length);
            }

            public void Dispose()
            {
                Ink.Dispose();
            }
        }

        private static InkPoint CreateInkPoint(Stroke stroke, StrokePoint point)
        {
            float widthFactor = stroke.EnablePressure ? BoardSceneMath.GetStrokeWidthFactor(point.Pressure) : 1.0f;
            float diameter = Math.Max(0.5f, stroke.BaseSize * widthFactor);
            float radius = diameter / 2.0f;

            return new InkPoint
            {
                X = point.Position.X,
                Y = point.Position.Y,
                Radius = radius,
            };
        }

        private static InkBezierSegment CreateInkSegment(Stroke stroke, StrokePoint p0, StrokePoint p1)
        {
            Vector2 startPos = p0.Position;
            Vector2 endPos = p1.Position;
            Vector2 delta = endPos - startPos;

            Vector2 c1Pos = startPos + delta / 3.0f;
            Vector2 c2Pos = startPos + delta * 2.0f / 3.0f;

            float r0 = CreateInkPoint(stroke, p0).Radius;
            float r3 = CreateInkPoint(stroke, p1).Radius;
            float r1 = r0 + (r3 - r0) / 3.0f;
            float r2 = r0 + (r3 - r0) * 2.0f / 3.0f;

            return new InkBezierSegment
            {
                Point1 = new InkPoint { X = c1Pos.X, Y = c1Pos.Y, Radius = r1 },
                Point2 = new InkPoint { X = c2Pos.X, Y = c2Pos.Y, Radius = r2 },
                Point3 = new InkPoint { X = endPos.X, Y = endPos.Y, Radius = r3 },
            };
        }

        public void Dispose()
        {
            _strokeBrush?.Dispose();
            _strokeBrush = null;

            _elementFillBrush?.Dispose();
            _elementFillBrush = null;

            _elementBorderBrush?.Dispose();
            _elementBorderBrush = null;

            _elementTextBrush?.Dispose();
            _elementTextBrush = null;

            _elementSecondaryTextBrush?.Dispose();
            _elementSecondaryTextBrush = null;

            _elementIconTextBrush?.Dispose();
            _elementIconTextBrush = null;

            _strokeStyle?.Dispose();
            _strokeStyle = null;

            _inkStyle?.Dispose();
            _inkStyle = null;

            foreach (ID2D1Bitmap bmp in _imageBitmapCache.Values)
            {
                bmp.Dispose();
            }
            _imageBitmapCache.Clear();
            _imageBitmapCacheRenderTargetPtr = 0;

            _elementTitleTextFormat?.Dispose();
            _elementTitleTextFormat = null;

            _elementBodyTextFormat?.Dispose();
            _elementBodyTextFormat = null;

            _elementIconTextFormat?.Dispose();
            _elementIconTextFormat = null;

            _dwriteFactory?.Dispose();
            _dwriteFactory = null;

            foreach (var entry in _inkCache.Values)
            {
                entry.Dispose();
            }
            _inkCache.Clear();

            _factory?.Dispose();
            _factory = null;
        }
    }
}
