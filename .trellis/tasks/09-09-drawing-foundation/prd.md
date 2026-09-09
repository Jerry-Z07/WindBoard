# 阶段一：绘制架构可插拔地基重构

> 父任务：`09-09-shape-drawing`（需求全集与决策见其 `prd.md`，需求编号 R1-R4 由父任务定义）

## Goal

在不改变任何用户可见行为的前提下，把绘制链路重构为可插拔地基：绘制条目抽象、工具策略化、序列化单点收敛（WBIX v3）、参数链收敛。使阶段二新增形状时，每个形状只需"一个实现 + 一条注册"，不再触碰散落式分发。

## 背景与约束

现状（证据锚点见父 PRD"已确认事实"）：Stroke 是唯一笔迹类型且"折线假设"渗透渲染/命中/擦除/撤销/序列化 5 处算法；工具无策略抽象，`BoardInputController` 为 ~15 字段共享状态机；快照↔域重建逻辑存在三份拷贝；参数传递为 4 跳属性复制链。

硬约束：
- **行为零回归**：本任务不新增、不修改、不删除任何用户可见功能；绘制/擦除/选择/变换/撤销/保存/加载/导出/屏幕批注行为与重构前一致。
- **WBIX 兼容**：升级 v3 后必须能读 v1/v2 旧文件（新 App 向后读）；v1/v2 文件不含形状数据，无迁移逻辑。
- 保持既有目录结构与分层（Board/Interaction/Rendering/Controls/Features），不引入 DI 容器与 MVVM。
- 阶段二（`09-09-shape-tools`）依赖本任务合入，抽象设计须满足父 PRD R5-R8 的能力要求（两点式形状、命令栈同栈、属性面板数据源、两层 UI 接入）。

## Requirements

- R1 绘制条目抽象：文档层对"折线笔迹"与"几何形状"有统一表达（接口/基类），`BoardDocument`、命令栈、渲染器、命中测试按类型**单点分发**；本阶段形状类仅做占位实现（不交付具体形状），抽象本身须可承载阶段二的直线/矩形/椭圆/箭头。
- R2 工具策略化：每个工具一个独立状态对象（生命周期：Begin/Move/End/Cancel，含预览渲染钩子），工具经注册表解析；`BoardInputController` 退化为事件路由与工具调度，不再持有各工具的运行态字段。现有 Select/Pen/Eraser 三工具迁移为策略实现，`IBoardEraser` 策略点保留语义。
- R3 序列化收敛：快照↔域重建逻辑收敛到单点（消除 Applier/BoardRasterExporter/WbiWorkspaceImporter 三份拷贝中的重复）；快照引入类型标识字段；`WbixWorkspaceSerializer.CurrentVersion` 升 3。
- R4 参数链收敛：引入 ToolOptions 值对象承载"当前工具/颜色/粗细/压感"等绘制参数，替代 `MainWindow → BoardCanvasControl → BoardInputController → Stroke` 的逐跳属性复制；ScreenAnnotation 入口同步走 ToolOptions。
- R5 重构过程保持测试可运行：既有单测随重构适配（不改测试意图），核心路径补齐针对新抽象的用例。

## Acceptance Criteria

- [ ] `dotnet build WindBoard.slnx -c Release` 无警告新增、`dotnet test WindBoard.slnx` 全量通过（含本地化 Key 审计）。
- [ ] 手测回归清单通过：画笔绘制（含压感）、荧光笔色、橡皮两种模式、选择/框选/拖拽变换、多页、撤销/重做、保存/加载（旧 v2 文件可打开）、导出 PNG/PDF、屏幕批注全流程。
- [ ] 代码评审标准：渲染、命中、序列化三处均存在"按绘制条目类型"的单点分发结构；`BoardInputController` 不再持有工具专属运行态字段（工具有状态对象）。
- [ ] 旧 v1/v2 WBIX 文件在新版可正常打开且内容一致（往返测试：v2 文件 → 读取 → 保存 → 再读取一致）。
- [ ] 不出现行为变更：UI 布局、快捷键、设置项均与重构前一致。

## Out of Scope

- 具体形状工具的实现与 UI（阶段二任务 `09-09-shape-tools`）
- 属性面板 UI 机制（阶段二；本阶段仅保证 ToolOptions/命令栈可承载）
- 程序集级插件系统、函数绘制、贝塞尔、吸附（见父 PRD Out of Scope）

## 依赖与顺序

- 无前置任务依赖。
- 阶段二任务必须在本任务合入后才开始实施（依赖关系写在 `09-09-shape-tools/prd.md`）。
