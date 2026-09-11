# P2 交互桥接单测

## Goal

为 `BoardInputController` 中尚未覆盖的指针路由/状态机逻辑建立单元测试，降低输入翻译层（WinUI 指针事件 → 域操作）的回归风险。

## 范围澄清（已调研确认）

- 交互**工具层已有覆盖**：`PenToolTests`、`EraserToolTests`、`SelectToolTests`、`ShapeToolTests` 基于 `ToolInput` 纯数据驱动测试，状态机（Begin/Move/End/Cancel）已覆盖。本任务**不重复**这部分。
- 剩余空白在 **controller 层**：`BoardInputController`（partial，6 个文件）中的事件路由与全局状态机。

## Requirements

覆盖以下逻辑（可测部分）：

1. **PointerId 跟踪状态机**：`_activePointerId` / `_panPointerId` / `_selectionPointerId` / `_marqueePointerId` 的分配、互斥与释放时序（按下-移动-抬起/取消/捕获丢失）。
2. **滚轮缩放节流**：`WheelZoomIdleTimeoutMs`（150ms）与 `WheelZoomTimerIntervalMs`（50ms）的延迟缩放合并逻辑（`_isWheelZooming` 生命周期）。
3. **多指触摸路由**：`_activeTouchPointers` 集合与单指画线/双指视口操作的切换边界（`TouchManipulationTarget`）。
4. **指针数据提取**：从 `PointerRoutedEventArgs` 提取点位/设备类型/压力/按键状态的逻辑，若与 WinUI 类型耦合不可直接实例化，则将该提取部分抽为接收原始数据的可测纯函数（保持行为等价的最小重构），controller 事件处理器仅做参数转发。

## 约束

- 依赖 `SwapChainPanel`（WinUI 控件，测试中不可创建）的构造路径不硬测；优先把可测逻辑收敛到接收原始数据的函数/上下文。
- 若第 4 点需要重构 `BoardInputController`，改动须保持事件处理行为完全等价，且重构范围仅限"提取纯函数"，不做其他结构调整。
- 工具层既有测试（PenTool 等）不得回归。

## Acceptance Criteria

- [x] 上述 1~3 项各有关键分支（含边界：取消/捕获丢失/多指交叠）的单测。
  - 第 1 项：`PointerRouteStateTests`（分配/互斥闸门/移动路由优先级/抬起-取消-捕获丢失共用清理转移）。
  - 第 2 项：`PointerRoutingDecisionsTests.EvaluateWheelZoomTick_*`（149/150ms 边界、时钟回拨、空闲停表）+ 契约常量断言。
  - 第 3 项：`ResolveTouchPressRoute_*`（单指/多指 ≥2 边界/选择禁用回退/捕获闸门）、`ResolveTouchGestureEnd_*`（点状笔迹丢弃/多段提交/形状丢弃/擦除提交/非触摸来源）、`ActiveTouchPointers_TracksCountAcrossFingers`、`CancelSelectionMove_ResetsTouchTargetToViewport`。
- [x] 若发生提取重构：`dotnet test WindBoard.slnx` 全绿，工具层测试无改动或仅命名适配。
  - 提取了 `PointerRouteState`（纯状态机）与 `PointerRoutingDecisions`（纯决策函数），控制器事件处理器仅做转发；Release 构建 0 告警；工具层测试零改动。
- [x] 新增测试不含 UI 线程/WinUI 控件依赖，可在 CI 环境运行。
  - 仅使用 `PointerDeviceType` 枚举与原始数据，无控件/DispatcherQueue/事件参数构造。
