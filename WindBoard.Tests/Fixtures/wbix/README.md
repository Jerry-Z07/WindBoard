# WBIX 字节级 golden 语料

本目录是 `.wbix` 写出形态与读入兼容性的**单一事实源**。桌面端实现与后续的网页端（PWA）实现都以这里的字节为准对齐；任何写侧形态的变化都必须先在语料上体现，再由测试锁死。

## 目录结构

| 路径 | 内容 |
|---|---|
| `model/standard.json` | 主模型：3 页、6 个笔迹条目（多段变宽 stroke + line/rect/ellipse/arrow）、4 种元素（text/link/media/file）、2 个资源（内嵌图片元素 + 封面）、含视口字段；第 3 页专锁极端坐标 ±1e6 的浮点写法 |
| `model/minimal.json` | 最小模型：1 页空内容、无资源、无视图字段（用于锁住 `null` 成员仍然输出） |
| `expected/<model>/entries.txt` | zip 条目名清单，**按写出顺序**，manifest 恒为最后一项 |
| `expected/<model>/manifest.json` | `manifest.json` 的期望文本 |
| `expected/<model>/pages/page-XXX.json` | 页文件的期望文本 |
| `files/standard-v3.wbix` | 生产写侧对 `standard` 模型的真实输出（读侧回归与跨端 conformance 的输入） |
| `files/legacy-v1.wbix` | v1 扁平形态样本（手写，见下文说明） |
| `files/legacy-v2.wbix` | v2 扁平形态样本（手写，见下文说明） |

## 语料如何生成

`expected/` 与 `files/` 中的文件**全部由生产写侧生成**，测试代码不含任何手抄的 JSON 片段。重新生成：

```bash
WINDBOARD_REGEN_WBIX_GOLDEN=1 dotnet test WindBoard.slnx --filter "FullyQualifiedName~WbixGoldenFixtureTests"
```

不带该环境变量时测试进入校验模式：语料缺失即失败，内容不符即失败。

`files/legacy-v1.wbix` / `files/legacy-v2.wbix` 是例外——桌面端只写当前版本（v3），拿不到真实的历史导出文件，因此这两份由测试用「v1/v2 的扁平笔迹形态」构造后写出（见 `WbixGoldenFixtureTests.BuildLegacyFixture`）。它们锁定的是**读侧兼容契约**，不代表任何真实历史产物。

## 两处非确定性的处理约定

1. **`createdUtc`**：写侧取 `DateTimeOffset.UtcNow`，且没有可注入时钟（本阶段刻意不改生产代码）。因此期望文本中用占位符 `<createdUtc>` 代替真实时间；测试先校验真实值的格式（UTC、ISO-8601、小数位 0~7 位可变，因为 `System.Text.Json` 会去掉小数末尾的 0），再替换成占位符后与语料逐字节比对。
2. **zip 条目时间戳**：每次写出的 zip 条目时间都不同，因此只比对**条目名与顺序**和解压后的**内容字节**，不比对 zip 头。

## 行尾约定

`.wbix` 内部的 JSON 由 `JsonSerializerOptions.WriteIndented = true` 写出，**行分隔符固定为 CRLF**（写侧显式设置 `JsonSerializerOptions.NewLine = "\r\n"`，不使用平台默认值），且**没有尾随换行**。语料文件按此保存（仓库 `.editorconfig` 也要求 CRLF）。

比对时两侧统一把 `\r\n` 归一为 `\n` 再逐字符比较，同时单独断言写侧实际输出的换行确实是 CRLF、且没有尾随换行——这样即使某次检出把语料换行改成 LF，测试也不会误报为「格式变化」，而真正的写侧换行退化仍会被抓到。

## 变更规则

- 语料变更必须在提交说明中给出理由（与渲染快照基线同一约定）。
- 语料是事实源：**不允许为了让测试通过而修改写侧或修改语料**。写侧形态变化 → 先确认是有意变更 → 重新生成语料 → 在提交说明中记录。
- 破坏性格式变更（需要提升 `version`）的完整流程见 `docs/dev/guides/wbix.zh-CN.md` 的「版本契约」一节。
