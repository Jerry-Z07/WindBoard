# 执行计划：v2.9.5 过渡版本

> 需求见 `prd.md`，技术设计见 `design.md`。**阶段 G 是硬闸门：未获用户明确确认，不得执行阶段 7 的任何推送/打 tag 操作。**

## 阶段 0 预检（只读，不改任何文件）

- [ ] `git status --short --branch` 确认工作树干净（`.trellis/` 下未提交的任务产物属预期，单独确认，不误提交/误丢弃）。
- [ ] `git rev-parse --verify main` / `git rev-parse --verify bf394d6` 记录当前 `main` HEAD（回滚锚点 = `3b2afad`）。
- [ ] 确认 `bf394d6` 与 MSIX 无关：`git show --stat bf394d6` 只改 `.github/workflows/ci.yml`。
- [ ] `git fetch origin --tags --prune`，确认 `origin/main` 未领先本地（若领先，停下来报告，不要强推）。

## 阶段 1 并入非 MSIX 更新

**阶段 0 实测结论（修正原假设）**：`origin/main` = `3bcb519`（2026-09-12「Merge pull request #52 from Jerry-Z07/develop」），其**内容与 `bf394d6` 完全一致**（`git diff --name-only bf394d6 origin/main` 为空）且**不含** MSIX 提交块；本地 `main`（`3b2afad`）只是尚未更新。因此并入动作 = **把本地 `main` 快进到 `origin/main`**，而不是直接指向 `bf394d6` —— 后者相对 `origin/main` 属于回退，会产生非快进推送。

- [ ] `git checkout main`
- [ ] `git merge --ff-only origin/main`（必须 fast-forward；出现非 ff 提示即停止并报告）
- [ ] `git log --oneline -3` 确认 `main` HEAD == `3bcb519`，且 `git diff --name-only bf394d6 main` 为空
- [ ] 基线验证（并入后、新功能前必须先绿）：`dotnet build WindBoard.slnx -c Release -p:CodeAnalysisTreatWarningsAsErrors=true` + `dotnet test WindBoard.slnx`
- [ ] 确认无 MSIX 残留：`rg -i "WindBoardPackage|EnableMsixTooling|appxmanifest" WindBoard/WindBoard.csproj .github/workflows/`

## 阶段 2 公告能力实现

按依赖顺序：

- [ ] `WindBoard/Settings/AppSettings.cs`：新增 `AppSettings.Announcements` 根节点与 `AnnouncementsSettings`（含 `DismissedIds`）。
- [ ] `WindBoard/Settings/AppSettingsCloner.cs`：克隆 `Announcements.DismissedIds`（新建 `List<string>`，不得共享引用）。
- [ ] `WindBoard/Settings/AppSettingsStore.cs`：`NormalizeInPlace` 补 `Announcements` 归一化（`??=`、`Trim`、丢空白、`Ordinal` 去重保序、上限 32）。
- [ ] `WindBoard/Settings/AppSettingsService.cs`：新增 `GetDismissedAnnouncementIds()`（返回副本）与 `DismissAnnouncement(string id)`（经 `Update`）。
- [ ] `WindBoard/Settings/AppAnnouncement.cs`（新）：`AppAnnouncement` + `AppAnnouncementAction`。
- [ ] `WindBoard/Settings/AppAnnouncementCatalog.cs`（新）：`All`（v2.9.5 一条定义）+ `SelectNext` 纯函数。
- [ ] `WindBoard/Strings/zh-CN/SettingsWindow.resw` 与 `en-US/SettingsWindow.resw`：各加 3 个 key（文案见 `design.md` C6）。
- [ ] `WindBoard/Settings/SettingsWindow.xaml`：根 `Grid` 改 3 行；`NavigationView` 移到 row2；row1 新增 `InfoBar` 公告位（含 `ActionButton`、`CloseButtonClick`、AutomationId）。
- [ ] `WindBoard/Settings/SettingsWindow.xaml.cs`：新增 `_currentAnnouncement` 字段、`UpdateAnnouncementBar()`（构造函数中调用）、`OnAnnouncementCloseButtonClick`、`OnAnnouncementActionClicked` + 备份设置跳转（复用 `_pendingBringIntoViewPageType/_pendingBringIntoViewElementName`）。
- [ ] 自检：无硬编码用户可见文案、无硬编码颜色、未新增自定义控件、设置写入只经 `AppSettingsService.Update`。

## 阶段 3 单测

- [ ] `WindBoard.Tests/Settings/AppSettingsStoreTests.cs`：`DismissedIds` 归一化 5 个断言（null / Trim / 丢空白 / 去重保序 / 截断 32）+ 序列化往返。
- [ ] `WindBoard.Tests/Settings/AppSettingsServiceTests.cs`：关闭后可见且不重复；`ResetToDefaultsAsync` 后清空。
- [ ] `WindBoard.Tests/Settings/AppAnnouncementCatalogTests.cs`（新）：`All` 的 Id 非空/唯一；`SelectNext` 四类边界。
- [ ] 运行：`dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Settings"`

## 阶段 4 版本号与更新日志

- [ ] `Directory.Build.props`：`VersionPrefix` → `2.9.5`。
- [ ] 归纳变更清单：`git log --oneline --no-merges main~1..bf394d6`（即 `main` 并入进来的提交），按用户可见性分类，**排除**任何 MSIX/商店相关条目。
- [ ] 新增 `docs/release-notes/v2.9.5.zh-CN.md` 与 `docs/release-notes/v2.9.5.en-US.md`，格式对齐 `v2.9.0` 两份；迁移提醒按已确认口径（不提 Microsoft Store、不含链接）。
- [ ] 跑一致性校验（`release-main-tag` 技能步骤 3 的脚本）：`VersionPrefix == 2.9.5` 且两个 release note 文件存在并含版本标识。

## 阶段 5 全量验证

- [ ] `dotnet build WindBoard.slnx -c Release -p:CodeAnalysisTreatWarningsAsErrors=true` → 零告警。
- [ ] `dotnet test WindBoard.slnx` → 全绿。
- [ ] 本地手工验证（临时运行应用）：公告出现 → 点 X → 重开设置窗口不再出现 → 手改 `settings.json` 删掉该 Id → 重开又出现；点「去备份设置」→ 落到设置管理页且「导出设置」卡片可见。（若不便运行，需在交付说明中标注未执行的原因，不得默认通过。）
- [ ] `git diff --stat` 复核改动面未越界（无 `WindBoard.csproj`、无 `.github/workflows/release.yml`、无 MSIX 相关文件）。

## 阶段 6 提交

- [ ] 提交 1：`feat(settings): 设置窗口新增可关闭公告位并展示安装版分发调整提醒`
- [ ] 提交 2：`chore(release): v2.9.5`
- [ ] 任务产物（`.trellis/tasks/09-15-v2.9.5-transitional-release/`）按仓库既有约定单独提交：`chore(task): 记录 v2.9.5 过渡版本规划产物`
- [ ] 不执行任何 `git push`。

## 阶段 G 发版前确认闸门（硬暂停）

- [ ] 向用户汇报并等待明确确认，内容至少包含：`main` 最终 commit hash、`Directory.Build.props` 版本号、一致性校验结果、**更新日志全文（zh-CN 与 en-US）**、tag 计划（`v2.9.5`）、tag 是否已存在、预期 Release 资产清单（`latest.json` + 三架构 zip + 三架构安装版 exe + 三架构无运行库安装版 exe）。
- [ ] 未收到确认前**停止**，不推送、不打 tag。

## 阶段 7 发版（仅在阶段 G 确认后执行）

按 `.agents/skills/release-main-tag/SKILL.md`：

- [ ] `git push origin main`
- [ ] tag 处理：不存在则 `git tag -a v2.9.5 -m "v2.9.5"` + `git push origin v2.9.5`；若已存在，**仅当用户明确要求**时才删除并重建（禁止强推）。
- [ ] `gh run list --workflow "Release" --limit 3` → `gh run watch <run-id> --exit-status`
- [ ] `gh release view v2.9.5 --json tagName,url,publishedAt,assets` 校验资产数量与名称。
- [ ] 失败即停止并报告失败点（不重复打 tag；修正后用 `workflow_dispatch` 重跑）。

## 风险文件与回滚点

| 风险文件 | 风险 | 回滚 |
|---|---|---|
| `WindBoard/Settings/SettingsWindow.xaml(.cs)` | 壳层布局与导航被改坏（影响所有设置页） | 单文件 `git checkout -- <file>` |
| `WindBoard/Settings/AppSettings*.cs` | 设置持久化链路回归（影响全部设置） | 单文件回滚；`AppSettings` 新字段是可选追加，旧版可读 |
| `Directory.Build.props` | 版本号错传进 Release 产物 | 单文件回滚 |
| `docs/release-notes/v2.9.5.*.md` | 更新日志写错/含 MSIX 内容 | 单文件回滚（tag 前） |
| 整体 | 并入后发现问题 | 推送前：`git reset --hard 3b2afad`；推送后：`git revert` 相关提交（需用户确认） |

## 前置检查（`task.py start` 之前）

- [ ] `prd.md` / `design.md` / `implement.md` 齐备且与最新决策一致。
- [ ] `implement.jsonl` / `check.jsonl` 已按 spec/research 清单填好真实条目。
- [ ] 无阻塞性开放问题。
- [ ] 用户已明确批准最终规划摘要。
