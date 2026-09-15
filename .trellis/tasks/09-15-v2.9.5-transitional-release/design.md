# 技术设计：v2.9.5 过渡版本（设置公告位 + 选择性并入 + 发版）

> 需求与验收标准见 `prd.md`；执行顺序见 `implement.md`。本文件只记录技术设计。

## 1. 边界与不变量

- 全部改动落在 `main` 分支。`main` 先以 **fast-forward** 推进到 `bf394d6`（`develop` 中除 MSIX 提交块 `084963a..fea453f` 外的全部更新），新功能叠加在其之上。
- **不引入**任何 MSIX 打包能力：不改 `WindBoard/WindBoard.csproj` 的 `EnableMsixTooling`/`WindowsPackageType`，不新增 `Package.appxmanifest`，不改 `.github/workflows/release.yml`（保持 Inno 安装包流程），不改 `ci.yml`。
- 公告位是**设置窗口壳层**能力，不进入主窗口、不进入 Board/Rendering/Interaction 各层。

## 2. 分层与文件落点

| 关注点 | 落点 | 说明 |
|---|---|---|
| 公告模型与动作标识 | `WindBoard/Settings/AppAnnouncement.cs` | 新增 |
| 公告定义目录 + 选择纯函数 | `WindBoard/Settings/AppAnnouncementCatalog.cs` | 新增 |
| 「已关闭公告 Id」持久化模型 | `WindBoard/Settings/AppSettings.cs` | 新增 `AnnouncementsSettings` 根节点 |
| 快照深拷贝 | `WindBoard/Settings/AppSettingsCloner.cs` | 同步新字段 |
| 归一化 | `WindBoard/Settings/AppSettingsStore.cs` | `NormalizeInPlace` 同步新字段 |
| 服务读写入口 | `WindBoard/Settings/AppSettingsService.cs` | 新增 `GetDismissedAnnouncementIds` / `DismissAnnouncement` |
| 公告位 XAML | `WindBoard/Settings/SettingsWindow.xaml` | 新增一行 + `InfoBar` |
| 公告位渲染与交互 | `WindBoard/Settings/SettingsWindow.xaml.cs` | 新增方法，复用既有导航/滚入视野机制 |
| 文案 | `WindBoard/Strings/zh-CN/SettingsWindow.resw`、`WindBoard/Strings/en-US/SettingsWindow.resw` | 新增 3 个 key |
| 单测 | `WindBoard.Tests/Settings/AppSettingsStoreTests.cs`、`AppSettingsServiceTests.cs`、新增 `AppAnnouncementCatalogTests.cs` | 见 §9 |
| 版本号 / 更新日志 | `Directory.Build.props`、`docs/release-notes/v2.9.5.{zh-CN,en-US}.md` | §10 |

**为什么放在 `Settings/` 而不是 `Features/`**：`Features/` 的约定是 `*Flow.cs + Models/ + Services/ + UI/` 的独立功能模块（伪装/快捷栏/导入导出等），入口在主窗口。本能力只服务设置窗口壳层、无独立 Flow 与页面，且其状态属于应用设置。把它放进 `Features/` 会造出一个没有独立用户入口的空壳模块，反而违反 `frontend/directory-structure.md` 的分层意图。文件沿用 `Settings/` 根目录既有的扁平风格（`AppSettings.cs`、`PenSettingsDefaults.cs`、`SettingsNormalizationReport.cs` 同类）。

**为什么不再抽 `Settings/Announcements/` 子目录**：仅 2 个新文件，建目录等于引入无收益的层级；若后续公告类型增多（多公告队列、多动作），再提升为子目录。

## 3. 契约

### C1 设置字段（持久化）

`AppSettings` 新增根节点，与 `Appearance`/`Dock`/`Writing`/`Diagnostics` 同级：

```csharp
public AnnouncementsSettings Announcements { get; set; } = new();

internal sealed class AnnouncementsSettings
{
    /// 已被用户关闭、不再提醒的公告 Id。
    public List<string> DismissedIds { get; set; } = new();
}
```

落盘形态（`camelCase`，见 `AppSettingsStore.JsonOptions`）：

```json
{ "announcements": { "dismissedIds": ["installer-distribution-changed"] } }
```

`NormalizeInPlace` 归一化规则（对齐既有 `NormalizeOrder` 风格）：`null` → 空列表 → 逐项 `Trim()`、丢弃空白项、按 `StringComparer.Ordinal` 去重保留首次出现、上限 32 项（防无界增长）。

### C2 公告模型（`AppAnnouncement`）

```csharp
internal enum AppAnnouncementAction { None, OpenBackupSettings }

internal sealed class AppAnnouncement
{
    internal AppAnnouncement(
        string id,
        InfoBarSeverity severity,
        Func<string> titleProvider,
        Func<string> messageProvider,
        Func<string>? actionButtonProvider,
        AppAnnouncementAction action);

    internal string Id { get; }                    // 稳定标识，也是"已关闭"去重的键
    internal InfoBarSeverity Severity { get; }
    internal Func<string> TitleProvider { get; }
    internal Func<string> MessageProvider { get; }
    internal Func<string>? ActionButtonProvider { get; }
    internal AppAnnouncementAction Action { get; }
}
```

**为什么文案用 `Func<string>` 而不是 key 字符串**：`LocalizationKeyAuditTests` 明确禁止 `L10n.Get/Format` 传入非字面量 key（`LocalizationKeyAuditTests.cs:33-36,74-108`）。本文件既有的动态文案做法就是提供器 + lambda 内字面量（`SettingsWindow.xaml.cs:29-52` 的 `static () => L10n.Get("Settings_General_Title")`），沿用同一模式即可在满足审计的前提下把文案放到目录里。

**为什么 `Severity` 直接用 `InfoBarSeverity`**：模型只被设置窗口消费、不参与持久化，直接复用控件枚举可省掉一层无信息量的映射；`Settings/` 目录本身已依赖 XAML（`SettingsWindow.xaml.cs`）。若未来出现跨层消费者，再引入自有枚举。

### C3 公告目录（`AppAnnouncementCatalog`）

```csharp
internal static class AppAnnouncementCatalog
{
    internal static IReadOnlyList<AppAnnouncement> All { get; }   // v2.9.5：仅 1 条

    internal static AppAnnouncement? SelectNext(
        IReadOnlyList<AppAnnouncement> announcements,
        IReadOnlyCollection<string> dismissedIds);
}
```

v2.9.5 唯一定义：

| 字段 | 值 |
|---|---|
| `Id` | `"installer-distribution-changed"` |
| `Severity` | `InfoBarSeverity.Warning` |
| `Title` | `SettingsWindow_Announcement_InstallerDistribution_Title` |
| `Message` | `SettingsWindow_Announcement_InstallerDistribution_Message` |
| `Action` | `OpenBackupSettings`（按钮文案 `SettingsWindow_Announcement_InstallerDistribution_Action`）|

`SelectNext` 语义（**纯函数，无 UI/无 IO**，即 R3 的可测载体）：按 `All` 的顺序返回第一条 `Id` 不在 `dismissedIds` 中的公告；全部已关闭返回 `null`。比较用 `StringComparer.Ordinal`（Id 是程序内部稳定标识，不受用户文化影响）。

### C4 服务 API（`AppSettingsService`）

```csharp
internal IReadOnlyCollection<string> GetDismissedAnnouncementIds();   // 返回快照副本
internal void DismissAnnouncement(string id);                          // 经 Update() 写入并触发防抖落盘
```

两者都必须走 `Update(s => ...)`，以复用「归一化 + `Changed` 事件 + 350ms 防抖保存」既定链路，不得直接改 `Current`（`frontend/state-management.md` 的 ❌ DON'T 条目）。

### C5 壳层渲染（`SettingsWindow`）

`SettingsWindow.xaml` 根 `Grid` 由 2 行改为 3 行：

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />  <!-- 0: TitleBar（不变） -->
    <RowDefinition Height="Auto" />  <!-- 1: 公告位（新增） -->
    <RowDefinition Height="*" />     <!-- 2: NavigationView（原 row1 下移） -->
</Grid.RowDefinitions>
```

公告位元素（`Grid.Row="1"`）：`InfoBar`，`IsOpen="False"`（无公告时不占位）、`IsClosable="True"`（有 X 关闭按钮，官方默认值）、`IsIconVisible="True"`、`CloseButtonClick="OnAnnouncementCloseButtonClick"`，`AutomationProperties.AutomationId="SettingsWindow_AnnouncementBar"`（对齐本仓库 E2E 的 AutomationId 标注约定），内含 `<InfoBar.ActionButton><Button …/></InfoBar.ActionButton>`。所有属性值必须显式给出或由 code-behind 赋值，不硬编码文案（禁用 `Title=`/`Message=` 字面量）。

构造函数中在 `RebuildSearchTargets()` 附近调用 `UpdateAnnouncementBar()`：

```
UpdateAnnouncementBar()
  announcement = AppAnnouncementCatalog.SelectNext(All, AppSettingsService.Instance.GetDismissedAnnouncementIds())
  if (announcement is null) { AnnouncementBar.IsOpen = false; return; }
  _currentAnnouncement = announcement
  AnnouncementBar.Severity = announcement.Severity
  AnnouncementBar.Title = announcement.TitleProvider()
  AnnouncementBar.Message = announcement.MessageProvider()
  ActionButton 文案/可见性 = announcement.ActionButtonProvider 是否为空
  AnnouncementBar.IsOpen = true
```

两条交互路径：

- **关闭**：`OnAnnouncementCloseButtonClick` → `AppSettingsService.Instance.DismissAnnouncement(_currentAnnouncement.Id)`；InfoBar 自身随即 `IsOpen=false`（官方行为）。窗口重开或应用重启后，`SelectNext` 因该 Id 已在 `DismissedIds` 中而跳过它。
- **动作**：`OnAnnouncementActionClicked` → 按 `AppAnnouncementAction` 分派；`OpenBackupSettings` 复用 `SettingsWindow.xaml.cs` 已有的「跳页 + 把元素滚入视野」机制，与 `NavigateToSearchTarget`（`:420-442`）保持同构：

```csharp
_pendingBringIntoViewPageType = typeof(SettingsManagementPage);
_pendingBringIntoViewElementName = "ExportSettingsCard";
// 选中"关于"根节点（会清空二级返回栈），再压入设置管理页，返回键可用
NavView.SelectedItem = FindNavigationItemByTag("about") ?? NavView.SelectedItem;
if (ContentFrame.CurrentSourcePageType != typeof(SettingsManagementPage))
    ContentFrame.Navigate(typeof(SettingsManagementPage));
TryBringPendingElementIntoView();
```

不新增导航机制、不新增页面、不新增自定义控件（`frontend/quality-guidelines.md` 的「Unnecessary custom controls」条目：优先组合/复用内置控件）。

### C6 本地化 key（`SettingsWindow.resw`，zh-CN + en-US 双份）

| Key | zh-CN | en-US |
|---|---|---|
| `SettingsWindow_Announcement_InstallerDistribution_Title` | 安装方式调整提醒 | Installer distribution is changing |
| `SettingsWindow_Announcement_InstallerDistribution_Message` | 安装版（安装包）分发方式将调整，建议先备份设置，以便迁移后导入。 | The installer-based distribution will be adjusted. Back up your settings first so you can import them after migrating. |
| `SettingsWindow_Announcement_InstallerDistribution_Action` | 去备份设置 | Back up settings |

口径已由用户确认：**不提 Microsoft Store、不含任何链接**。

## 4. ASCII UI 示意

```
┌─ 设置 - 常规 ─────────────────────────────────────────────────────── [_][□][×] ┐
│  ☰   [🔍 搜索设置                                        ]                     │  ← row0 TitleBar（不变）
├───────────────────────────────────────────────────────────────────────────────┤
│ ⚠  安装方式调整提醒  安装版（安装包）分发方式将调整，建议先备份设置，     [去备份设置]  [×] │  ← row1 新增公告位
│                      以便迁移后导入。                                          │
├────────────┬──────────────────────────────────────────────────────────────────┤
│  常规      │                                                                  │
│  外观      │                                                                  │
│  书写      │            当前设置页内容（ContentFrame）                        │  ← row2 NavigationView
│  快捷键    │                                                                  │
│            │                                                                  │
│  关于      │                                                                  │
└────────────┴──────────────────────────────────────────────────────────────────┘
```

- 无公告、或当前公告已被关闭且无新公告时：row1 高度为 0，窗口外观与改动前一致。
- 窗口较窄时，官方行为是 Title / Message / ActionButton 各自换行（`InfoBar` 文档「Content wrapping」）。这是可接受的降级，不额外设定宽度或截断逻辑。
- 点击「去备份设置」后：进入「关于 / 设置管理」页，并把「导出设置」卡片滚入视野，左上角出现返回按钮。

## 5. 数据流

```
窗口构造 → SelectNext(目录, 已关闭集合)
                ↑                          ↑
     AppAnnouncementCatalog.All    AppSettingsService.GetDismissedAnnouncementIds()
                                            ↑
                                    AppSettings.Announcements.DismissedIds
                                            ↑
点击 X → OnAnnouncementCloseButtonClick → AppSettingsService.DismissAnnouncement(id)
         → Update()（归一化 → Changed → 350ms 防抖）→ settings.json
```

## 6. 兼容性与迁移

- **settings.json 新字段是可选追加**：旧版本读到多余字段会忽略（`System.Text.Json` 默认行为），不会报错；新版本读到缺失字段时由 `NormalizeInPlace` 补 `null` → 空列表，即"从未关闭过任何公告"⇒ 公告正常展示。与既有 `EnterScreenAnnotationWhenMinimized` 的历史兼容处理同一思路。
- **导出/导入设置**：`Announcements.DismissedIds` 会随导出文件一起带走（`ExportToFileAsync` 走 `AppSettingsCloner.Clone` + `Serialize`），这是可接受的——它不属于隐私或机器相关信息。
- **恢复默认设置**（`ResetToDefaultsAsync`）会清空 `DismissedIds`，即重置后公告重新出现；符合"恢复默认"的语义，不做特殊豁免。
- **不涉及**配置文件版本号迁移、不涉及数据目录变更。

## 7. 权衡与取舍

| 取舍 | 选择与理由 |
|---|---|
| 关闭状态存哪里 | 存 `settings.json`（新增 `announcements` 根节点）。备选是 `ApplicationData.LocalSettings`，但本项目**从未使用**它，且 `LastNotifiedVersion` 已确立了"提醒去重状态落 settings.json"的先例，另起一路会分裂状态管理。 |
| 单条 Id 还是 Id 列表 | 用列表。需求是"每个通知关闭后便不再提醒"，即需按 Id 分别记忆；单值会因后续新公告覆盖旧值而导致旧公告复现。上限 32 项兜底。 |
| 公告定义放代码还是配置 | 放代码（静态目录）。公告是随版本发布的内容，必须与本地化资源同步；放外部配置会引入"配置与资源不同步"的失效模式，也与本项目无外部配置的设计一致。 |
| 是否做"仅安装版显示" | 用户已决策：不区分形态、对所有用户显示、以"可关闭"作为降噪手段。因此**不引入** `AppInstallProbe` 依赖。 |
| 是否抽成 UserControl | 不抽。壳层只渲染一个内置 `InfoBar`，抽控件会同时引入依赖属性/模板等无需求支撑的复杂度（且违反"优先复用内置控件"）。 |

## 8. 风险与回滚

| 风险 | 影响 | 处置 |
|---|---|---|
| `main` fast-forward 后与 `develop` 分叉 | 后续把 MSIX 版本合回 `main` 时，`Directory.Build.props`、`docs/release-notes/`、`SettingsWindow.*` 会冲突 | 已知且刻意为之（过渡版本）；回滚点是 `main` 原 HEAD `3b2afad`，`git reset --hard 3b2afad` 即可整体撤销（仅本地、未推送前）。 |
| 公告位挤压设置页布局 | 所有设置页可视高度变小 | 无公告时 row 高度为 0（`IsOpen=false` 不占位）；公告文案短句 + 不做高度硬编码。 |
| `CloseButtonClick` 与程序化关闭混淆 | 可能把非用户关闭也记为"不再提醒" | 只订阅 `CloseButtonClick`（仅用户点击 X 触发），不在 `IsOpen` 变更回调里写状态。 |
| tag 已存在 | 发布被阻塞 | 按 `release-main-tag` 安全规则：仅在用户明确要求时删除并重建 tag。 |
| Release workflow 失败 | 无资产产出 | 失败即停止并报告失败点，不重复打 tag；修正后重跑 `workflow_dispatch`。 |

## 9. 测试策略

- **单测（CI 覆盖）**
  - `AppSettingsStoreTests`：`DismissedIds` 归一化——`null` 补空、`Trim`、丢空白项、去重保序、超 32 项截断；序列化往返。
  - `AppSettingsServiceTests`：`DismissAnnouncement` 后 `GetDismissedAnnouncementIds` 可见且去重；`ResetToDefaultsAsync` 后清空。
  - 新增 `AppAnnouncementCatalogTests`：`All` 的 `Id` 非空且唯一；`SelectNext` 四类边界——无已关闭→首条；首条已关闭→次条；全部已关闭→`null`；`dismissedIds` 含未知 Id→不影响结果。
- **不做单测（对齐 `backend/e2e-testing-guidelines.md` 的测试分层）**：`InfoBar` 的可见性与关闭按钮属 XAML 合成层，归 E2E；本任务不新增 E2E 用例（见 `prd.md` Out of Scope），其决策逻辑已通过 `SelectNext` 纯函数单测覆盖。
- **手工验证清单**（发版前由实现者执行并在交付说明中给出结论）：公告出现 → 点 X → 重开设置窗口不出现 → 手改 `settings.json` 删除该 Id → 重开又出现；点「去备份设置」→ 落到设置管理页且导出设置卡片可见。
- **构建约束**：`dotnet build WindBoard.slnx -c Release -p:CodeAnalysisTreatWarningsAsErrors=true` 必须零告警（CI 同款约束）。

## 10. 版本与更新日志

- `Directory.Build.props`：`VersionPrefix` 由 `2.9.0` 改为 `2.9.5`（`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` 均引用 `$(VersionPrefix)`，无需逐个改）。
- `docs/release-notes/v2.9.5.zh-CN.md` / `.en-US.md`：沿用 `v2.9.0` 的格式（`# 更新内容（v2.9.5）` + `- ✨ feat:` / `- 🐛 fix:` 条目，必要时 `# 已知问题`）。条目需从 `git log main..bf394d6`（69 个非 MSIX 提交）按用户可见性归纳，不得把 MSIX 相关内容写入。
- 一致性校验（发版前必跑，取自 `release-main-tag` 技能）：`VersionPrefix == '2.9.5'` 且两个 release note 文件存在并含版本标识。

## 11. 依据

- InfoBar 行为：<https://learn.microsoft.com/en-us/windows/apps/design/controls/infobar>（`IsClosable` 默认 `true` 即显示关闭按钮；`ActionButton` 传 `Button`；内容超一行时 Title/Message/ActionButton 分别换行）与 <https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.infobar>（`CloseButtonClick` 事件、`Severity`/`IsOpen`/`ActionButton` 成员）。
- 仓库证据：`AppSettingsService.cs`（`Update`/`Get*Snapshot`/`Changed`/防抖）、`AppSettingsStore.cs:117-178`（归一化入口）、`AppSettings.cs:104-106`（`LastNotifiedVersion` 先例）、`LocalizationKeyAuditTests.cs`（key 必须字面量）、`SettingsWindow.xaml.cs:29-52,420-442,465-486`（`Func<string>` 提供器与滚入视野机制）、`frontend/quality-guidelines.md`（禁用硬编码颜色/多余自定义控件/硬编码文案）、`frontend/state-management.md`（设置必须经 `Update`）。
