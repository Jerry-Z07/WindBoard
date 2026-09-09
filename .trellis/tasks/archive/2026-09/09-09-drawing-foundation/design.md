# 技术设计：绘制架构可插拔地基重构（阶段一）

> 需求与约束见本任务 `prd.md`；父任务决策见 `../09-09-shape-drawing/prd.md`。

## 设计总览

四个正交改动，全部以"行为零回归"为边界：

| 改动 | 目标 | 对应需求 |
|---|---|---|
| A. 绘制条目抽象 | 笔迹层统一表达 + 分发点单点化 | R1 |
| B. 工具策略化 | 工具状态内聚 + 注册表 | R2 |
| C. 参数链收敛 | ToolOptions 值对象 | R4 |
| D. 序列化收敛 | 单点 Codec + WBIX v3 | R3 |

## A. 绘制条目抽象（R1）

### 核心决策：`IBoardInkItem` 统一笔迹层

形状与笔迹必须按创建顺序交错叠放（z-order 语义），**不能**为形状单开集合层；元素层（Below/AboveInk）保持不动。

```csharp
// Board/Items/IBoardInkItem.cs（新目录 Board/Items/）
internal interface IBoardInkItem
{
    Guid Id { get; }
    RectBounds BoundsWorld { get; }
    void Translate(Vector2 delta);
}
```

- `Stroke` 实现该接口（既有行为原样封装，Points 折线语义不变）。
- 阶段二形状经 `BoardShapeBase : IBoardInkItem` 接入，本阶段**只交付抽象与分发点，不交付具体形状**。
- `BoardDocument.Strokes` → `InkItems: List<IBoardInkItem>`：属性改名以匹配语义（避免 `Strokes` 装形状的误导），全引用编译器驱动替换。

### 分发点单点化（本任务的核心验收结构）

| 分发点 | 收敛为 | 说明 |
|---|---|---|
| 渲染 | `BoardSceneRenderer.DrawInkItem(IBoardInkItem)` | 单点 switch；现仅 Stroke 分支（原 DrawStroke 三分支逻辑原样保留）；Ink 几何缓存键保持 `Stroke`（仅折线分支使用） |
| 命中 | `InkItemHitTest`（原 StrokeHitTest 改造） | 折线算法保留为 Stroke 分支；不可折线条目走"Bounds/几何命中"通用路径 |
| 擦除 | 擦除路由按条目类型分流 | 像素分割擦除仅对 Stroke 有意义；其它条目直接走整笔删除路径（`WholeStrokeEraser` 语义），`IBoardEraser` 策略注入点保留 |
| 序列化 | 见 D 节 Codec | |
| 选择/变换 | `UpdateStrokePointsCommand` 泛化 | 折线专属的"点集 diff"命令保持 Stroke 专用；通用位移/变换经接口契约，命令栈不区分类型（`BoardSession.Execute` 已天然支持） |

不引入渲染器注册表的 abstraction：单点 switch 已满足"单点分发"验收，避免过度设计；阶段二若形状渲染器增多再评估升级。

## B. 工具策略化（R2）

### 接口与状态内聚

```csharp
internal interface IBoardTool
{
    BoardToolId Id { get; }
    void Begin(in ToolInput input);
    void Move(in ToolInput input);
    void End(in ToolInput input);
    void Cancel();
}
```

- `ToolInput`：指针位置/压感/设备类型 + `BoardInputContext`（viewport、document、session、脏矩形请求、ToolOptions、预览项挂载）。
- 三个工具各迁移为独立类型，运行态字段随之内聚：
  - `PenToolState`：`ActiveStroke`（预览条目经 context 暴露，渲染器照常绘制活动笔迹）
  - `EraserToolState`：`_isErasing/_lastEraserWorld/快照`（内部继续使用 `IBoardEraser` 策略）
  - `SelectToolState`：marquee、变换快照、选中集
- `BoardInputController` 瘦身为：指针事件路由、活动 pointerId 跟踪、工具调度；**触摸双指手势（Manipulation partial）与工具正交，留在控制器不迁移**，避免行为风险。

### 注册表与 UI 属性面

- `BoardToolRegistry`：id → 工具实例的简单字典，`BoardCanvasControl` 初始化时注册 3 个内置工具。
- `BoardTool` 枚举与 `Tool` 属性**对外语义不变**（主窗口/批注层零改动），内部经枚举→注册表解析工具实例。
- 阶段二接入形状工具 = 枚举加值 + 注册新工具实例，不需要改控制器结构。

## C. 参数链收敛（R4）

```csharp
internal readonly record struct ToolOptions(
    BoardTool Tool, Color4 PenColor, float PenBaseSize, bool PenEnablePressure);
```

- `BoardCanvasControl` 属性面收敛：`ToolOptions` 单属性 + 保留 `Tool` 便捷属性（沿用现有 UI 调用习惯）；`PenColor/PenBaseSize/PenEnablePressure` 旧属性删除，`MainWindow`、`ScreenAnnotationWindow/Flow` 调用点同步改为组合 ToolOptions。
- 控制器/工具经 `ToolInput` 读取，不再持有可写参数属性。
- "当前选中值不持久化"现状不变（不扩需求，见父 PRD R4 边界）。

## D. 序列化收敛（R3）

### 快照结构：扁平 + Kind 判别

```csharp
internal sealed class InkItemSnapshot
{
    public string Kind { get; set; } = "stroke";   // 旧文件缺省 → "stroke"
    public StrokeSnapshot? Stroke { get; set; }
    // 阶段二：public ShapeSnapshot? Shape { get; set; }
}
```

- 与 Wbix 元素的 `{kind, data}` 模式（`WbixWorkspaceSerializer.Save.cs:249`）保持一致。
- `WbixWorkspaceSerializer.CurrentVersion` 2 → 3；读侧门禁 `<= CurrentVersion` 不变；无逐版本迁移代码（父决策）。

> **实施勘误（2026-09-09）**：原伪代码注释"旧文件缺省 → 天然兼容 v1/v2"不完整——v1/v2 的条目 JSON 是**扁平形态**（`points/colorRgba/...` 直接在条目对象上，无 `stroke` 子对象），仅靠 C# 属性缺省值反序列化会得到 `Stroke=null` 并导致整个文件读取失败。实际实现新增 `InkItemSnapshotJsonConverter` 单点处理两种形态（读 v2 扁平自动包装 / v3 `{kind, stroke}` 包装读写），职责划分：Converter 管形态、Codec 管值换算。读侧另将 Kind 为 null/空白显式归一为 "stroke"（不依赖缺省值），未知 Kind 记 `Warn("WBIX")` 后跳过单条、不阻断导入。

### 三份重建拷贝收敛为单点 Codec

新建 `BoardInkItemCodec`（快照↔域双向转换 + Bounds 重算），以下三处改为调用 Codec，消除重复：
1. `BoardWorkspaceSnapshotConverter/Applier`（主链路）
2. `Features/Export/Services/BoardRasterExporter`（快照→域重建，`BoardRasterExporter.cs:230-252` 删除私有重建逻辑）
3. `Features/Import/Wbi/WbiWorkspaceImporter`（旧 Wbi 格式无 Kind → 强制按 stroke 路径）

## 数据流（重构后）

```
UI(Tool/颜色) → ToolOptions → BoardCanvasControl → BoardInputController(路由)
  → IBoardTool.Begin/Move/End(BoardInputContext)
  → BoardSession.Execute(命令) → BoardDocument.InkItems
  → DirtyRect → BoardSceneRenderer.DrawInkItem(单点分发) → D2D
```

## 兼容与回滚

- 纯重构无数据迁移：v1/v2 文件经 `Kind` 缺省值兼容；输出 v3 文件，旧 App 拒读（现状门禁行为延续，父决策已确认）。
- `implement.md` 分步提交，每步 build+test 绿后作为独立回滚点。

## 风险与对策

| 风险 | 对策 |
|---|---|
| `List<Stroke>` → `InkItems` 迁移波及面大 | 编译器驱动逐文件替换；单独成步、单独提交 |
| Ink 几何缓存键类型变化 | 缓存仅存在于 Stroke 渲染分支，键保持 `Stroke` 不动 |
| 触摸 Manipulation 与工具状态机交互 | Manipulation 不迁移、行为不变；重构后专项手测双指缩放/平移 |
| 预览（活动笔迹）渲染路径改动 | 手测压感绘制与荧光笔色；`PreviewItem` 挂载点语义与原 `ActiveStroke` 等价 |
| 序列化往返回归 | v2 样例文件往返测试 + 新增单测覆盖 Kind 缺省路径 |
