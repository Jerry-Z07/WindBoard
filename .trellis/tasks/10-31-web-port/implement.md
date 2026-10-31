# 实施计划：WindBoard Web

阶段门禁：每个阶段末尾必须跑通该阶段验收，否则不进入下一阶段。P0 是硬门禁——互操作不通，后续全部作废。

图例：`[ ]` 未开始 / `[~]` 进行中 / `[x]` 完成

---

## P0 契约先行（互操作测试台）

目标：在没有 UI 的情况下，证明 `.wbix` 双端读写语义一致。本阶段不写任何绘画代码。

- [x] **P0.1 仓库骨架**
  - `web/` 初始化（Vite + React + TS + Fluent UI v9 + vitest + Playwright），版本冻结进 `package.json`
  - `web/.editorconfig` 覆盖根配置（LF、2 空格）；根 `.gitignore` 增加 `node_modules/ dist/ .interop/`
  - `web/README.md` 说明定位与命令；根 `AGENTS.md` 的 Docs 列表增加指向
  - 验收：`pnpm install && pnpm build` 通过；`git status` 干净（除预期新增）
- [x] **P0.2 数值与文本格式探针**
  - 实测 .NET `float.ToString("R")` 对 `0.1f / 1e-7f / 3.4028235e38f / 1f/3f / -0f / 1234567.8f` 的真实输出
  - 实现 `persistence/wbix/f32-format.ts`（最短往返 float32）与 `json-options.ts`（2 空格缩进、`\uXXXX` 转义规则）
  - 单元测试：对上述探针值逐一断言字符串相等
  - 验收：TS 与 .NET 输出逐字符一致；不一致项记录为已知差异并降级为语义比对
- [x] **P0.3 WBIX 读**
  - `manifest.ts` / `page-payload.ts` / `ink-codec.ts`（v1/v2/v3 + shape 嗅探 + `kind` 缺省）/ `paths.ts`（`\`→`/`、前缀校验、禁 `..`）/ `errors.ts`
  - 容忍策略与桌面一致（跳过坏元素、未知类型、超大资源）
  - 单元测试：每个容忍分支一条用例
- [x] **P0.4 WBIX 写**
  - 固定 v3；条目顺序 pages → assets → manifest 最后；`null` 成员输出；`page-{i:000}.json` 命名；`assets/elements/{id:N}{ext}`
  - 验收：写出的包能被桌面版打开（人工验证一次并记录）
- [x] **P0.5 语料与一致性测试台**
  - 语料目录 `WindBoard.Tests/Fixtures/wbix/`（单一事实源，web 用相对路径读取，不复制）
  - `model/*.json`：空文档、单笔单点、变宽多段笔、四种形状、双图层元素、多页、Unicode 文本、极端坐标（±1e6）
  - `*.wbix`：桌面版真实导出（v1/v2/v3 各若干）
  - C# 侧 `WindBoard.Tests/Board/Persistence/WbixInteropDumpTests.cs`（环境变量驱动目录）
  - `web/scripts/interop.mjs` 五步 A~E 全绿
  - 验收：AC3 达成；任何一步 diff 失败时脚本以非零码退出并打印首个差异路径
- [x] **P0.6 设置格式调研**
  - 读 `WindBoard/Settings/` 源码，确认设置导出文件的结构与字段名，写入 `research/05-settings-format.md`
  - 验收：文档给出字段表与一份真实样例
- [x] **P0.7 版本契约：读侧分派**（桌面端，见 design D11）
  - 引入 `IWbixPageReader` + `WbixPageReaderV1V2` / `WbixPageReaderV3` + `WbixPageReaderRegistry.For(version)`
  - `InkItemSnapshotJsonConverter` 增加读模式（`LegacyFlat` / `Wrapped`），由 options 实例注入，不改公开 API
  - v3 保留扁平条目兜底并 Warn 一次；未知版本沿用 `Wbix_VersionNotSupported_Fmt` 文案
  - 新增测试：v1 扁平、v2 扁平、v3 包装、v3 含扁平条目（兜底生效）、v4 拒绝
  - 验收：AC11；既有 50 个 `.wbix` 测试全绿且一行未改
- [x] **P0.8 字节级 golden**（桌面端，见 design D12）
  - `WindBoard.Tests/Fixtures/wbix/expected/entries.txt` + `expected/*.golden.json`
  - 断言 zip 条目名与顺序、每条解压文本逐字节相等；`createdUtc` 先正则校验格式、再归一化后比对
  - 反向验证一次：人为改动缩进/键序/数字格式，测试必须失败
  - 验收：AC12
- [x] **P0.9 笔迹几何规范**（文档）
  - 新建 `docs/dev/guides/ink-rendering.zh-CN.md`：折线段、每段线性插值半径、圆头圆角、单点圆、箭头翼、绘制顺序、常量表
  - `docs/dev/guides/wbix.zh-CN.md` 增加"版本契约"小节并交叉链接；根 `AGENTS.md` 的 Docs 列表补一行
  - 验收：AC13；文档描述与 31 张渲染快照基线一致

**P0 出口**：`pnpm verify` 全绿 + AC3 / AC11 / AC12 / AC13 达成 + 设置格式有据可查。

**P0 完成记录**（2026-10-31）：出口已满足。桌面端 Release 构建 0 警告 0 错误，`dotnet test` 649 通过 / 1 跳过 / 0 失败；网页端 `pnpm verify:all` 182 例单测 + A~E 五步全绿。
- AC3：`pnpm interop` 的 B 步比对 10 个包（7 个网页端产出 + 3 个桌面语料）逐字段零差异；C 步网页端写出的包与 `Fixtures/wbix/expected/**` 逐字节相同。
- AC11：`WbixPageReaderRegistry` 按版本区间分派，v1/v2 走扁平读器、v3 走包装读器（保留扁平兜底并每文件告警一次），既有 50 个 `.wbix` 测试一行未改。
- AC12：golden 锁定解压后条目文本 + 条目名与顺序；反向验证 4 组篡改（缩进、`24`→`24.0`、键序、f32 精度）全部被捕获。
- AC13：`docs/dev/guides/ink-rendering.zh-CN.md` 落地（238 行）。
- 实现与计划的偏差：P0.1 的 Playwright 推迟到 P5.1（P0 不需要浏览器）；P0.4 的"人工验证一次"由 `pnpm interop` 的 D/E 步自动化取代；P0.7 用版本区间（`MinVersion`/`MaxVersion`）而非单值 `SupportedVersion`；`JsonSerializerOptions.NewLine` 显式钉死 CRLF（原为平台默认）。
- 遗留：`wbix.en-US.md` 尚未同步 zh-CN 新增小节；语料 regen 先写临时目录再原子替换（防并发）；`LocalizationKeyAuditTests.CreateSkipException` 的既有反射缺陷（见交付说明）。

---

## P1 画布内核（能画、能缩、能撤）

- [x] **P1.1 域模型与命令栈**：`document/page/workspace/viewport/session` + 五类命令，粒度与桌面一致
- [x] **P1.2 几何与轮廓**：`geometry.ts`（圆头折线、变宽、箭头翼）+ `outline.ts`（展开 + zoom 分桶缓存）
- [ ] **P1.3 场景渲染**：绘制顺序、背景、单点圆、实时预览层；`rasterizer.ts` 供导出复用
- [ ] **P1.4 帧调度**：rAF、DPR 后备存储（上限 2）、书写局部重绘、运动 0.95 降质
- [x] **P1.5 指针路由**：决策表纯函数 + 表驱动测试；笔/鼠标/触摸；右键平移；滚轮锚点缩放
- [ ] **P1.6 工具**：笔（`0.5/zoom` 采样阈值、压感 clamp）、形状（两点定义）、橡皮（像素 + 整笔 + 滑动清屏，一次手势一条命令）
- [x] **P1.7 快捷键**：`Ctrl+Z`/`Ctrl+Y` 与改绑模型
- 验收：能画能缩能撤；保存 `.wbix` → 重新载入 → 视觉一致；桌面渲染快照对比无结构性差异

---

## P2 选择、元素与多页

- [ ] **P2.1 命中测试**：笔迹/形状/元素，容差 8 DIP；marquee 阈值 6 DIP
- [ ] **P2.2 选择工具**：点击/框选、移动、8 手柄缩放、双指旋转、组合命令
- [ ] **P2.3 覆盖层 UI**：选择框、8×14 DIP 手柄、操作条（置顶/复制/删除）、形状属性快捷面板（两个 NumberBox）
- [ ] **P2.4 元素卡**：文本/链接/媒体/文件四类渲染 + 双击打开（500ms/10 DIP）
- [ ] **P2.5 多页**：增删切换、恒 ≥1 页、每页独立撤销栈
- 验收：AC1 中的元素与多页部分；选择变换可撤销且重做正确

---

## P3 UI 外壳（1:1 还原）

- [ ] **P3.1 标题栏**：自定义 TitleBar、全屏隐藏；Chromium 下可选 `window-controls-overlay`
- [ ] **P3.2 Dock**：三段式布局、60×52 / 20px 图标 / 11px 文字 / 14px 圆角 / 0.85 底板 / 强制浅色；页面指示 `n / m` 单页隐藏
- [ ] **P3.3 弹出面板**：笔（**二次点击**才弹）、橡皮（两模式 + 滑动清屏）、形状（四项）、页面管理（280px 宽、104px 缩略图）、更多菜单
- [ ] **P3.4 提醒栈**：右上 InfoBar，≤3 条，自动关闭 6/8/10s
- [ ] **P3.5 设置窗口**：NavigationView + SettingsCard，5 页（常规/外观/书写/快捷键/Dock/关于）
- [ ] **P3.6 对话框**：导出、导入、确认类
- 验收：AC6、AC7；Playwright 截图与桌面参考图结构一致

---

## P4 文件能力

- [ ] **P4.1 WBIX 导入流程**：拖拽 / 选择文件 / `launchQueue`；独占警告、替换页确认、缺资源提示
- [ ] **P4.2 PNG 导出**：自动内容边界（内容 + 2×padding 24 DIP）、固定画幅（方形/4:3/16:9 × 720/1080/2160）、DPI 写入 `pHYs` 块、批量命名规则
- [ ] **P4.3 PDF 导出**：每页一张整页位图，页面尺寸 `px*72/dpi`，DPI 96/192/288（默认 192）
- [ ] **P4.4 PlatformIO**：FSA 主路径 + `<a download>` / `<input file>` 回退 + 多页 PNG 降级为 ZIP
- [ ] **P4.5 自动保存与恢复**：OPFS 分片 + IndexedDB 索引；节流 2s；崩溃后恢复提示
- [ ] **P4.6 设置导入导出**：兼容桌面设置文件（白名单合并、未知字段忽略）
- 验收：AC2、AC4、AC5；导出文件能在桌面版/系统阅读器中正常打开

---

## P5 打磨

- [ ] **P5.1 视觉回归**：Playwright 截图基线入库；Mica/亚克力区域只比对结构
- [ ] **P5.2 性能**：1 万笔迹实测，必要时启用分块位图缓存；给出帧时间与输入延迟数据
- [ ] **P5.3 触屏**：iPad Safari 压感与掌拒实测；iOS canvas 尺寸限制处理
- [ ] **P5.4 i18n**：`gen-i18n.mjs` + key 集合审计测试
- [ ] **P5.5 PWA**：manifest、图标（192/512）、SW 预缓存与静默更新、`file_handlers`
- 验收：AC8、AC9；Lighthouse PWA 可安装

---

## 依赖与顺序

```
P0 ──▶ P1 ──▶ P2 ──▶ P3 ──▶ P4 ──▶ P5
        └────────────▶ P4.2/P4.3 只依赖 P1.3 的 rasterizer，可在 P3 前并行
P0.6 ──▶ P4.6
P0.4 ──▶ P0.7 / P0.8（先有稳定的写侧，才有分派与 golden 的判据）
```

- P1.2（几何）与 P0 可并行，但 P1.3 依赖 P0 的 f32 约定
- P0.9 是纯文档，任何时刻可做；建议在 P1.2 之前完成，作为几何实现的依据
- P0.7 与 P0.8 都只动 `WindBoard/Board/Persistence/Wbix/` 与 `WindBoard.Tests/`，与 web 端完全解耦，可与 P0.1~P0.3 并行
- P2.3 覆盖层与 P3.2 Dock 可并行（不同文件）
- P5 全部依赖前序阶段完成

## 里程碑判据

| 里程碑 | 判据 |
|---|---|
| M0 契约冻结 | P0 出口全部满足；此后 `.wbix` 写出格式由 golden 测试强制锁定，任何格式变化必须同步更新 golden 并在提交说明中给出理由 |
| M1 可书写 | P1 出口：能画能缩能撤，重载一致 |
| M2 可编辑 | P2 出口：选择变换与多页完整 |
| M3 可用 | P3 + P4 出口：AC1/AC2/AC4/AC5/AC6/AC7 全绿 |
| M4 可交付 | P5 出口：AC8/AC9 全绿（AC10 是全程约束，每阶段回归验证） |
