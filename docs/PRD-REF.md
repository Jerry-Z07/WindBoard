# WindBoard 代码重构 PRD

## 1. 背景与动机
当前工程的模块边界已经比较清晰（`Core/`、`Services/`、`MainWindow/`、`Views/`），并且 `MainWindow/` 已采用
partial 拆分。但仍有几个明显“热点文件”，使得维护成本偏高、改动风险偏大：

- `Views/SettingsWindow.xaml.cs` 体积较大，混合了 UI 绑定状态、事件处理、校验、持久化调用与预览逻辑。
- `MainWindow/MainWindow.UI.cs` 与 `MainWindow/MainWindow.Attachments.cs` 偏大，UI 交互/服务编排/对话框拼装/附件导入等职责混杂。
- `Core/Modes/InkMode.cs` 位于性能敏感路径（高频输入），内部包含多个子职责（平滑、分段、压力合成、end taper等），阅读与修改风险高。

本 PRD 目标是：在尽量不改变用户可见行为的前提下，降低耦合与修改风险，让后续迭代更容易。

## 2. 目标、范围与非目标

### 2.1 目标
- 以“子域/功能点”为单位拆分热点文件，减少单文件多职责。
- 保持性能不回退，尤其是输入/墨迹链路。
- 尽量保持 `settings.json` 兼容；如必须变更结构，提供迁移策略。

### 2.2 范围（In Scope）
- `Views/SettingsWindow.xaml(.cs)` 的职责拆分与（可选）MVVM 引入。
- `MainWindow/` 下 partial 文件进一步按子域拆分。
- `Core/Modes/InkMode.cs` 的内部结构重构（不改变外部行为/公共接口，且避免引入额外分配）。
- `Services/SettingsService.cs` 与设置模型的文件组织调整。
- `Views/Controls/PageNavigatorControl` 与 `Views/Dialogs/ImportDialog` 的局部改善（低优先级）。

### 2.3 非目标（Out of Scope）
- 不做 UI 重新设计/改样式（除非为了解耦必须微调绑定方式）。
- 不引入全量 MVVM 框架迁移（可先用“轻 MVVM/渐进式”）。
- 不以“对外稳定 API”为约束（项目处于活跃开发），仅保证用户可见行为与数据兼容。

## 3. 需求概述

### 3.1 功能性改造项

#### 3.1.1 SettingsWindow 拆分（高优先级）
建议（先做“结构搬迁/拆分”，再决定是否上 MVVM）：
- 保持 `Views/SettingsWindow.xaml` 视觉不变，目标是“结构变好但 UI 不变”。
- 拆分 `SettingsWindow.xaml.cs` 的职责（推荐两条路线，先 A 后 B）：
  - 路线 A（渐进式）：保留 code-behind 的绑定属性，但将“按功能域”的校验/同步/持久化逻辑抽到独立类（例如VideoPresenter / Camouflage / Ink）。
  - 路线 B（MVVM）：新增 `SettingsWindowViewModel`（建议放入新增的 `ViewModels/` 目录），code-behind 只保留窗口生命周期与必须的 UI 交互（如 `OpenFileDialog`、`DialogHost`）。
- 明确设置写入策略（即时写入 vs 延迟写入），重构后保持一致或在文档中明确改变。
- 通用控件只在出现重复 XAML/交互时再提取（避免“为提取而提取”）。

主要影响：
- 风险：绑定/事件拆分容易引入“设置不生效/关闭未保存/预览不刷新”等回归。
- 兼容：若设置模型结构变化（见 3.1.4），会影响所有用户的 `settings.json`。

#### 3.1.2 MainWindow 进一步拆分（高优先级）
现状：`MainWindow/` 已按关注点拆分，但 `MainWindow.UI.cs`、`MainWindow.Attachments.cs` 仍混合多个子域。

建议（保持现有目录约定：`Views/` 放 XAML；`MainWindow/` 放主窗口逻辑）：
- 继续按“功能域”拆分 partial 文件，而不是引入 `Views/MainWindow/` 这种与现有结构冲突的新目录。
  - 示例：`MainWindow.VideoPresenter.cs`、`MainWindow.Camouflage.cs`、`MainWindow.Popups.cs`、
`MainWindow.Dialogs.cs`、`MainWindow.Attachments.Import.cs` 等。
- 将“动态构建对话框 UI（TextBlock/Button/StackPanel）”的拼装逻辑提到 helper（仍在 `MainWindow/` 或 `Services/`），减少 UI 文件噪音。
- 明确 `MainWindow` 定位：作为 orchestrator，尽量只做编排/路由，细节下沉到 `Services/` 或 `Core/`。

#### 3.1.3 InkMode 拆分（中高优先级，性能敏感）
建议：
- 以“结构重排”为主，不改变算法与行为；避免新增分配/闭包捕获/LINQ。
- 将压力合成、end taper、分段/flush 相关逻辑拆到同命名空间的内部类/静态类中，`InkMode` 只保留输入入口与
ActiveStroke 生命周期编排。
- 明确哪些对象必须复用（timer/scratch collection 等），防止拆分后增加 GC 压力。

#### 3.1.4 Services 拆分（中优先级）
建议：
- 将 `AppSettings` 从 `Services/SettingsService.cs` 拆到独立文件（例如 `Models/AppSettings.cs` 或 `Services/
Settings/AppSettings.cs`），降低模型与服务耦合。
- “把设置项拆成多个设置类”与“配置文件格式兼容”存在内在冲突，需要二选一并写清楚：
  - 若必须保持 `settings.json` 结构不变：优先保持 `AppSettings` 平铺字段，仅在代码层做分组/包装。
  - 若允许调整结构：增加 `SettingsVersion`，提供迁移逻辑（旧 -> 新），并定义失败回退与回滚策略。
- `CamouflageService` 当前职责相对集中，可选做内部 helper 抽取，但优先级低于 SettingsWindow/MainWindow/
InkMode。

#### 3.1.5 Controls / Dialogs（低优先级）
现状：
- `PageNavigatorControl` 采用 DP + event 转发，符合“控件复用/宿主负责业务”的模式。
- `ImportDialog` 已内置轻量 VM（局部 MVVM）。

建议：
- `PageNavigatorControl`：只做小范围整理（命名/提取小函数/减少 FindName），不强制上 MVVM，避免破坏宿主调用方
式。
- `ImportDialog`：可将内置 VM 抽为独立类便于复用与扩展，但不是必须项。

### 3.2 非功能性需求

#### 3.2.1 性能
- 重构后书写体验不得劣化（连续书写不明显卡顿/断笔）。
- 重点路径（`InkMode`）禁止引入 LINQ 枚举、频繁分配、同步 IO 等。

#### 3.2.2 兼容性与数据
- 保持现有功能行为一致（多页、撤销/恢复、附件导入、缩放/平移、伪装、设置持久化）。
- `settings.json`：默认要求可直接读取旧版本；如结构调整，必须提供迁移策略与版本字段。

#### 3.2.3 可维护性（可执行验收）
原“注释覆盖率/圈复杂度/重复率”指标当前工程未接入自动化统计，无法可靠验收；改为可执行口径：

- 热点文件完成按子域拆分后，单文件职责清晰，修改某子域不需要通读整文件。
- 公共入口（服务/模式）保持命名一致、分组清晰；关键异常路径可用 `Debug.WriteLine` 排障（避免完全吞异常）。

## 4. 技术架构

### 4.1 架构原则
- **单一职责**：按“子域”拆分，而非过度按“技术层”拆分。
- **最小侵入**：优先搬文件/拆文件完成收益，避免同时改行为与结构。
- **性能优先**：输入/墨迹链路避免额外分配与抽象层。
- **兼容优先**：涉及持久化格式变更必须有迁移。

### 4.2 分层建议（可选 ViewModels）
```
┌────────────────────────────┐
│ Views (XAML + code-behind) │  UI 层
├────────────────────────────┤
│ ViewModels (optional)      │  视图模型层
├────────────────────────────┤
│ Services                   │  服务层（业务编排/持久化/渲染）
├────────────────────────────┤
│ Core                       │  核心层（算法/输入/交互模式）
├────────────────────────────┤
│ Models                     │  数据模型
└────────────────────────────┘
```

### 4.3 目标目录结构（与现有工程保持一致）
不引入 `Views/MainWindow/` 这种与当前布局冲突的目录；`MainWindow/` 继续承载主窗口逻辑拆分。

```
WindBoard/
├── Views/
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── SettingsWindow.xaml
│   ├── SettingsWindow.xaml.cs          # 目标：变“薄”
│   ├── Controls/
│   └── Dialogs/
├── ViewModels/ (optional)
│   ├── Settings/
│   └── Dialogs/
├── MainWindow/
│   ├── MainWindow.Architecture.cs
│   ├── MainWindow.Attachments.cs       # 可继续拆到 *.Attachments.*.cs
│   ├── MainWindow.UI.cs                # 可继续拆到 *.UI.*.cs
│   └── ...
├── Services/
│   ├── SettingsService.cs              # 可下沉到 Services/Settings/
│   ├── CamouflageService.cs
│   └── ...
├── Core/
└── Models/
```

## 5. 手工验证清单（验收口径）
- 启动：背景色应用正确；伪装标题/图标应用正确；“视频展台”按钮显示/隐藏正确。
- 书写（鼠标/触摸/手写笔至少覆盖两种）：连续快速书写无明显卡顿、无断笔；启用/禁用模拟压力表现一致；缩放下笔迹粗
细一致性功能正确。
- 多页：新增/切换/删除页面；缩略图预览刷新与释放内存行为符合预期。
- 附件：导入图片/视频/文本/链接；移动/缩放/置顶无回归；撤销/恢复覆盖附件操作。
