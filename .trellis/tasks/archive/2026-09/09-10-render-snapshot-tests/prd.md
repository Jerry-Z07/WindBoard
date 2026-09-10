# P1 渲染快照测试

## Goal

为 `BoardSceneRenderer` 的像素输出建立 golden image（基准图像）回归测试：构造文档/视口 → 离屏渲染 → 与基准 PNG 逐像素比对，失败时输出 expected/actual/diff 三张图，实现"渲染错在哪一眼可见"。

## 背景与可行性（已调研确认）

- `BoardSceneRenderer.Draw(ID2D1RenderTarget ctx, BoardDocument, IBoardInkItem?, BoardViewport)` 面向 D2D 渲染目标抽象编程，brush 等资源按传入 ctx 懒创建（`EnsureStrokeBrush`），并经 `WithOptionalDeviceContext2` 兼容无 DeviceContext2 的目标——离屏渲染出口具备可行性。
- 主程序 `DxSwapChainPanelRenderer.CreateDeviceResources` 已有 `DriverType.Warp` 回退先例（`TryCreateD3DDevice` static 方法），软件光栅化路径与项目现状一致。
- D2D/WARP 是微软官方能力：无 GPU 环境（CI）可跑。

## Requirements

### 测试场景（首批）

1. 空白画布（纯背景）。
2. 单条笔迹（含压感粗细变化）。
3. 多条笔迹混合透明度叠加。
4. 视口缩放/平移后的同一文档（验证 `WithWorldTransform` 变换正确性）。
5. 形状元素（矩形/椭圆/箭头/直线至少各一）。
6. 文本/元素卡片（深色 + 浅色主题各一）。
7. 选中态 overlay（选中框/手柄）。
8. 框选 marquee 矩形。

### 基准管理

- 基准 PNG 存放于测试项目内固定目录（如 `WindBoard.Tests/Rendering/__snapshots__/`），按测试名命名，纳入版本库。
- 提供基准（重新）生成机制（环境变量或显式开关），基准变更须在提交说明中说明理由。
- 比对采用逐像素 + 每通道容差阈值（抗锯齿与文本渲染的合法抖动），阈值常量化并可按场景调整。

### 失败诊断

- 比对失败时输出三张图到测试产物目录：`expected.png` / `actual.png` / `diff.png`（差异高亮），并在断言消息中给出差异像素占比。

## 约束

- 测试不得依赖真实 GPU 与显示器；全部走 WARP 软件路径。
- 主工程改动最小化：理想为零改动（harness 全部放测试侧）；若实现中发现必须改主工程（如渲染器内部强依赖 DeviceContext2 导致经典/离屏路径行为不一致），改动方案须先回报确认。
- 文本渲染跨机器/驱动版本可能存在细微差异：涉及文本的场景以容差吸收；若仍不稳定，允许将文本场景降级为结构断言（非像素级）并在代码注释与本文档同步记录。
- 渲染快照测试运行时间不得显著拖慢全量单测（目标：单场景 < 1s）。

## Acceptance Criteria

- [x] 上述 8 类场景各至少 1 条快照测试，全部可重复通过。（场景 7/8 见下方偏差记录：D2D 快照不可行，以既有结构断言覆盖并归入 P3）
- [x] `dotnet test WindBoard.slnx` 全绿（含新快照测试），CI 环境可运行。（WARP 软件路径，无 GPU/显示器依赖）
- [x] 人为引入渲染回归（如注释一行绘制调用）时，对应场景测试失败且产出 diff 三件套。（已实测：注释椭圆绘制调用 → ShapesAllKinds 失败，diff 图红色高亮缺失的椭圆）
- [x] 基准生成机制文档化（注释或 README 片段），主工程零改动或经确认的最小改动。（主工程零改动，机制见 `SnapshotBaseline` 注释）

## 实现偏差记录（2026-09-10）

1. **场景 7（选中态 overlay）/ 场景 8（框选 marquee）无法做 D2D 像素快照**：实现时确认两者均为 XAML 层元素（`BoardCanvasControl` 的 Border/Thumb，见 `UpdateSelectionOverlay`/`ShowMarqueeSelectionOverlay`），不经过 `BoardSceneRenderer` 的 D2D 输出。处理：
   - 逻辑层已有结构断言覆盖：marquee 状态机见 `SelectToolTests.Marquee_*`，选中框几何见 `InkItemScreenBoundsTests`；
   - XAML 合成层的像素级验证归入 P3 FlaUI E2E（父任务验收的 E2E 冒烟可一并覆盖）；
   - 偏差已同步注释在 `BoardSceneRendererSnapshotTests` 类头。
2. **PNG 编码改用 System.Drawing.Common**：设计稿原定 WIC 编码，但 WIC 需引入 Vortice.WIC 新包（不在既有依赖内），按“优先使用已有依赖”约定改用主工程既有依赖 System.Drawing.Common（仅 Windows 可用，测试环境恒为 Windows）。
3. **文本卡片场景需覆写进程级 MRT PrimaryLanguageOverride**：`AppSettingsServiceTests` 经 `AppLanguageService.Apply` 设置进程级 override 且其清理只还原 CultureInfo，残留 override（如 en-US）会让 L10n 回退输出 key 字符串、破坏基准比对（unpackaged 环境下赋空串清除会抛异常，实测）。文本场景在渲染前覆写 override 为 zh-CN 并 finally 尽力还原；并行的写入竞态由测试程序集级 `DisableTestParallelization`（`TestAssemblyConfig.cs`）整体消除，详见 `BoardSceneRendererSnapshotTests.RunTextCardSnapshot` 注释。
