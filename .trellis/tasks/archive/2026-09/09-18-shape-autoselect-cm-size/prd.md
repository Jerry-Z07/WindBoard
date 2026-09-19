# 形状创建后自动选中与尺寸厘米显示

## Goal

绘制形状后立即进入"已选中"状态，用户无需再手动点选即可调整尺寸；形状属性浮层的长度/宽高以厘米显示与编辑，让教学场景中的尺寸数值具备物理意义。

## Background / 已核实事实

创建链路：

- 形状工具注册与调度：`WindBoard/Interaction/BoardInputController/BoardInputController.cs:73-76`、`WindBoard/Interaction/BoardInputController/BoardInputController.Operations.cs:53-63`
- 两点式绘制与提交：`WindBoard/Interaction/Tools/ShapeTool.cs:53`（Begin）、`WindBoard/Interaction/Tools/ShapeTool.cs:93`（End 提交 `AddInkItemCommand`，**不触碰选择集**）

选中链路：

- 选中集内聚在 `SelectTool`：`WindBoard/Interaction/Tools/SelectTool.cs:38-39`，设置入口 `WindBoard/Interaction/Tools/SelectTool.cs:218-221`
- 控制器已具备"按条目选中"能力：`WindBoard/Interaction/BoardInputController/BoardInputController.cs:228-243`
- 控件层只对页面元素开了口：`WindBoard/Controls/BoardCanvasControl.xaml.cs:320`（`SetSelectedElement`），**形状/笔迹无对应出口**
- 选择框与属性浮层仅在 `Tool == BoardTool.Select` 时显示：`WindBoard/Controls/BoardCanvasControl.Rendering.cs:140`
- 离开选择工具会清空选择：`WindBoard/Controls/BoardCanvasControl.xaml.cs:135-139`
- 既有同类先例：导入元素后切到选择工具并选中：`WindBoard/UI/MainWindow/MainWindow.Import.cs:53-58`
- 形状工具在批注模式被复用，且批注禁用了选择交互：`WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationWindow.xaml.cs:225`

尺寸数值链路：

- 世界坐标是抽象单位，无物理量纲：`docs/dev/guides/wbix.zh-CN.md:141`、`WindBoard/Board/Editing/ShapePropertyMath.cs:11`
- 属性浮层字段配置/读/写：`WindBoard/Controls/BoardCanvasControl.ShapeProperties.cs:99-140`、`:146-179`、`:204-256`
- 字段换算纯函数：`WindBoard/Board/Editing/ShapePropertyMath.cs:21-46`
- 字段文案资源：`WindBoard/Strings/zh-CN/ShapeProperties.resw:20-31`、`WindBoard/Strings/en-US/ShapeProperties.resw:20-31`

## Requirements

### R1 形状创建后自动选中

形状（直线/矩形/椭圆/箭头）提交成功后，自动切换到选择工具并选中该形状。

- R1.1 退化几何被丢弃（点击未拖动）时不发生自动选中，也不发生工具切换。
- R1.2 画笔、橡皮、选择工具自身的提交不触发该行为。
- R1.3 仅在允许选择交互的宿主（主白板）生效；屏幕批注禁用了选择交互，其现有行为不变。
- R1.4 切换后主 Dock 工具按钮选中态与形状 Flyout 收起行为与手动切换一致（沿用 `ApplyToolSelection`）。
- R1.5 `_lastShapeTool`（`WindBoard/MainWindow.xaml.cs:32`）不受影响，再次点击形状按钮仍回到上次使用的形状。

### R2 形状属性浮层以厘米显示与编辑

换算系数固定为：1 世界单位 = 1/96 英寸 ≈ 0.2646 mm ≈ 0.02646 cm，不引入设置项。

- R2.1 显示：浮层数值 = 世界坐标尺寸 × 换算系数，保留现有 2 位小数呈现。
- R2.2 编辑：用户输入的厘米值 ÷ 换算系数后写回世界坐标，仍经 `UpdateShapeGeometryCommand` 提交（可撤销）。
- R2.3 角度字段不做长度换算，取值范围不变。
- R2.4 字段标签体现单位（如"长度 (cm)"），中英文资源同步更新。
- R2.5 `NumberBox` 最小/最大值按换算后的显示单位设定，维持"不允许退化几何"的原有语义。
- R2.6 域存储、渲染、导出与 WBIX 持久化仍为世界坐标，无数据迁移。

### R3 属性浮层输入框的触控目标尺寸

选中单个形状时出现的属性浮层，其数值输入框在触摸屏上过小（高 32），需对齐项目既有的浮层触控基准。

- R3.1 两个输入框高度统一为 44（对齐主 Dock / 屏幕批注栏的 `44×44` 基准，`.trellis/spec/frontend/component-guidelines.md:306`）。
- R3.2 宽度以"聚焦编辑态下文本输入区可用宽度 ≥ 90 DIP"为准（当前取 200）。编辑态的 `NumberBox` 内部最多同时占位的元素为：文本区、清除按钮 ✕、两个内联步进按钮（后两者合计约 84 DIP），故宽度必须显著大于"仅输入框"的尺度——加高而不加宽只会多出无效留白。
- R3.3 步进按钮呈现方式由 `Compact` 改为 `Inline`：`Compact` 会在输入框获得焦点时以 **Flyout** 弹出步进按钮，压住自身输入区与相邻字段，且**无法通过尺寸规避**（Flyout 浮在控件之上）。字号保持不变。
- R3.4 统一放大，不区分鼠标/触摸输入设备。
- R3.5 浮层定位与 Dock 排布沿用既有自适应逻辑（按测量尺寸定位并做边界钳制），不得引入固定偏移或硬编码面板宽高。
- R3.6 聚焦编辑态下不得出现覆盖输入区或相邻字段的弹出层。

## Acceptance Criteria

- [ ] AC1 用形状工具拖拽出矩形并释放：工具自动切换到选择工具，矩形显示选择框、Dock 与属性浮层。
- [ ] AC2 该矩形浮层显示"宽度 (cm) / 高度 (cm)"，数值等于世界坐标尺寸 × (2.54/96)。
- [ ] AC3 在浮层把宽度改为 5 并确认：矩形宽度按 5 cm 对应世界坐标重设（TopLeft 锚点不变），回读数值一致，撤销可回到改前几何。
- [ ] AC4 用形状工具单击（不拖动）不产生形状，也不发生工具切换或选中变化。
- [ ] AC5 用画笔连续书写：工具、选中状态与浮层行为与改动前一致。
- [ ] AC6 屏幕批注模式下绘制形状的行为与改动前一致（不切换工具、不出现选中框）。
- [ ] AC7 厘米换算函数有单元测试覆盖（含往返一致与最小值边界）。
- [ ] AC8 `dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true` 零告警；`dotnet test WindBoard.slnx` 全绿。
- [ ] AC9（回归，见 design 第 6 节）画一条直线 → 在浮层把角度改为 0 → 再画矩形/椭圆/直线：每个新形状都保持拖拽出的尺寸，不被改写为上一次形状的数值；随后重新选中此前的形状，其数值仍正确。
- [ ] AC10 触摸屏选中单个形状并点进任一输入框：输入框为 44 高、200 宽；步进按钮常驻框内右侧且不弹出浮层；文本输入区能完整显示 5 字符数值（如 `-359.99`）并有编辑余量，相邻字段不被遮挡；浮层仍完整落在画布内（边界钳制生效），其下方的选中 Dock 不被遮挡。

## Out of Scope

- 不新增设置项、标定流程或比例尺配置（换算系数固定）。
- 不显示 px，不提供单位切换。
- 不给笔迹、页面元素（图片/文件/文本/链接）显示尺寸数值。
- 不做图形旁的尺寸标注（工程图式）。
- 不按显示器物理 DPI 校准"屏幕上量出来等于多少厘米"。
- 不改角度字段的单位与取值范围。

## Open Questions

无。阻塞项已清空。
