# 实施计划：形状创建后自动选中与尺寸厘米显示

## 前置检查

- 需求已在 `prd.md` 收敛，技术方案见 `design.md`，无阻塞性开放问题。
- 两个交付项相互独立：R2（显示层换算）不依赖 R1（交互事件），可分别实现与验证；本计划按 R2 → R1 顺序执行，先落纯逻辑与可单测部分。

## 实施清单（有序）

### 步骤 1 — 换算层（R2 基础）

1. 在 `WindBoard/Board/Editing/ShapePropertyMath.cs` 新增 `CentimetersPerWorldUnit = 2.54 / 96.0` 及 `WorldToCentimeters` / `CentimetersToWorld`，并更新类注释中"长度/宽/高单位"的说明。
2. 在 `WindBoard.Tests/Board/Editing/ShapePropertyMathTests.cs` 补充换算用例：已知值（如 1 世界单位 → 0.0264583 cm）、往返一致、`MinPropertyValue` 边界换算。

验证：`dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Board.Editing.ShapePropertyMathTests"`

### 步骤 2 — 属性浮层改为厘米显示与编辑（R2）

3. `WindBoard/Controls/BoardCanvasControl.ShapeProperties.cs`：
   - `ConfigureShapePropertyFields`：长度/宽度/高度的 `Minimum` 改为 `WorldToCentimeters(ShapePropertyMath.MinPropertyValue)`。
   - `SyncShapePropertyBoxes`：读值经 `WorldToCentimeters` 后写入 `NumberBox`（角度原样）。
   - `CommitShapePropertiesFromBoxes`：先把输入经 `CentimetersToWorld` 换回世界坐标，再执行 `Math.Max(MinPropertyValue, ...)` 与几何换算（顺序不可颠倒）。
4. `WindBoard/Strings/zh-CN/ShapeProperties.resw` 与 `WindBoard/Strings/en-US/ShapeProperties.resw`：`ShapeProperties_Length` / `_Width` / `_Height` 文案补单位（如"长度 (cm)" / "Length (cm)"），`ShapeProperties_Angle` 不变。

验证：构建 + 手工验收 AC2、AC3。

### 步骤 3 — 创建形状后自动选中（R1）

5. `WindBoard/Interaction/Tools/ShapeTool.cs`：新增 `LastCommittedShape`；`End` 入口置 `null`，提交成功分支赋值。
6. `WindBoard/Interaction/BoardInputController/BoardInputController.cs` 与 `BoardInputController.Operations.cs`：新增 `ShapeCommitted` 事件；`CommitActiveToolGesture` 在 `FinalizeGestureState()` 之后读取 `ShapeTool.LastCommittedShape` 并触发（不得在 `tool.End()` 与状态清理之间触发）。
7. `WindBoard/Controls/BoardCanvasControl.xaml.cs`：
   - 新增 `internal event Action<BoardShape>? ShapeCommitted` 转发，并在 `EnsureInitialized` 与 `BindSession` 两处与 `_input` 成对订阅/退订。
   - 新增 `internal void SetSelectedInkItem(IBoardInkItem? item)`，与 `SetSelectedElement` 同构。
8. `WindBoard/MainWindow.xaml.cs`：在构造函数订阅 `BoardCanvas.ShapeCommitted`，处理为 `ApplyToolSelection(BoardTool.Select)` + `BoardCanvas.SetSelectedInkItem(shape)`。

验证：构建 + 手工验收 AC1、AC4、AC5、AC6。

### 步骤 4 — 修复：属性浮层切换目标时的误提交（bugfix，详见 design.md 第 6 节）

9. `WindBoard/Controls/BoardCanvasControl.ShapeProperties.cs`：
   - `SyncShapePropertyBoxes` 增加 `bool force = false` 参数：`force` 为 true 时跳过早退的焦点保护（同一目标的刷新仍保留焦点保护）。
   - `ShowShapePropertiesOverlay`：先算 `bool targetChanged = !ReferenceEquals(_shapePropertiesTarget, shape)` 再赋值 `_shapePropertiesTarget`；把 `ConfigureShapePropertyFields(shape.Kind)` 包进 `_isSyncingShapeProperties = true` / `finally { false }`；随后调用 `SyncShapePropertyBoxes(shape, force: targetChanged)`。
   - `CommitShapePropertiesFromBoxes` 的 NaN 回读分支保持原调用（不 force）。

验证：构建 + 手工回归（下表 BUG 回归用例）。

### 步骤 5 — 属性浮层输入框对齐触控基准（R3）

10. `WindBoard/Controls/BoardCanvasControl.xaml`：`ShapeProperty1Box` 与 `ShapeProperty2Box` 的 `MinHeight="32"` → `44`、`Width` → `200`、`SpinButtonPlacementMode="Compact"` → `"Inline"`。
    - 宽度依据 R3.2：编辑态固定占位约 100 DIP（✕ + 两个内联步进按钮），200 宽可留出约 100 DIP 文本输入区；
    - 其余属性（`Padding="6,2"`、`SmallChange="1"`、`AcceptsExpression="False"`、字号、`ValueChanged` 绑定）保持不变；
    - `Inline` 是本次关键修复：`Compact` 在聚焦时以 Flyout 弹出步进按钮、遮挡自身输入区与相邻字段，尺寸无法规避（见 design 第 7 节）；
    - `ShowShapePropertiesOverlay` 的测量与边界钳制逻辑不动（面板变宽后由既有自适应逻辑处理）。

验证：构建 + 手工验收 AC10（含"聚焦编辑态无浮层遮挡"）。

## 验证命令

```bash
dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true
dotnet test WindBoard.slnx
dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Board.Editing.ShapePropertyMathTests"
```

## 手工验收清单

| 用例 | 操作 | 期望 |
|---|---|---|
| AC1 | 形状工具拖拽矩形后释放 | 工具切到选择，矩形显示选择框 + Dock + 属性浮层 |
| AC2 | 查看浮层 | 显示"宽度 (cm) / 高度 (cm)"，数值 = 世界坐标 × 2.54/96 |
| AC3 | 浮层宽度改为 5 并确认 | 宽度按 5 cm 重设，TopLeft 锚点不变，数值回读一致，撤销可还原 |
| AC4 | 形状工具单击不拖动 | 不产生形状，不切工具，不出现选中 |
| AC5 | 画笔连续书写 | 工具、选中、浮层行为与改动前一致 |
| AC6 | 屏幕批注模式画形状 | 与改动前一致（不切工具、无选中框） |
| 附加 | 缩放画布（滚轮）后查看浮层 | 厘米数值不随缩放变化 |
| 附加 | 画完形状后再次点击形状按钮 | 回到上次使用的形状（`_lastShapeTool` 未被改写） |
| BUG 回归 | 画一条直线 → 在浮层把角度改为 0 → 再画一个矩形/椭圆 → 再画一条直线 | 每个新形状都保持拖拽出的尺寸，字段不被改写为上一次形状的数值；再选中此前的形状，数值仍正确 |
| AC10 | 触摸屏选中单个形状，点进输入框编辑，再把形状拖到画布边缘附近 | 输入框 44 高 / 200 宽；聚焦时不弹出遮挡层、文本区可完整显示 `-359.99` 仍有余量；浮层被钳制在画布内，下方的选中 Dock 仍可见 |

## 风险文件与回滚点

- `WindBoard/Interaction/BoardInputController/BoardInputController.Operations.cs`：指针释放路径上的代码，事件触发必须留在状态清理之后，且不得引入日志或常态分配（高频路径约束）。
- `WindBoard/Controls/BoardCanvasControl.xaml.cs`：`_input` 在 `EnsureInitialized`、`BindSession` 两处创建，漏掉任一处订阅/退订会导致事件失效或泄漏。
- `WindBoard/Controls/BoardCanvasControl.ShapeProperties.cs`：厘米→世界坐标的换算与下限钳制的先后顺序直接影响"退化几何防护"；切换目标时的"屏蔽 + 强制回读"契约直接影响"新形状是否被旧值改写"。
- 回滚点：步骤 1–2（R2）与步骤 3（R1）彼此独立，可分别回退；整体回退为回滚本次提交，无持久化影响。

## 后续检查（`task.py start` 前）

- [ ] `implement.jsonl` 与 `check.jsonl` 已填入实际 spec 条目（非占位）。
- [ ] 用户已对最终规划摘要给出明确批准。
