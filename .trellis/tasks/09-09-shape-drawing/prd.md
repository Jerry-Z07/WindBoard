# 图形绘制：可插拔绘制架构与形状工具（父任务）

## Goal

为白板新增直线、矩形、椭圆/圆、箭头等图形绘制能力（可插入白板、可输入长度等参数）。由于现有绘制链路建立在"所有笔迹 = 折线点集"的隐式假设上，直接加形状会触碰 15~20 个文件，因此分两阶段：**阶段一**先把绘制架构重构为可插拔地基（不改用户可见行为），**阶段二**在新地基上实现形状工具与属性面板。函数绘制等后续扩展依赖同一套地基，但不在本期范围内。

## 关键决策（全部已收敛，2026-09-09）

| 决策 | 结论 | 备注 |
|---|---|---|
| 路线 | 先重构地基，再加形状（方案 B） | 用户确认 |
| 插件系统形态 | **不做**程序集级插件系统；采用进程内"注册表 + 策略"的内置插件形态 | 前置成本即重构本身；无第三方扩展需求；待真有需求时注册表可升级为程序集加载 |
| 任务结构 | 父任务 + 两个子任务，独立验收 | 即本任务树 |
| 阶段二形状清单 | 直线、矩形、椭圆/圆、箭头（全部两点式手势） | 贝塞尔曲线留到二期 |
| 参数输入形态 | 拖拽绘制 + 选中后属性浮层精调 | 数值改动走命令栈可撤销；一套面板按形状类型显示字段，后续函数绘制参数 UI 落同一机制 |
| 形状编辑语义 | 橡皮碰形状→整笔删除；形状用画笔颜色/线宽；无压感；MVP 无半透明描边 | 形状不可分割擦除，语义等同现有"整笔删除"橡皮 |
| 批注层范围 | 主白板与屏幕批注层（ScreenAnnotation）**同步**获得形状工具 | 阶段二需接入两套工具栏 UI |
| WBIX v3 兼容 | 沿用现状门禁行为：新 App 向后读旧文件；旧 App 拒读 v3 文件 | 仓库证据回答（`WbixWorkspaceSerializer.cs:16-17` 现状即版本门禁），无逐版本迁移 |

## 任务地图

| 子任务 | 交付物 | 依赖 |
|---|---|---|
| `09-09-drawing-foundation`（阶段一） | 可插拔绘制地基：绘制条目抽象、工具策略化、序列化收敛（WBIX v3）、参数链收敛；不改用户可见行为 | 无 |
| `09-09-shape-tools`（阶段二） | 直线/矩形/椭圆/圆/箭头工具 + 属性面板 + 命中/擦除策略 + 持久化导出，主白板与批注层两层接入 | **必须先完成阶段一合入**（依赖其抽象与注册点） |

## 跨子任务验收标准（父任务持有）

- 阶段一合入后：现有全部行为（绘制/擦除/选择/撤销/序列化/导出/屏幕批注）与重构前一致，全量测试通过。
- 阶段二合入后：新增形状走"注册"路径接入（工具、渲染、序列化、命中各有单点注册处），不复活散落式 if/else 分发。
- 最终集成复查：完整走查 新建形状 → 撤销/重做 → 保存 → 重新加载 → 导出 PNG 全链路，主白板与屏幕批注层两条链路均通过。

## 已确认事实（代码库证据，2026-09-09 全量只读调研）

1. **Stroke 单一 sealed 类，无类型体系**：`WindBoard/Board/BoardDocument.cs:24-132`，`List<StrokePoint>` 折线点集 + 颜色/粗细/压感；"钢笔/荧光笔"只是颜色参数差异。全项目无 StrokeKind/StrokeType 概念。
2. **"折线假设"渗透 5 处算法**：
   - 渲染：`WindBoard/Rendering/Board/BoardSceneRenderer.cs`（1300+ 行单文件）`DrawStroke` 三分支（单点椭圆 / D2D DrawInk + Ink 几何缓存 / 逐段 DrawLine 降级），Ink 缓存假设连续墨迹，形状无法走此路径。
   - 命中：`WindBoard/Board/Editing/StrokeHitTest.cs:57-82` 按折线段集做线段距离。
   - 擦除：`PixelStrokeEraser` 沿折线分割（形状无法分割）；`WholeStrokeEraser` 整笔删除。策略接口 `IBoardEraser` 是全项目唯一既有策略注入点（`BoardInputController.cs:149-153`）。
   - 撤销：`UpdateStrokePointsCommand` 前后点集快照 diff。
   - 序列化：快照→域重建逻辑写了三份（`BoardWorkspaceSnapshotApplier.CreateStroke`、`Features/Export/Services/BoardRasterExporter.cs:230-252`、`Features/Import/Wbi/WbiWorkspaceImporter.cs:482-486`）。
3. **工具无策略抽象**：`BoardTool` 仅 Select/Pen/Eraser 三值枚举；三个工具行为以 ~15 个平级字段 + if/else 纠缠在 `BoardInputController`（partial 拆 7 文件，500+ 行共享状态机），`ActiveStroke` 单槽位不支持多手势并行。
4. **参数 4 跳属性复制链**：`MainWindow → BoardCanvasControl → BoardInputController → Stroke`，ScreenAnnotation 另有第 5 条平行入口；无 ToolOptions 类值对象。设置只持久化色板/粗细预设，不持久化当前选中值（`AppSettings.cs:145-170`）。
5. **元素侧已有类型分发 switch 散落 4+ 处**（Converter:83、Applier:81、Wbix Save:249、Wbix Load + 渲染器内按子类绘制）——新形状大概率重演，是收敛注册表的明确信号。
6. **WBIX 版本机制**：`WbixWorkspaceSerializer.cs:16-17`，`CurrentVersion=2`，只做向后可读门禁（<=CurrentVersion），无逐版本迁移；元素/笔迹快照无归一化层，兼容靠 JSON 可空字段兜底。
7. **绘制顺序**：下层元素 → 笔迹 → 上层元素 → 活动笔迹（`BoardSceneRenderer.cs:267-292`）；命令栈 `BoardSession.Execute` 不区分类型，天然支持形状命令同栈。
8. **UI 接入面**：主窗口 `MainWindow.xaml.cs:171-191 ApplyToolSelection` 与 ScreenAnnotation 工具栏（`ScreenAnnotationFlow` → `ScreenAnnotationWindow` → `BoardCanvas.Tool/属性`）两套 UI，阶段二均需接入形状工具。

## Requirements（编号供子任务引用）

- R1（阶段一）绘制内容抽象：折线笔迹与几何形状在文档层有统一表达，渲染/命中/序列化按类型单点分发。
- R2（阶段一）工具策略化：每个工具一个状态对象（Begin/Move/End/Cancel/预览），拆解 `BoardInputController` 的字段纠缠；工具经注册表接入。
- R3（阶段一）序列化收敛：快照↔域重建收敛到单点，WBIX 升 v3，旧文件可读。
- R4（阶段一）参数链收敛：引入 ToolOptions 值对象替代 4 跳属性复制。
- R5（阶段二）形状工具：直线、矩形、椭圆/圆、箭头（全部两点式手势：按下定起点、拖动定终点，Shift 约束正方/正圆/水平垂直线为可选增强），主白板与屏幕批注层两层接入。
- R6（阶段二）形状的编辑语义：橡皮碰到形状 → 整笔删除（形状不可分割擦除）；形状使用当前画笔颜色与线宽；不支持压感；MVP 不做荧光笔半透明描边。
- R7（阶段二）持久化：形状可保存/加载（WBIX v3），导出 PNG 正确渲染。
- R8（阶段二）属性浮层：选中形状后弹出属性面板，按形状类型显示可编辑字段（直线/箭头=长度+角度，矩形=宽+高，圆=半径），数值输入经命令栈提交（可撤销）；面板机制为后续功能（如函数绘制）预留复用点。

## Acceptance Criteria

- [x] 阶段一：行为零回归（跨子任务验收第 1 条），`dotnet test WindBoard.slnx` 全量通过（445/445，手测清单待用户人工执行）
- [ ] 阶段二：R5-R8 各自验收清单在两个子任务 PRD 中细化，全部通过
- [ ] 父任务：跨子任务验收 3 条全部通过后归档

## Out of Scope

- 程序集级插件系统（外部 DLL/manifest/动态加载）
- 函数绘制（y=f(x)）——依赖同一地基，作为后续任务
- 贝塞尔曲线（带控制点编辑）——留到二期；阶段一抽象只需覆盖两点式
- 网格吸附/智能参考线（未提出，默认不做）
- 荧光笔半透明描边（MVP 不做）

## Notes

- 阶段一为结构性重构、改动面大（预估 15~20 文件），实施前需用户对 design.md 最终评审确认。
- 本 PRD 为父任务，持有需求全集与任务地图；子任务各自维护可独立验收的 prd/design/implement。
