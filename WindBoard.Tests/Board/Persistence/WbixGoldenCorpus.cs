using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using WindBoard.Board.Persistence.Wbix;

namespace WindBoard.Tests.Board.Persistence;

/// <summary>
/// WBIX 字节级 golden 语料的定位、读取与重建（语料位于 <c>WindBoard.Tests/Fixtures/wbix/</c>，纳入版本库）。
/// </summary>
/// <remarks>
/// 语料结构：<c>model/*.json</c> 是声明式输入模型（测试据此构造 <c>BoardWorkspaceSnapshot</c>）；<c>expected/{模型名}/entries.txt</c> 记 Zip 条目名与写出顺序（每行一个）；<c>expected/{模型名}/&lt;条目名&gt;</c> 为该条目解压后的期望文本（CRLF、无尾随换行）；<c>files/*.wbix</c> 是读侧一致性样本（v3 为生产写侧真实输出，v1/v2 按格式手写）。
/// 重建机制与渲染快照的 <c>WINDBOARD_REGEN_SNAPSHOTS</c> 一致：语料缺失视为测试失败（防止新增模型忘提交语料、语料被误删等情况被静默放过）；<c>WINDBOARD_REGEN_WBIX_GOLDEN=1</c> 时用本次写出结果覆盖语料并跳过断言，用于首次生成或写侧形态有意变更后重建，语料变更须在提交说明中说明理由。
/// 行尾约定：Zip 内 JSON 由 <c>System.Text.Json</c> 缩进写出，Windows 上换行为 CRLF；语料文件按 CRLF 保存（与写出结果一致），比对时仍做 <c>\r\n</c>→<c>\n</c> 归一，以免 <c>core.autocrlf</c> 等检出差异造成假失败；“写侧确实使用 CRLF”由测试单独断言。
/// </remarks>
internal static class WbixGoldenCorpus
{
    /// <summary>强制重新生成语料的环境变量名。</summary>
    internal const string RegenerateEnvVarName = "WINDBOARD_REGEN_WBIX_GOLDEN";

    /// <summary>语料相对测试工程的目录。</summary>
    private const string CorpusRelativeDir = "Fixtures/wbix";

    /// <summary>模型文件的扩展名。</summary>
    private const string ModelExtension = ".json";

    /// <summary>二进制样本文件的扩展名。</summary>
    private const string BinaryExtension = ".wbix";

    /// <summary>是否要求强制重新生成语料。</summary>
    internal static bool RegenerateRequested =>
        Environment.GetEnvironmentVariable(RegenerateEnvVarName) == "1";

    /// <summary>语料根目录的完整路径。</summary>
    internal static string Root => Path.Combine(ResolveTestProjectDir(), CorpusRelativeDir);

    /// <summary>读取模型 JSON 文本。</summary>
    internal static string ReadModelText(string modelName)
    {
        string path = Path.Combine(Root, "model", modelName + ModelExtension);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(BuildMissingMessage(path));
        }

        return File.ReadAllText(path);
    }

    /// <summary>读取条目清单（去空行、去行尾空白）。</summary>
    internal static string[] ReadEntryList(string modelName)
    {
        string path = Path.Combine(Root, "expected", modelName, "entries.txt");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(BuildMissingMessage(path));
        }

        string[] lines = File.ReadAllLines(path);
        var entries = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                entries.Add(trimmed);
            }
        }

        return entries.ToArray();
    }

    /// <summary>读取某条目对应的期望文本（条目名可含 <c>/</c>，映射为子目录）。</summary>
    internal static string ReadExpectedText(string modelName, string entryName)
    {
        string path = ResolveExpectedPath(modelName, entryName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(BuildMissingMessage(path));
        }

        return File.ReadAllText(path);
    }

    /// <summary>写入条目清单（重建用）。</summary>
    internal static void WriteEntryList(string modelName, IReadOnlyList<string> entries)
    {
        string path = Path.Combine(Root, "expected", modelName, "entries.txt");
        EnsureParentDirectory(path);
        File.WriteAllText(path, ToCrlf(string.Join('\n', entries) + "\n"));
    }

    /// <summary>写入某条目的期望文本（重建用）。</summary>
    internal static void WriteExpectedText(string modelName, string entryName, string text)
    {
        string path = ResolveExpectedPath(modelName, entryName);
        EnsureParentDirectory(path);
        File.WriteAllText(path, ToCrlf(text));
    }

    /// <summary>读取二进制样本（如 <c>standard-v3.wbix</c>）。</summary>
    internal static byte[] ReadBinary(string fileName)
    {
        string path = ResolveBinaryPath(fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(BuildMissingMessage(path));
        }

        return File.ReadAllBytes(path);
    }

    /// <summary>写入二进制样本（重建用）。</summary>
    internal static void WriteBinary(string fileName, byte[] bytes)
    {
        string path = ResolveBinaryPath(fileName);
        EnsureParentDirectory(path);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>行尾归一（<c>\r\n</c>→<c>\n</c>），用于跨检出环境比对。</summary>
    internal static string NormalizeEol(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>把任意行尾统一为 CRLF（重建写盘用）。</summary>
    internal static string ToCrlf(string text)
    {
        return NormalizeEol(text).Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static string ResolveExpectedPath(string modelName, string entryName)
    {
        string relative = entryName.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Root, "expected", modelName, relative);
    }

    private static string ResolveBinaryPath(string fileName)
    {
        return Path.Combine(Root, "files", fileName + BinaryExtension);
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string BuildMissingMessage(string path)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "WBIX golden 语料缺失：{0}。新增模型或写侧形态变更时，请设置环境变量 {1}=1 重新生成语料并提交（语料变更须在提交说明中说明理由）。",
            path,
            RegenerateEnvVarName);
    }

    private static string ResolveTestProjectDir()
    {
        // 测试输出目录位于 <测试工程>/bin/...，向上回溯到含解决方案文件的仓库根，再进入测试工程目录。
        DirectoryInfo? repoRoot = RepoRootLocator.Find()
            ?? throw new InvalidOperationException("未能定位仓库根（缺少 WindBoard.slnx）");

        return Path.Combine(repoRoot.FullName, "WindBoard.Tests");
    }
}

/// <summary>
/// golden 语料中的声明式模型（<c>model/*.json</c>）。
/// </summary>
/// <remarks>
/// <see cref="Pages"/> 的每个元素与 WBIX 页文件（<c>pages/page-XXX.json</c>）同构，因此 <see cref="WbixPagePayload"/> 可直接复用生产转换器解析。
/// 元素的 <c>data.layer</c> 决定归入 belowInk 还是 aboveInk（写侧按层重排）；<c>data.order</c> 为负数表示按层内数组下标重排；<c>data.resourceId</c> 不参与输入（写侧由 <see cref="Resources"/> 的 <c>meta</c> 推导）。
/// </remarks>
internal sealed record WbixGoldenModel(
    int CurrentIndex,
    IReadOnlyList<WbixPagePayload> Pages,
    IReadOnlyList<WbixGoldenResource>? Resources = null,
    Vector2? ViewportCameraWorld = null,
    float? ViewportZoom = null,
    Vector2? ViewportSizeDip = null);

/// <summary>
/// golden 语料中的资源声明（二进制以 Base64 内联，避免语料目录混入不可读的占位二进制）。
/// </summary>
internal sealed record WbixGoldenResource(
    string Id,
    string Type,
    string Path,
    string ContentType,
    IReadOnlyDictionary<string, string>? Meta,
    string Base64);
