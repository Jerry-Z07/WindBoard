# 执行计划：MSIX 打包形态本地化失效修复

## 0. 前置与复现证据（可选）

- **P1** 现状互斥关系已确认（prd `F3`/`F6`），无需重复取证。
- **P2（可选，建议执行）** 抓取"修复前"复现证据：启动本机已安装的商店版，关闭后读取其日志，确认存在 `L10n` 的 `缺少资源 key：...` 警告。打包形态日志可能落在 `%LOCALAPPDATA%\Packages\JerryZ07.1570025E0CBCA_*/LocalCache/...`（受虚拟化影响，需实地定位）。

## 1. 代码改动

- **1.1** `WindBoard/Localization/L10n.cs`
  - 新增 `using System.IO;`
  - 新增 `internal static string? ResolvePriFileName(string baseDirectory)`：目录下存在 `WindBoard.pri` 时返回该文件名，否则返回 `null`（空白入参返回 `null`）
  - `CreateResourceManager()` 改为按 `ResolvePriFileName(AppContext.BaseDirectory)` 的返回值选择 `new ResourceManager(priFileName)` 或 `new ResourceManager()`
  - 更新类顶部注释与 `AppPriFileName` 注释，说明两种形态的 PRI 命名与加载方式（替换现有"读取 `WindBoard.pri`"的单形态表述）
- **1.2** 新增 `WindBoard.Tests/Localization/L10nPriFileResolutionTests.cs`（文件作用域命名空间，风格对齐 `LocalizationKeyAuditTests.cs`）
  - 临时目录含 `WindBoard.pri` → 返回 `"WindBoard.pri"`
  - 临时目录不含该文件 → 返回 `null`
  - 入参为空白 → 返回 `null`
  - 测试内自建/自清临时目录，避免与并行测试互相影响
- **1.3** 文档修正（`R4`）
  - `docs/dev/guides/localization.zh-CN.md`：第 3 行与第 17 行的 "`WindBoard.pri`" 单形态表述改为区分打包/未打包
  - `docs/dev/guides/localization.en-US.md`：同步对应段落（第 3 行、第 16 行）

## 2. 验证命令（本地闸门）

```powershell
# 零告警构建
dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true

# 全量单测（含渲染快照、本地化 key 审计）
dotnet test WindBoard.slnx
```

## 3. 产包与解包自检（AC2）

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$makeappx = "C:\Users\Helicop\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.28000.2705\bin\10.0.28000.0\x64\makeappx.exe"
# 参数与 docs/dev/guides/msix-packaging.zh-CN.md 一致；不加 -p:WindBoardUnsignedTest=true
# AppxPackageDir 指向 <repo>\artifacts\msix\win-x64\，PublishDir 指向 <repo>\artifacts\publish\win-x64\msix\
```

自检：`.msixupload` 改名 `.zip` → 解压 → `makeappx unpack` → 断言包内存在 `resources.pri`、不存在 `WindBoard.pri`（可用 `makepri dump` 抽检含 `Settings_General_Language_Title`）。

## 4. 真机验收（AC3，用户已确认走此路径）

> 第 4.2 步为高危操作（卸载本机已安装的商店版），执行前必须再次向用户确认命令。

1. 记录当前安装包全名：`JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r`
2. **（高危，需二次确认）** `Remove-AppxPackage JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r`
3. `Add-AppxPackage -Register <解包目录>\AppxManifest.xml`（本机开发者模式已开启）
4. 启动应用；先确认主窗口标题与设置窗口导航/语言项
5. **程序化判据**：关闭应用后读取其日志，确认**不再出现** `L10n` 的 `缺少资源 key：...`；修复前同一路径应有大量该警告
6. 收尾：`Remove-AppxPackage <松散注册包全名>`，并告知用户可从商店重新安装

若第 3 步在非管理员下失败：记录错误码与现象，回退到"静态验证 + 向用户提供自测步骤"，并在任务中显式标注该项未验证。

## 5. 便携版不回归（AC5）

```powershell
dotnet publish WindBoard\WindBoard.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile= -o <tmp>
# 断言：输出目录仅含 WindBoard.pri（无 resources.pri）
```

## 6. 风险文件与回滚点

| 文件 | 风险 | 回滚 |
|---|---|---|
| `WindBoard/Localization/L10n.cs` | 影响所有本地化取值路径 | `git checkout --` 该文件即恢复 |
| `WindBoard.Tests/Localization/L10nPriFileResolutionTests.cs` | 新增，仅测试 | 删除文件 |
| `docs/dev/guides/localization.*.md` | 纯文档 | `git checkout --` |

真机环节回滚：`Remove-AppxPackage` 移除松散注册 → 由用户从商店重装 2.10.0.0。

## 7. `task.py start` 前检查

- [ ] `prd.md` 已收敛（无阻塞 open question、无重复事实；已含 `D1`/`D2` 决策）
- [ ] `design.md`、`implement.md` 已完成
- [ ] `implement.jsonl`、`check.jsonl` 已写入真实 spec/research 条目（非示例行）
- [ ] 最终规划摘要已提交用户，并取得显式批准（需在批准后的下一轮才可 `start`）
