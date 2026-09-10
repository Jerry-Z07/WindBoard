using System;
using System.Globalization;
using System.IO;

namespace WindBoard.Tests.Rendering.Snapshot;

/// <summary>
/// 快照基准管理：基准 PNG 存放于 <c>WindBoard.Tests/Rendering/__snapshots__/</c>（按测试名命名，纳入版本库）。
/// </summary>
/// <remarks>
/// 基准（重新）生成机制：
/// - 基准缺失时自动写入并跳过断言（首次生成/新场景）；
/// - 环境变量 <c>WINDBOARD_REGEN_SNAPSHOTS=1</c> 时强制用本次渲染结果覆盖基准并跳过断言，
///   用于渲染行为有意变更后重建基准。基准变更须在提交说明中说明理由。
///
/// 基准生成环境约定（保证跨机器可比）：
/// - WARP 软件渲染 + 96 DPI（1 DIP = 1 像素），无 GPU/显示器依赖；
/// - 清屏色固定为 #2E2F33（与主程序默认画布底色一致）；
/// - 文本场景在测试内固定 zh-CN 区域性；跨机器文本/图标字体版本差异由每通道容差与
///   场景级差异像素占比阈值吸收，若仍不稳定按 prd 约定降级为结构断言。
/// </remarks>
internal static class SnapshotBaseline
{
    /// <summary>强制重新生成基准的环境变量名。</summary>
    internal const string RegenerateEnvVarName = "WINDBOARD_REGEN_SNAPSHOTS";

    /// <summary>基准 PNG 相对测试工程的目录。</summary>
    private const string SnapshotRelativeDir = "Rendering/__snapshots__";

    /// <summary>失败诊断产物相对测试工程的目录（不入版本库）。</summary>
    private const string FailureArtifactsRelativeDir = "artifacts/snapshot-failures";

    /// <summary>是否要求强制重新生成基准。</summary>
    internal static bool RegenerateRequested =>
        Environment.GetEnvironmentVariable(RegenerateEnvVarName) == "1";

    /// <summary>解析基准 PNG 的完整路径（按快照名命名，不含扩展名参数）。</summary>
    internal static string ResolveSnapshotPath(string snapshotName)
    {
        string testProjectDir = ResolveTestProjectDir();
        return Path.Combine(testProjectDir, SnapshotRelativeDir, snapshotName + ".png");
    }

    /// <summary>
    /// 加载基准；基准缺失或要求重新生成时，写入 actual 并返回 false（调用方跳过断言）。
    /// </summary>
    internal static bool TryLoadOrRegenerate(
        string snapshotName, byte[] actual, int width, int height, out byte[] expected)
    {
        string path = ResolveSnapshotPath(snapshotName);
        if (!RegenerateRequested && File.Exists(path))
        {
            expected = SnapshotComparer.LoadPng(path, width, height);
            return true;
        }

        Save(snapshotName, actual, width, height);
        expected = [];
        return false;
    }

    /// <summary>判断基准是否存在（供测试跳过逻辑判断，不触发写入）。</summary>
    internal static bool Exists(string snapshotName) => File.Exists(ResolveSnapshotPath(snapshotName));

    /// <summary>写入基准 PNG（目录不存在时自动创建）。</summary>
    internal static void Save(string snapshotName, byte[] bgra, int width, int height)
    {
        string path = ResolveSnapshotPath(snapshotName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SnapshotComparer.SavePng(path, bgra, width, height);
    }

    /// <summary>
    /// 写入失败诊断三件套：<c>expected.png</c> / <c>actual.png</c> / <c>diff.png</c>。
    /// </summary>
    /// <returns>产物目录路径。</returns>
    internal static string WriteFailureArtifacts(
        string snapshotName,
        byte[]? expected,
        byte[] actual,
        byte[] diff,
        int width,
        int height)
    {
        string dir = Path.Combine(ResolveTestProjectDir(), FailureArtifactsRelativeDir, snapshotName);
        Directory.CreateDirectory(dir);

        SnapshotComparer.SavePng(Path.Combine(dir, "actual.png"), actual, width, height);
        SnapshotComparer.SavePng(Path.Combine(dir, "diff.png"), diff, width, height);
        if (expected is not null)
        {
            SnapshotComparer.SavePng(Path.Combine(dir, "expected.png"), expected, width, height);
        }

        return dir;
    }

    /// <summary>构造断言失败消息（含差异像素占比与产物路径，机器可读格式用 InvariantCulture）。</summary>
    internal static string BuildFailureMessage(
        string snapshotName,
        SnapshotCompareResult result,
        int tolerance,
        double maxDiffPixelRatio,
        string artifactsDir)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "渲染快照不匹配：{0}\n"
            + "差异像素：{1} / {2}（占比 {3:P2}，阈值 {4:P2}，每通道容差 {5}）\n"
            + "诊断三件套（expected/actual/diff）：{6}",
            snapshotName,
            result.DiffPixelCount,
            result.TotalPixels,
            result.DiffPixelRatio,
            maxDiffPixelRatio,
            tolerance,
            artifactsDir);
    }

    private static string ResolveTestProjectDir()
    {
        // 测试输出目录位于 <测试工程>/bin/...，向上回溯到含解决方案文件的仓库根，再进入测试工程目录。
        DirectoryInfo? repoRoot = RepoRootLocator.Find()
            ?? throw new InvalidOperationException("未能定位仓库根（缺少 WindBoard.slnx）");

        return Path.Combine(repoRoot.FullName, "WindBoard.Tests");
    }
}
