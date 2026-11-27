# 设计：远程公告源（announcements.json）

## 1. 架构与边界

```
仓库根 announcements.json
        │ HTTPS（GitHub raw 优先，镜像前缀兜底）
        ▼
AppAnnouncementService（单例，WindBoard/Settings/）
  ├─ 内存快照 IReadOnlyList<AppAnnouncement>
  ├─ TTL / 并发闸 / Changed 事件
  ├─ AppAnnouncementPayloadParser（纯函数校验 + 解析）
  └─ AppAnnouncementCacheStore（last-known-good 落盘）
        │ Current / Changed
        ▼
SettingsWindow（InfoBar 渲染、已关闭去重、动作分发）
```

- `AppAnnouncementService` 是唯一网络入口，UI 不直接发请求。
- 解析与选择逻辑保持纯函数，便于单测。
- 已关闭状态仍由 `AppSettingsService` 负责，公告服务只读取 `GetDismissedAnnouncementIds()` 的结果。

## 2. 数据契约

远端文件（UTF-8 JSON，camelCase）：

```json
{
  "schemaVersion": 1,
  "announcements": [
    {
      "id": "installer-distribution-changed",
      "severity": "warning",
      "action": "openBackupSettings",
      "text": {
        "zh-CN": {
          "title": "安装方式调整提醒",
          "message": "安装版（安装包）分发方式将调整，建议先备份设置，以便迁移后导入。",
          "actionText": "去备份设置"
        },
        "en-US": {
          "title": "Installer distribution is changing",
          "message": "The installer-based distribution will be adjusted. Back up your settings first so you can import them after migrating.",
          "actionText": "Back up settings"
        }
      }
    }
  ]
}
```

规则：

| 字段 | 规则 |
|---|---|
| `schemaVersion` | 必须为 `1`；其它值整份拒绝 |
| `announcements` | 可为空数组（表示隐藏公告条）；最多 8 条 |
| `id` | 必填，1–64 字符，仅 `a-z` / `0-9` / `-`；同一份内容内不得重复 |
| `severity` | 必填，匹配 `InfoBarSeverity`：`informational` / `success` / `warning` / `error`（忽略大小写） |
| `action` | 可选，默认 `none`；只接受 `none` / `openBackupSettings`（忽略大小写） |
| `text` | 必填，最多 8 个语言；必须包含 `zh-CN` |
| `title` | 必填，trim 后 1–120 字符 |
| `message` | 必填，trim 后 1–500 字符 |
| `actionText` | `action != none` 时必填，trim 后 1–32 字符；否则可省略 |
| 未知字段 | 忽略（向前兼容） |

任何一条公告不满足规则 → 整份内容拒绝，保留旧缓存，写 `AppLog.Warn("Announcements", ...)`。不做版本范围过滤。

## 3. 模型与文件

| 文件 | 职责 |
|---|---|
| `WindBoard/Settings/AppAnnouncement.cs` | 领域模型：`Id`、`Severity`、`Action`、按语言文本；`ResolveText(cultureName)` 做回退 |
| `WindBoard/Settings/AppAnnouncementText.cs` | `Title` / `Message` / `ActionText` 值对象 |
| `WindBoard/Settings/AppAnnouncementSelector.cs` | 纯函数 `SelectNext(list, dismissedIds)`；替换现有 `AppAnnouncementCatalog`（删除静态目录） |
| `WindBoard/Settings/AppAnnouncementPayloadParser.cs` | JSON → `IReadOnlyList<AppAnnouncement>`，含全部校验常量 |
| `WindBoard/Settings/AppAnnouncementCache.cs` | 缓存 DTO + `AppAnnouncementCacheStore`（原子写） |
| `WindBoard/Settings/AppAnnouncementService.cs` | 单例、TTL、并发闸、镜像失败轮询、`Changed` 事件 |
| `announcements.json`（仓库根） | 唯一内容源 |

`AppAnnouncementAction` 枚举保留：`None`、`OpenBackupSettings`。

语言解析：把当前 `CultureInfo.CurrentUICulture.Name` 规范化（`_`→`-`），依次尝试自身、父语言链，最后 `zh-CN`；命中 `text` 的第一项即用。解析结果在**渲染时**计算，不在拉取时固化，因此切换语言后重开窗口无需重新拉取。

## 4. 拉取与缓存

- URL：`https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/announcements.json`，复用 `UpdateConstants.RepoOwner/RepoName`。
- 镜像：扩展 `DownloadSourceUrlRewriter.Rewrite` 使其同时识别 `https://raw.githubusercontent.com/`；失败顺序复用 `BuildFailoverOrder(DownloadSourceId.Github)`（GitHub → GhProxy → ZeroSeven）。只改写 URL，不触发测速。
- HTTP：独立 `HttpClient`，超时 12 秒，`User-Agent: WindBoard/{version}`，`Accept: application/json`；带 `If-None-Match` / `If-Modified-Since`。
- 结果处理：
  - `200` → 读取内容并解析；解析失败整份拒绝，**不再尝试镜像**（避免把镜像篡改内容当合法内容）。
  - `304` → 只刷新缓存时间，不触发 UI 更新。
  - 非成功状态 / 传输异常 → 记录 Debug/Warn，尝试下一个源。
  - 全部失败 → 记录一次 Warn，界面沿用缓存。
- TTL：`6` 小时。`RefreshIfStaleAsync()` 在启动和打开设置窗口时调用；并发闸保证同一时刻只有一次刷新。
- 缓存文件：`{AppDataPaths.RootDirectory}/announcements.cache.json`，内容为 `content`（原始 JSON 字符串）+ `etag` + `lastModified` + `fetchedAtUtc` + `schemaVersion`；写入使用 `.tmp` + `File.Move(overwrite: true)`，读取/解析失败静默忽略并 Warn。
- 缓存中的内容每次启动重新走一遍当前版本的解析器，旧版本写入的非法内容不会直接展示。

## 5. UI 集成

`SettingsWindow`：

- 构造函数中先 `UpdateAnnouncementBar()`（用服务当前快照，通常来自缓存），再订阅 `AppAnnouncementService.Instance.Changed`，并触发 `RefreshIfStaleAsync()`。
- `Changed` 回调通过 `DispatcherQueue.TryEnqueue(UpdateAnnouncementBar)` 回 UI 线程；窗口 `Closed` 时退订。
- `UpdateAnnouncementBar()`：
  - `SelectNext(service.Current, GetDismissedAnnouncementIds())`；
  - 无结果 → `IsOpen=false`；
  - 有结果 → 用 `ResolveText(CultureInfo.CurrentUICulture.Name)` 赋值 Title/Message；
  - `Action == None` 或无 `ActionText` → 隐藏按钮，否则显示；
  - `Severity` 来自远端。
- 关闭按钮仍只做 `DismissAnnouncement(_currentAnnouncement.Id)`，InfoBar 自行收起；不立即切换下一条（保持现有交互）。
- 动作分发保持 `OpenBackupSettings → NavigateToBackupSettings()`。

启动预热：`App.OnLaunched` 在 `MainWindow` 创建后 fire-and-forget `AppAnnouncementService.Instance.RefreshIfStaleAsync()`，方法内部捕获全部异常，不阻塞启动。

## 6. 安全与合规

- 远端只能提供纯文本与已知动作枚举；动作字符串白名单映射，绝不接受 URL。
- 文本长度、条数、语言数量、Id 字符集均有上限；`InfoBar` 以纯文本显示，不做 XAML/HTML 解析。
- 只通过 HTTPS 请求；GitHub 原链接优先，镜像仅在传输层失败时兜底；解析失败不回退到镜像。
- 请求不携带用户标识；不新增设置开关，与“公告只读展示”的定位一致。
- MSIX 为 `runFullTrust` 桌面应用，不需要 `internetClient` 能力声明；三种安装形态行为一致。

## 7. 本地化与文档同步

- 删除 `SettingsWindow_Announcement_InstallerDistribution_Title/Message/Action` 三个 `.resw` key；跑 `cd web && pnpm i18n` 重新生成 `web/src/i18n/locales/*.generated.ts`。
- `AGENTS.md`「文案与注释」新增例外：公告文案由仓库根 `announcements.json` 提供，要求同时给出 `zh-CN` 与 `en-US`。
- `docs/dev/guides/web-port.zh-CN.md` 的“不在网页端范围内”表新增一行：设置窗口壳层公告不做。
- `LocalizationKeyAuditTests` / `TerminologyAuditTests` 不覆盖远端内容，不需要改测试规则。

## 8. 兼容性与迁移

- 旧版本客户端不认识远端公告，继续显示当前内置公告；升级后 `.resw` key 删除，公告只来自网络/缓存。
- `dismissedIds` 格式不变；现有已关闭用户升级后不会重新看到同 Id 公告。
- 首次升级且离线：无缓存 → 公告条不显示（已确认的产品取舍）。
- 网页端不变：仍不展示公告，导入设置时忽略 `announcements`。

## 9. 权衡

| 方案 | 取舍 |
|---|---|
| 仓库 raw JSON（本设计） | 改文案零发版；依赖 GitHub/镜像可达性；首次离线无公告 |
| Release 附件 | 复用发布链路但更新需重传资产，且与 Store 用户脱节 |
| 设置内自定义 URL | 灵活但扩大攻击面，且用户配置成本高 |
| 保留内置兜底 | 离线可用，但等于继续维护一份写死文案，与需求冲突（已否决） |

## 10. 测试计划

- 解析器：合法内容、空列表、schema 版本错误、重复 Id、非法 Id 字符、未知 severity/action、缺 `zh-CN`、超长/空文本、条数超限、未知字段忽略。
- 语言回退：`en-US` 命中、`zh-Hans-CN` 回退 `zh-CN`、未知语言回退 `zh-CN`（测试传 culture 名，不改进程全局状态）。
- 选择器：现有 6 个用例迁移到 `AppAnnouncementSelector`，补空列表用例。
- 缓存：读写往返、损坏缓存忽略、原子写后内容可读。
- 服务：TTL 内不发请求；过期后请求；304 不改变快照；失败轮询顺序；解析失败中止轮询并保留旧快照；成功后触发 `Changed`（均用注入的 fetch 委托，不发真实网络请求）。
- URL 改写：新增 `raw.githubusercontent.com` 的镜像前缀用例。
- 构建/测试命令见 `implement.md`。

## 11. 回滚

- 单个提交内完成；回滚即恢复 `AppAnnouncementCatalog` 与 `.resw` key，删除 `announcements.json` 与缓存文件读取。
- 缓存文件可安全删除，客户端会重新拉取；不影响 `settings.json`。
