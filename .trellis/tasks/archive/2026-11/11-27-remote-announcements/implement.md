# 实施计划：远程公告源（announcements.json）

## 顺序

1. **内容源**：新增仓库根 `announcements.json`，初始为空列表 `{"schemaVersion":1,"announcements":[]}`（不迁移现有公告）。
2. **模型与选择器**：
   - 新增 `AppAnnouncementText`；
   - 重写 `AppAnnouncement` 为不可变数据 + `ResolveText`（父语言链 → `zh-CN`）；
   - 用 `AppAnnouncementSelector.SelectNext` 替换 `AppAnnouncementCatalog`，删除静态 `All`；
   - 迁移 `WindBoard.Tests/Settings/AppAnnouncementCatalogTests.cs` 为 `AppAnnouncementSelectorTests.cs`，补空列表用例。
3. **解析器**：`AppAnnouncementPayloadParser` + `TryParse`，实现 design.md §2 全部校验与上限；新增解析器测试（含未知字段忽略、整份拒绝场景）。
4. **缓存**：`AppAnnouncementCache` / `AppAnnouncementCacheStore`，路径 `AppDataPaths.RootDirectory/announcements.cache.json`，`.tmp` + `File.Move(overwrite: true)`；补读写、损坏缓存测试。
5. **服务**：`AppAnnouncementService` 单例；注入式 fetch 委托、缓存存储、时钟；TTL 6h、并发闸、`Changed`、镜像失败轮询；补服务测试（TTL、304、失败顺序、解析失败保留旧快照）。
6. **URL 改写**：扩展 `DownloadSourceUrlRewriter.Rewrite` 支持 `raw.githubusercontent.com`；补用例并确认原有 `github.com` 用例不回归。
7. **UI 接入**：`SettingsWindow` 订阅 `Changed` + `DispatcherQueue` 回 UI 线程，`Closed` 退订；`UpdateAnnouncementBar` 改用服务快照与 `ResolveText`；启动时触发刷新。
8. **启动预热**：`App.OnLaunched` 在 `MainWindow` 创建后 fire-and-forget `RefreshIfStaleAsync()`。
9. **文案清理**：删除 `WindBoard/Strings/{zh-CN,en-US}/SettingsWindow.resw` 中三个公告 key；`cd web && pnpm i18n` 重新生成词表。
10. **规则同步**：更新 `AGENTS.md` 文案例外；更新 `docs/dev/guides/web-port.zh-CN.md` 的“不在网页端范围”表。
11. **验证**：执行下方命令，修到全绿；确认 `git diff --check` 无行尾问题。

## 关键文件 / 风险点

| 文件 | 风险 |
|---|---|
| `WindBoard/Settings/SettingsWindow.xaml.cs` | 后台线程回调必须回 UI 线程；窗口关闭后退订，避免对已关闭窗口操作 |
| `WindBoard/Updates/DownloadSourceUrlRewriter.cs` | 更新下载共用；只新增 raw 判定，不能改变 `github.com` 行为 |
| `WindBoard/Strings/**/SettingsWindow.resw` | 删除 key 后必须重新生成 web i18n，否则 `i18n.test.ts` 失败 |
| `WindBoard.Tests/Settings/AppAnnouncementCatalogTests.cs` | 删除/重命名后注意命名空间仍为 `WindBoard.Tests.Settings` |
| `WindBoard/Settings/AppAnnouncementService.cs` | 单例静态初始化不得读 UI/设置；异常必须内部捕获，不能产生未观察任务异常 |
| `announcements.json` | 远端内容即产品文案；提交前核对中英文与长度上限 |

## 验证命令

```powershell
dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true
dotnet test WindBoard.slnx
cd web; pnpm i18n; pnpm verify
git diff --check
```

## 手工冒烟

1. 正常联网启动，打开设置窗口，确认公告条显示远端文案与按钮。
2. 点击「去备份设置」确认跳转到“关于 → 设置管理”并滚动到导出卡片。
3. 点击关闭 ×，重开设置窗口，确认不再显示；检查 `settings.json` 的 `announcements.dismissedIds`。
4. 断网重启，确认有缓存时仍显示；删除 `announcements.cache.json` 后断网重启，确认公告条不显示且无错误弹窗。

## 完成前检查

- [ ] 解析器/服务测试均为离线测试，不依赖真实网络
- [ ] `AppAnnouncementCatalog` 引用与三份公告 `.resw` key 已无残留
- [ ] `AGENTS.md` 与 `web-port` 指南已同步
- [ ] Release 构建零告警、全量单测、`pnpm verify` 全绿
