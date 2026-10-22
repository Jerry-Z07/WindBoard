# 技术设计：MSIX 打包形态下调试页打开配置与日志文件

## 1. 问题边界

失效点是「把应用自身数据位置交给外部进程」这条链路，不涉及数据落点、迁移、打包工程与清单（prd R4），也不涉及调试页其余功能。

根因链（证据见 prd F1-F9）：

```
调试页 → AppLog.LogDirectory / AppSettingsService.SettingsFilePath
       → %LOCALAPPDATA%\WindBoard\...        // GetFolderPath 在打包进程内返回真实路径（F5），
                                             // 但该路径下的内容被系统重定向到包私有位置、仅本进程 merge
       → StorageFolder/StorageFile.GetFromPathAsync + Launcher.Launch*Async
       → broker 把「重定向前的友好路径」交给资源管理器（非打包进程）
       → 外部进程在真实磁盘上找不到该路径 → 系统弹窗「找不到路径」
       → 而 Launcher 返回 true ⇒ 调试页按成功分支给出反馈（假成功，且无日志）
```

## 2. 方案（策略 A）

两个正交改动，互不耦合：

1. **路径映射**：打包形态把友好路径映射为「外部进程可见的真实路径」。
2. **打开方式**：统一改用 shell（`explorer.exe` / `Process.Start(UseShellExecute = true)`），与仓库既有约定一致（prd F12），不再使用 WinRT `Launcher`。

### 2.1 路径映射契约

```
friendlyRoot = <Environment.GetFolderPath(LocalApplicationData)>  // 打包进程内 = C:\Users\<u>\AppData\Local（F5 实测）
friendlyPath = <friendlyRoot>\WindBoard\...                        // 由 AppDataPaths 产出（F3）

localCacheRoot = Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalCachePath
                 // 官方：等于 Windows.Storage.ApplicationData.LocalCacheFolder().Path

visiblePath  = <localCacheRoot>\Local\<friendlyPath 相对 friendlyRoot 的尾段>
```

依据（必须随代码可追溯）：

- **官方语义**：`%LOCALAPPDATA%` 下新建文件/目录被重定向到 per-user / per-package 私有位置，且「merged at runtime to appear in the real AppData location」——该 merge 只存在于应用进程内（`windows/msix/desktop/desktop-to-uwp-behind-the-scenes`）。
- **重定向根写法**：官方 MSIX 故障排查页把重定向落点写作 `%LocalAppData%\Packages\<PackageFamilyName>\LocalCache\Local\VFS\`，即 `%LOCALAPPDATA%` → `<LocalCache>\Local`（`windows/msix/msix-troubleshooting-guide`）。
- **本机实测**：`%LOCALAPPDATA%\WindBoard\Logs\...` 的实际文件位于 `...\Packages\<PFN>\LocalCache\Local\WindBoard\Logs\...`（prd F4）。
- **诚实标注**：`Local` 这一层由「官方文档示例 + 本机实测」共同支撑，**不是 API 契约**；因此必须真机验证（prd AC2）并写入 spec（prd AC6），失败时按 prd R7 明确报错而非静默降级。

### 2.2 纯函数与注入

新增 `WindBoard/Persistence/AppDataVisiblePathResolver.cs`：

```csharp
internal static class AppDataVisiblePathResolver
{
    // 纯函数（不做文件系统访问），便于单测
    internal static bool TryMap(
        string friendlyPath,
        bool isPackaged,
        string friendlyLocalAppDataRoot,
        string? localCacheRoot,
        out string visiblePath);

    // 组合入口：仅打包分支才调用 WinAppSDK/WinRT，异常一律降级为 false
    internal static bool TryResolve(string friendlyPath, out string visiblePath);
}
```

规则：

- `isPackaged == false` → `visiblePath = friendlyPath`，返回 `true`（便携版 / 开发运行 / 旧安装版：友好路径就是真实路径）。
- `isPackaged == true`：
  - `friendlyPath` 不在 `friendlyLocalAppDataRoot` 之下 → `false`（无法映射）。
  - `localCacheRoot` 为空/空白 → `false`。
  - 否则把尾段接到 `<localCacheRoot>\Local\` 之下。
- 打包判定复用既有 `Updates/AppInstallProbe.IsPackagedProcess()`（`AppDataPaths` 已依赖该类型，无新增跨层依赖）。
- `LocalCachePath` 取值放在组合入口内并 try/catch；若实测 `Microsoft.Windows.Storage.ApplicationData.GetDefault()` 在 mediumIL 打包桌面应用中不可用，退化为 `Path.Combine(friendlyRoot, "Packages", Package.Current.Id.FamilyName, "LocalCache")`（同一判据点、可替换，不改变纯函数契约）。

### 2.3 调试页调用链

```
OnOpenLogDirectoryClicked / OnOpenCurrentLogFileClicked
OnOpenSettingsDirectoryClicked / OnOpenSettingsFileClicked
  → AppDataVisiblePathResolver.TryResolve(friendly)
       ├─ false → AppLog.Warn + 失败反馈（Settings_Debug_ActionFailed_Fmt）
       └─ true  → Directory.Exists / File.Exists(visible)     // 判断对象改为外部可见路径，与外部真实视图一致
                  → TryOpenFolderAsync(visible) / TryOpenFileAsync(visible)
                       ├─ 成功分支与失败分支都写日志（补齐当前盲区）
                       └─ 目录：explorer.exe "<dir>"；文件：Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })

OnCopyLogDirectoryClicked / OnCopySettingsFilePathClicked
  → 复制 TryResolve 的结果（prd D2）；解析失败时沿用现有失败反馈 + Warn 日志，不复制友好路径
       └─ 剪贴板失败判定只覆盖 Clipboard.SetContent；Clipboard.Flush() 失败（0x800401D0）仅记 AppLog.Warn，
          不改写为失败反馈（prd R9：SetContent 成功即已复制，Flush 只影响应用退出后的可用性）
```

### 2.4 与既有实现的关系

- 复用 `Settings/Pages/AboutSettingsPage.Updates.cs:845-859` 的 `explorer.exe` 约定（prd R6）。
- 移除 `DebugSettingsPage.xaml.cs` 中因本次改动而不再使用的 `Windows.Storage` / `Windows.System` using（仅清理本次改动引入的失效引用）。
- 不改动 `AppDataPaths` / `AppLog` / `AppSettingsService` 的对外语义与产出路径（prd R4）。

## 3. 兼容性与回归面

| 形态 | 现状 | 修复后 |
|---|---|---|
| MSIX / Store | 外部报「找不到路径」，界面误报成功 | 以可见路径 shell 打开；成功/失败都与事实一致 |
| 便携版 / 开发运行 | WinRT Launcher 打开，可用 | shell 打开；不映射路径（行为等价） |
| 旧安装版（已停发，代码仍在） | 同便携版 | 同上 |

回归关注点：

- 未打包形态改用 shell 后，`.log` / `.json` 的默认关联仍生效（本机已确认两者都有关联，prd F7）。
- 不新增本地化 key（见 §5），因此不影响本地化 key 审计；纯函数单测不影响渲染快照测试。

## 4. 风险与缓解

| 风险 | 影响 | 缓解 |
|---|---|---|
| `<LocalCache>\Local` 布局在其它/未来 Windows 版本不同 | 映射失败，功能不可用 | 失败即明确报错（R7）+ 真机验证（AC2）+ 写入 spec（AC6） |
| `Microsoft.Windows.Storage.ApplicationData.GetDefault()` 在 mediumIL 打包桌面应用中抛异常/不可用 | 映射不可用 | 组合入口 try/catch → `false` → R7 报错；退化为 `Package.Current.Id.FamilyName` 拼路径（同一判据点） |
| 未打包形态从 Launcher 改为 shell | 行为变化 | AC4 覆盖；shell 是仓库既有约定（F12） |
| 真机验证需卸载商店版 | 包数据（settings.json / 日志）随卸载丢失 | implement.md §4 列为高危步骤，执行前二次确认；验证前提示数据影响 |

## 5. 本地化与文案

不改卡片文案、不新增本地化 key：现有文案未承诺路径的具体形态，映射后语义仍准确；失败反馈复用既有 `Settings_Debug_ActionFailed_Fmt`。仅当实现中确需说明「打包形态显示外部可访问位置」时，再单独评估（不在本次范围）。

## 6. 测试策略

- 单测 `WindBoard.Tests/Persistence/AppDataVisiblePathResolverTests.cs`（文件作用域命名空间，风格对齐既有 `LocalizationKeyAuditTests`）：
  - 未打包 → 原样返回；
  - 打包 + 友好路径位于 LocalAppData 之下 → 前缀替换为 `<localCacheRoot>\Local\...`；
  - 打包 + `localCacheRoot` 为空白 → `false`；
  - 打包 + 友好路径不在 LocalAppData 之下 → `false`；
  - 边界：尾随分隔符、大小写、相对路径/异常入参。
- 打包形态的运行时行为（真实落点、broker 行为）无法单测覆盖，由 AC2 / AC3 真机验收。

## 7. 回滚

改动面：新增 `Persistence/AppDataVisiblePathResolver.cs`、修改 `Settings/Pages/DebugSettingsPage.xaml.cs`、新增单测、更新 `packaging-guidelines.md`（+ 指南同步）。`git revert` 即恢复；打包工程、清单、签名与版本注入均未改动。
