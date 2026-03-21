using Xunit;

namespace WindBoard.Tests.Logging;

public sealed class LogNoiseAuditTests
{
    [Fact]
    public void HighNoiseLogSnippets_Should_Not_Exist_In_ApplicationSources()
    {
        DirectoryInfo? repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return;
        }

        string windBoardDir = Path.Combine(repoRoot.FullName, "WindBoard");
        if (!Directory.Exists(windBoardDir))
        {
            return;
        }

        string[] bannedSnippets =
        [
            "Windows 通知通道注册成功",
            "数据目录：root=",
            "用户设置启动窗口形态：value=",
            "用户设置“最小化进入屏幕批注”：enabled=",
            "用户切换语言偏好：value=",
            "快捷键冲突提醒开关已更新：enabled=",
            "快捷键已更新：slot=",
            "快捷键已恢复默认值",
            "自动检查更新频率变更：interval=",
            "用户选择下载源：auto",
            "用户选择下载源：fixed/",
            "用户点击检查更新",
            "添加工作区文件到队列：source=",
            "添加文件到队列：source=",
            "添加文本到队列：length=",
            "添加链接到队列：parsed=",
            "清空导入队列：count=",
            "移除队列项：kind=",
            "开始导入 WBIX：path=",
            "开始导入 WBI：path=",
            "替换工作区：pages=",
            "覆盖当前页并插入：workspaceCurrent=",
            "覆盖导入完成：switchTo=",
            "追加页面：startIndex=",
            "开始导入：images=",
            "导入完成：created=",
            "开始导出：format=",
            "导出 WBIX：path=",
            "导出 WBIX 完成：path=",
            "导出 PDF：path=",
            "导出 PDF 完成：path=",
            "导出 PNG：path=",
            "导出 PNG 完成：path=",
            "批量导出 PNG：folder=",
            "批量导出 PNG 完成：folder=",
            "打开链接：",
            "程序启动：target=",
            "打开文件/文件夹：'",
            "开始进入屏幕批注：source=",
            "进入屏幕批注成功。",
            "开始退出屏幕批注：restoreOwnerWindow=",
            "退出屏幕批注完成。",
            "模式切换：mode=",
            "检测到主窗口重新激活，开始退出屏幕批注：state=",
            "检测到桌面批注窗口被关闭，开始执行回收流程。",
            "主窗口触发进入屏幕批注成功：source=",
            "已发送 Windows 通知：signature=",
            "已展示应用内弹条：signature=",
            "已捕获系统语言：culture=",
            "语言已应用：preference=",
            "清空 PrimaryLanguageOverride 失败，已降级为设置为系统语言：fallback=",
            "未提供语言资源：culture=",
            "已应用图标字体资源：SymbolThemeFontFamily=",
            "检测到 Win11+（build=",
            "检测到系统已安装图标字体：",
            "已私有加载图标字体：family='",
            "开始下载源测速：installKind=",
            "开始获取最新版本信息：source=",
            "下载完成：source=",
            "断点续传不可用，将从头下载：source=",
            "远端未返回 PartialContent，将从头下载：source=",
        ];

        List<string> matches = CollectMatches(windBoardDir, bannedSnippets);

        Assert.True(
            matches.Count == 0,
            "发现不应保留的高噪声日志片段：\n" + string.Join('\n', matches));
    }

    private static DirectoryInfo? FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 20 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "WindBoard.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static List<string> CollectMatches(string windBoardDir, IEnumerable<string> bannedSnippets)
    {
        var matches = new List<string>();

        foreach (string filePath in EnumerateProjectFiles(windBoardDir))
        {
            string text = File.ReadAllText(filePath);
            foreach (string snippet in bannedSnippets)
            {
                int index = text.IndexOf(snippet, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                (int line, int column) = GetLineColumn(text, index);
                matches.Add($"{NormalizePath(filePath)}:{line}:{column} - {snippet}");
            }
        }

        return matches;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string windBoardDir)
    {
        foreach (string filePath in Directory.EnumerateFiles(windBoardDir, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return filePath;
        }
    }

    private static (int line, int column) GetLineColumn(string text, int index)
    {
        int line = 1;
        int lastNewLine = -1;

        int max = Math.Min(index, text.Length);
        for (int i = 0; i < max; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastNewLine = i;
            }
        }

        return (line, index - lastNewLine);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
