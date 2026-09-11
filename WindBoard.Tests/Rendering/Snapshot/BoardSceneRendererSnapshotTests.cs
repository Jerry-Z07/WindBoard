using System;
using System.Globalization;
using WindBoard.Board;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using WindBoard.Rendering.Board;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Rendering.Snapshot;

/// <summary>
/// <see cref="BoardSceneRenderer"/> 渲染快照（golden image）回归测试。
/// </summary>
/// <remarks>
/// 测试流程：构造文档/视口 → WARP 离屏渲染 → 与基准 PNG 逐像素比对，失败时输出
/// expected/actual/diff 三件套到 <c>WindBoard.Tests/artifacts/snapshot-failures/</c>。
/// 基准重建方式见 <see cref="SnapshotBaseline"/>（<c>WINDBOARD_REGEN_SNAPSHOTS=1</c>）。
///
/// 同一 collection 内的用例由 xUnit 串行执行（本类与语言状态写方同属
/// <see cref="ProcessGlobalLanguageState"/>，同时满足 D2D SingleThreaded 工厂的串行要求）。
///
/// 场景覆盖说明（对应 prd 场景清单 1-8）：
/// - 场景 1-6 为 D2D 像素级快照（本类）；
/// - 场景 7（选中态 overlay：选中框/手柄）与场景 8（框选 marquee 矩形）为 XAML 层元素
///   （BoardCanvasControl 的 Border/Thumb，见 UpdateSelectionOverlay/ShowMarqueeSelectionOverlay），
///   不经过 BoardSceneRenderer 的 D2D 输出，无法做 D2D 像素快照。其逻辑已有结构断言覆盖：
///   marquee 状态机见 SelectToolTests.Marquee_*，选中框几何见 InkItemScreenBoundsTests；
///   XAML 合成层的像素级验证**当前尚无自动化用例**（P3 E2E 冒烟未覆盖画布选择交互），列为后续增强。
/// </remarks>
[Collection(ProcessGlobalLanguageState.CollectionName)]
public sealed class BoardSceneRendererSnapshotTests
{
    private const int Width = 320;
    private const int Height = 240;

    /// <summary>文本场景差异像素占比阈值：吸收跨机器文本/图标字体渲染差异（prd 允许放宽）。</summary>
    private const double TextSceneMaxDiffPixelRatio = 0.005;

    [Fact]
    public void Harness_SelfCheck_BlankCanvasIsPureBackgroundColor()
    {
        // harness 自检：验证“清屏 → 渲染 → staging 读回”管线本身正确（防假阴性）。
        using var harness = new OffscreenRenderHarness(Width, Height);
        byte[] pixels = harness.Render(
            BoardSceneFixtures.CreateBlankDocument(),
            activeInkItem: null,
            BoardSceneFixtures.CreateViewport(BoardSceneFixtures.DefaultViewportSize));

        byte[] expected = OffscreenRenderHarness.CanvasClearColorBgra;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            Assert.Equal(expected[0], pixels[offset]);
            Assert.Equal(expected[1], pixels[offset + 1]);
            Assert.Equal(expected[2], pixels[offset + 2]);
            Assert.Equal(expected[3], pixels[offset + 3]);
        }
    }

    [Fact]
    public void BlankCanvas_MatchesBaseline() => RunSnapshot(
        nameof(BlankCanvas_MatchesBaseline),
        () => (BoardSceneFixtures.CreateBlankDocument(), null, BoardSceneFixtures.CreateViewport(BoardSceneFixtures.DefaultViewportSize)));

    [Fact]
    public void SingleStrokeWithPressure_MatchesBaseline() => RunSnapshot(
        nameof(SingleStrokeWithPressure_MatchesBaseline),
        () => (BoardSceneFixtures.CreateSingleStrokeDocument(), null, BoardSceneFixtures.CreateViewport(BoardSceneFixtures.DefaultViewportSize)));

    [Fact]
    public void MultiStrokeAlphaBlending_MatchesBaseline() => RunSnapshot(
        nameof(MultiStrokeAlphaBlending_MatchesBaseline),
        () => (BoardSceneFixtures.CreateMultiStrokeBlendingDocument(), null, BoardSceneFixtures.CreateViewport(BoardSceneFixtures.DefaultViewportSize)));

    [Fact]
    public void ViewportTransformedScene_MatchesBaseline()
    {
        // 场景 4：同一文档经缩放 + 相机平移渲染，验证 WithWorldTransform 的世界→屏幕变换正确性
        // （笔迹被放大且部分移出视野，同时验证可见性裁剪）。
        RunSnapshot(
            nameof(ViewportTransformedScene_MatchesBaseline),
            () => (
                BoardSceneFixtures.CreateSingleStrokeDocument(),
                null,
                BoardSceneFixtures.CreateViewport(
                    BoardSceneFixtures.DefaultViewportSize,
                    cameraWorld: new System.Numerics.Vector2(40.0f, -30.0f),
                    zoom: 1.5f)));
    }

    [Fact]
    public void ShapesAllKinds_MatchesBaseline() => RunSnapshot(
        nameof(ShapesAllKinds_MatchesBaseline),
        () => (BoardSceneFixtures.CreateShapesDocument(), null, BoardSceneFixtures.CreateViewport(BoardSceneFixtures.DefaultViewportSize)));

    [Fact]
    public void TextCardDarkTheme_MatchesBaseline() => RunTextCardSnapshot(
        nameof(TextCardDarkTheme_MatchesBaseline), ElementCardTheme.Dark);

    [Fact]
    public void TextCardLightTheme_MatchesBaseline() => RunTextCardSnapshot(
        nameof(TextCardLightTheme_MatchesBaseline), ElementCardTheme.Light);

    /// <summary>
    /// 通用快照流程：渲染 → 基准缺失时生成并跳过 → 比对 → 失败输出三件套并断言失败。
    /// </summary>
    private static void RunSnapshot(
        string snapshotName,
        Func<(BoardDocument Document, IBoardInkItem? ActiveInkItem, BoardViewport Viewport)> sceneFactory,
        int tolerance = SnapshotComparer.DefaultTolerance,
        double maxDiffPixelRatio = 0.0)
    {
        (BoardDocument document, IBoardInkItem? activeInkItem, BoardViewport viewport) = sceneFactory();

        using var harness = new OffscreenRenderHarness(Width, Height);
        byte[] actual = harness.Render(document, activeInkItem, viewport);

        if (!SnapshotBaseline.TryLoadOrRegenerate(snapshotName, actual, Width, Height, out byte[] expected))
        {
            // 显式重建（WINDBOARD_REGEN_SNAPSHOTS=1）：本次跳过断言，结果已写入 __snapshots__。
            return;
        }

        SnapshotCompareResult result = SnapshotComparer.Compare(expected, actual, Width, Height, tolerance);
        if (result.DiffPixelRatio <= maxDiffPixelRatio)
        {
            return;
        }

        string artifactsDir = SnapshotBaseline.WriteFailureArtifacts(
            snapshotName, expected, actual, result.DiffBgra, Width, Height);
        Assert.Fail(SnapshotBaseline.BuildFailureMessage(
            snapshotName, result, tolerance, maxDiffPixelRatio, artifactsDir));
    }

    /// <summary>
    /// 文本卡片快照：渲染前固定 zh-CN 区域性并把进程级 MRT PrimaryLanguageOverride 固定为 zh-CN。
    /// </summary>
    /// <remarks>
    /// 卡片标题/提示文案经 L10n 按 CurrentUICulture 解析（资源含 zh-CN/en-US），
    /// 不固定区域性时跨机器 UI 语言差异会导致文本内容不同、基准不可比。
    ///
    /// 另需固定 <c>ApplicationLanguages.PrimaryLanguageOverride</c>（进程级全局状态）为 zh-CN：
    /// MRT 候选语言会受该 override 影响，与本类显式设置的 zh-CN 上下文不一致时，
    /// L10n 判定语言不匹配而回退输出 key 字符串，导致基准比对失败。
    /// 语言状态的捕获/还原统一走 <see cref="TestLanguageState"/>（含 unpackaged 环境无法用空串
    /// 清除 override 的降级处理）；与可能改写语言的 AppSettingsServiceTests 同属
    /// <see cref="ProcessGlobalLanguageState"/>，由 xUnit 串行执行，消除写读竞态。
    /// </remarks>
    private static void RunTextCardSnapshot(string snapshotName, ElementCardTheme theme)
    {
        TestLanguageState.Snapshot languageState = TestLanguageState.Capture();
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            CultureInfo.CurrentCulture = new CultureInfo("zh-CN");
            TestLanguageState.SetPrimaryLanguageOverrideBestEffort("zh-CN");

            var document = BoardSceneFixtures.CreateTextCardDocument();
            using var harness = new OffscreenRenderHarness(Width, Height);
            byte[] actual = harness.Render(ctx =>
            {
                var renderer = new BoardSceneRenderer { ElementCardTheme = theme };
                renderer.Draw(
                    ctx,
                    document,
                    activeInkItem: null,
                    BoardSceneFixtures.CreateViewport(BoardSceneFixtures.DefaultViewportSize));
                renderer.Dispose();
            });

            if (!SnapshotBaseline.TryLoadOrRegenerate(snapshotName, actual, Width, Height, out byte[] expected))
            {
                return;
            }

            SnapshotCompareResult result = SnapshotComparer.Compare(
                expected, actual, Width, Height, SnapshotComparer.DefaultTolerance);
            if (result.DiffPixelRatio <= TextSceneMaxDiffPixelRatio)
            {
                return;
            }

            string artifactsDir = SnapshotBaseline.WriteFailureArtifacts(
                snapshotName, expected, actual, result.DiffBgra, Width, Height);
            Assert.Fail(SnapshotBaseline.BuildFailureMessage(
                snapshotName, result, SnapshotComparer.DefaultTolerance, TextSceneMaxDiffPixelRatio, artifactsDir));
        }
        finally
        {
            TestLanguageState.Restore(languageState);
        }
    }
}
