using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindBoard.Features.Dock.Models;
using WindBoard.Features.Shortcuts.Models;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

/// <summary>
/// 设置默认值的跨端 golden 测试：把"首次启动写出的默认设置"锁进语料，供网页端逐字段比对。
/// </summary>
/// <remarks>
/// 动机：WBIX 有字节级 golden 语料强制两端一致，设置项此前只靠人工核对（见 <c>docs/dev/guides/web-port.zh-CN.md</c> 的“没有自动保障”清单），桌面改默认色板/默认语言时网页端测试照样全绿；语料只覆盖两端共有的子集（general / appearance / dock / writing.pen / keyboardShortcuts）。
/// 桌面独有的 camouflage / updates / diagnostics / announcements 不参与（网页端不实现这些能力）；语料位于 <c>WindBoard.Tests/Fixtures/settings/defaults.json</c>（LF、两空格缩进、键序固定），重建用 <c>WINBOARD_REGEN_SETTINGS_GOLDEN=1 dotnet test --filter SettingsDefaultsGoldenTests</c>，语料变更须在提交说明中给出理由。
/// </remarks>
public sealed class SettingsDefaultsGoldenTests
{
    /// <summary>强制重新生成语料的环境变量名。</summary>
    internal const string RegenerateEnvVarName = "WINBOARD_REGEN_SETTINGS_GOLDEN";

    /// <summary>语料相对仓库根的路径。</summary>
    private const string FixtureRelativePath = "WindBoard.Tests/Fixtures/settings/defaults.json";

    /// <summary>是否为"重建语料"模式。</summary>
    private static bool RegenerateRequested =>
        Environment.GetEnvironmentVariable(RegenerateEnvVarName) == "1";

    [RepoFact]
    public void DefaultAppSettings_MatchesGoldenFixture()
    {
        string path = ResolveFixturePath();
        string expectedText = BuildDefaultSettingsJson();
        JsonNode expected = ParseOrThrow(expectedText);

        if (RegenerateRequested)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expectedText + "\n");
            return;
        }

        Assert.True(
            File.Exists(path),
            $"缺少设置默认值语料：{path}。用 {RegenerateEnvVarName}=1 重建后提交。");

        string actualText = File.ReadAllText(path);
        JsonNode actual = ParseOrThrow(actualText);

        Assert.True(
            JsonNode.DeepEquals(actual, expected),
            "设置默认值语料与当前桌面默认值不一致（改默认值后需要同步网页端并重建语料）。"
            + Environment.NewLine
            + "语料：" + Environment.NewLine + actualText
            + Environment.NewLine
            + "当前默认值：" + Environment.NewLine + expectedText);
    }

    /// <summary>按语料形态（网页端设置子集、camelCase）序列化默认设置。</summary>
    private static string BuildDefaultSettingsJson()
    {
        // 直接 new 一份设置对象：默认值写在属性初始化器里，改默认值必然反映到这里。
        var settings = new AppSettings();

        var root = new JsonObject
        {
            ["general"] = new JsonObject
            {
                ["languagePreference"] = settings.General.LanguagePreference,
                ["startupWindowMode"] = settings.General.StartupWindowMode,
            },
            ["appearance"] = new JsonObject
            {
                ["canvasBackgroundHex"] = settings.Appearance.CanvasBackgroundHex,
                ["elementCardTheme"] = settings.Appearance.ElementCardTheme,
            },
            ["dock"] = new JsonObject
            {
                ["leftOrder"] = ToJsonArray(settings.Dock.LeftOrder),
                ["toolsOrder"] = ToJsonArray(settings.Dock.ToolsOrder),
                ["undoRedoOrder"] = ToJsonArray(settings.Dock.UndoRedoOrder),
                ["pagesOrder"] = ToJsonArray(settings.Dock.PagesOrder),
                ["isUndoRedoVisible"] = settings.Dock.IsUndoRedoVisible,
                ["isShortcutDocksVisible"] = settings.Dock.IsShortcutDocksVisible,
            },
            ["writing"] = new JsonObject
            {
                ["pen"] = new JsonObject
                {
                    // 空色块（null）在两端都表示"未配置颜色"，必须原样保留占位。
                    ["paletteHexes"] = ToJsonArray(settings.Writing.Pen.PaletteHexes),
                    ["thicknessPresets"] = ToJsonArray(settings.Writing.Pen.ThicknessPresets),
                    ["useThicknessSlider"] = settings.Writing.Pen.UseThicknessSlider,
                },
            },
            ["keyboardShortcuts"] = new JsonObject
            {
                ["conflictReminderEnabled"] = settings.KeyboardShortcuts.ConflictReminderEnabled,
                ["undo"] = settings.KeyboardShortcuts.Undo,
                ["redo"] = settings.KeyboardShortcuts.Redo,
            },
        };

        // 缩进写出；换行统一成 \n，避免 Windows 的 CRLF 与语料检出行尾设置打架。
        string text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static JsonArray ToJsonArray<T>(IEnumerable<T> values)
    {
        var array = new JsonArray();
        foreach (T value in values)
        {
            array.Add(value is null ? null : JsonValue.Create(value));
        }

        return array;
    }

    private static JsonNode ParseOrThrow(string text)
    {
        JsonNode? node = JsonNode.Parse(text);
        if (node is null)
        {
            throw new InvalidOperationException("设置默认值语料不是合法 JSON 对象。");
        }

        return node;
    }

    private static string ResolveFixturePath()
    {
        DirectoryInfo? root = RepoRootLocator.Find()
            ?? throw new InvalidOperationException("未找到仓库根目录（WindBoard.slnx）。");

        return Path.Combine(root.FullName, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
