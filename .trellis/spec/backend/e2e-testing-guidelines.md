# E2E Testing Guidelines

> WindBoard.UITests（FlaUI/UIA3）工程约定与 WinUI 3 UIA 自动化实测契约。单测约定见 [Quality Guidelines](./quality-guidelines.md)。

---

## 选型依据（2026-09 核实，保留来源）

- **WinAppDriver 不再采用**：`microsoft/WinAppDriver` 自 2025-04 起无代码推送（GitHub API `pushed_at`），仓库活跃度标记为 false、未关闭 issue 逾千，不适合作为新自动化方案的基座。
- **FlaUI 采用**：`FlaUI.UIA3` v5.0.0 为当前最新稳定版（GitHub `releases/latest`，非 prerelease，2025-02 发布；目标框架 net8.0+，与本项目 .NET 10 兼容），直接驱动 Windows UI Automation（UIA3），不引入 Node/Appium 工具链。
- **E2E 不进 CI**：需要交互式桌面会话（真实鼠标/键盘/UIA）。CI（`.github/workflows/ci.yml`）只跑零告警构建 + 全量单测（含渲染快照；WARP 软件路径无显示器依赖）；E2E 保持本地显式运行。

## 工程隔离契约（构建开关）

E2E 启动真实应用进程，必须与默认单测口径隔离。xUnit v2 无运行时 Skip，用构建开关实现：

```xml
<!-- WindBoard.UITests.csproj -->
<IsTestProject>false</IsTestProject>
<IsTestProject Condition="'$(RunUITests)' == 'true'">true</IsTestProject>
```

- 默认口径：`dotnet test WindBoard.slnx`（不发现 E2E，仅 WindBoard.Tests）
- E2E 口径：`dotnet test WindBoard.slnx -c Release -p:RunUITests=true --filter "Category=E2E"`

### Wrong vs Correct：条件属性赋值

```xml
<!-- Wrong：同一 PropertyGroup 内无条件赋值在后，覆盖前面的条件赋值，开关永远失效 -->
<IsTestProject Condition="'$(RunUITests)' != 'true'">false</IsTestProject>
<IsTestProject>true</IsTestProject>

<!-- Correct：默认值在前（无条件），开关升级在后（带条件） -->
<IsTestProject>false</IsTestProject>
<IsTestProject Condition="'$(RunUITests)' == 'true'">true</IsTestProject>
```

> **Warning**: MSBuild 同一 PropertyGroup 内后出现的属性赋值直接覆盖先前的（无论是否带 Condition）。验证开关是否生效必须实际跑一次默认口径，不能只看 csproj。

## 用例结构约定

- 用例标注 `[Trait("Category", "E2E")]`；每条用例独立启动应用（`WindBoardAppFixture`：Launch → retry 等主窗口 → 提供自动化会话）。
- **本工程禁用 xUnit 并行**（`AssemblyInfo.cs` 的 assembly 级 `DisableTestParallelization`）：E2E 会启动多个真实应用实例，窗口查找/设置文件互相串台，必须串行。
- 幂等性（`SettingsBackup`）：用例前置备份并移除两个候选 `settings.json`（`{exe}\data\` 便携路径与 `%LocalAppData%\WindBoard\`），备份同时**落盘**到 `%TEMP%\windboard-uitests\settings-backup.json`；用例后恢复原文件、删除测试期间新建的文件、清理备份与临时目录（`Path.GetTempPath()` 下 GUID 子目录）。`UiTestBase.InitializeAsync` 失败时立即回滚环境——xUnit v2 在 `InitializeAsync` 抛出时**不会**调用 `DisposeAsync`（实测确认），依赖 `DisposeAsync` 兜底会永久丢失用户设置。
- 失败诊断：断言/异常路径截图 + 步骤日志落 `TestResults/uitests-artifacts/<用例名>/`。
- 被测 exe 定位：Release 优先，Debug 兜底；`WINDBOARD_EXE` 环境变量可覆盖。
- 静态分析同样作用于本工程（告警即破坏“构建零告警”基线）：新增代码须零告警，实测常见陷阱为 `StringBuilder.AppendLine($"...")`（CA1305，须传 `CultureInfo.InvariantCulture`）与 P/Invoke 的 `StringBuilder` 参数（CA1838，改用 `char[]` 缓冲区）。
- 已知覆盖缺口（后续增强）：画布绘制/擦除、选中 overlay 与框选 marquee 的 XAML 层像素验证尚无 E2E 用例（当前仅由单测结构断言覆盖，见 `BoardSceneRendererSnapshotTests` 类头）；补用例需经鼠标拖拽驱动画布。

## WinUI 3 UIA 实测契约（ FlaUI 5.0 ）

以下均为 2026-09 在本项目实测确认的行为，写新用例前必读：

| 场景 | 实测行为 | 正确做法 |
|---|---|---|
| 桌面全树条件搜索 | `desktop.FindFirstDescendant(ByAutomationId)` 在大桌面树上慢（可达数十秒）且间歇超时 | 主窗口 `FindFirstDescendant` 优先；未命中再 `EnumWindows` 枚举顶层 HWND → `FromHandle` → 限深 BFS 查找 |
| 弹层/菜单 HWND | WinUI 弹层（MenuFlyout/ContentDialog）与主窗口同为 `WinUIDesktopWin32WindowClass`，owner 关系不稳定 | 不依赖类名/owner 过滤，遍历全部非主窗口顶层 HWND 逐个尝试 |
| FileSavePicker/OpenPicker | 对话框由 Shell 侧进程承载（pid ≠ 被测应用），需在桌面范围找 | `EnumWindows` + 对话框类名/标题定位，再按文件名 Edit（ValuePattern）+ 确认按钮驱动 |
| ContentDialog 按钮激活 | `InvokePattern.Invoke()` 不触发应用事件处理逻辑 | 一律 `ClickCenter` 鼠标点击（先确保目标窗口前置） |
| 弹层出现时机 | 覆盖确认等弹层可延迟 8–15 秒才出现在 UIA 树 | 等待窗 ≥ 20s；可选步骤（如覆盖确认）超时即跳过，不视为失败 |
| 文件对话框覆盖确认 | IFileDialog 是否预创建 0 字节目标文件**依环境而异**（实测不一致）：出现则命中应用侧 `File.Exists` 判重弹确认，未出现则直接写入 | 用例必须兼容两条路径（出现即点到消失，未出现不视为失败），不得假设弹窗必现 |
| WinUI SendKeys | WinUI 3 下 post-message 键盘注入静默失效（官方已知问题） | 键盘输入走 FlaUI UIA SendKeys / ValuePattern.SetValue |

## 断言基元约定

- 元素定位优先 `ByAutomationId`；无 Id 的系统/应用弹窗按钮按显示文案匹配，文案常量集中 `UiText.cs`（zh-CN 为主 + 英文兜底），禁止散落硬编码。
- `UiText` 的文案必须与被测实现对齐：应用内单按钮提示弹窗（`DialogHelpers.ShowMessageAsync` 默认）的按钮文案是 `Common_Close`（“关闭”/`Close`），**不是**“确定”/`OK`；改文案或新增弹窗时先核对 `L10n` 资源 key，不要凭按钮语义猜测。
- 等待一律 `Retry.WhileNull`（显式超时），禁止 `Thread.Sleep`。
- AutomationId 命名规范见 [Component Guidelines](../frontend/component-guidelines.md)。

## Common Mistakes

### Common Mistake: 依赖 InvokePattern 关闭 ContentDialog

**Symptom**: 用例对弹窗按钮 Invoke 成功返回但应用无反应，后续步骤超时。
**Cause**: WinUI 3 ContentDialog 的按钮事件不经 UIA Invoke 路径。
**Fix**: 鼠标点击元素中心；点击前把被测窗口带到前台。

### Common Mistake: 全量 E2E 时长失控

**Symptom**: 套件总时长远超各用例之和的预期。
**Cause**: 桌面全局条件搜索作为主查找路径，每次重试都全树扫描。
**Fix**: 遵循"主窗口优先 + Win32 枚举限深"查找顺序；只对确需跨进程的（文件对话框）用桌面范围查找。
