# 技术设计：图形绘制工具（阶段二）

> 需求与约束见本任务 `prd.md`（R5-R8）；父任务决策见 `../09-09-shape-drawing/prd.md`；阶段一抽象契约见其归档 `design.md`。

## 设计总览

| 改动 | 目标 | 对应需求 |
|---|---|---|
| A. 形状域模型 + 命令 | `BoardShape` 接入笔迹层，几何编辑走命令栈 | R5/R6/R8 |
| B. 形状工具 | `ShapeTool` 策略实例 ×4，经注册表接入 | R5 |
| C. 渲染/命中单点分发 | 在阶段一唯一 switch/注册点补分支 | R5/R6/跨验收第 2 条 |
| D. 选择集泛化 | `SelectTool` 从 `Stroke` 强类型泛化到 `IBoardInkItem` | R8（选中形状） |
| E. 序列化扩展 | WBIX v3 快照承载形状（不升版本） | R7 |
| F. 属性面板 | 按形状类型的数值浮层，编辑经命令栈可撤销 | R8 |
| G. 两条 UI 链路 | 主白板 + 屏幕批注工具栏接入形状入口 | R5 |

## 关键决策：单类 `BoardShape` + `Kind` 枚举（对阶段一预告的修正）

阶段一 design 曾预告"形状经 `BoardShapeBase : IBoardInkItem` 基类接入"。实施时修正为**单个 `BoardShape` 类 + `BoardShapeKind` 枚举（Line/Rectangle/Ellipse/Arrow）**，理由：

- 父 PRD 已确认本期形状全部为两点式（状态完全同构：Start/End/Color/Width），差异只在"渲染与命中的几何解释"，不构成独立类型的状态差异；
- 四个子类会让命令、选择快照、序列化、Codec、属性面板全部引入类型分支，与"不复活散落式分发"的父验收相悖；
- 与阶段一先例一致：单点 switch 即"单点注册处"，避免过度设计；未来带异构状态的形状（贝塞尔控制点等，二期）再评估拆类。

## A. 形状域模型与命令（R5/R6/R8）

### `Board/Items/BoardShape.cs`（新）

```csharp
internal enum BoardShapeKind { Line, Rectangle, Ellipse, Arrow }

internal sealed class BoardShape : IBoardInkItem
{
    public Guid Id { get; }                       // 同 Stroke：内存定位，不持久化
    public BoardShapeKind Kind { get; }
    public Vector2 Start { get; private set; }    // 世界坐标
    public Vector2 End { get; private set; }
    public Color4 Color { get; set; }             // 取当前画笔颜色（R6）
    public float Width { get; set; }              // 线宽；不支持压感（R6）
    public Rect BoundsWorld { get; }              // AABB(Start,End) 外扩 Width/2（与 Stroke 半宽语义一致）
    public void Translate(Vector2 delta);         // IBoardInkItem 契约
    internal void SetGeometry(Vector2 start, Vector2 end);  // 重算 Bounds，供命令/工具使用
}
```

- 矩形/椭圆的几何解释为轴对齐 (Start,End)；渲染与属性面板按 `Min/Max` 规范化读取，域内不强制归一化存储（保留拖拽方向信息无意义，但归一化会引入命令前后快照歧义——由读侧规范化）。
- 箭头 = 直线 + 端点箭头头部，头部尺寸 = 常量系数 × Width，无额外状态（MVP）。

### 命令（Board/Commands/）

| 命令 | 决策 |
|---|---|
| `AddStrokeCommand` → 泛化为 `AddInkItemCommand(IBoardInkItem)` | 实现本身类型无关（Insert/Remove），消除"为形状复制一个 27 行类"；PenTool 与测试同步替换 |
| `RemoveStrokeCommand` → `RemoveInkItemCommand(IBoardInkItem)` | 同上（Delete 键 / 选择 Dock 删除需覆盖形状） |
| `BringStrokeToFrontCommand` → `BringInkItemToFrontCommand(IBoardInkItem)` | 同上（选择 Dock 置顶） |
| `UpdateShapeGeometryCommand(BoardShape, (Start,End) before, (Start,End) after)` | 新增；镜像 `UpdateStrokePointsCommand` 的"前后快照 + Apply"结构；属性面板与拖拽移动共用 |
| `UpdateStrokePointsCommand` | 保持 Stroke 专用（点集 diff），不动 |

## B. 形状工具（R5）

- `BoardTool` 枚举新增 `Line / Rectangle / Ellipse / Arrow` 四值。
- `Interaction/Tools/ShapeTool.cs`（新）：**一个类，按 (BoardTool id, BoardShapeKind kind) 注册 4 个实例**。
  - `Begin`：读 `ToolOptions`（快照颜色/粗细；压感忽略），创建 `BoardShape` 挂 `PreviewItem`（语义同 PenTool）；
  - `Move`：`End = ScreenToWorld(position)` → `SetGeometry` → 脏矩形（旧∪新几何 AABB，机制与 PenTool 等价）；
  - `End`：退化几何（长度 < 1e-3 世界单位，即点击未拖动）→ 丢弃不提交；否则 `Session.Execute(new AddInkItemCommand(shape))`；
  - `Cancel`：清空（预览挂载点由控制器统一清理，与既有工具一致）。
- 注册点：`BoardInputController` 构造处与三内置工具同点 `Register`，控制器结构零改动（阶段一预留的接入方式）。
- `ToolOptions` 不扩展：形状复用 PenColor/PenBaseSize，工具身份由 `BoardTool` 枚举承载。

## C. 渲染 / 命中 / 擦除单点分发（R5/R6，跨验收第 2 条）

| 分发点 | 改动 |
|---|---|
| `BoardSceneRenderer.DrawInkItem` | switch 增加 `case BoardShape shape: DrawShape(ctx, shape)`；`DrawShape` 内按 Kind 单点分支：Line→DrawLine、Arrow→DrawLine+箭头头部、Rectangle→DrawRectangle、Ellipse→DrawEllipse；统一 `_strokeBrush`（Color=shape.Color、线宽=Width） |
| Ink 几何缓存 | 零改动（缓存键保持 Stroke，`PruneInkCache` 天然忽略形状） |
| 预览渲染 | 零改动（`DrawActiveStroke` 已接收 `IBoardInkItem`） |
| `InkItemPickTest`（点选） | 增加 `case BoardShape`：Line/Arrow → 点到线段距离 ≤ 容差+Width/2（Bounds AABB 对斜线误命中过多）；Rectangle/Ellipse → 沿用 default AABB 路径（内部+边界均可点选，符合直觉） |
| `InkItemRectSelectTest`（框选） | 零改动（default AABB 相交路径覆盖形状） |
| `InkItemHitTest`（橡皮） | 零改动（default"擦除轨迹 AABB ∩ 条目 AABB"→ 整笔删除，即 R6 语义；阶段一擦除路由已把非 Stroke 分流到整笔删除路径） |

## D. 选择集泛化（R8 前置，改动面最大）

`SelectTool` 与 `BoardCanvasControl` 侧从 `Stroke` 强类型泛化到 `IBoardInkItem`（阶段一刻意遗留的扩展点）：

- `SelectTool`：`_selectedStrokes: List<Stroke>` → `_selectedInkItems: List<IBoardInkItem>`；点选不再 `as Stroke` 截断；框选不再过滤 `is Stroke`；`ValidateSelection`/归一化按 `IBoardInkItem` 处理。对外保留 `SelectedItems`（原 `SelectedStrokes` 语义泛化）。
- 变换快照：`StrokeTransformSnapshot` 泛化为按条目类型分支——Stroke 走点列快照（`UpdateStrokePointsCommand`），BoardShape 走 (Start,End) 快照（`UpdateShapeGeometryCommand`）；混合选择经 `CompositeCommand` 合并为一次撤销（现有结构）。
- **滚轮/双指矩阵变换（缩放/旋转）对 BoardShape 跳过**：轴对齐两点式几何无法无损承载旋转/非均匀缩放；MVP 形状仅支持拖拽平移 + 属性面板精调。混合选择时矩阵变换仅作用于 Stroke（记录为已知边界）。
- `BoardCanvasControl` 侧适配（编译器驱动）：`TryGetSelectedStrokesScreenRect`、`AreSelectedStrokesTopMost`、复制/置顶快照比较等改用 `IBoardInkItem`（`InkItemScreenBounds` 阶段一已泛化）；选择 Dock（置顶/复制/删除）对形状自动生效。复制 = `BoardShape` 同 Kind/几何/样式克隆。

## E. 序列化扩展（R7）

- **版本保持 v3 不升 v4**：形状是 v3 格式内的 kind 扩展，无 v3 文件存量兼容问题（v3 尚未发布）。
- `InkItemSnapshot` 增加 `ShapeSnapshot? Shape`：`{ Vector2 Start, Vector2 End, Vector4 ColorRgba, float Width }`。
- Kind 常量集中单点：`BoardInkItemCodec` 增加 `"line" / "rect" / "ellipse" / "arrow"` ↔ `BoardShapeKind` 映射。
- `InkItemSnapshotJsonConverter`：写侧 v3 固定输出 `{kind, shape}`（Shape 为空时 fail-fast，同 Stroke 约定）；读侧识别 `shape` 包装字段，kind ∈ 形状集合 → 解析 ShapeSnapshot；v1/v2 扁平路径不变。
- `BoardInkItemCodec`：`ToSnapshot` 增加 BoardShape 分支；`ToItem` switch 增加形状 Kind → `BuildShape`（构造 + `SetGeometry` 重算 Bounds 单点，与 BuildStroke 对称）；未知 Kind 记 Warn 跳过约定不变。
- **旧 App 兼容语义**（父决策延续）：带形状的 v3 文件被旧 v3 App 读取时，Converter 扁平解析失败 → 文件拒读，与"旧 App 拒读 v3"门禁语义一致；`WbiWorkspaceImporter` 强制 stroke 路径不变。

## F. 属性面板（R8）

- 载体：`Controls/BoardCanvasControl.ShapeProperties.cs`（新 partial），仿 `SelectionDock` 的 overlay 模式：选中**单个** `BoardShape` 时显示、清选/多选/切换工具时隐藏；锚定选择包围盒下方。
- 字段按 Kind：Line/Arrow = 长度+角度；Rectangle = 宽+高；Ellipse = 宽+高（圆 = 宽高相等的特例，不单设半径字段）。
- 编辑语义：
  - Rectangle/Ellipse 以 TopLeft 为锚点改宽高；Line/Arrow 以 Start 为锚点按长度/角度重设 End；
  - 数值提交（NumberBox 提交时机：确认/失焦且值实际变化）→ `Session.Execute(new UpdateShapeGeometryCommand(...))`，天然可撤销；
  - 撤销/重做/拖拽移动后面板数值随 `UpdateSelectionOverlay` 刷新。
- 复用点：面板字段构造按 (标签, 取值, 写回) 三元组注册，为后续功能（函数绘制参数）预留同一机制。

## G. 两条 UI 链路（R5）

- 主白板：`ToolsDockPanel` 新增形状 ToggleButton，二次点击弹出 Flyout（直线/矩形/椭圆/箭头四项）；`ApplyToolSelection` 泛化isChecked 映射（工具值 → 按钮集合）。
- 屏幕批注：`ScreenAnnotationMode` 增加 Line/Rectangle/Ellipse/Arrow；工具栏新增形状按钮 + Flyout 四项；`ScreenAnnotationWindowState.SetMode` 映射到同名 `BoardTool`；`ScreenAnnotationFlow` 现有链路把 `ActiveCanvasTool` 传给画布，零结构改动。
- 本地化：新增 key 走字面量约定（C# `L10n.Get/Format`、XAML `{l10n:Loc}`），中英文资源同步补齐（LocalizationKeyAuditTests 强制）。
- Shift 约束（正方/正圆/水平垂直）为可后置增强，本阶段不实现（prd 已注明）。

## 数据流（新增形状路径）

```
工具栏(形状▾) → BoardTool.Line → ToolOptions(Tool=Line)
  → ShapeTool.Begin(读 ToolOptions 快照) → PreviewItem(BoardShape)
  → Move(SetGeometry+脏矩形) → End → AddInkItemCommand → BoardSession → BoardDocument.InkItems
  → DrawInkItem(case BoardShape) / InkItemPickTest(case BoardShape) / Codec(case kind)
  选中 → SelectTool(SelectedItem) → 属性面板 → UpdateShapeGeometryCommand
```

## 兼容与回滚

- WBIX：v3 格式内扩展；读侧未知 kind 跳过单条不阻断（新 App 容错）；v1/v2 读路径回归由既有 19 个序列化测试守护。
- `implement.md` 分步提交，每步 build+test 绿后作为独立回滚点。

## 风险与对策

| 风险 | 对策 |
|---|---|
| SelectTool 泛化波及 BoardCanvasControl 多个 partial | 编译器驱动替换，独立成步；置顶/复制/删除/框选在步骤内逐项核对手测点 |
| 滚轮变换跳过形状造成混合选择行为差异 | 设计决策明示（MVP 边界）；手测项列入回归清单 |
| NumberBox 高频 ValueChanged 造成撤销栈膨胀 | 仅在提交时机（确认/失焦且值变化）执行命令 |
| 序列化往返回归 | v2 兼容测试不删；新增 v3 形状往返 + 未知 kind 容错用例 |
| 命令类重命名波及测试 | 编译器驱动替换；命令语义（Do/Undo）不变，测试意图不动 |
