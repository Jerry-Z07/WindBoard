# 远程公告源（announcements.json）

## Goal

设置窗口壳层公告的内容改由仓库根目录 `announcements.json` 下发，维护者改文案只需提交该文件、无需发版；客户端保留校验、缓存、镜像失败轮询与「已关闭」去重。远端与缓存都不可用时公告条不显示，代码中不再保留内置公告文案。

## Background / Confirmed Facts

- 当前公告定义在 `WindBoard/Settings/AppAnnouncementCatalog.cs`：静态目录，仅一条 `installer-distribution-changed`（Warning，文案来自 `.resw`，动作 `OpenBackupSettings`）。
- 展示位置：`WindBoard/Settings/SettingsWindow.xaml` 标题栏下方、导航区上方的 `InfoBar`；`UpdateAnnouncementBar()` 在窗口构造时按目录顺序取第一条未关闭公告，全部关闭则 `IsOpen=false`，行高为 0。
- 已关闭状态持久化在 `settings.json` 的 `announcements.dismissedIds`；归一化 trim、Ordinal 去重、上限 32 条，不过滤未知 Id。
- 公告条只在设置窗口内展示，主窗口不展示；网页端（`web/`）不展示公告，导入设置时把 `announcements` 视为桌面独有分区忽略。
- MSIX（Store）形态在 `AppUpdateService` 中跳过更新检查，公告不能搭 `latest.json` 的便车，必须独立拉取。
- 仓库为公开仓库 `Jerry-Z07/WindBoard`，默认分支 `main`；现有镜像前缀只改写 `github.com` 链接，`raw.githubusercontent.com` 需补规则。
- 仓库文案规则要求界面文案只放 `.resw` / web json；远端公告文案需要在该规则上开例外并记录。
- 远端内容不可信：只允许纯文本与本地已知动作枚举，不能由远端决定任意跳转 URL。

## Requirements

- R1 内容源：仓库默认分支根目录新增 `announcements.json`；客户端从 `raw.githubusercontent.com` 拉取，并通过现有镜像前缀做失败轮询（GitHub 原链接优先）。
- R2 结构：JSON 带 `schemaVersion`；公告列表包含稳定 `id`、`severity`、按语言的 `title` / `message` / 可选 `actionText`、动作标识；未知字段忽略。不做版本范围过滤。
- R3 拉取时机：应用启动后异步拉取一次，打开设置窗口时按 TTL 判断是否刷新；拉取不得阻塞启动或窗口展示，请求超时沿用现有更新检查的 12 秒。
- R4 缓存：成功拉取后写入本地 last-known-good 缓存（含内容、ETag/Last-Modified、拉取时间）；请求失败或离线时使用缓存，不发提示、不打断用户。
- R5 校验：远端内容必须通过 schema 版本、Id 格式、severity 枚举、动作白名单、条数上限、单字段长度上限校验；任何一条公告非法则整份内容拒绝并保留旧缓存。动作只允许映射到本地已知枚举，绝不执行远端下发的任意 URL。
- R6 展示：与现状一致，按数组顺序展示第一条未被关闭的公告；同一时间只展示一条；标题、正文、按钮文案、Severity 全部来自远端。
- R7 已关闭：继续以稳定 Id 记入 `announcements.dismissedIds`；Id 不变即永久关闭，需要重新提醒时由内容侧更换 Id。
- R8 本地化：按当前界面语言取对应文案，缺失时按父语言链回退，最终回退 `zh-CN`；语言切换后重开设置窗口即生效。
- R9 失败处理：请求/解析/校验失败只写 `AppLog.Warn`，界面沿用缓存；无可用内容时公告条不显示。
- R10 兜底：不保留编译期公告文案；远端与缓存都不可用时 `IsOpen=false`。初始 `announcements.json` 为空列表（不迁移现有公告），对应 `.resw` key 删除。
- R11 规则同步：在 `AGENTS.md` 文案规则中记录「公告文案由远端 JSON 提供」的例外；更新 `web-port` 指南的“不在网页端范围”表；删除 `.resw` key 后重新生成网页端 i18n 词表。
- R12 测试：覆盖 JSON 解析、校验拒绝、语言回退、选择顺序、缓存读写、TTL、镜像失败轮询；网络访问通过可注入的请求函数隔离，单测不发真实请求。

## Acceptance Criteria

- [ ] 仓库 `announcements.json` 中的文案变更（不重新构建应用）能在 TTL 到期后或重开设置窗口时反映到公告条。
- [ ] 中文界面显示中文标题/正文/按钮，英文界面显示英文文案；缺少当前语言时回退中文且不报错。
- [ ] 关闭公告后，`settings.json` 的 `announcements.dismissedIds` 追加该 Id，下次打开设置窗口不再展示。
- [ ] 离线或接口返回错误时：有缓存显示缓存内容；无缓存时公告条不显示，且不崩溃、不弹错误框。
- [ ] 非法 JSON、未知 severity、未知动作、超长文本、超过条数上限、重复 Id 时整份内容被拒绝，界面沿用旧缓存或隐藏，并记录 Warn 日志。
- [ ] MSIX、便携版、旧安装版三种形态都能拉取公告（不依赖 `AppUpdateService`）。
- [ ] 现有 `WindBoard.Tests` 全绿，Release 构建通过零告警闸门；网页端 `pnpm i18n` 生成物与 `.resw` 一致。
- [ ] 网页端行为不变：不展示公告，设置导入仍忽略 `announcements` 分区。

## Out of Scope

- 网页端公告展示与同步。
- 用户在设置界面自定义公告源 URL。
- 按应用版本号过滤公告（`minAppVersion` / `maxAppVersion`）。
- 富文本/HTML/Markdown 公告内容；仅纯文本。
- 点击统计、公告曝光上报。
- 远端下发任意链接或新增未内置的动作类型。
