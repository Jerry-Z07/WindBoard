# 测试自动化体系建设 P0-P3

## Goal

建立分层自动化测试体系，减少人工测试量，提升问题的发现效率与调试效率。覆盖四层：静态分析（编译期）、渲染快照（像素级回归）、交互桥接单测（输入翻译逻辑）、UI 自动化（壳层 E2E）。

## 背景

- 当前 `WindBoard.Tests` 已覆盖 Board 域模型（纯 C#）与交互工具层（PenTool/EraserTool/SelectTool/ShapeTool）。
- 渲染层（`BoardSceneRenderer` 像素输出）、壳层 UI（设置页/导入导出/Dock/伪装）零自动化覆盖，依赖人工点检。
- 人工测试痛点：画几笔/擦除/缩放后目检渲染、设置项与导入导出流程点检、改动后担心碰坏其他功能。

## 任务地图（子任务）

| 子任务 | 目录 | 交付物 | 建议顺序 |
|---|---|---|---|
| P0 启用 Roslyn 静态分析 | `09-10-static-analysis` | 全解决方案静态分析零新增告警 | 1（零风险） |
| P2 交互桥接单测 | `09-10-interaction-bridge-tests` | `BoardInputController` 路由状态机单测 | 2（零风险） |
| P1 渲染快照测试 | `09-10-render-snapshot-tests` | WARP 离屏渲染 + golden image 回归 | 3（价值最高） |
| P3 FlaUI UI 自动化测试 | `09-10-flaui-ui-tests` | AutomationId 体系 + FlaUI E2E 套件 | 4（引入新依赖） |

依赖关系说明：
- P0/P2 无前置依赖，可先行。
- P1 依赖 P0 完成后构建告警基线干净（避免快照测试代码引入新告警难以区分）。
- P3 独立于 P1/P2，但其 AutomationId 补充改动涉及 XAML，建议最后做以避免与其余子任务产生合并冲突。

## 跨子任务验收标准

- [x] `dotnet build WindBoard.slnx -c Release` 无分析告警（P0 交付后持续成立）。（2026-09-10 审查修复 P1/P3 引入的 14 个告警后归零）
- [x] `dotnet test WindBoard.slnx` 全绿，且包含渲染快照测试与交互桥接单测。（538 通过，含 8 个快照用例；1s 内完成）
- [x] 渲染回归失败时输出 expected/actual/diff 三张图，可定位到具体场景。（P1 实测：注释椭圆绘制 → ShapesAllKinds 失败并产出三件套）
- [x] P3 交付后存在可一键运行的 E2E 冒烟套件（设置/导入导出/Dock/伪装至少各 1 条用例）。（6 条用例，`dotnet test WindBoard.slnx -c Release -p:RunUITests=true --filter Category=E2E`，连续 3 轮全绿）
- [x] 各子任务归档时其自身 prd.md 验收标准全部满足。（P1/P3 prd 验收已于 2026-09-10 审查后勾选）

## 非目标

- 不引入 Appium（E2E 规模扩大后再评估）。
- 不引入 MVVM/依赖注入重构（与项目架构约定冲突）。
- 不把 WinUI Unit Test App 模板（MSTest）引入现有 xUnit 体系。
- 不追求 100% 自动化覆盖：画笔手感、笔迹平滑度等主观体验仍需人工验证。

## Notes

- 来源调研（2026-09）：微软官方测试指南确认 WinAppDriver 已停更、推荐 Appium/静态分析方案；winapp CLI 的 UI 自动化作为开发期调试工具使用，不进测试套件。
