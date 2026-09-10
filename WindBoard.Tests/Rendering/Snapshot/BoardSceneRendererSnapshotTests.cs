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
/// 同一测试类内的用例由 xUnit 串行执行（D2D 工厂为 SingleThreaded，避免资源竞争）。
///
/// 场景覆盖说明（对应 prd 场景清单 1-8）：
/// - 场景 1-6 为 D2D 像素级快照（本类）；
/// - 场景 7（选中态 overlay：选中框/手柄）与场景 8（框选 marquee 矩形）为 XAML 层元素
///   （BoardCanvasControl 的 Border/Thumb，见 UpdateSelectionOverlay/ShowMarqueeSelectionOverlay），
///   不经过 BoardSceneRenderer 的 D2D 输出，无法做 D2D 像素快照。其逻辑已有结构断言覆盖：
///   marquee 状态机见 SelectToolTests.Marquee_*，选中框几何见 InkItemScreenBoundsTests；
///   XAML 合成层的像素级验证归入 P3 FlaUI E2E（偏差已同步记录到子任务 prd.md）。
/// </remarks>
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
            // 基准首次生成/强制重建：本次跳过断言，结果已写入 __snapshots__。
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
    /// 文本卡片快照：渲染前固定 zh-CN 区域性并把进程级 MRT PrimaryLanguageOverride 覆写为 zh-CN。
    /// </summary>
    /// <remarks>
    /// 卡片标题/提示文案经 L10n 按 CurrentUICulture 解析（资源含 zh-CN/en-US），
    /// 不固定区域性时跨机器 UI 语言差异会导致文本内容不同、基准不可比。
    ///
    /// 另需覆写 <c>ApplicationLanguages.PrimaryLanguageOverride</c>（进程级全局状态）为 zh-CN：
    /// 部分设置类测试（AppSettingsServiceTests）会调用 AppLanguageService.Apply 设置该值且
    /// 其清理逻辑只还原 CultureInfo、不还原 override；残留的 override（如 en-US）会使 MRT 在
    /// zh-CN 上下文下返回 en-US 候选，L10n 判定语言不匹配而回退输出 key 字符串，导致基准比对失败。
    /// 注意：unpackaged 环境下把 override 赋值为空串会抛“未指定的错误”（实测），无法清除，
    /// 因此改为直接覆写为目标语言 zh-CN，finally 再尽力还原原值（还原空串失败时保持 zh-CN 残留，
    /// 不影响其他测试——运行时读取 L10n 的测试仅本类）。
    /// 并行竞态（其他类在“覆写 → 渲染”窗口内改写 override）已由测试程序集级
    /// DisableTestParallelization（TestAssemblyConfig.cs）整体消除。
    /// </remarks>
    private static void RunTextCardSnapshot(string snapshotName, ElementCardTheme theme)
    {
        CultureInfo originalCurrent = CultureInfo.CurrentCulture;
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        string originalPrimaryLanguageOverride = ReadPrimaryLanguageOverride();
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            CultureInfo.CurrentCulture = new CultureInfo("zh-CN");
            SetPrimaryLanguageOverride("zh-CN");

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
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalCurrent;
            SetPrimaryLanguageOverride(originalPrimaryLanguageOverride);
        }
    }

    /// <summary>读取进程级 MRT PrimaryLanguageOverride（不可读时返回空串，视为未设置）。</summary>
    private static string ReadPrimaryLanguageOverride()
    {
        try
        {
            return Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride ?? string.Empty;
        }
        catch
        {
            // 无包身份等环境下该 API 可能不可用：测试环境中无法读取即视为未设置（与 AppLanguageService 的容错一致）。
            return string.Empty;
        }
    }

    /// <summary>尽力设置进程级 MRT PrimaryLanguageOverride（两个 API 变体依次尝试；均不可用时静默跳过）。</summary>
    private static void SetPrimaryLanguageOverride(string value)
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = value;
            return;
        }
        catch
        {
            // 空串赋值在 unpackaged 环境实测会抛“未指定的错误”；非空值在多数环境可成功（见 AppLanguageService 同款降级）。
        }

        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = value;
        }
        catch
        {
            // 双变体均失败：保持当前 override；若与目标语言不一致，文本场景会以明显 diff 失败并产出三件套，不会静默漏检。
        }
    }
}
