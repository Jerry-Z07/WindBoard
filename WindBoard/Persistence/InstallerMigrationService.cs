using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Logging;
using WindBoard.Settings;
using WindBoard.Updates;

namespace WindBoard.Persistence
{
    /// <summary>
    /// 旧 Inno 安装版数据迁移的跳过原因（仅决策与日志使用，便于单测断言与故障排查）。
    /// </summary>
    internal enum InstallerMigrationSkipReason
    {
        /// <summary>命中迁移条件：需要弹窗向用户确认。</summary>
        None,

        /// <summary>当前形态不是 MSIX（Store）版：完全不介入。</summary>
        NotMsix,

        /// <summary>自身数据目录已有 settings.json：老用户，不打扰。</summary>
        OwnSettingsExist,

        /// <summary>已写过迁移标记（marker）：不再重复询问。</summary>
        AlreadyMigrated,

        /// <summary>旧数据目录下没有 settings.json。</summary>
        LegacySettingsMissing,

        /// <summary>旧 settings.json 存在但读取失败。</summary>
        LegacySettingsUnreadable,

        /// <summary>旧 settings.json 存在且可读，但 JSON 无法解析。</summary>
        LegacySettingsCorrupt,
    }

    /// <summary>
    /// 迁移决策输入（“已发生的事实”）：全部由 <see cref="InstallerMigrationService"/> 采集。
    /// 决策本身不触碰文件系统/注册表/UI，便于单元测试覆盖各分支。
    /// </summary>
    internal sealed class InstallerMigrationFacts
    {
        /// <summary>当前运行实例的安装形态。</summary>
        internal AppInstallKind InstallKind { get; init; } = AppInstallKind.Unknown;

        /// <summary>自身数据目录（MSIX 下为写入落点）是否已有 settings.json。</summary>
        internal bool OwnSettingsFileExists { get; init; }

        /// <summary>
        /// 自身数据目录与旧安装版数据目录是否路径重合。
        ///
        /// MSIX 虚拟化下“读取先私有位置、未命中回落真实目录”（design.md §1.2），路径重合时无法凭路径判断
        /// 读到的是自身文件还是旧文件；该情况不按“老用户”处理（继续走弹窗：确认则导入、取消仅写 marker，均无副作用）。
        /// </summary>
        internal bool OwnSettingsFileSharedWithLegacyData { get; init; }

        /// <summary>自身数据目录是否已有迁移标记（marker）。</summary>
        internal bool MigrationMarkerExists { get; init; }

        /// <summary>旧安装版数据目录下是否存在 settings.json。</summary>
        internal bool LegacySettingsFileExists { get; init; }

        /// <summary>旧 settings.json 的文本内容（不存在或读取失败时为 null）。</summary>
        internal string? LegacySettingsJson { get; init; }

        /// <summary>旧 settings.json 的读取失败原因（读取成功时为 null）。</summary>
        internal string? LegacySettingsReadError { get; init; }

        /// <summary>旧安装版数据目录（仅诊断/日志）。</summary>
        internal string LegacyDataDirectory { get; init; } = string.Empty;

        /// <summary>旧 settings.json 的完整路径（仅诊断与弹窗展示）。</summary>
        internal string LegacySettingsFilePath { get; init; } = string.Empty;

        /// <summary>
        /// 旧安装版的安装目录（来自 HKLM 标记，仅用于展示旧安装路径，不作为形态判定依据）。
        /// </summary>
        internal string LegacyInstallDir { get; init; } = string.Empty;
    }

    /// <summary>
    /// 迁移决策结果：是否需要弹窗确认，以及决策依据（跳过原因/告警）。
    /// </summary>
    internal sealed class InstallerMigrationEvaluation
    {
        /// <summary>命中迁移条件：需要弹窗向用户确认。</summary>
        internal bool ShouldPrompt { get; init; }

        /// <summary>跳过原因（<see cref="InstallerMigrationSkipReason.None"/> 表示命中）。</summary>
        internal InstallerMigrationSkipReason SkipReason { get; init; } = InstallerMigrationSkipReason.None;

        /// <summary>旧安装版数据目录（仅日志/诊断）。</summary>
        internal string LegacyDataDirectory { get; init; } = string.Empty;

        /// <summary>旧 settings.json 的完整路径。</summary>
        internal string LegacySettingsFilePath { get; init; } = string.Empty;

        /// <summary>旧安装版的安装目录（仅展示，可能为空）。</summary>
        internal string LegacyInstallDir { get; init; } = string.Empty;

        /// <summary>
        /// 旧设置的归一化问题数量。
        /// 说明：这里只统计（供日志）；真正的修复与用户提示仍走既有导入链路（<c>AppSettingsService.ImportFromFileAsync</c>）。
        /// </summary>
        internal int NormalizationIssueCount { get; init; }

        /// <summary>需要记录的告警（旧文件不可读/JSON 损坏等；无告警时为 null）。</summary>
        internal string? Warning { get; init; }
    }

    /// <summary>
    /// 迁移决策纯逻辑：输入“已发生事实”，输出“是否弹窗 + 结果/告警”，不做任何 I/O。
    /// </summary>
    internal static class InstallerMigrationPolicy
    {
        /// <summary>
        /// 评估是否需要向用户提示导入旧安装版设置（design.md §3 的决策顺序）。
        /// </summary>
        internal static InstallerMigrationEvaluation Evaluate(InstallerMigrationFacts facts)
        {
            ArgumentNullException.ThrowIfNull(facts);

            // 1) 非 MSIX（便携版/旧安装版）：不介入，行为与改动前一致。
            if (facts.InstallKind != AppInstallKind.Msix)
            {
                return Skip(InstallerMigrationSkipReason.NotMsix, facts);
            }

            // 2) 自身已有 settings.json（且不是与旧目录重合导致无法区分的情况）→ 老用户，不打扰。
            if (facts.OwnSettingsFileExists && !facts.OwnSettingsFileSharedWithLegacyData)
            {
                return Skip(InstallerMigrationSkipReason.OwnSettingsExist, facts);
            }

            // 3) 已迁移（marker 存在）→ 不再重复询问。
            if (facts.MigrationMarkerExists)
            {
                return Skip(InstallerMigrationSkipReason.AlreadyMigrated, facts);
            }

            // 4) 旧数据不存在/不可读 → 无迁移标的，跳过。
            if (string.IsNullOrWhiteSpace(facts.LegacySettingsFilePath) || !facts.LegacySettingsFileExists)
            {
                return Skip(InstallerMigrationSkipReason.LegacySettingsMissing, facts);
            }

            if (!string.IsNullOrWhiteSpace(facts.LegacySettingsReadError))
            {
                return Skip(
                    InstallerMigrationSkipReason.LegacySettingsUnreadable,
                    facts,
                    warning: $"旧版设置读取失败，已跳过迁移：{facts.LegacySettingsReadError}");
            }

            if (string.IsNullOrWhiteSpace(facts.LegacySettingsJson))
            {
                return Skip(InstallerMigrationSkipReason.LegacySettingsMissing, facts);
            }

            // 5) JSON 损坏：不崩溃、记录告警并跳过。
            //    不写 marker：旧文件被修好后（例如旧版再次正常落盘）下次启动仍可迁移。
            var report = new SettingsNormalizationReport();
            if (!TryParseLegacySettings(facts.LegacySettingsJson, report, out string? parseError))
            {
                return Skip(
                    InstallerMigrationSkipReason.LegacySettingsCorrupt,
                    facts,
                    warning: $"旧版设置解析失败，已跳过迁移：{parseError}");
            }

            // 6) 命中：弹窗确认。
            return new InstallerMigrationEvaluation
            {
                ShouldPrompt = true,
                SkipReason = InstallerMigrationSkipReason.None,
                LegacyDataDirectory = facts.LegacyDataDirectory,
                LegacySettingsFilePath = facts.LegacySettingsFilePath,
                LegacyInstallDir = facts.LegacyInstallDir,
                NormalizationIssueCount = report.KeyboardShortcutIssues.Count,
            };
        }

        /// <summary>
        /// 解析旧版 settings.json 文本（纯逻辑，不抛异常）。
        /// 复用既有 <see cref="AppSettingsStore.Deserialize"/>（含归一化），归一化问题通过 <paramref name="report"/> 透传。
        /// </summary>
        internal static bool TryParseLegacySettings(
            string json,
            SettingsNormalizationReport report,
            out string? errorMessage)
        {
            ArgumentNullException.ThrowIfNull(report);

            errorMessage = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "旧版设置为空。";
                return false;
            }

            try
            {
                AppSettingsStore.Deserialize(json, report);
                return true;
            }
            catch (JsonException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static InstallerMigrationEvaluation Skip(
            InstallerMigrationSkipReason reason,
            InstallerMigrationFacts facts,
            string? warning = null)
        {
            return new InstallerMigrationEvaluation
            {
                ShouldPrompt = false,
                SkipReason = reason,
                LegacyDataDirectory = facts.LegacyDataDirectory,
                LegacySettingsFilePath = facts.LegacySettingsFilePath,
                LegacyInstallDir = facts.LegacyInstallDir,
                Warning = warning,
            };
        }
    }

    /// <summary>
    /// 旧 Inno 安装版数据迁移（MSIX 形态首次运行）：
    /// - 决策：<see cref="InstallerMigrationPolicy"/>（纯逻辑，可单测）；
    /// - 薄壳：采集事实、只读旧 settings.json（不写回旧目录）、复用既有导入链路写入自身数据目录、写迁移标记。
    ///
    /// 说明：
    /// - 本类不做 UI（弹窗在 UI 层），也不依赖注册表判定形态；
    /// - 任何失败都只记录日志，不阻断启动。
    /// </summary>
    internal sealed class InstallerMigrationService
    {
        /// <summary>迁移标记文件名（放在自身数据目录，不依赖注册表）。</summary>
        internal const string MarkerFileName = "migrated-from-installer.flag";

        private const string SettingsFileName = "settings.json";
        private const string LogCategory = "Migration";

        internal static InstallerMigrationService Instance { get; } = new();

        private InstallerMigrationService()
        {
        }

        /// <summary>
        /// 采集“已发生事实”并给出迁移决策。
        /// 注意：只有 MSIX 形态才会触碰文件系统（便携版/旧安装版零 I/O、行为不变）。
        /// </summary>
        internal InstallerMigrationEvaluation Evaluate()
        {
            AppInstallKind kind = AppDataPaths.InstallKind;
            if (kind != AppInstallKind.Msix)
            {
                return InstallerMigrationPolicy.Evaluate(new InstallerMigrationFacts { InstallKind = kind });
            }

            string ownDataDirectory = AppDataPaths.OwnDataDirectory.Trim();
            string legacyDataDirectory = AppDataPaths.LegacyInstallerDataDirectory.Trim();
            string ownSettingsPath = CombinePath(ownDataDirectory, SettingsFileName);
            string legacySettingsPath = CombinePath(legacyDataDirectory, SettingsFileName);

            (bool legacyExists, string? legacyJson, string? legacyReadError) = TryReadLegacySettings(legacySettingsPath);

            var facts = new InstallerMigrationFacts
            {
                InstallKind = kind,
                OwnSettingsFileExists = SafeFileExists(ownSettingsPath),
                OwnSettingsFileSharedWithLegacyData = IsSameDirectory(ownDataDirectory, legacyDataDirectory),
                MigrationMarkerExists = SafeFileExists(GetMarkerFilePath(ownDataDirectory)),
                LegacySettingsFileExists = legacyExists,
                LegacySettingsJson = legacyJson,
                LegacySettingsReadError = legacyReadError,
                LegacyDataDirectory = legacyDataDirectory,
                LegacySettingsFilePath = legacySettingsPath,
                LegacyInstallDir = AppInstallProbe.TryReadInstallerRegistryInstallDir(),
            };

            InstallerMigrationEvaluation evaluation = InstallerMigrationPolicy.Evaluate(facts);
            LogEvaluation(evaluation, ownDataDirectory, legacyDataDirectory);
            return evaluation;
        }

        /// <summary>
        /// 导入旧版设置：
        /// - 只读旧 settings.json，写入自身（虚拟化）数据目录，绝不写回旧目录；
        /// - 复用 <see cref="AppSettingsService.ImportFromFileAsync"/>（整包替换 + 归一化 + 语言同步 + 原子落盘），
        ///   确认后设置即时生效，不新增一套反序列化/保存实现；
        /// - 成功后写迁移标记；失败只记录日志（不写 marker，下次启动可重试）。
        /// </summary>
        /// <returns>true 表示已导入；false 表示未导入（调用方可保持静默，用户不受影响）。</returns>
        internal async Task<bool> TryImportAsync(string legacySettingsFilePath, CancellationToken cancellationToken = default)
        {
            string path = (legacySettingsFilePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                AppLog.Warn(LogCategory, "跳过导入：旧版设置文件路径为空");
                return false;
            }

            try
            {
                await AppSettingsService.Instance.ImportFromFileAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error(LogCategory, $"导入旧版设置失败：legacy='{path}'", ex);
                return false;
            }

            AppLog.Info(LogCategory, $"已导入旧版设置：legacy='{path}', own='{AppDataPaths.SettingsFilePath}'");
            TryWriteMarker(AppDataPaths.OwnDataDirectory, action: "imported");
            return true;
        }

        /// <summary>用户选择不导入：仅写迁移标记，不再重复询问。</summary>
        internal void MarkDeclined()
        {
            bool written = TryWriteMarker(AppDataPaths.OwnDataDirectory, action: "declined");
            AppLog.Info(LogCategory, $"用户选择不导入旧版设置：markerWritten={written}");
        }

        /// <summary>迁移标记文件路径（自身数据目录）。</summary>
        internal static string GetMarkerFilePath(string ownDataDirectory)
        {
            return CombinePath(ownDataDirectory, MarkerFileName);
        }

        /// <summary>
        /// 写入迁移标记（独立可测，便于验证“取消后不再询问”）。
        /// 内容仅用于诊断；写入失败只记录日志（可能导致下次启动再问一次，不影响功能）。
        /// </summary>
        internal bool TryWriteMarker(string ownDataDirectory, string action)
        {
            string path = GetMarkerFilePath(ownDataDirectory);
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 与设置落盘一致：临时文件 + 替换，降低写入中断导致文件损坏的概率。
                string content = string.Format(
                    CultureInfo.InvariantCulture,
                    "action={0}{1}utc={2:O}{1}own={3}{1}legacy={4}{1}",
                    action,
                    Environment.NewLine,
                    DateTimeOffset.UtcNow,
                    ownDataDirectory,
                    AppDataPaths.LegacyInstallerDataDirectory);

                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, content);
                File.Move(tempPath, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogCategory, $"写入迁移标记失败：path='{path}'", ex);
                return false;
            }
        }

        private static void LogEvaluation(
            InstallerMigrationEvaluation evaluation,
            string ownDataDirectory,
            string legacyDataDirectory)
        {
            if (evaluation.ShouldPrompt)
            {
                AppLog.Info(
                    LogCategory,
                    $"检测到旧安装版数据，将提示用户导入：legacy='{legacyDataDirectory}', own='{ownDataDirectory}'");
            }
            else
            {
                // 常规跳过（非 MSIX / 老用户 / 已迁移 / 无旧数据）用 Debug：Release 默认不落盘，避免每次启动刷日志。
                AppLog.Debug(LogCategory, $"跳过旧安装版数据迁移：reason={evaluation.SkipReason}");
            }

            if (!string.IsNullOrWhiteSpace(evaluation.Warning))
            {
                AppLog.Warn(LogCategory, evaluation.Warning);
            }

            if (evaluation.NormalizationIssueCount > 0)
            {
                AppLog.Info(
                    LogCategory,
                    $"旧版设置存在 {evaluation.NormalizationIssueCount} 项归一化问题，导入时将按既有规则自动修复");
            }
        }

        private static (bool exists, string? json, string? error) TryReadLegacySettings(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (false, null, null);
            }

            try
            {
                if (!File.Exists(path))
                {
                    return (false, null, null);
                }

                return (true, File.ReadAllText(path), null);
            }
            catch (Exception ex)
            {
                // 读取失败不影响启动：记录日志并把原因交给决策（跳过迁移）。
                AppLog.Warn(LogCategory, $"读取旧版设置失败，将跳过迁移：path='{path}'", ex);
                return (true, null, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool SafeFileExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogCategory, $"检查文件是否存在失败：path='{path}'", ex);
                return false;
            }
        }

        private static string CombinePath(string directory, string fileName)
        {
            string dir = (directory ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(dir) ? string.Empty : Path.Combine(dir, fileName);
        }

        private static bool IsSameDirectory(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
