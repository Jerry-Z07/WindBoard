using System;
using System.Numerics;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Board.Elements;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;

namespace WindBoard.Tests.Rendering.Snapshot;

/// <summary>
/// 快照场景构造工厂：集中管理文档/笔迹/形状/元素卡片与视口参数的构造逻辑。
/// </summary>
/// <remarks>
/// 约定：所有场景内容围绕世界坐标原点布置，默认相机对准原点、缩放 1，
/// 使场景稳定落在快照中心区域；场景变更属于基准变更，须重建基准并说明理由。
/// </remarks>
internal static class BoardSceneFixtures
{
    /// <summary>默认快照视口尺寸（DIP）。</summary>
    internal static readonly Vector2 DefaultViewportSize = new(320.0f, 240.0f);

    /// <summary>
    /// 构造视口：尺寸 + 相机 + 缩放。
    /// </summary>
    internal static BoardViewport CreateViewport(
        Vector2 size,
        Vector2? cameraWorld = null,
        float zoom = 1.0f)
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(size);
        viewport.SetView(cameraWorld ?? Vector2.Zero, zoom);
        return viewport;
    }

    /// <summary>场景 1：空白画布（纯背景，用于 harness 自检与清屏基准）。</summary>
    internal static BoardDocument CreateBlankDocument() => new();

    /// <summary>
    /// 场景 2：单条笔迹，压感从 0.1 → 1.0 线性递增（验证 Ink/DeviceContext2 绘制路径与压感粗细变化）。
    /// </summary>
    internal static BoardDocument CreateSingleStrokeDocument()
    {
        var document = new BoardDocument();
        var stroke = new Stroke
        {
            Color = new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            BaseSize = 10.0f,
            EnablePressure = true,
        };

        const int pointCount = 24;
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            var position = new Vector2(
                -90.0f + (180.0f * t),
                -40.0f + (60.0f * MathF.Sin(t * MathF.PI * 1.5f)));
            float pressure = 0.1f + (0.9f * t);

            stroke.Points.Add(new StrokePoint(position, pressure));
            stroke.ExpandBounds(position, pressure);
        }

        document.InkItems.Add(stroke);
        return document;
    }

    /// <summary>
    /// 场景 3：两条半透明笔迹交叉叠放（验证颜色 alpha 混合的正确性）。
    /// </summary>
    internal static BoardDocument CreateMultiStrokeBlendingDocument()
    {
        var document = new BoardDocument();

        // 红：水平波浪线。
        var red = new Stroke
        {
            Color = new Color4(1.0f, 0.0f, 0.0f, 0.55f),
            BaseSize = 8.0f,
            EnablePressure = false,
        };
        for (int i = 0; i <= 24; i++)
        {
            float t = i / 24.0f;
            var position = new Vector2(-110.0f + (220.0f * t), 50.0f * MathF.Sin(t * MathF.PI * 2.0f));
            red.Points.Add(new StrokePoint(position, 1.0f));
            red.ExpandBounds(position, 1.0f);
        }

        // 蓝：对角直线，与红笔迹在中心交叉。
        var blue = new Stroke
        {
            Color = new Color4(0.0f, 0.0f, 1.0f, 0.55f),
            BaseSize = 8.0f,
            EnablePressure = false,
        };
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12.0f;
            var position = new Vector2(-100.0f + (200.0f * t), 70.0f - (140.0f * t));
            blue.Points.Add(new StrokePoint(position, 1.0f));
            blue.ExpandBounds(position, 1.0f);
        }

        document.InkItems.Add(red);
        document.InkItems.Add(blue);
        return document;
    }

    /// <summary>场景 5：四种两点式形状（直线/矩形/椭圆/箭头）。</summary>
    internal static BoardDocument CreateShapesDocument()
    {
        var document = new BoardDocument();

        document.InkItems.Add(CreateShape(
            BoardShapeKind.Line,
            new Vector2(-110.0f, -80.0f),
            new Vector2(-10.0f, -30.0f),
            new Color4(1.0f, 0.30f, 0.30f, 1.0f),
            width: 4.0f));

        document.InkItems.Add(CreateShape(
            BoardShapeKind.Rectangle,
            new Vector2(20.0f, -80.0f),
            new Vector2(120.0f, -10.0f),
            new Color4(0.30f, 1.0f, 0.30f, 1.0f),
            width: 3.0f));

        document.InkItems.Add(CreateShape(
            BoardShapeKind.Ellipse,
            new Vector2(-110.0f, 10.0f),
            new Vector2(-20.0f, 90.0f),
            new Color4(1.0f, 0.70f, 0.10f, 1.0f),
            width: 3.0f));

        document.InkItems.Add(CreateShape(
            BoardShapeKind.Arrow,
            new Vector2(20.0f, 30.0f),
            new Vector2(120.0f, 90.0f),
            new Color4(0.30f, 0.80f, 1.0f, 1.0f),
            width: 5.0f));

        return document;
    }

    /// <summary>场景 6：文本元素卡片（深/浅主题由测试设置渲染器主题切换）。</summary>
    internal static BoardDocument CreateTextCardDocument()
    {
        var document = new BoardDocument();
        document.ElementsBelowInk.Add(new BoardTextElement
        {
            PositionWorld = new Vector2(-115.0f, -70.0f),
            SizeWorld = new Vector2(230.0f, 120.0f),
            Text = "渲染快照测试：文本元素卡片内容预览，用于验证卡片与文本绘制。",
        });
        return document;
    }

    private static BoardShape CreateShape(BoardShapeKind kind, Vector2 start, Vector2 end, Color4 color, float width)
    {
        var shape = new BoardShape(kind)
        {
            Color = color,
            Width = width,
        };
        shape.SetGeometry(start, end);
        return shape;
    }
}
