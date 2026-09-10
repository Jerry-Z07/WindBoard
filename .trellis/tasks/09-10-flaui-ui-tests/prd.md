# P3 FlaUI UI 自动化测试

## Goal

为关键 XAML 控件建立 AutomationId 体系，引入 FlaUI（UIA3）在 xUnit 体系中建立 E2E 冒烟测试，覆盖设置页、导入导出、Dock、伪装模式等壳层流程，替代相应的人工点检。

## 背景（已调研确认）

- 项目为 unpackaged WinUI 3 应用（`WindowsPackageType=None`），可直接 `Process.Start` 启动 exe，无 MSIX 激活复杂度。
- 代码中当前无任何 `AutomationProperties`（已全库检索确认）；WinUI 3 对 UIA 树暴露完整，FlaUI/UIA3 可驱动。
- 设置持久化于 `%LOCALAPPDATA%\WindBoard\settings.json`（`AppSettingsStore.CreateDefault`），E2E 需考虑环境隔离。
- 微软官方（WinAppDriver 已停更）与社区现状下，FlaUI 是 .NET 生态做 Windows 桌面 UI 自动化的主流选择。

## Requirements

### 1. AutomationId 体系

- 为 E2E 涉及的关键控件补充 `AutomationProperties.AutomationId`，命名规范见 design.md。
- 范围：设置页主要控件、Dock 显隐入口、伪装模式进出入口、导入/导出入口、页切换控件。
- 仅为测试所需控件补充，不做全量铺开；既有的可用标识（如已有 Name/Content 语义）优先复用。

### 2. E2E 冒烟场景（首批）

1. **启动冒烟**：启动进程 → 主窗口出现 → 关键元素存在 → 正常退出。
2. **设置持久化**：打开设置页 → 修改一个可断言的开关/数值项 → 关闭并重启应用 → 断言值保留。
3. **Dock 显隐**：切换 Dock 显示/隐藏并断言状态。
4. **伪装模式**：进入伪装态 → 断言伪装 UI 生效 → 退出恢复正常。
5. **导出 PNG**：触发导出 → 经保存对话框写入临时目录 → 断言文件生成。
6. **导入 WBIX**：经打开对话框选择临时目录中的测试文件 → 断言导入成功标志（如页面/元素出现）。

### 3. 工程与运行约束

- FlaUI 以新 NuGet 依赖引入（用户已批准方向，具体包版本实现时确认最新稳定版）。
- E2E 与既有单测隔离运行（方案见 design.md），避免拖慢/污染 `dotnet test` 全量单测。
- 测试失败时自动截图存档，便于事后定位。
- 测试必须可重复运行（幂等）：不残留临时文件、不污染用户真实设置数据。

## 约束

- 每条 E2E 用例总时长目标 < 30s（含应用启动）。
- 不修改被测功能的行为逻辑；主工程改动仅限 AutomationId 标注与（若用户确认）设置目录重定向支持。
- 文件对话框为系统 Win32 UI，经 UIA 驱动（文件名输入 + 确认按钮），不 mock。
- 不引入 Appium/Node 工具链。

## Acceptance Criteria

- [x] 首批 6 条场景全部可重复通过（2026-09-10 审查验证：连续 3 轮全绿，无环境残留导致的失败）。
- [x] E2E 与单测可分别运行；默认单测命令不受 E2E 干扰。（csproj 条件化 `IsTestProject`：默认 `dotnet test WindBoard.slnx` 不发现 E2E；显式 `-p:RunUITests=true --filter Category=E2E` 单独运行）
- [x] 失败用例自动留下截图与失败步骤日志。（`RunStep` 异常点截图 + steps.log，产物在 `TestResults/uitests-artifacts/`）
- [x] 主工程改动仅限 AutomationId（及经确认的设置目录重定向），`dotnet build/test` 全绿。（设置隔离采用方案 B 备份/恢复，主工程零额外改动；2026-09-10 审查修复 14 个构建告警后构建零告警）

## 审查偏差记录（2026-09-10）

1. **导出 PNG 用例曾稳定失败**：`UiText.MessageBoxOkButton` 误假设单按钮提示弹窗按钮文案为 Common_OK（"确定"），而 `DialogHelpers.ShowMessageAsync` 默认文案为 Common_Close（"关闭"）。已更名为 `MessageBoxCloseButton = { "关闭", "Close" }` 并修正注释，修复后连续 3 轮全绿。
