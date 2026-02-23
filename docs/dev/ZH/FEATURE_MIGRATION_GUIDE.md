# Feature-first 迁移指南（代码编排整理）

本指南用于把“同一功能散落在多个目录（例如 `Importing/` + `UI/MainWindow/` + `UI/Dialogs/`）”的实现，迁移为 **按功能垂直切片（Feature-first）** 的结构，降低阅读与维护成本，并让 `MainWindow` 保持轻薄。

> 适用范围：`WindBoard/` 主程序（WinUI 3）代码。  
> 日志规范：主程序统一使用 `WindBoard.Logging.AppLog`（CrashReporter 例外，见根 `AGENTS.md`）。

---

## 1. 为什么要做 Feature-first

当目录按“技术类型/层”组织（例如：逻辑在 `Importing/`，UI 在 `UI/*`，样式在 `UI/Dialogs`），功能增多后会出现：
- 改一个功能要跨多个目录跳转，定位成本高
- `MainWindow.*.cs` 逐渐变成“功能大杂烩”
- 复用逻辑难以沉淀（重复的对话框/忙碌弹窗/错误处理散落各处）

Feature-first 的核心是：**一个功能的“入口、编排、UI、应用层服务、私有模型”尽量放在同一棵目录下**；只把真正跨功能复用的内容上提到 `UI/Common` 或更高层的公共模块。

---

## 2. 目标目录结构（建议）

以导入（Import）为例：

```
WindBoard/
  Features/
    Import/
      ImportFlow.cs                 # 功能编排（入口/分支/异常处理）
      Models/
        ImportRequests.cs           # 该功能的请求/参数模型
      Services/
        BoardImportService.cs       # 应用层服务：把输入转换为元素并放置
        TextImportReader.cs
        ImageImportDecoder.cs
        ImportUrlNormalizer.cs
        ImportPlacementPlanner.cs
      UI/
        ImportDialog.xaml           # 该功能的 UI（尽量功能私有）
        ImportDialog.xaml.cs
      Wbi/
        WbiWorkspaceImporter.cs     # 兼容导入（旧格式）
        WbiPreviewReader.cs
        WbiFormatModels.cs

  UI/
    Common/
      DialogHelpers.cs              # 跨功能复用的 UI 工具（消息/忙碌弹窗）
```

测试工程建议镜像结构：

```
WindBoard.Tests/
  Features/
    Import/
      ImportUrlNormalizerTests.cs
      ImportPlacementPlannerTests.cs
      Wbi/
        WbiWorkspaceImporterTests.cs
```

---

## 3. 命名空间约定（强烈建议跟随目录）

建议：**目录与命名空间一致**，减少“文件在 A 目录却属于 B namespace”的困惑。

- `WindBoard/Features/Import/...` → `namespace WindBoard.Features.Import.*`
- `WindBoard/Features/Import/UI/...` → `namespace WindBoard.Features.Import.UI`
- `WindBoard/Features/Import/Services/...` → `namespace WindBoard.Features.Import.Services`
- `WindBoard/Features/Import/Models/...` → `namespace WindBoard.Features.Import.Models`

> 提醒：本项目存在“本地化 Key 审计测试”，`L10n.Get/Format` 的 key 需要是**字符串字面量**（详见 `docs/dev/ZH/LOCALIZATION.md`）。迁移时不要把 key 抽成变量常量导致审计失败。

---

## 4. 迁移原则（避免一次性大爆炸）

- 一次只迁移一个 Feature（一个 PR/提交一个功能），保证可回滚、可验证
- 优先迁移“纯逻辑/无 UI 依赖”的文件；UI 最后再迁移
- `MainWindow` 只保留“入口薄层”：事件 → 调用 Feature 的 Flow（编排器）
- 真正跨功能复用的 UI/工具，放到 `WindBoard/UI/Common`；不要在多个 Feature 中复制粘贴
- 关键路径要保留日志与错误处理：失败要提示用户、日志要能定位问题

---

## 5. 标准迁移步骤（Checklist）

下面的步骤可以直接按顺序执行；执行完每一步都确保能编译/通过测试。

### Step 0：梳理边界（先“列清单”，再动手）

对要迁移的功能，列出：
- 入口：从哪里触发（例如 `MainWindow.xaml.cs` 的 Click 事件）
- UI：涉及哪些 Dialog/Page/UserControl（含 XAML 样式资源）
- 应用层逻辑：有哪些 service/decoder/reader/parser
- 依赖：是否依赖 `_workspace`、`BoardCanvas`、窗口 `hwnd`、文件选择器等
- 测试：有哪些单测需要迁移/修正 namespace

### Step 1：创建 Feature 目录骨架

新增：
- `WindBoard/Features/<FeatureName>/`
- `Models/`、`Services/`、`UI/`（可选 `Interop/`、`Persistence/`、`Wbi/` 等）

### Step 2：迁移纯逻辑文件（先搬 Service/Model）

把旧目录（例如 `Importing/`）下的逻辑文件移动到：
- `Features/<Feature>/Services/` 或 `Models/`

并同步修改：
- `namespace`（建议跟随目录）
- `using` 引用（指向新 namespace）
- 仍需对外访问的类型保持 `internal`，避免扩散 public API

### Step 3：迁移兼容/子格式模块（如 WBI/WBIX）

如果功能包含“子格式/兼容层”，建议独立子目录：
- `Features/<Feature>/Wbi/*`

迁移时注意：
- 日志 Tag 保持一致（例如 `AppLog.Warn("WBI", ...)`）
- 兼容失败路径要“保守”：返回错误信息、不要崩溃

### Step 4：迁移 UI（XAML + code-behind）

移动 XAML 与 code-behind 到 `Features/<Feature>/UI/`，并同步修改：
- `x:Class`（例如改为 `WindBoard.Features.Import.UI.ImportDialog`）
- code-behind 的 `namespace` 与 `using`
- 若 code-behind 引用到了旧的请求模型/服务，全部改为新命名空间

### Step 5：抽通用 UI 帮手（可选但推荐）

如果功能中出现通用模式（例如消息弹窗、忙碌弹窗）：
- 抽取到 `WindBoard/UI/Common/*`（例如 `DialogHelpers`）
- 将多个功能内的重复实现替换为统一调用

> 原则：只有“跨功能”才上提；只在本功能使用的 UI 辅助仍放 Feature 内。

### Step 6：新增 Flow（功能编排器）

在 `Features/<Feature>/<Feature>Flow.cs`（或 `FeatureFlow.cs`）中集中处理：
- 展示 UI（Dialog/Page）
- 分支（例如：导入元素 / 导入工作区）
- 异常捕获 + 用户提示（不要把异常冒到 UI 线程导致崩溃）
- 关键日志（开始、完成、失败、重要参数）

为了让 `MainWindow` 变薄，Flow 需要从外部注入“Host 依赖”，推荐方式：
- **委托（Func/Action）**：例如 `Func<(cameraWorld, zoom)>`、`Action<BoardElement>`
- 或小接口（仅当依赖较多时）

避免在 Flow 内直接依赖 `MainWindow` 的私有成员。

### Step 7：瘦身 MainWindow（只保留入口薄层）

在 `WindBoard/UI/MainWindow/MainWindow.<Feature>.cs` 中只保留：
- `Start<Feature>Async()`：获取 `XamlRoot`、`hwnd`，创建 Flow 并调用
- 与窗口强耦合的小桥接方法（例如“导入后切换到选择工具并选中新元素”）

把原来散落的大量实现（FilePicker、解析、布局、导入流程）从 `MainWindow` 中移除。

### Step 8：迁移测试

把测试文件移动到：
- `WindBoard.Tests/Features/<Feature>/...`

并修正：
- `namespace WindBoard.Tests.Features.<Feature>`
- `using` 指向新 namespace

### Step 9：验证

至少执行：
- `dotnet test WindBoard.slnx -p:Platform=x64`

必要的手工 Smoke（以 Import 为例）：
- 弹窗打开/切换 Tab，InfoBar 提示逻辑正常
- 导入图片/媒体/文本/链接均可用
- 导入 WBIX/WBI 的两种模式可用，替换模式有风险确认
- 导入失败能提示用户且有日志

---

## 6. 常见坑与建议

- **不要在迁移中顺手重写功能行为**：迁移的价值是结构清晰与可维护；行为变更要单独 PR 更容易回归验证
- **不要把一切都上提到 Common**：Common 目录膨胀会变成新的“垃圾场”。只有确定跨功能复用才上提
- **XAML 的 `x:Class` 一定要同步**：否则会导致运行期 `LoadComponent` 失败（XAML 解析异常）
- **保持日志 Tag 一致**：迁移后排查问题仍能按 Tag（如 `Import/WBIX/WBI`）快速定位

---

## 7. 参考：Import 迁移后的落点（示例）

- Flow：`WindBoard/Features/Import/ImportFlow.cs`
- UI：`WindBoard/Features/Import/UI/ImportDialog.xaml`
- 应用层服务：`WindBoard/Features/Import/Services/BoardImportService.cs`
- 请求模型：`WindBoard/Features/Import/Models/ImportRequests.cs`
- 通用弹窗：`WindBoard/UI/Common/DialogHelpers.cs`

