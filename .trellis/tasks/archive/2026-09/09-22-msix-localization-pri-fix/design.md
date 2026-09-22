# 技术设计：MSIX 打包形态本地化失效修复

## 1. 问题边界

失效点是 `WindBoard/Localization/L10n.cs` 的 **ResourceManager 构造方式**，不涉及：

- 资源文件本身（`.resw` 已正确进包，见 prd `F4`）
- 资源树结构（打包 PRI 子树名仍为 `Settings`，与未打包一致，见 prd `F4`）
- 语言列表与 key 元数据（构建期生成，见 prd `F8`）
- 打包工程配置（`AppxPackage`、`EnableDefaultPriItems`、清单等已工作）

因此改动收敛在 `L10n` 内部的 PRI 选择逻辑 + 一处可测的纯函数抽出。

## 2. 方案对比

| 方案 | 判据 | 优点 | 缺点 |
|---|---|---|---|
| A 包身份探测 | `AppInstallProbe.IsPackagedProcess()` → 打包用 `new ResourceManager()`，否则用 `WindBoard.pri` | 语义直白，直接对应官方文档的两种形态 | `Localization` 需依赖 `WindBoard.Updates`（跨层）；PRI 命名若再变化（如 `PriInitialPath` 生效）无法自适应 |
| **B 文件存在性（采纳）** | 应用目录存在 `WindBoard.pri` → 显式传该名；否则用默认构造（打包形态读包根 `resources.pri`） | 无跨层依赖；两种形态与未来命名变化均自适应；实测两形态文件互斥 | 判据是“隐式”的，需注释解释来由 |

采纳 **B**。支撑证据：未打包输出仅有 `WindBoard.pri`（prd `F6`），打包包目录仅有 `resources.pri`、无 `WindBoard.pri`（prd `F3`），两侧互斥。

边界行为：

- 打包形态若将来把 `WindBoard.pri` 一并带入包，B 会选中它。**该场景未经取证**：官方明确规定打包应用不可更改 PRI 文件名（prd `F5`），该场景本身违反打包约束；若真出现，显式加载 `WindBoard.pri` 在包身份下能否被 MRT 接受尚未验证（`trellis-check` 于 2026-09-22 指出该断言缺少证据，`makepri dump` 未能取得该文件的 ResourceMap 名）。当前两种真实形态实测互斥（prd `F3`/`F6`），不构成现实风险。
- 未打包形态若只有 `resources.pri`（异常部署），B 走默认构造读该文件，比现状更好。
- 两文件均缺失时回退到 key 并记录 `AppLog.Warn`，与现状一致。

## 3. 契约与数据流

```
L10n.Get(key)
  └─ Lazy<ResourceManager>.Value
       └─ CreateResourceManager()
            ├─ ResolvePriFileName(AppContext.BaseDirectory)
            │    └─ File.Exists(<baseDir>/WindBoard.pri) ? "WindBoard.pri" : null
            ├─ null  → new ResourceManager()                  // 打包形态：包根 resources.pri
            └─ 非 null → new ResourceManager("WindBoard.pri")  // 未打包形态
       └─ MainResourceMap.GetSubtree(<feature>) → TryGetValue(key, context)
```

- `ResolvePriFileName` 为纯文件系统判据，抽出为 `internal static` 以便单测。
- `CreateResourceManager` 仍只经 `Lazy<ResourceManager>` 求值一次，无热路径开销。
- 对外 API、`LocExtension`、缺 key 回退策略均不变（prd `R3`）。

拟定实现（最终以代码评审为准）：

```csharp
private static ResourceManager CreateResourceManager()
{
    string? priFileName = ResolvePriFileName(AppContext.BaseDirectory);
    return priFileName is null ? new ResourceManager() : new ResourceManager(priFileName);
}

internal static string? ResolvePriFileName(string baseDirectory)
{
    if (string.IsNullOrWhiteSpace(baseDirectory))
    {
        return null;
    }

    return File.Exists(Path.Combine(baseDirectory, AppPriFileName)) ? AppPriFileName : null;
}
```

`using System.IO;` 为新增引用；`L10n.cs` 顶部注释中“读取 `WindBoard.pri`”的描述需按两形态改写。

## 4. 兼容性与回归面

| 形态 | 现状 | 修复后 | 判定依据 |
|---|---|---|---|
| 便携版 / 开发运行 / 单测 | `WindBoard.pri` 命中 | 不变（仍显式传名） | prd `F6`、`AC5` |
| MSIX / Store | 全部回退为 key | 默认构造读包根 `resources.pri` | prd `F3`、`F5`、`AC3` |

回归关注点：

- 渲染快照测试（`WindBoard.Tests/Rendering/Snapshot/`，文本场景）依赖 unpackaged 下 L10n 正常取值，走的是未打包分支，不应受影响。
- 本地化 key 审计测试只做静态扫描，不受影响。

## 5. 测试策略

- **新增单测** `WindBoard.Tests/Localization/L10nPriFileResolutionTests.cs`（文件作用域命名空间，遵循测试工程约定）：
  - 临时目录存在 `WindBoard.pri` → 返回 `"WindBoard.pri"`
  - 临时目录不存在该文件 → 返回 `null`
  - 入参为空/空白 → 返回 `null`（防御分支）
- 打包形态的运行时行为（`new ResourceManager()` 能否读到包根 `resources.pri`）**无法在单测中覆盖**，只能由真机验收（`AC3`）证明。

## 6. 验收执行方案（本机真机，用户已确认）

前置：本机非管理员、开发者模式已开启、已安装商店版 2.10.0.0（prd `F9`）。

1. 零告警构建 + 全量单测（`AC4`）。
2. 用 VS `MSBuild.exe` 产 MSIX（`-p:WindBoardPackage=Msix`，参数同 `docs/dev/guides/msix-packaging.zh-CN.md`，`AppxPackageSigningEnabled=false`）。
3. 解包自检（`AC2`）：`.msixupload` → 改名 `.zip` 解压 → `makeappx unpack` → 断言包内 PRI 为 `resources.pri`、无 `WindBoard.pri`。
4. **高危步骤（执行前二次确认）**：`Remove-AppxPackage JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r` 卸载本机商店版。
5. `Add-AppxPackage -Register <解包目录>\AppxManifest.xml` 松散布局注册。
6. 启动应用核对译文（主窗口标题、设置窗口导航项、常规页语言项）。
7. 收尾：`Remove-AppxPackage` 移除松散注册，用户可从商店重装。
8. 便携版不回归：`dotnet publish` 到临时目录检查仍为 `WindBoard.pri`，可运行核对译文（`AC5`）。

## 7. 风险与回滚

| 风险 | 影响 | 应对 |
|---|---|---|
| 非管理员下 `Add-AppxPackage -Register` 失败 | 真机验收受阻 | 记录失败码与现象，回退到“静态验证 + 提供用户自测步骤”，并在任务中标注未验证项 |
| 卸载商店版后无法立即从商店重装（网络/账号） | 本机暂态无可用安装 | 仅在用户确认后执行；如需，可保留最后一步为可选手动操作 |
| 松散布局注册下包目录可写，与商店版只读目录行为不同 | 验收结论略有偏差 | 本修复只依赖包目录内容与包身份，与可写性无关；如出现差异单独记录 |
| `new ResourceManager()` 在 packed 环境行为与预期不符 | 修复无效 | 真机步骤 3/6 直接暴露；备选方案是打包分支显式传 `"resources.pri"`（同一判据点即可切换） |

回滚：改动仅 `L10n.cs` + 新增单测 + 文档，`git revert` 即恢复；打包工程与清单均未改动。
