# 执行计划：MSIX 打包形态下调试页打开配置与日志文件

## 1. 代码改动

- **1.1** 新增 `WindBoard/Persistence/AppDataVisiblePathResolver.cs`（块作用域 namespace，中文注释）
  - `TryMap(friendlyPath, isPackaged, friendlyLocalAppDataRoot, localCacheRoot, out visiblePath)`：纯函数、不访问文件系统（design §2.2 规则）
  - `TryResolve(friendlyPath, out visiblePath)`：组合入口，内部取 `AppInstallProbe.IsPackagedProcess()` 与 `Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalCachePath`，全程 try/catch，异常降级为 `false`
  - 不新增 `Linux`… 无；不新增第三方依赖（`Microsoft.Windows.Storage` 由既有 `Microsoft.WindowsAppSDK 2.4.0` 提供）
- **1.2** 修改 `WindBoard/Settings/Pages/DebugSettingsPage.xaml.cs`
  - 四个打开动作：`TryResolve`（失败 → `AppLog.Warn` + `Settings_Debug_ActionFailed_Fmt` 反馈）→ 对**可见路径**做 `Directory.Exists` / `File.Exists` 判断 → shell 打开
  - `TryOpenFolderAsync`：`explorer.exe "<dir>"`；`TryOpenFileAsync`：`Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`
  - 两个方法补日志：成功分支同样写 `AppLog.Info`，失败分支写 `AppLog.Warn`（消除当前"假成功无日志"）
  - 两个复制动作：复制 `TryResolve` 的结果（R8 / D2）
  - 清理本次改动后不再使用的 `using Windows.Storage;` / `using Windows.System;`（以编译结果为准）
- **1.3** 新增 `WindBoard.Tests/Persistence/AppDataVisiblePathResolverTests.cs`（文件作用域 namespace，用例见 design §6；临时目录自建自清）
- **1.4** 文档与规范同步
  - `.trellis/spec/backend/packaging-guidelines.md`：`Validation & Error Matrix` 新增「打包形态下外部进程无法解析应用友好路径」症状行；`Pending verification` 的 V4 / V5 按本次实测结论更新；记录 `<LocalCache>\Local` 落点契约与失败兜底要求
  - 必要时同步 `docs/dev/guides/msix-packaging.zh-CN.md` 的"已知限制"段落

## 2. 本地闸门（改动后先跑）

```powershell
dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true
dotnet test WindBoard.slnx
```

## 3. 产 MSIX 与解包自检（AC2 前置）

- 命令与 `docs/dev/guides/msix-packaging.zh-CN.md`「本地产包」一致（VS `MSBuild.exe`，`AppxPackageDir` / `PublishDir` 用**绝对路径**）
- **不要**传 `-p:WindBoardUnsignedTest=true`（该开关只用于 `-AllowUnsigned` 安装路径）
- 自检：`.msixupload` → 改名 `.zip` → 解压 → `makeappx unpack` → 确认包内 `resources.pri` 存在、无 `WindBoard.pri`

## 4. 真机验收（AC2 / AC3）

> **高危**：第 4.2 步会卸载本机已安装的商店版，包数据目录 `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\WindBoard`（含 `settings.json` 与 `Logs`）会随卸载被删除。执行前必须再次向用户确认命令与数据影响。

1. 记录当前包全名：`JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r`
2. **（高危，需二次确认）** `Remove-AppxPackage JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r`
3. `Add-AppxPackage -Register <解包目录>\AppxManifest.xml`（本机开发者模式已开启）
4. 启动应用 → 设置页解锁调试入口 → 依次执行：
   - 打开日志目录 / 打开当前日志文件 / 打开设置目录 / 打开设置文件 → **资源管理器 / 默认程序实际打开**
   - 复制日志目录路径 / 复制设置文件路径 → 粘到资源管理器地址栏**能定位到目标**
   - 核对打开/复制的路径形如 `C:\Users\<u>\AppData\Local\Packages\<PFN>\LocalCache\Local\WindBoard\...`
5. 程序化判据：读取该包日志（`...\LocalCache\Local\WindBoard\Logs\`），确认
   - 存在本次修复新增的成功/失败日志行；
   - 不再出现「界面提示成功但外部未打开」的组合。
6. **AC3 失败路径**：把 `Logs` 目录改名后再点「打开日志目录」→ 明确的失败反馈 + `AppLog.Warn` 记录（随后恢复目录名）。
7. 收尾：`Remove-AppxPackage <松散注册包全名>`，并告知用户可从商店重装（届时会拿到 2.10.1）。

松散布局限制（非本次缺陷）：该形态下 `AppNotificationManager.Register()` 会抛异常（spec 已记录），不影响本功能验收。

## 5. 便携版不回归（AC4）

```powershell
dotnet publish WindBoard\WindBoard.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile= -o <tmp>
```

运行后核对四个打开动作与两个复制动作（未打包形态不映射路径，路径与现状一致）。

## 6. 风险文件与回滚点

| 文件 | 风险 | 回滚 |
|---|---|---|
| `WindBoard/Persistence/AppDataVisiblePathResolver.cs` | 新增，仅被调试页调用 | 删除文件 |
| `WindBoard/Settings/Pages/DebugSettingsPage.xaml.cs` | 调试页全部打开/复制动作 | `git checkout --` 该文件 |
| `WindBoard.Tests/Persistence/AppDataVisiblePathResolverTests.cs` | 新增，仅测试 | 删除文件 |
| `.trellis/spec/backend/packaging-guidelines.md` / `docs/dev/guides/msix-packaging.zh-CN.md` | 纯文档 | `git checkout --` |

真机环节回滚：`Remove-AppxPackage` 移除松散注册 → 用户从商店重装。

## 7. `task.py start` 前检查

- [ ] `prd.md` 已收敛（Key Decisions 完整；无 blocking open question）
- [ ] `design.md`、`implement.md` 已完成
- [ ] `implement.jsonl`、`check.jsonl` 已写入真实 spec/research 条目（非 placeholder）
- [ ] 最终规划摘要已提交用户，并取得显式批准（需在批准后的下一轮才可 `start`）
