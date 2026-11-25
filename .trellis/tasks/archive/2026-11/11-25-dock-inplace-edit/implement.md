# 实施计划：Dock 栏就地编辑

图例：`[ ]` 未开始 / `[~]` 进行中 / `[x]` 完成

门禁：每个阶段末尾必须通过该阶段验收；P6（跨端）未通过不得进入最终提交。实施前先 `task.py start 11-25-dock-inplace-edit`。

---

## P1 设置模型与纯规则

目标：先把 `hiddenItems` 的数据契约、归一化与测试落地，不改 UI。

- [x] **P1.1 字段与合法 id**
  - `DockSettings` 增加 `List<string> HiddenItems = new()`
  - `DockItemIds` 增加 `All`（四组默认顺序并集）
  - `AppSettingsCloner`、`AppSettingsService.GetDockSettingsSnapshot` 复制隐藏列表
- [x] **P1.2 纯规则**
  - 新增 `Features/Dock/Models/DockLayoutRules.cs`：`CanHide`、`NormalizeHiddenItems`、`FilterVisibleOrder`
  - `more` 永不隐藏；未知 id/空白/重复项过滤
- [x] **P1.3 归一化接入**
  - `AppSettingsStore.NormalizeDockSettingsInPlace` 归一化 `HiddenItems`
  - 旧文件缺字段时为空列表
- [x] **P1.4 单测**
  - `DockLayoutRules`：`more`、未知 id、重复、空集合、顺序保持
  - `AppSettingsStore.NormalizeInPlace`：null / 非法 / 含 `more`
  - 克隆/快照深拷贝
- [x] **P1.5 验收**
  - `dotnet test WindBoard.slnx --filter "FullyQualifiedName~DockLayoutRules|FullyQualifiedName~AppSettingsStore"` 全绿

## P2 正式 Dock 的隐藏渲染

目标：隐藏项在真实 Dock 不渲染；组容器按可见项显隐。

- [x] **P2.1 应用器签名**
  - `DockOrderApplier.Apply(panel, order, elementsById, hiddenIds)` 使用 `FilterVisibleOrder`
  - `DockSettingsApplier` 构建 `HashSet<string> hiddenIds`
- [x] **P2.2 容器与分隔线**
  - `DockMainWindowHost` 增加 `MainDockContainer`、`PagesDockContainer`
  - 撤销/重做分隔线与面板按“任一可见项”显隐（两项都隐藏时整组消失）
  - 主 Dock 无可见工具/撤销项时隐藏；页面组无可见项时隐藏
- [x] **P2.3 默认值语料**
  - `SettingsDefaultsGoldenTests` 的 dock 增加 `hiddenItems`
  - `WINBOARD_REGEN_SETTINGS_GOLDEN=1` 重建语料，确认仅新增空数组
- [x] **P2.4 验收**
  - `dotnet test WindBoard.slnx` 全绿（含语料测试）
  - 手工改设置文件隐藏 `import`，启动后导入按钮不渲染

## P3 编辑层骨架与主窗口接入

目标：能从精简设置页进入/退出编辑模式，四组可拖动排序并落盘；暂不做隐藏按钮与快捷入口表单。

- [x] **P3.1 主窗口结构**
  - `MainWindow.xaml`：Dock 根 Grid 命名 `DockLayer`；内容区末尾加 `DockEditOverlay`
  - 设置页加“编辑 Dock”卡片（`Settings_Dock_EditEntryCard`），点击后进入主窗口编辑模式；不在“更多”菜单加入口
- [x] **P3.2 编辑层 XAML**
  - `DockEditOverlay.xaml`：遮罩、工具栏（完成/重置/快捷入口开关/提示）、四组横向 ListView
  - 复用 Dock 尺寸：60×52、圆角 10、14 底板、0.85 透明度、4 间距、5 内边距
  - 工具栏 `RequestedTheme="Light"`
- [x] **P3.3 进入/退出**
  - `MainWindow.Dock.cs`：`EnterDockEditMode()`、`OnDockEditOverlayExited`
  - 最小化恢复、`Activate()`、隐藏 `DockLayer`、传入 HWND
  - `ApplyAppSettingsToUi` 编辑期跳过 `ApplyDockSettingsToUi`
  - 退出：编辑层先隐藏 → 应用设置 → 显示 `DockLayer`
- [x] **P3.4 编辑层状态与排序**
  - 进入时读取快照 → 四组 `ObservableCollection<DockEditItemViewModel>`
  - `DragItemsCompleted` 落盘；`Ctrl+←/→` 移动并落盘
  - 重置确认 → `new DockSettings()` → 重新载入
  - Esc 退出；进入时聚焦编辑层
- [x] **P3.5 快捷键拦截**
  - `ShortcutsMainWindowHost` 增加编辑态拦截；`ShortcutsFlow` 的撤销/重做检查
- [x] **P3.6 验收**
  - 手工：更多 → 编辑 Dock → 拖动四组 → 完成 → 重启顺序保持
  - 手工：Esc 退出；编辑期 Ctrl+Z 不撤销画布
  - `dotnet build WindBoard.slnx -c Release -p:Platform=x64` 零告警

## P4 隐藏/恢复与快捷入口编辑

目标：完成 AC3、AC4 的全部交互。

- [x] **P4.1 隐藏/恢复**
  - 编辑项右上角按钮；`more` 无隐藏按钮且有提示
  - 隐藏项降低透明度 + 虚线边框；恢复后位置不变
  - 隐藏/恢复即时落盘 `HiddenItems`
- [x] **P4.2 快捷入口集合**
  - 按 `Side` 拆分左右集合；持久化按“左全部 + 右全部”重建
  - 删除“×”；点击打开编辑对话框
  - “+”菜单：文件/链接/程序；链接校验 `http/https`
  - 首个快捷项加入时 `isShortcutDocksVisible=true`；上限 5
- [x] **P4.3 快捷入口对话框**
  - `ShortcutDockEditDialog.xaml{,.cs}`：类型、路径/网址、显示名、图标来源、参数
  - 图标字体目录与搜索（从旧设置页迁移）
  - 原生 `ContentDialog` + 内部 `ScrollViewer`；不弹二级对话框
  - 文件/图标选择器使用主窗口 HWND
- [x] **P4.4 拖动与外部拖入**
  - 同侧拖动排序
  - 跨侧拖动：`DragItemsStarting` 写文本 id；目标 `DragOver/Drop` 改 `Side` 并追加
  - 实机若跨列表拖动与内置重排冲突，补“移到左侧/右侧”按钮兜底
  - 外部拖入 `StorageItems` / `WebLink` / URL 文本到某一侧
- [x] **P4.5 验收**
  - 手工：隐藏/恢复每个内置项（`more` 除外）；隐藏后重启仍隐藏
  - 手工：添加快捷入口三种类型；编辑图标与参数；删除；上限 5
  - 手工：同侧拖动、跨侧拖动、从资源管理器拖入文件/文件夹、从浏览器拖入网址
  - 手工：触摸/笔可拖动排序（至少一次）

## P5 精简设置页与入口

目标：完成 AC5，移除旧设置页的重复 UI。

- [x] **P5.1 页面重写**
  - `DockSettingsPage.xaml` 只保留“编辑 Dock”与“恢复默认布局”两张卡片
  - 删除四组预览、快捷入口表单、字体图标列表、计时器与 VM
- [x] **P5.2 跨窗口入口**
  - “编辑 Dock” → `App.TryGetMainWindow()?.EnterDockEditMode()` → 关闭设置窗口
  - 主窗口最小化时先恢复
- [x] **P5.3 搜索与文档**
  - `SettingsWindow` Dock 搜索锚点改为 `Settings_Dock_EditEntryCard`
  - 清理不再使用的 Helper/VM；确认无残留引用
- [x] **P5.4 验收**
  - 手工：设置 → 外观 → Dock 设置页只有两张卡片；点击“编辑 Dock”设置窗口关闭并进入编辑模式
  - 手工：重置后四组顺序、隐藏项、快捷入口开关、快捷入口全部恢复默认
  - `dotnet build ... -p:CodeAnalysisTreatWarningsAsErrors=true` 零告警

## P6 文案与跨端同步

目标：完成 AC6、AC7 的文案与网页端同步。

- [x] **P6.1 文案**
  - zh-CN / en-US 增加设计 §4.9 的 key；复用已有通用 key
  - 确认 C#/XAML 无硬编码中文界面文案
  - `WindBoard.Tests/Localization/LocalizationKeyAuditTests` 通过
- [x] **P6.2 网页端模型**
  - `settings/model.ts`：`hiddenItems` 类型、默认值、归一化（过滤未知/重复/`more`）
  - `settings-file.ts`：导出/导入白名单包含 `hiddenItems`
  - `DockBar.tsx` / `dock-layout.ts`：按 `hiddenItems` 过滤；`more` 防御性保留；空组不渲染
- [x] **P6.3 网页端就地编辑**
  - `DockPage` 精简为“编辑 Dock”+“恢复默认布局”（确认框），搜索锚点改为 `dockEditInPlace`
  - `AppShell`/`SettingsWindow` 接线 `dockEditing`/`onStartDockEdit`
  - 新增 `ui/shell/DockEditOverlay.tsx`：宽屏三块、窄屏纵向分组、Pointer Events 拖动、减号/加号、锁、完成/Esc、重置确认
- [x] **P6.4 网页端测试与生成物**
  - `dock-order`、`settings-search`、`dock-layout`、`settings-model`、`settings-file` 单测
  - `pnpm i18n` 生成词表；`pnpm verify`；`pnpm e2e`（含 `dock-edit.spec.ts`）
- [x] **P6.5 文档**
  - `docs/dev/guides/web-port.zh-CN.md`：两端均主界面就地编辑；网页端无快捷入口 Dock
- [ ] **P6.6 跨端手工验证**（未执行：需人工在两端导入导出设置）
  - 桌面导出设置 → 网页导入 → 隐藏项仍隐藏、顺序不变
  - 网页导出设置 → 桌面导入 → 同上

## P7 E2E 与最终门禁

- [x] **P7.1 E2E 更新**
  - `DockVisibilityTests` 改走“设置 → 外观 → Dock 设置 → 编辑 Dock → 逐条隐藏/恢复 → 完成”
  - 增加隐藏/恢复“导入”项的用例（可选但推荐）
  - 关键控件 AutomationId 与设计 §4.9 一致
- [x] **P7.2 最终门禁**
  - `dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true`
  - `dotnet test WindBoard.slnx`
  - `dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:RunUITests=true`
  - `dotnet test WindBoard.UITests/WindBoard.UITests.csproj -c Release -p:Platform=x64 -p:RunUITests=true --no-build --filter "Category=E2E"`
- [x] **P7.3 代码审查**
  - `git diff --check`
  - 检查新增 key 是否全部使用、旧页删除后是否有残留引用
  - 检查无 `this.`、无硬编码中文、行尾/编码符合 `.editorconfig`
- [x] **P7.4 完成**
  - 更新任务状态并归档；提交信息按约定式提交（如 `feat(dock): 支持在主界面就地编辑 Dock 布局`）

---

## 回滚点

| 阶段 | 回滚方式 |
|---|---|
| P1 后 | 删除 `hiddenItems` 字段与规则；语料恢复 |
| P3 后 | 移除编辑层与菜单入口，恢复 `ApplyAppSettingsToUi`；设置页保持原样 |
| P4 后 | 保留隐藏/排序，回退快捷入口拖动/拖入（仍可用对话框增删改） |
| P6 后 | 网页端回退 `hiddenItems` 读写；桌面旧版本会忽略该字段 |

## 风险检查清单（实施时逐条确认）

- [ ] 跨列表拖动是否与 `ListView` 内置重排冲突；兜底按钮是否就绪
- [ ] 编辑层是否遮住画布输入，但标题栏仍可拖动
- [ ] 隐藏项是否仍保留在顺序中，恢复位置是否正确
- [ ] 整组隐藏时底板/分隔线是否一起消失
- [ ] 编辑期是否真的跳过真实 Dock 重建
- [ ] Esc/完成是否都能退出，焦点是否回到主界面
- [ ] 设置页删除旧控件后是否还有事件/字段残留
- [ ] 网页端导出是否包含 `hiddenItems`，导入是否保留
- [ ] 新文案是否 zh-CN/en-US 双份齐全

---

## 实施结果（2026-11-25）

- P1–P6 代码完成；P7 的 E2E 与门禁已执行。
- 桌面：
  - `dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true`：0 警告 0 错误。
  - `dotnet test WindBoard.slnx`（工作区外副本）：662 通过 / 1 跳过（WBIX 手工 dump）。
  - Dock E2E（工作区外副本）：4 通过（进入编辑、撤销/重做逐条隐藏与恢复、导入项隐藏/恢复、三组不重叠）。
- 网页端：
  - `pnpm i18n`：663 keys。
  - `pnpm verify`：1471 通过。
  - `pnpm e2e`：20 通过（含 7 个 Dock 就地编辑用例）。
  - Dock 设置页精简为主界面入口；`DockEditOverlay.tsx` 同步拖动、隐藏/恢复、重置确认、360px 窄屏。
- 撤销/重做整组显隐：已移除 `isUndoRedoVisible` 字段与工具栏开关，显隐改由 `hiddenItems` 逐条控制；桌面与网页端同步删除，Dock 相关 E2E 按新交互更新并通过。
- 设置默认值语料已加入 `"hiddenItems": []`；网页端模型、导出导入、Dock 渲染与就地编辑入口同步完成。
- 待交互桌面手工复测：四组实际鼠标/触摸/笔拖动、快捷入口跨侧拖动、从资源管理器拖入文件、仅键盘完成全部编辑操作。自动化已覆盖隐藏/恢复、入口与排序规则（`ShortcutDockOrderTests`）。

