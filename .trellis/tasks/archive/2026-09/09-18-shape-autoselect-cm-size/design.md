# 技术设计：形状创建后自动选中与尺寸厘米显示

## 1. 范围与边界

两个交付项都停留在"交互层 + 控件层 + 显示层"，不触碰域模型存储格式：

| 层 | 是否改动 | 说明 |
|---|---|---|
| `Board/`（域模型、几何） | 改 `ShapePropertyMath`（纯函数新增换算） | 世界坐标存储不变 |
| `Interaction/`（工具、控制器） | 改 `ShapeTool`、`BoardInputController` | 新增"提交结果"出口 |
| `Controls/`（BoardCanvasControl） | 改事件转发、选中入口、属性浮层读写 | UI 层 |
| `MainWindow` | 订阅新事件 | 工具按钮态的唯一持有者 |
| 持久化 / 导出 / 渲染 | 不改 | 无迁移 |

## 2. 交付项 A：形状创建后自动选中

### 2.1 责任划分

- `ShapeTool`（域无关的工具）**不**切换工具、**不**设置选中：它不知道 Dock 按钮，也不应依赖 UI。它只额外记录"本次已提交的形状"。
- `BoardInputController` 在工具提交后把结果抛成事件，保持"控制器只做路由与调度"的现状。
- `BoardCanvasControl` 转发事件并把选中能力下沉为内部 API。
- `MainWindow` 决定"切到选择工具 + 选中"，因为工具按钮选中态与 `_lastShapeTool` 都由它持有（`WindBoard/MainWindow.xaml.cs:196-232`）。

### 2.2 契约与数据流

```text
PointerReleased / 触摸单指抬起
  └─ BoardInputController.CommitActiveToolGesture()          Operations.cs:53
       ├─ tool.End(...)                                      → AddInkItemCommand 提交
       ├─ _routes.EndActiveStroke()
       ├─ FinalizeGestureState()                             ← 控制器手势状态已清理
       └─ [新增] ShapeTool.LastCommittedShape 非空 → ShapeCommitted 事件
            └─ BoardCanvasControl.ShapeCommitted（转发）
                 └─ MainWindow.OnShapeCommitted(shape)
                      ├─ ApplyToolSelection(BoardTool.Select)  → 工具按钮态 + 收起形状 Flyout
                      └─ BoardCanvas.SetSelectedInkItem(shape) → 选中框 / Dock / 属性浮层
```

新增成员：

- `ShapeTool.LastCommittedShape { get; private set; }`：`End` 入口先置 `null`，提交成功时赋值为该形状；退化几何走丢弃分支时保持 `null`（满足 R1.1）。
- `BoardInputController.ShapeCommitted : event Action<BoardShape>?`：仅在解析到的工具是 `ShapeTool` 且 `LastCommittedShape` 非空时触发（满足 R1.2，笔迹/橡皮/选择路径工具类型不同）。
- `BoardCanvasControl.ShapeCommitted : internal event Action<BoardShape>?`：转发控制器事件，随 `_input` 的创建/重建成对订阅与退订。
- `BoardCanvasControl.SetSelectedInkItem(IBoardInkItem?)`：与既有 `SetSelectedElement`（`BoardCanvasControl.xaml.cs:320`）同构——先 `CancelActiveToolOperation()`，再 `_input.SetSelection(item)`，最后 `RequestRender()`。

### 2.3 触发时机与重入安全

事件必须在 `FinalizeGestureState()` **之后**触发，理由：宿主响应时会调用 `BoardCanvas.Tool = Select`，其 setter 内部执行 `_input.CancelActiveToolOperation()`（`BoardCanvasControl.xaml.cs:133`）。若在手势状态清理前触发，`DiscardActiveToolGesture()` 会走非早退分支，与释放路径的收尾动作重复释放指针捕获。

在清理完成后再触发时，`DiscardActiveToolGesture()` 的早退条件（`ActiveItem` 为空、无活动指针）成立，重入是 no-op，因此**采用同步触发**，不引入 `DispatcherQueue.TryEnqueue` 的异步时序与竞态。

同时 `Tool` setter 的"离开选择工具即清空选择"分支（`BoardCanvasControl.xaml.cs:136-139`）条件为 `previousTool == Select`，本次是"形状工具 → 选择工具"，不会误清刚才的选中。

### 2.4 宿主边界（屏幕批注）

屏幕批注窗口复用同一套 `ShapeTool`，但通过 `SetInteractionOptions(allowViewportManipulation: false, allowSelectionInteraction: false)` 禁用了选择交互（`ScreenAnnotationWindow.xaml.cs:225`），其工具体验与主白板不同。

边界约定：**事件只由宿主决定是否消费**。`MainWindow` 订阅；屏幕批注窗口不订阅，因此批注模式行为完全不变（满足 R1.3、AC6）。`BoardCanvasControl` 不额外加 `_allowSelectionInteraction` 判断，避免无依据的防御分支。

### 2.5 权衡

| 选择 | 备选 | 理由 |
|---|---|---|
| 事件 + 宿主消费 | 在 `ShapeTool.End` 里直接切工具 | 工具层无 UI 依赖，且无法同步 Dock 按钮态 |
| 事件 + 宿主消费 | 在 `BoardCanvasControl` 内直接改 `Tool` | `Tool` setter 只更新画布，不同步主 Dock 按钮，会出现"按钮说在画形状、实际是选择工具"的不一致 |
| 同步触发 | `TryEnqueue` 延后一帧 | 状态已清理，同步无竞态且无延迟 |
| 记录 + 读取（`LastCommittedShape`） | 给 `IBoardTool` 加通用"提交结果"契约 | 前者与既有 `SelectTool.LastMarqueeClickedElement`（`BoardInputController.Operations.cs:143`）模式一致，改动面最小 |

## 3. 交付项 B：尺寸厘米显示与编辑

### 3.1 换算契约

在 `WindBoard/Board/Editing/ShapePropertyMath.cs` 内新增（与既有字段换算同职责，纯函数、可单测）：

- 常量 `CentimetersPerWorldUnit = 2.54 / 96.0`（即"1 世界单位 = 1/96 英寸"）。
- `WorldToCentimeters(double) → double`
- `CentimetersToWorld(double) → double`

选定依据：`Board/Editing/ShapePropertyMath.cs:11` 已声明"长度/宽/高单位为世界坐标（缩放 100% 时与屏幕像素 1:1）"，而项目把世界单位定义为 DIP 近似（`docs/dev/guides/wbix.zh-CN.md:141`）；Windows 的 DIP 名义定义为 1/96 英寸，故该系数是唯一与现有坐标系自洽的固定取值。

关键性质：换算发生在**世界坐标层**，与 `BoardViewport.Zoom` 无关，因此缩放画布不会改变显示数值，图上任意两段长度之比恒等于其厘米值之比（满足用户对"画板比例"的诉求）。

### 3.2 读写路径

| 位置 | 改动 |
|---|---|
| `ConfigureShapePropertyFields`（`ShapeProperties.cs:99-140`） | 长度/宽度/高度字段的 `Minimum` 改为 `WorldToCentimeters(ShapePropertyMath.MinPropertyValue)`；角度字段范围保持 `-360..360` |
| `SyncShapePropertyBoxes`（`:146-179`） | 读到的世界坐标长度/宽高先 `WorldToCentimeters` 再写入 `NumberBox`；角度原样 |
| `CommitShapePropertiesFromBoxes`（`:204-256`） | 先把 `NumberBox` 值 `CentimetersToWorld`，再执行现有 `Math.Max(MinPropertyValue, ...)` 下限钳制与几何换算 |

不变量（R2.5）：下限钳制必须在**换回世界坐标之后**做，否则会把"厘米下限"当成"世界坐标下限"，改变"不允许退化几何"的语义。

`SmallChange="1"`（`BoardCanvasControl.xaml:92`）保持不动：语义由"1 世界单位"自然变为"1 cm"，对形状尺寸尺度更合适。

### 3.3 资源与文案

`ShapeProperties_Length / _Width / _Height` 三个 key 的唯一消费点是 `ShapeProperties.cs:108/125/126`，可直接改文案为带单位形式（如"长度 (cm)" / "Length (cm)"），无需新增 key、不产生未使用资源：

- `WindBoard/Strings/zh-CN/ShapeProperties.resw:20-31`
- `WindBoard/Strings/en-US/ShapeProperties.resw:20-31`

`ShapeProperties_Angle` 文案不变（R2.3、Out of Scope）。文案必须保持字面量 key 传参，以满足 `LocalizationKeyAuditTests` 的约束。

### 3.4 兼容性

- 域模型、快照（`ShapeSnapshot.Start/End/Width`）与 WBIX 格式不变，**旧文档无需迁移**；变化仅限浮层呈现与输入的数值单位。
- 视觉副作用：同一形状的浮层数值将约为改动前的 1/37.8（例如原 200 → 5.29 cm）。这是需求预期，不视为回归。
- 屏幕批注窗口若复用同一浮层，其形状尺寸也会按 cm 显示；这是同一控件的自然结果，不额外区分（未列入 Out of Scope 的差异处理）。

### 3.5 权衡

| 选择 | 备选 | 理由 |
|---|---|---|
| 固定系数常量 | 设置项/标定流程 | 用户已明确不引入设置项 |
| 复用 `ShapePropertyMath` | 新建单位换算类 | 该类的既有职责就是"属性字段 ↔ 几何"换算，避免为单点需求新增文件 |
| 直接改现有 3 个 key 文案 | 新增带单位 key | 避免留下未使用的旧资源 |
| 显示 2 位小数（沿用） | 提高精度 | 2 位 cm ≈ 0.1 mm，对本场景足够，且不引入与现有舍入逻辑的差异 |

## 4. 测试与验证策略

- 单元测试（xUnit，`WindBoard.Tests/Board/Editing/ShapePropertyMathTests.cs`）：`WorldToCentimeters`/`CentimetersToWorld` 的已知值、往返一致、最小边界换算；用 `AssertEx.Equal` 带容差比较。
- UI 层不做单测（遵循 `frontend/quality-guidelines.md` 的"不测 UI/渲染"约定），交互行为走手工/E2E 验收清单（见 `implement.md`）。
- 构建闸门：`-p:CodeAnalysisTreatWarningsAsErrors=true` 零告警。

## 5. 回滚

改动全部位于交互/显示层，无数据迁移、无设置项落盘。回滚方式为整体回退本次提交；已由本次改动创建的画板文档在任何版本下都保持有效。

## 6. 追加修复（bugfix）：属性浮层切换目标时的一致性

**现象**：把一条直线的角度改为 0 后，再绘制任意形状，新形状的字段会被写成接近 0 的数值（矩形被压扁）；再新建一个形状又恢复正常。

**根因（两处程序化写值未被隔离）**：

1. `ConfigureShapePropertyFields`（`BoardCanvasControl.ShapeProperties.cs:101-145`）每次显示浮层都会重设 `NumberBox.Minimum/Maximum`，但**不在** `_isSyncingShapeProperties` 保护内。当字段语义从"角度（下限 -360）"切到"长度类（下限 ≈ 0.000265 cm）"时，输入框里遗留的 `0` 越界并被控件收敛到新下限；该收敛是一次真实的 `Value` 变更，会进入 `CommitShapePropertiesFromBoxes`，用**尚未回读**的另一个字段（上一个形状的长度）+ 收敛后的值提交到**新形状**。
2. `SyncShapePropertyBoxes` 的焦点保护（`ShapeProperties.cs:158-162`：任一输入框聚焦即跳过回读）只对"同一目标的刷新"成立。切换目标时沿用旧值，会让上一个形状留在输入框里的值在后续失焦时提交到新形状。

**契约（修复后）**：

- 浮层切换服务目标（`_shapePropertiesTarget` 变化，含从 null 首次显示）时**必须强制回读**目标形状的真实值，不受焦点保护约束；焦点保护仅保留给同一目标的后续刷新（避免打断用户输入）。
- `ConfigureShapePropertyFields` 属于程序化写值，必须在 `_isSyncingShapeProperties` 保护内执行：其间的控件值收敛不得被当作"用户编辑"提交。
- 顺序固定为：保护内 `Configure`（重置字段语义与边界）→ `Sync(force: 目标是否变化)`（回读真实值）。

**兼容性**：属既有缺陷（改动前手动换选形状同样可能触发），本次"创建后自动选中"使其每次创建都会触发。修复不改变正常编辑路径与撤销语义。

## 7. 追加修复（R3 修订）：浮层输入框的编辑态可用性

**现象**：R3 首版把两个输入框放大到 44 高 / 104 宽后，触摸下点击输入框进入编辑态，输入区反而更窄、相邻字段被压住。

**根因**：`SpinButtonPlacementMode="Compact"` 的语义就是"仅在获得焦点时以 **Flyout** 显示步进按钮"（[Number box](https://learn.microsoft.com/windows/apps/design/controls/number-box)）。Flyout 浮在控件之上，与自身几何尺寸无关——加大宽高只会多出无效留白，挡住的输入区一点没变。

**决策**：改用 `SpinButtonPlacementMode="Inline"`（步进按钮常驻框内右侧，不弹层），并把宽度提到 **200**。触摸下可直接点按 ± 微调，无需软键盘。

**宽度依据（三轮实测收敛）**：编辑态的 `NumberBox` 内部固定占位约 100 DIP（边框 + 内边距 + 清除按钮 ✕ + 两个内联步进按钮），文本输入区 ≈ `Width − 100`。

- 首版只把高度加到 44（宽 104）：聚焦时 `Compact` 的 Flyout 仍遮挡，输入区没有改善；
- 改 `Inline` + 宽 140：Flyout 消失，但文本区被内部元素挤到约 24 DIP（表现为"只剩一个光标"）；
- 最终宽 200：文本区约 100 DIP，可完整显示 `-359.99` 并保留编辑余量。

**契约**：浮在画布之上的浮层控件，不使用会在聚焦时弹出遮挡层的步进按钮呈现方式（`Inline` 或 `Hidden`），默认 `Inline`。

**取舍**：浮层总宽由约 244 增至约 436 DIP（2×200 + 间距 12 + 边距 24）。`Hidden` 虽能让输入区最大（同宽下约 106 DIP），但触摸下会失去"点按微调"这一最可靠的操作方式；若将来要兼得，需自定义模板把 ± 做成 44×44 触控尺寸。
