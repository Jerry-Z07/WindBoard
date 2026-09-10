# P3 FlaUI UI 自动化测试 — 技术设计

## 1. 工程归属

**新建独立项目 `WindBoard.UITests`**（xUnit + FlaUI.UIA3），加入 `WindBoard.slnx`：

| 维度 | 独立项目（采用） | 并入 WindBoard.Tests |
|---|---|---|
| 运行时长 | 可单独跑/可跳过，全量单测用 `--filter Category!=E2E` 排除 | 每次全量多 1~3 分钟 |
| 失败隔离 | E2E 崩溃不影响单测结果解读 | 混杂 |
| 依赖 | FlaUI 只进 E2E 工程 | FlaUI 进单测工程依赖图 |

- 用例统一 `[Trait("Category", "E2E")]`；`dotnet test WindBoard.slnx --filter Category!=E2E` 为单测默认口径，E2E 单独 `--filter Category=E2E` 运行。
- `WindBoard.Tests.csproj` 的 `IsTestProject`/collect 覆盖率配置模式可参考，但不复用工程。

## 2. 依赖与启动

- NuGet：`FlaUI.UIA3`（实现时确认最新稳定版本）+ 传递的 `FlaUI.Core`。
- 应用启动：`FlaUI.Core.Application.Launch(WindBoard.exe 路径)`；路径经 `RepoRootLocator` 风格解析到主工程构建输出（Release|x64 优先，回退 Debug）。
- 窗口等待：`retry` 机制等待主窗口出现（超时 15s），失败即 fail-fast 并截图。
- 生命周期：xUnit `IAsyncLifetime`/fixture 级启动应用，用例级复用；每类共享一个应用实例，测试间通过场景前置恢复状态。

## 3. AutomationId 命名规范

格式：`<区域>_<控件语义名>`，PascalCase + 下划线：

```
SettingsPage_LanguageCombo      SettingsPage_DarkModeToggle
Dock_ToggleButton               Camouflage_EnterButton / Camouflage_ExitButton
Toolbar_ExportPngButton         Toolbar_ImportButton
Pages_NextButton / Pages_PrevButton
```

- XAML 写法：`AutomationProperties.AutomationId="Dock_ToggleButton"`。
- 同名控件在不同实例（如 Dock 每个按钮）需带功能后缀（`Dock_Tool_Pen`）。
- 补充原则：仅 E2E 场景触达路径上的控件；能复用现有 Name/语义文本的不加新 Id。

## 4. 环境隔离（设置与临时文件）

已知事实：设置持久化于 `%LOCALAPPDATA%\WindBoard\settings.json`（`AppSettingsStore.CreateDefault`，主路径解析 + LocalAppData 兜底）。

| 方案 | 说明 | 取舍 |
|---|---|---|
| A. 主工程支持重定向 | 约定环境变量（如 `WINDBOARD_DATA_DIR`）在 `CreateDefault` 中优先解析 | 最干净；**涉及主工程改动，需用户确认后实施** |
| B. 测试前备份/测试后恢复 settings.json | E2E 自管 | 零主工程改动；并发跑 E2E 与真实应用有竞争风险（可接受：E2E 串行） |
| C. 预置/清理式 | 启动前写入预期 settings.json，结束后删除新增状态 | 介于两者之间 |

**推荐**：先按 B 落地（零改动）；若 E2E 与手工使用冲突频发，再提出 A 征求确认。实现时在 E2E 工程内封装 `SettingsBackup` 工具类。

临时文件（导入样例 WBIX、导出产物）统一放 `Path.GetTempPath()` 下按用例 GUID 隔离的子目录，`Dispose` 清理。

## 5. 关键场景的技术要点

| 场景 | 要点 |
|---|---|
| 设置持久化 | 修改项 → 触发关闭（防抖 350ms 后落盘）→ 结束进程 → 重新启动断言 |
| 导出 PNG | 保存对话框是 Win32 UI：`FileName` 编辑框 set-value + "保存"按钮 invoke；产物路径用临时目录 |
| 导入 WBIX | 打开对话框同上；断言导入成功用页面/元素出现（避免依赖弹窗文案本地化差异，必要时用 L10n key 反查） |
| 伪装模式 | 伪装态的退出途径可能含热键（send-keys）；注意 WinUI 3 下 FlaUI 键盘注入走 UIA SendKeys 与主程序手势路径差异，热键若不可靠改用 UI 点击退出 |
| 元素断言 | 优先 `ByAutomationId`；文本断言经 `L10n.Get` 取值比对，避免硬编码中文 |

## 6. 失败诊断

- 用例失败（断言/异常）在 `Dispose`/finally 中截图（FlaUI `Capture.Screen` 或窗口区域）至 `TestResults/uitests-artifacts/<用例名>/`。
- 截图与失败步骤日志（步骤枚举，经 xUnit `IMessageSink` 或简单日志类）一并留存。

## 7. 风险与对策

| 风险 | 对策 |
|---|---|
| WinUI 3 `send-keys` post-message 静默失效（官方文档已知问题） | 键盘输入统一走 FlaUI 的 UIA SendKeys（ValuePattern/键盘模式），不依赖 Win32 消息路径 |
| 弹窗文案随本地化变化 | 断言用 AutomationId 或 L10n key 反查，不硬编码文案 |
| 应用启动慢/首帧渲染延迟 | retry + 超时兜底；不 sleep 固定时长 |
| E2E 在 CI（windows runner）上的稳定性 | 首批场景保持简单路径；CI 接入作为可选后续步骤，不阻塞本任务验收 |

## 8. 回滚

- 删除 `WindBoard.UITests/` 工程 + slnx 条目 + XAML 中 AutomationId 标注即可完整回滚；AutomationId 标注本身无运行时行为影响。
