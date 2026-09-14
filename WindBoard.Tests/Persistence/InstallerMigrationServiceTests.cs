using WindBoard.Persistence;
using WindBoard.Settings;
using WindBoard.Updates;

namespace WindBoard.Tests.Persistence;

/// <summary>
/// 旧安装版数据迁移（MSIX 首次运行）单测：
/// - 决策与 JSON 解析是纯逻辑（<see cref="InstallerMigrationPolicy"/>），直接注入“已发生事实”覆盖各分支；
/// - 迁移标记写入用临时目录验证（对应“取消后不再询问”）。
///
/// 说明：本测试类不改写 <c>CultureInfo</c> / MRT <c>PrimaryLanguageOverride</c>，
/// 无需加入 <c>ProcessGlobalLanguageState</c> 集合串行执行。
/// </summary>
public sealed class InstallerMigrationServiceTests
{
    private const string ValidLegacySettingsJson = """
        {
          "general": { "languagePreference": "zh-CN", "startupWindowMode": "windowed" }
        }
        """;

    private const string InvalidShortcutLegacySettingsJson = """
        {
          "general": { "languagePreference": "zh-CN" },
          "keyboardShortcuts": { "undo": "Ctrl+Z+Y" }
        }
        """;

    // ---- 决策规则 ----

    // 说明：AppInstallKind 是 internal 类型，不能出现在 public 测试方法的签名里（CS0051），故按名称传参。
    [Theory]
    [InlineData("Portable")]
    [InlineData("Installer")]
    [InlineData("Unknown")]
    public void Evaluate_WhenInstallKindIsNotMsix_SkipsWithoutPrompt(string installKindName)
    {
        // 便携版/旧安装版：即使存在旧数据也不介入，保证这两种形态行为零变化。
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(installKind: Enum.Parse<AppInstallKind>(installKindName)));

        Assert.False(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.NotMsix, evaluation.SkipReason);
    }

    [Fact]
    public void Evaluate_WhenOwnSettingsExist_SkipsAsExistingUser()
    {
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(ownSettingsFileExists: true));

        Assert.False(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.OwnSettingsExist, evaluation.SkipReason);
    }

    [Fact]
    public void Evaluate_WhenOwnSettingsSharedWithLegacyData_Prompts()
    {
        // MSIX 虚拟化下自身目录可能与旧目录路径重合（读取先私有后回落）：无法区分归属时不按“老用户”处理。
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(ownSettingsFileExists: true, ownSettingsFileSharedWithLegacyData: true));

        Assert.True(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.None, evaluation.SkipReason);
    }

    [Fact]
    public void Evaluate_WhenMigrationMarkerExists_Skips()
    {
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(migrationMarkerExists: true));

        Assert.False(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.AlreadyMigrated, evaluation.SkipReason);
    }

    [Fact]
    public void Evaluate_WhenLegacySettingsFileMissing_Skips()
    {
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(legacySettingsFileExists: false, legacySettingsJson: null));

        Assert.False(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.LegacySettingsMissing, evaluation.SkipReason);
    }

    [Fact]
    public void Evaluate_WhenLegacySettingsUnreadable_SkipsWithWarning()
    {
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(legacySettingsJson: null, legacySettingsReadError: "IOException: 拒绝访问"));

        Assert.False(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.LegacySettingsUnreadable, evaluation.SkipReason);
        Assert.False(string.IsNullOrWhiteSpace(evaluation.Warning));
    }

    [Fact]
    public void Evaluate_WhenLegacySettingsCorrupt_DoesNotThrow_AndSkipsWithWarning()
    {
        // 旧 JSON 损坏：不崩溃、记录告警并跳过；不写 marker（旧文件修好后仍可迁移）。
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(legacySettingsJson: "{ \"general\": "));

        Assert.False(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.LegacySettingsCorrupt, evaluation.SkipReason);
        Assert.False(string.IsNullOrWhiteSpace(evaluation.Warning));
    }

    [Fact]
    public void Evaluate_WhenLegacySettingsAvailable_PromptsWithPaths()
    {
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(CreateFacts());

        Assert.True(evaluation.ShouldPrompt);
        Assert.Equal(InstallerMigrationSkipReason.None, evaluation.SkipReason);
        Assert.Equal(@"C:\Users\test\AppData\Local\WindBoard\settings.json", evaluation.LegacySettingsFilePath);
        Assert.Equal(@"C:\Program Files\WindBoard", evaluation.LegacyInstallDir);
        Assert.Equal(0, evaluation.NormalizationIssueCount);
        Assert.Null(evaluation.Warning);
    }

    [Fact]
    public void Evaluate_WhenLegacySettingsHaveInvalidShortcut_PromptsAndPassesNormalizationIssuesThrough()
    {
        InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
            CreateFacts(legacySettingsJson: InvalidShortcutLegacySettingsJson));

        // 归一化问题不影响“命中迁移”，但必须透传出来（供日志/既有提示链路使用）。
        Assert.True(evaluation.ShouldPrompt);
        Assert.Equal(1, evaluation.NormalizationIssueCount);
    }

    // ---- JSON 解析（复用既有反序列化 + 归一化） ----

    [Fact]
    public void TryParseLegacySettings_WhenJsonIsValid_ReturnsTrue()
    {
        var report = new SettingsNormalizationReport();

        bool ok = InstallerMigrationPolicy.TryParseLegacySettings(ValidLegacySettingsJson, report, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Empty(report.KeyboardShortcutIssues);
    }

    [Fact]
    public void TryParseLegacySettings_WhenJsonIsInvalid_ReturnsFalseWithError()
    {
        var report = new SettingsNormalizationReport();

        bool ok = InstallerMigrationPolicy.TryParseLegacySettings("not-json", report, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseLegacySettings_WhenJsonIsEmpty_ReturnsFalse(string json)
    {
        var report = new SettingsNormalizationReport();

        bool ok = InstallerMigrationPolicy.TryParseLegacySettings(json, report, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ---- 迁移标记：用户取消后不再询问 ----

    [Fact]
    public void TryWriteMarker_ThenMarkerExists_AndEvaluationSkipsAsAlreadyMigrated()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"wb_migration_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            bool written = InstallerMigrationService.Instance.TryWriteMarker(tempDirectory, action: "declined");
            Assert.True(written);

            string markerPath = InstallerMigrationService.GetMarkerFilePath(tempDirectory);
            Assert.Equal(Path.Combine(tempDirectory, InstallerMigrationService.MarkerFileName), markerPath);
            Assert.True(File.Exists(markerPath));
            Assert.Contains("action=declined", File.ReadAllText(markerPath), StringComparison.Ordinal);

            // 下次启动：marker 存在 → 跳过，不再重复询问。
            InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(
                CreateFacts(migrationMarkerExists: File.Exists(markerPath)));

            Assert.False(evaluation.ShouldPrompt);
            Assert.Equal(InstallerMigrationSkipReason.AlreadyMigrated, evaluation.SkipReason);
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { }
        }
    }

    private static InstallerMigrationFacts CreateFacts(
        AppInstallKind installKind = AppInstallKind.Msix,
        bool ownSettingsFileExists = false,
        bool ownSettingsFileSharedWithLegacyData = false,
        bool migrationMarkerExists = false,
        bool legacySettingsFileExists = true,
        string? legacySettingsJson = ValidLegacySettingsJson,
        string? legacySettingsReadError = null,
        string legacyInstallDir = @"C:\Program Files\WindBoard")
    {
        return new InstallerMigrationFacts
        {
            InstallKind = installKind,
            OwnSettingsFileExists = ownSettingsFileExists,
            OwnSettingsFileSharedWithLegacyData = ownSettingsFileSharedWithLegacyData,
            MigrationMarkerExists = migrationMarkerExists,
            LegacySettingsFileExists = legacySettingsFileExists,
            LegacySettingsJson = legacySettingsJson,
            LegacySettingsReadError = legacySettingsReadError,
            LegacyDataDirectory = @"C:\Users\test\AppData\Local\WindBoard",
            LegacySettingsFilePath = @"C:\Users\test\AppData\Local\WindBoard\settings.json",
            LegacyInstallDir = legacyInstallDir,
        };
    }
}
