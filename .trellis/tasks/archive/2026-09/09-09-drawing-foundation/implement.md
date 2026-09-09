# 执行计划：绘制架构可插拔地基重构（阶段一）

> 设计依据见本任务 `design.md`；每步完成后必须全绿（build + test）并作为独立回滚点。

## 前置

- [ ] 确认工作区干净（当前 develop 分支），创建任务分支 `feature/drawing-foundation`
- [ ] 基线：`dotnet build WindBoard.slnx -c Release` 与 `dotnet test WindBoard.slnx` 全绿后记录结果

## 步骤清单（顺序执行）

### 步骤 1：绘制条目抽象与集合迁移（design A 前半）

- [x] 新建 `Board/Items/IBoardInkItem.cs`（Id/BoundsWorld/Translate 契约；BoundsWorld 采用 Vortice Rect，Empty 哨兵语义见实现注释）
- [x] `Stroke` 实现 `IBoardInkItem`（行为不变，仅加接口）
- [x] `BoardDocument.Strokes` → `InkItems: List<IBoardInkItem>`，编译器驱动替换全部引用（含测试工程）
- [ ] 验证：build + test 全绿（✅ 374/374）；手测项留待步骤 7 统一执行

**回滚点 1**

### 步骤 2：渲染分发单点化（design A）

- [x] `BoardSceneRenderer.DrawStroke` 收敛为 `DrawInkItem(IBoardInkItem)` 单点 switch（Stroke 分支内部逻辑原样）
- [x] Ink 几何缓存键保持 `Stroke` 不动
- [ ] 验证：build + test 全绿（✅ 374/374）；手测压感绘制、荧光笔色、缩放下的脏矩形刷新留待步骤 7

**回滚点 2**

### 步骤 3：命中与擦除分发单点化（design A）

- [x] `StrokeHitTest` → `InkItemHitTest`（折线算法保留为 Stroke 分支）
- [x] 擦除路由按条目类型分流：折线走像素分割，其它走整笔删除路径；`IBoardEraser` 注入点语义不变
- [x] `StrokePickTest/StrokeRectSelectTest/StrokeScreenBounds` 对应泛化（独立 `InkItem*` 类，保持文件职责对齐）
- [ ] 验证：build + test 全绿（✅ 393/393，+19 新用例）；手测橡皮两种模式、框选、点选留待步骤 7

**回滚点 3**

### 步骤 4：工具策略化（design B，拆四个子步）

- [x] 4a 新建 `IBoardTool/ToolInput/BoardInputContext/BoardToolRegistry`，迁移 `PenTool`（ActiveStroke 状态内聚，预览经 context 暴露）
- [x] 4b 迁移 `EraserTool`（橡皮快照状态内聚）
- [x] 4c 迁移 `SelectTool`（marquee/变换快照内聚）
- [x] 4d `BoardInputController` 瘦身收尾：删除已迁移的工具运行态字段；Manipulation partial 不迁移
- [x] 验证（每个子步）：build + test 全绿（✅ 404/412/423/423，+30 新用例）；4d 后手测三工具全流程 + 双指手势留待步骤 7

**回滚点 4a/4b/4c/4d**

### 步骤 5：ToolOptions 参数链收敛（design C）

- [x] 新建 `ToolOptions` 值对象；`BoardCanvasControl` 属性面收敛（保留 `Tool` 便捷属性）
- [x] `MainWindow`、`ScreenAnnotationWindow/Flow` 调用点同步改造
- [x] 控制器/工具经 `ToolInput` 读取参数，删除逐跳可写属性
- [ ] 验证：build + test 全绿（✅ 426/426，+3 新用例：LastMarqueeClickedElement 正向用例、PenTool 参数快照/新笔迹用例；含前轮 check 修复 S1 回退映射统一、S2 e.Handled 对齐、S3 事件计数断言）；手测主窗口与屏幕批注两条入口的颜色/粗细/压感设置留待步骤 7

**回滚点 5**

### 步骤 6：序列化收敛与 WBIX v3（design D）

- [x] `InkItemSnapshot`（Kind 缺省 "stroke"）替换 `StrokeSnapshot` 直接暴露；`CurrentVersion` → 3
- [x] 新建 `BoardInkItemCodec`，`Converter/Applier`、`BoardRasterExporter`、`WbiWorkspaceImporter` 三处改为调用 Codec；另新增 `InkItemSnapshotJsonConverter` 处理 v2 扁平/v3 包装双形态（design 伪代码勘误见 design.md D 节）
- [x] 新增单测 19 个：v2 无 Kind 读 / 显式 null Kind 读 / v2 读→存→再读逐值一致 / v3 写格式固定 / Wbi 导入 / Bounds 精确断言（445/445 全绿）
- [ ] 验证：build + test 全绿（✅）；手测项（旧 v2 文件打开→保存→重开一致、导出 PNG）留待用户人工执行

**回滚点 6**

### 步骤 7：全量回归与收尾

- [x] `dotnet build WindBoard.slnx -c Release` 零新增警告；`dotnet test WindBoard.slnx` 全量通过 445/445（含本地化 Key 审计与日志噪声审计）
- [ ] 手测回归清单（prd.md 验收第 2 条）逐项过：画笔/荧光/橡皮两种/选择框选变换/多页/撤销重做/保存加载/导出/屏幕批注/双指手势（**待用户人工执行**）
- [x] 对照 prd.md 验收第 3 条做代码结构复核（渲染/命中/擦除/序列化单点分发 + 控制器无工具运行态字段，三轮 check 完成）
- [x] 更新 `.trellis/spec`：backend/directory-structure（Board/Items 抽象与单点分发契约）、backend/database-guidelines（WBIX v3 + Codec 约定 + 两条 DON'T）、frontend/directory-structure（Interaction/Tools 策略化与 ToolOptions 约定）

## 验证命令

```bash
dotnet build WindBoard.slnx -c Release
dotnet test WindBoard.slnx
# 单测过滤示例
dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Board"
```

## 高风险文件（改动时重点复核）

| 文件 | 风险 |
|---|---|
| `WindBoard/Board/BoardDocument.cs` | 集合类型迁移核心 |
| `WindBoard/Interaction/BoardInputController*.cs`（7 个 partial） | 工具状态机拆解，触摸手势勿动 |
| `WindBoard/Rendering/Board/BoardSceneRenderer.cs` | 1300+ 行，渲染分支收敛 + Ink 缓存键 |
| `WindBoard/Board/Persistence/*` | 版本与兼容，往返测试必须先写 |
| `WindBoard/Features/Export/Services/BoardRasterExporter.cs` | 第三份重建拷贝消除 |
| `WindBoard/Features/ScreenAnnotation/**` | 第 5 条参数入口，两条 UI 链路都要手测 |

## 完成标准

- prd.md 全部验收项通过；父任务跨验收第 1 条（行为零回归）达成
- 阶段二任务（`09-09-shape-tools`）可基于本抽象开工
