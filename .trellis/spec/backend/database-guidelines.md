# Data Persistence Guidelines

> Data persistence patterns and conventions for this project.

> **Note**: This project does not use a database. Data persistence is handled through file-based formats (WBIX, JSON) and application data directories.

---

## Overview

WindBoard 的数据持久化分为三层：

1. **工作区持久化**：WBIX 格式（Zip 包含 JSON + 资源），由 `IBoardWorkspaceSerializer` 接口抽象
2. **应用设置持久化**：JSON 文件，由 `AppSettingsStore` 管理
3. **运行时数据持久化**：崩溃报告、日志文件、缓存目录，由 `AppDataPaths` 统一路径管理

---

## File Format: WBIX

WBIX（`.wbix`）是工作区的持久化格式，本质是 Zip 包：

```
.wbix (Zip)
├── manifest.json          — 格式/版本/页面索引/视口信息/资源列表
├── pages/
│   ├── page-000.json      — 笔迹(strokes) + 元素(elements)
│   └── ...
└── assets/                — 二进制资源（封面图、内嵌图片等）
```

**当前版本**: 2（`WbixWorkspaceSerializer.CurrentVersion`）

**manifest.json** 结构：
```csharp
record WbixManifest(
    string Format,          // "wbix"
    int Version,            // 2
    DateTimeOffset CreatedUtc,
    int CurrentIndex,
    IReadOnlyList<WbixManifestPage> Pages,
    IReadOnlyList<WbixResourceEntry>? Resources,
    Vector2? ViewportCameraWorld,
    float? ViewportZoom,
    Vector2? ViewportSizeDip);
```

**元素半结构化**：`WbixPageElement(Type, JsonElement)` — Type 为 "text"/"link"/"media"/"file"，Data 使用 JsonElement 不提前锁死 schema。

### 安全防护

- **路径校验**：`IsSafeZipPath` 禁止 `..` 路径穿越
- **资源大小限制**：单条目 32MB、总计 256MB
- **容错解析**：单个元素解析失败不阻断整个流程（catch 后 Warn 继续）

---

## Serialization Patterns

### 快照-运行态转换

- **运行态 → 快照**：`BoardWorkspaceSnapshotConverter.CreateSnapshot(workspace, viewportCamera, zoom, size)`
- **快照 → 运行态**：`BoardWorkspaceSnapshotApplier.CreatePages(snapshot)` — 导入直接填充 Document，不污染 Undo/Redo 栈

### 序列化接口

```csharp
internal interface IBoardWorkspaceSerializer
{
    Task SaveAsync(BoardWorkspaceSnapshot snapshot, Stream output, CancellationToken cancellationToken = default);
    Task<BoardWorkspaceSnapshot> LoadAsync(Stream input, CancellationToken cancellationToken = default);
}
```

UI 与文件格式解耦：UI 只关心 `BoardWorkspaceSnapshot`，不关心具体序列化实现。

### JSON 选项

- `System.Text.Json`
- `CamelCase` 命名策略
- 允许注释和尾逗号（`JsonCommentHandling.Skip`、`JsonTrailingCommasHandling.Allow`）

---

## Application Settings Storage

### 存储格式

- JSON 文件（`settings.json`），路径由 `AppDataPaths.SettingsFilePath` 决定
- 安装版：`%LocalAppData%\WindBoard\settings.json`
- 便携版：`{AppDir}\data\settings.json`

### 保存策略

- **防抖保存**：350ms Timer，避免高频更新频繁写磁盘
- **原子写入**：临时文件替换（`.tmp` → `Move overwrite`）
- **归一化**：加载/更新/保存后执行 `NormalizeInPlace`，补齐 null、修正非法值
- **读取容错**：读取失败/JSON 损坏时回退默认值，避免影响启动
- **快照克隆**：`AppSettingsCloner.Clone` 深拷贝，避免外部修改内部状态

---

## Path Infrastructure

### AppDataPaths

根据 `AppInstallProbe` 判断安装形态：

| 形态 | 根目录 |
|------|--------|
| Installer | `%LocalAppData%\WindBoard` |
| Portable | `{AppDir}\data`（不可写时回退 LocalAppData） |

提供的路径属性：`RootDirectory`、`SettingsFilePath`、`LogsDirectory`、`CamouflageCacheDirectory`、`DownloadsDirectory`

### AppRuntimeLayout

解析产品根目录、运行时目录、便携数据目录、Launcher/CrashReporter 路径。兼容 `shared/` 子目录布局。

---

## Common Mistakes

### ❌ DON'T
- 直接使用硬编码路径（如 `Path.Combine(localAppData, "WindBoard")`），应使用 `AppDataPaths`
- 修改 `AppSettings.Current` 后不调用 `Update`（变更不会触发事件和保存）
- 在 WBIX 加载中让单个元素失败阻断整个流程
- 保存文件时直接 `File.WriteAllText`（应使用原子写入策略）

### ✅ DO
- 通过 `AppSettingsService.Update(Action<AppSettings>)` 修改设置
- WBIX 加载中对非关键元素使用 try-catch + AppLog.Warn 容错
- 使用 `IBoardWorkspaceSerializer` 接口而非具体实现类
- 路径相关的值通过 `AppDataPaths` 统一获取
