# 执行计划：图形绘制工具（阶段二）

> 设计依据见本任务 `design.md`；每步完成后必须全绿（build + test）并作为独立回滚点。

## 前置

- [x] 阶段一 `09-09-drawing-foundation` 已完成归档（本分支 `feature/shape-tools` 基于其 `feature/drawing-foundation` 分支）
- [x] 基线：`dotnet build WindBoard.slnx -c Release`（0 警告）与 `dotnet test WindBoard.slnx`（445/445）全绿

## 步骤清单（顺序执行）

### 步骤 1：形状域模型与命令（design A）

- [x] 新建 `Board/Items/BoardShape.cs`：`BoardShapeKind` 枚举 + `BoardShape : IBoardInkItem`（Start/End/Color/Width、BoundsWorld 外扩 Width/2、Translate、SetGeometry）
- [x] `AddStrokeCommand` → `AddInkItemCommand(IBoardInkItem)`、`RemoveStrokeCommand` → `RemoveInkItemCommand`、`BringStrokeToFrontCommand` → `BringInkItemToFrontCommand`（编译器驱动替换全部引用，含 PenTool 与测试；命令 Do/Undo 语义不变）
- [x] 新建 `UpdateShapeGeometryCommand`（(Start,End) 前后快照，镜像 UpdateStrokePointsCommand 结构）
- [x] 验证：build + test 全绿（替换后既有 445 用例不动意图）

**回滚点 1**

### 步骤 2：序列化扩展（design E）

- [x] `InkItemSnapshot` 增加 `ShapeSnapshot`（Start/End/ColorRgba/Width）；`BoardInkItemCodec` 增加 4 个形状 Kind 常量与 ↔ 枚举映射（单点）
- [x] `InkItemSnapshotJsonConverter`：写侧 `{kind, shape}`（Shape 空则 fail-fast）；读侧识别 shape 包装 + 形状 kind 集合；v1/v2 扁平路径不变
- [x] `BoardInkItemCodec.ToSnapshot/ToItem` 增加 BoardShape 分支（BuildShape 构造 + Bounds 重算单点）
- [x] 验证：build + test 全绿；新增单测：v3 形状写格式固定 / 形状读→存→再读逐值一致 / v2 兼容回归（既有用例不删）/ 未知 kind 跳过容错

**回滚点 2**

### 步骤 3：渲染与命中单点分发（design C）

- [x] `BoardSceneRenderer.DrawInkItem` 增加 `case BoardShape` → 新私有 `DrawShape`（按 Kind 分支：DrawLine/DrawLine+箭头头部/DrawRectangle/DrawEllipse，统一 _strokeBrush）
- [x] `InkItemPickTest.IsInkItemHitByPoint` 增加 `case BoardShape`：Line/Arrow 线段距离命中；Rectangle/Ellipse 走 AABB
- [x] 框选/橡皮测试类零改动确认（default 路径覆盖）
- [x] 验证：build + test 全绿；新增 PickTest 单测（线段命中/容差/矩形内部命中）

**回滚点 3**

### 步骤 4：形状工具（design B）

- [x] `BoardTool` 枚举新增 Line/Rectangle/Ellipse/Arrow
- [x] 新建 `Interaction/Tools/ShapeTool.cs`：Begin（ToolOptions 快照 → BoardShape → PreviewItem）/ Move（SetGeometry + 旧∪新脏矩形）/ End（退化几何丢弃；否则 AddInkItemCommand）/ Cancel
- [x] `BoardInputController` 构造处注册 4 个 ShapeTool 实例（与三内置工具同点）
- [x] 验证：build + test 全绿；新增 ShapeTool 单测（预览挂载/提交/退化丢弃/参数快照/取消）

**回滚点 4**

### 步骤 5：选择集泛化（design D）

- [x] `SelectTool`：选中集/点选/框选/归一化/校验泛化到 `IBoardInkItem`；变换快照按条目类型分支（Stroke→点列命令，BoardShape→UpdateShapeGeometryCommand，混合走 CompositeCommand）
- [x] BoardShape 明确跳过滚轮/双指矩阵变换（design D 决策），仅拖拽平移
- [x] `BoardCanvasControl` 侧编译器驱动适配（SelectedStrokes → SelectedItems）：选择 overlay、SelectionDock（置顶/复制/删除）、复制克隆 BoardShape
- [x] 验证：build + test 全绿；补充单测（形状选中/混合选择提交合并撤销/校验清理）

**回滚点 5**

### 步骤 6：主白板 UI + 属性面板（design F/G）

- [x] `MainWindow.xaml` ToolsDockPanel 新增形状 ToggleButton + Flyout 四项（直线/矩形/椭圆/箭头）；`ApplyToolSelection` 泛化映射
- [x] 新建 `BoardCanvasControl.ShapeProperties.cs`：单选形状时显示属性浮层（按 Kind 字段：长度+角度 / 宽+高 / 宽+高），编辑经 UpdateShapeGeometryCommand 提交，撤销/重做后刷新
- [x] 本地化：新增 key 中英文资源同步（C# 字面量 / XAML {l10n:Loc} 约定）
- [x] 验证：build + test 全绿（含本地化 Key 审计）；手测项留待步骤 8 清单

**回滚点 6**

### 步骤 7：屏幕批注接入（design G）

- [x] `ScreenAnnotationMode` 新增四形状值；工具栏新增形状按钮 + Flyout 四项；`ScreenAnnotationWindowState.SetMode` 映射同名 BoardTool
- [x] 验证：build + test 全绿；颜色/线宽经 ToolOptions 联动确认（代码链路复核）

**回滚点 7**

### 步骤 8：全量回归与收尾

- [x] `dotnet build WindBoard.slnx -c Release` 零新增警告；`dotnet test WindBoard.slnx` 全量通过（含本地化 Key 审计与日志噪声审计）
- [x] 对照 prd.md 验收清单做代码结构复核：形状接入仅经"注册点"（渲染/命中/序列化/工具四处单点），无跨类型散落 if/else 复活
- [ ] 手测回归清单（prd.md 验收 1-6 条）**待用户人工执行**：四形状绘制/预览/撤销重做、属性面板编辑、橡皮整删、颜色线宽联动、WBIX 往返、导出、屏幕批注链路、混合选择滚轮边界
- [x] 更新 `.trellis/spec`（形状接入契约、WBIX kind 扩展约定）

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
| `WindBoard/Interaction/Tools/SelectTool.cs` | 选择集泛化核心，混合选择撤销合并 |
| `WindBoard/Controls/BoardCanvasControl.Rendering.cs` | 选择 overlay/Dock/复制的类型泛化 |
| `WindBoard/Board/Persistence/*` | v3 格式扩展与 v1/v2 兼容回归 |
| `WindBoard/Rendering/Board/BoardSceneRenderer.cs` | 单点 switch 补分支，勿动 Ink 缓存 |
| `WindBoard/Features/ScreenAnnotation/**` | 模式枚举扩展与两条 UI 链路联动 |
| `WindBoard/Interaction/BoardInputController*.cs` | 仅注册点追加，触摸手势勿动 |

## 完成标准

- prd.md 全部验收项通过（手测项经用户人工执行确认）
- 父任务跨验收第 2 条达成：形状走注册路径接入，无散落式分发复活
