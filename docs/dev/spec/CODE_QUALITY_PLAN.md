# 代码质量持续治理计划（分阶段）

本计划用于指导后续对 `docs/dev/spec/CODE_QUALITY.md` 中的热点与异味进行持续治理，目标是**长期稳定地降低维护成本**，并避免“新功能把复杂度越堆越高”。

## 适用范围与基线

- 适用范围：本仓库所有 `C# / XAML` 源码（排除 `bin/obj`）。
- 基线文件：`docs/dev/spec/CODE_QUALITY.md`（包含 TOTAL 指标、Top 热点文件与 smells 清单）。
- 原则：
  - 先“**不变差**”（防回归），再“**逐步变好**”（拆分与重构）。
  - 重构优先围绕**用户高频路径/高风险逻辑**（导入、序列化、渲染、输入、更新）。

## 总体目标（建议）

- 3～6 个迭代内：
  - 将 Top 热点文件中 `complexity >= 100` 的数量降低至少 50%；
  - 将 `Function with high complexity` 数量降低至少 30%；
  - 把核心路径（导入/序列化/渲染/输入）的失败路径用例补齐（避免回归难定位）。

> 说明：阈值建议采用“分段收敛”，不要一刀切，否则初期会出现大量既有债务导致 CI 长期红灯。

## 阶段 0：准备与共识（1～2 天）

**目标**：让团队对“怎么衡量、怎么验收、怎么拆任务”达成一致。

- 固化当前基线：确认 `docs/dev/spec/CODE_QUALITY.md` 已在目标分支存在。
- 明确治理口径：
  - 本地最低要求：`qlty check --summary .`、`qlty smells --no-snippets .`、`qlty metrics --sort complexity .`
  - 涉及核心逻辑时额外要求：`dotnet test WindBoard.slnx`
- 约定治理方式：
  - 每次只改 1～2 个热点（“小步快跑”），避免大规模重构难回滚。
  - 不把“纯格式化/纯重命名”与“逻辑重构”混在同一个 PR。

**验收标准**：
- 计划文件与基线文件在仓库可见；
- 团队成员能复现报告与热点列表。

## 阶段 1：可见化

**目标**：让质量数据“可见、可追溯”，并让每个 PR 都能看到趋势。

建议落地项：

- 在 CI（GitHub Actions）增加 `code-quality` 工作流（PR 触发）：
  - `qlty check --summary --no-progress .`
  - `qlty smells --no-snippets .`
  - `qlty metrics .`、`qlty metrics --sort complexity .`
  - 上传上述输出为 artifact（文本文件即可）
- 在 PR 模板或贡献指南中补充“质量自检步骤”（可选）。

**验收标准**：
- 每个 PR 都能看到至少一份质量报告产物（artifact）。

## 阶段 2：软门禁

**目标**：先不强制“必须降低复杂度”，但要强制“不能明显变差”，尤其是热点区域。

推荐策略（软门禁）：

- **热点文件变更提醒**：
  - 当 PR 触达 `CODE_QUALITY.md` Top 20 文件时，CI 输出提示：
    - “该文件是复杂度热点，请在 PR 描述说明：是否引入新分支/新状态/新 IO；是否补测试；是否拆分函数。”
- **增量约束（建议先 Warning，稳定后再 Fail）**：
  - 若新增 `Function with high complexity`，标记为 warning；
  - 若热点文件的 complexity 明显上升，标记为 warning；
  - 若新增“参数过多/return 过多”的函数，标记为 warning。

**验收标准**：
- 热点文件被修改时，PR 至少包含一段“风险说明/测试说明/回滚说明”。

## 阶段 3：热点治理

**目标**：把“最贵的维护点”拆掉，把复杂逻辑从 UI/巨型函数中解耦出来，并补齐关键失败路径测试。

### 3.1 推荐优先级（从高到低）

1) 导入链路
- [x] `WindBoard/Features/Import/UI/ImportDialog.xaml.cs`（已完成：队列/预览/提交逻辑下沉到 Services；TreeView 改为“状态变更后整树重建”；complexity 146 → 63）
- [x] `WindBoard/Features/Import/Services/BoardImportService.cs`（已完成：拆分 `ImportElementsAsync`；统一媒体/`.url` 类型识别；补齐关键失败兜底与日志；complexity 50 → 39）
- [x] `WindBoard/Features/Import/Wbi/WbiWorkspaceImporter.cs`（已完成：引入 `WbiImportContext` 降参数；拆分分页/笔迹/附件导入；补齐缺资源用例；complexity 60 → 53）
- 完成记录：
  - `WindBoard/Features/Import/ImportFlow.cs`：统一消费 `ImportDialogSubmission`。
  - `WindBoard/Features/Import/Models/ImportDialogSubmission.cs`：新增提交结果模型。
  - `WindBoard/Features/Import/Services/ImportQueueState.cs`：新增队列状态机与提交构建。
  - `WindBoard/Features/Import/Services/ImportWorkspacePreviewService.cs`：新增工作区预览归一化读取。
  - `WindBoard.Tests/Features/Import/ImportQueueStateTests.cs`、`WindBoard.Tests/Features/Import/Wbix/WbixPreviewReaderTests.cs`：新增失败路径单测。
  - `WindBoard/Features/Import/Services/BoardImportService.cs`：拆分元素导入流程，失败场景可降级继续导入；媒体/快捷方式类型识别统一走 `ImportFileTypeResolver`。
  - `WindBoard/Features/Import/Wbi/WbiWorkspaceImporter.cs`：引入 `WbiImportContext` 收敛导入上下文，拆分分页导入职责并减少多参数/高复杂度方法。
  - `WindBoard.Tests/Features/Import/BoardImportServiceTests.cs`：新增元素导入关键失败路径与 `.url` 回退单测。
  - `WindBoard.Tests/Features/Import/Wbi/WbiWorkspaceImporterTests.cs`：补充内嵌图片资源缺失的失败路径单测。

2) WBIX 序列化/持久化
- [x] `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs`（已完成：改为 partial 拆分 Save/Load/路径与资源处理；引入导入上下文对象收敛参数；保留原有日志与兼容行为）
- [x] `WindBoard/Board/Persistence/BoardWorkspaceSnapshotConverter.cs`（已完成：`TryCreateElementSnapshot` 改为单出口，减少多 return smells）
- 完成记录：
  - `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs`：拆分为 partial 文件，序列化入口只保留常量与 JSON 配置。
  - `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.Save.cs`：将 `SaveAsync` 拆分为 pages/resources/manifest 三段写入，降低复杂度。
  - `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.Load.cs`：引入 `WbixLoadContext` 收敛资源索引/临时目录/总提取大小；元素解析失败按单元素降级并输出日志。
  - `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.ResourcesAndPaths.cs`：集中 Zip 路径归一化/安全校验与 JSON 解析小工具，避免散落重复逻辑。
  - `WindBoard.Tests/Board/Persistence/WbixWorkspaceSerializerFailureTests.cs`：新增缺 manifest、版本不支持、页路径不安全、资源路径不安全（降级不阻断）等失败路径单测。

3) 渲染与输入（回归成本最高）
- `WindBoard/Rendering/Board/BoardSceneRenderer.cs`
- `WindBoard/Controls/BoardCanvasControl.Rendering.cs`
- `WindBoard/Interaction/BoardInputController/*`

4) 更新/设置（分支多、兼容性/失败路径多）
- `WindBoard/Updates/*`
- `WindBoard/Settings/*`

### 3.2 每次治理的“任务模板”（建议）

- 目标：将某个文件或函数的复杂度降到可接受范围（例如把一个 200+ 行的大方法拆成 4～8 个小方法）。
- 做法：
  - 拆职责：UI code-behind 只保留 UI 状态与事件绑定；业务流程下沉到 service/flow。
  - 降参数：使用 `Context/Options` 对象承载临时状态，避免参数爆炸。
  - 降分支：把大 if/else 拆成策略表、模式匹配、状态机或命令对象。
  - 补测试：至少覆盖 1～3 个失败路径（无效输入、IO 失败、资源缺失、取消/超时）。
- 验证：
  - `qlty check --summary --no-progress .`
  - `dotnet test WindBoard.slnx`

**验收标准**（每个治理 PR）：
- 指标不变差：不新增 smells 中的高风险项（或明确说明原因）；
- 至少新增/调整一组测试，覆盖本次拆分触达的关键路径。
- 热点区域 PR 不再出现长期质量劣化（复杂度/异味持续上升）。

## 附：建议的阈值策略（分段收敛）

> 下面是“治理过程中的目标值”示例，可按团队节奏调整。

- 第 1 阶段（只看趋势）：不设硬阈值，只产出报告。
- 第 2 阶段（软门禁）：
  - 热点文件 complexity 不允许明显上升（超过 5～10 视为明显，具体阈值可根据实际调整）。
- 第 3 阶段（开始收敛）：
  - 将 `complexity >= 120` 的文件逐步压到 < 120；
  - 将 `Function with high complexity`（通常为 20+）逐步拆到 < 20；
  - 参数数 > 6 的函数尽量引入上下文对象（`Context/Options`）。
