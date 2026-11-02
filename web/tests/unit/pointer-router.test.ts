/**
 * 指针决策表用例（P1.5）。
 *
 * 每个用例都对应桌面版 PointerRoutingDecisions.cs / BoardInputController.*.cs 的某个判断分支，
 * 表驱动组织：一行一个桌面分支，边界值（500ms / 10 DIP / 150ms / 触点数量）两侧都覆盖。
 */

import { describe, expect, it } from 'vitest'
import {
  decidePointerAction,
  decideTouchGestureEnd,
  decideWheelAction,
  decideWheelZoomTick,
  horizontalWheelPanScreenDelta,
  normalizePressure,
  resolveTouchManipulationTarget,
  ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP,
  ELEMENT_DOUBLE_CLICK_TIMEOUT_MS,
  HORIZONTAL_WHEEL_PAN_DIP_PER_DELTA,
  MARQUEE_CLICK_THRESHOLD_DIP,
  WHEEL_ZOOM_IDLE_TIMEOUT_MS,
  WHEEL_ZOOM_TIMER_INTERVAL_MS,
  type PointerAction,
  type PointerContext,
  type PointerModifiers,
  type WheelContext,
} from '../../src/interaction/pointer-router.ts'

const NO_MODIFIERS: PointerModifiers = { ctrl: false, shift: false, alt: false }

/** 构造按下用例的默认上下文：鼠标左键 + 画笔 + 无选中。 */
function context(overrides: Partial<PointerContext> = {}): PointerContext {
  return {
    phase: 'down',
    pointerType: 'mouse',
    button: 0,
    buttons: 1,
    modifiers: NO_MODIFIERS,
    activePointerCount: 1,
    tool: 'pen',
    hasSelection: false,
    msSinceLastDown: null,
    dipSinceLastDown: null,
    ...overrides,
  }
}

interface ActionCase {
  readonly name: string
  readonly overrides: Partial<PointerContext>
  readonly expected: PointerAction
}

const DOWN_CASES: readonly ActionCase[] = [
  // —— 非触摸：ShouldStartPan / ShouldStartStroke / TryBeginNonSelectGesture ——
  {
    name: '鼠标左键 + 画笔 → draw（ShouldStartStroke：鼠标要求左键）',
    overrides: { tool: 'pen', button: 0 },
    expected: { type: 'draw' },
  },
  {
    name: '鼠标左键 + 橡皮 → erase',
    overrides: { tool: 'eraser', button: 0 },
    expected: { type: 'erase' },
  },
  {
    name: '鼠标左键 + 形状 → draw-shape',
    overrides: { tool: 'shape', button: 0 },
    expected: { type: 'draw-shape' },
  },
  {
    name: '鼠标右键 + 画笔 → pan（ShouldStartPan 在 ShouldStartStroke 之前）',
    overrides: { tool: 'pen', button: 2, buttons: 2 },
    expected: { type: 'pan' },
  },
  {
    name: '鼠标右键 + 选择工具 → pan（选择工具同样先判右键平移）',
    overrides: { tool: 'select', button: 2, buttons: 2 },
    expected: { type: 'pan' },
  },
  {
    name: '鼠标右键 + 禁止视口操作 → ignore（ShouldStartPan 的 allowViewportManipulation 门禁）',
    overrides: { tool: 'pen', button: 2, buttons: 2, allowViewportManipulation: false },
    expected: { type: 'ignore' },
  },
  {
    name: '鼠标中键 → ignore（ShouldStartStroke 对鼠标只认左键）',
    overrides: { tool: 'pen', button: 1, buttons: 4 },
    expected: { type: 'ignore' },
  },
  {
    name: '触控笔笔尖 → draw（ShouldStartStroke 对 Pen 恒为 true）',
    overrides: { pointerType: 'pen', tool: 'pen', button: 0 },
    expected: { type: 'draw' },
  },
  {
    name: '触控笔笔杆键（button=2）→ draw（桌面 Pen 不判断按键，原样行为）',
    overrides: { pointerType: 'pen', tool: 'pen', button: 2, buttons: 2 },
    expected: { type: 'draw' },
  },
  {
    name: '触控笔 + 选择工具 + 点在选区内 → move-selection',
    overrides: {
      pointerType: 'pen',
      tool: 'select',
      hasSelection: true,
      insideSelectionBounds: true,
    },
    expected: { type: 'move-selection' },
  },
  {
    name: '触控笔 + 选择工具 + 点在选区外 → select-marquee',
    overrides: {
      pointerType: 'pen',
      tool: 'select',
      hasSelection: true,
      insideSelectionBounds: false,
    },
    expected: { type: 'select-marquee' },
  },
  {
    name: '鼠标左键 + 选择工具 + 点在选区内 → move-selection',
    overrides: { tool: 'select', hasSelection: true, insideSelectionBounds: true },
    expected: { type: 'move-selection' },
  },
  {
    name: '鼠标左键 + 选择工具 + 点在选区外 → select-marquee',
    overrides: { tool: 'select', hasSelection: true, insideSelectionBounds: false },
    expected: { type: 'select-marquee' },
  },
  {
    name: '鼠标左键 + 选择工具 + 无选中 → select-marquee',
    overrides: { tool: 'select' },
    expected: { type: 'select-marquee' },
  },
  {
    name: '未注入 insideSelectionBounds 时退化为 hasSelection（有选中 → move-selection）',
    overrides: { tool: 'select', hasSelection: true },
    expected: { type: 'move-selection' },
  },
  {
    name: '矛盾输入：无选中但点在包围盒内 → ignore（桌面 BeginSelectionMoveGesture 提前返回）',
    overrides: { tool: 'select', hasSelection: false, insideSelectionBounds: true },
    expected: { type: 'ignore' },
  },
  {
    name: '选择工具 + 禁止选择交互 + 鼠标左键 → ignore（TryBeginSelectGesture 首查）',
    overrides: { tool: 'select', allowSelectionInteractions: false },
    expected: { type: 'ignore' },
  },
  {
    name: '选择工具 + 禁止选择交互 + 鼠标右键 → ignore（该判断在右键平移之前）',
    overrides: { tool: 'select', button: 2, buttons: 2, allowSelectionInteractions: false },
    expected: { type: 'ignore' },
  },
  {
    name: '已有指针捕获 + 鼠标左键 → ignore（HandleNonTouchPointerPressed 首查）',
    overrides: { tool: 'pen', hasActivePointerCapture: true },
    expected: { type: 'ignore' },
  },
  {
    name: '已有指针捕获 + 选择工具 → ignore',
    overrides: { tool: 'select', hasActivePointerCapture: true },
    expected: { type: 'ignore' },
  },

  // —— 触摸：ResolveTouchPressRoute 的四个分支 ——
  {
    name: '触摸单指 + 画笔 → draw（PenOrEraser 路由）',
    overrides: { pointerType: 'touch', tool: 'pen' },
    expected: { type: 'draw' },
  },
  {
    name: '触摸单指 + 橡皮 → erase',
    overrides: { pointerType: 'touch', tool: 'eraser' },
    expected: { type: 'erase' },
  },
  {
    name: '触摸单指 + 形状 → draw-shape',
    overrides: { pointerType: 'touch', tool: 'shape' },
    expected: { type: 'draw-shape' },
  },
  {
    name: '触摸单指 + 选择工具 → select-marquee（按在选区内也起框选，Pointer.cs:50-62）',
    overrides: {
      pointerType: 'touch',
      tool: 'select',
      hasSelection: true,
      insideSelectionBounds: true,
    },
    expected: { type: 'select-marquee' },
  },
  {
    name: '触摸单指 + 选择工具 + 禁止选择交互 → draw（ResolveActiveToolId 把 Select 映射为 Pen）',
    overrides: { pointerType: 'touch', tool: 'select', allowSelectionInteractions: false },
    expected: { type: 'draw' },
  },
  {
    name: '触摸单指 + 已有指针捕获（非选择工具）→ ignore（ResolveTouchPressRoute 的 Ignore 分支）',
    overrides: { pointerType: 'touch', tool: 'pen', hasActivePointerCapture: true },
    expected: { type: 'ignore' },
  },
  {
    name: '触摸单指 + 选择工具 + 已有指针捕获 → select-marquee（SelectGesture 在 Ignore 之前）',
    overrides: { pointerType: 'touch', tool: 'select', hasActivePointerCapture: true },
    expected: { type: 'select-marquee' },
  },
  {
    name: '触摸双指 + 画笔 → gesture viewport（MultiFinger + ManipulationStarting）',
    overrides: { pointerType: 'touch', tool: 'pen', activePointerCount: 2 },
    expected: { type: 'gesture', target: 'viewport' },
  },
  {
    name: '触摸双指 + 选择工具 + 手势起点在选区内 + 有选中 → gesture selection',
    overrides: {
      pointerType: 'touch',
      tool: 'select',
      activePointerCount: 2,
      hasSelection: true,
      touchManipulationTarget: 'selection',
    },
    expected: { type: 'gesture', target: 'selection' },
  },
  {
    name: '触摸双指 + 选择工具 + 起点在选区内但无选中 → gesture viewport（要求确有选中）',
    overrides: {
      pointerType: 'touch',
      tool: 'select',
      activePointerCount: 2,
      hasSelection: false,
      touchManipulationTarget: 'selection',
    },
    expected: { type: 'gesture', target: 'viewport' },
  },
  {
    name: '触摸双指 + 选择工具 + 起点在选区外 → gesture viewport',
    overrides: {
      pointerType: 'touch',
      tool: 'select',
      activePointerCount: 2,
      hasSelection: true,
      touchManipulationTarget: 'viewport',
    },
    expected: { type: 'gesture', target: 'viewport' },
  },
  {
    name: '触摸双指 + 禁止视口操作 → ignore（ManipulationStarting 早退）',
    overrides: {
      pointerType: 'touch',
      tool: 'pen',
      activePointerCount: 2,
      allowViewportManipulation: false,
    },
    expected: { type: 'ignore' },
  },
  {
    name: '触摸双指 + 有阻塞交互 → ignore（HasBlockingInteractionForManipulation 门）',
    overrides: {
      pointerType: 'touch',
      tool: 'pen',
      activePointerCount: 2,
      blockingInteraction: true,
    },
    expected: { type: 'ignore' },
  },
  {
    name: '触摸双指 + 无阻塞交互（显式 false）→ gesture viewport',
    overrides: {
      pointerType: 'touch',
      tool: 'pen',
      activePointerCount: 2,
      blockingInteraction: false,
    },
    expected: { type: 'gesture', target: 'viewport' },
  },
  {
    name: '触摸 + 触控笔按下（掌拒，浏览器补充）→ ignore',
    overrides: { pointerType: 'touch', tool: 'select', penActive: true },
    expected: { type: 'ignore' },
  },
  {
    name: '掌拒优先于双指手势（浏览器补充）',
    overrides: { pointerType: 'touch', tool: 'pen', activePointerCount: 2, penActive: true },
    expected: { type: 'ignore' },
  },
]

describe('decidePointerAction / down 相位（对应桌面 Pointer.cs + PointerRoutingDecisions.cs）', () => {
  for (const testCase of DOWN_CASES) {
    it(testCase.name, () => {
      expect(decidePointerAction(context(testCase.overrides))).toEqual(testCase.expected)
    })
  }
})

const MOVE_CASES: readonly ActionCase[] = [
  {
    name: '无手势归属 → ignore（桌面 ResolveMoveRoute 返回 None）',
    overrides: { phase: 'move', activeGesture: null },
    expected: { type: 'ignore' },
  },
  {
    name: '平移手势 → pan',
    overrides: { phase: 'move', activeGesture: 'pan' },
    expected: { type: 'pan' },
  },
  {
    name: '选区拖动 → move-selection',
    overrides: { phase: 'move', activeGesture: 'selection-move' },
    expected: { type: 'move-selection' },
  },
  {
    name: '框选 → select-marquee',
    overrides: { phase: 'move', activeGesture: 'marquee' },
    expected: { type: 'select-marquee' },
  },
  {
    name: '绘制 → draw',
    overrides: { phase: 'move', activeGesture: 'draw' },
    expected: { type: 'draw' },
  },
  {
    name: '擦除 → erase',
    overrides: { phase: 'move', activeGesture: 'erase' },
    expected: { type: 'erase' },
  },
  {
    name: '图形 → draw-shape',
    overrides: { phase: 'move', activeGesture: 'shape' },
    expected: { type: 'draw-shape' },
  },
]

describe('decidePointerAction / move 相位（对应桌面 ResolveMoveRoute）', () => {
  for (const testCase of MOVE_CASES) {
    it(testCase.name, () => {
      expect(decidePointerAction(context(testCase.overrides))).toEqual(testCase.expected)
    })
  }
})

const END_CASES: readonly ActionCase[] = [
  {
    name: '抬起 + 无手势 → ignore',
    overrides: { phase: 'up', activeGesture: null },
    expected: { type: 'ignore' },
  },
  {
    name: '抬起 + 平移 → end pan commit（平移无回滚语义）',
    overrides: { phase: 'up', activeGesture: 'pan' },
    expected: { type: 'end', gesture: 'pan', commit: true },
  },
  {
    name: '抬起 + 框选 → end marquee commit',
    overrides: { phase: 'up', activeGesture: 'marquee' },
    expected: { type: 'end', gesture: 'marquee', commit: true },
  },
  {
    name: '抬起 + 选区拖动 → end selection-move commit',
    overrides: { phase: 'up', activeGesture: 'selection-move' },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '抬起 + 绘制 → end draw commit',
    overrides: { phase: 'up', activeGesture: 'draw' },
    expected: { type: 'end', gesture: 'draw', commit: true },
  },
  {
    name: '抬起 + 擦除 → end erase commit',
    overrides: { phase: 'up', activeGesture: 'erase' },
    expected: { type: 'end', gesture: 'erase', commit: true },
  },
  {
    name: '抬起 + 图形 → end shape commit',
    overrides: { phase: 'up', activeGesture: 'shape' },
    expected: { type: 'end', gesture: 'shape', commit: true },
  },
  {
    name: '取消 + 无手势 → ignore',
    overrides: { phase: 'cancel', activeGesture: null },
    expected: { type: 'ignore' },
  },
  {
    name: '取消 + 平移 → end pan commit（桌面 Cancel 也走同一结束分支）',
    overrides: { phase: 'cancel', activeGesture: 'pan' },
    expected: { type: 'end', gesture: 'pan', commit: true },
  },
  {
    name: '取消 + 框选 → end marquee 丢弃',
    overrides: { phase: 'cancel', activeGesture: 'marquee' },
    expected: { type: 'end', gesture: 'marquee', commit: false },
  },
  {
    name: '取消 + 选区拖动 → end selection-move 丢弃',
    overrides: { phase: 'cancel', activeGesture: 'selection-move' },
    expected: { type: 'end', gesture: 'selection-move', commit: false },
  },
  {
    name: '取消 + 绘制 → end draw 丢弃',
    overrides: { phase: 'cancel', activeGesture: 'draw' },
    expected: { type: 'end', gesture: 'draw', commit: false },
  },
  {
    name: '取消 + 擦除 → end erase 丢弃',
    overrides: { phase: 'cancel', activeGesture: 'erase' },
    expected: { type: 'end', gesture: 'erase', commit: false },
  },
  {
    name: '取消 + 图形 → end shape 丢弃',
    overrides: { phase: 'cancel', activeGesture: 'shape' },
    expected: { type: 'end', gesture: 'shape', commit: false },
  },
]

describe('decidePointerAction / up 与 cancel 相位（对应桌面 HandlePointerEnded）', () => {
  for (const testCase of END_CASES) {
    it(testCase.name, () => {
      expect(decidePointerAction(context(testCase.overrides))).toEqual(testCase.expected)
    })
  }
})

/** 双击打开元素用例的公共上下文：选择工具 + 选区拖动 + 同一元素。 */
const OPEN_BASE: Partial<PointerContext> = {
  phase: 'up',
  tool: 'select',
  hasSelection: true,
  activeGesture: 'selection-move',
  elementId: 'element-1',
  lastElementClickId: 'element-1',
  msSinceLastDown: 100,
  dipSinceLastDown: 1,
}

interface OpenCase {
  readonly name: string
  readonly overrides: Partial<PointerContext>
  readonly expected: PointerAction
}

const OPEN_CASES: readonly OpenCase[] = [
  {
    name: '500ms + 10 DIP（含边界）→ open-element',
    overrides: {
      ...OPEN_BASE,
      msSinceLastDown: ELEMENT_DOUBLE_CLICK_TIMEOUT_MS,
      dipSinceLastDown: ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP,
    },
    expected: { type: 'open-element' },
  },
  {
    name: '501ms → 不打开（超出 500ms 边界）',
    overrides: {
      ...OPEN_BASE,
      msSinceLastDown: ELEMENT_DOUBLE_CLICK_TIMEOUT_MS + 1,
      dipSinceLastDown: ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP,
    },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '10.01 DIP → 不打开（超出 10 DIP 边界）',
    overrides: {
      ...OPEN_BASE,
      msSinceLastDown: ELEMENT_DOUBLE_CLICK_TIMEOUT_MS,
      dipSinceLastDown: ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP + 0.01,
    },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '0ms + 0 DIP → open-element',
    overrides: { ...OPEN_BASE, msSinceLastDown: 0, dipSinceLastDown: 0 },
    expected: { type: 'open-element' },
  },
  {
    name: '距上次点击为 null → 不打开（首次点击）',
    overrides: { ...OPEN_BASE, msSinceLastDown: null },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '屏幕距离为 null → 不打开',
    overrides: { ...OPEN_BASE, dipSinceLastDown: null },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '上次点击的是另一个元素 → 不打开',
    overrides: { ...OPEN_BASE, lastElementClickId: 'element-2' },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '没有命中元素 → 不打开',
    overrides: { ...OPEN_BASE, elementId: null },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '选区拖动产生了变换（SelectionModified）→ 不打开',
    overrides: { ...OPEN_BASE, selectionModified: true },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '非选择工具 → 不打开',
    overrides: { ...OPEN_BASE, tool: 'pen' },
    expected: { type: 'end', gesture: 'selection-move', commit: true },
  },
  {
    name: '框选路径（点击命中元素）→ open-element',
    overrides: { ...OPEN_BASE, activeGesture: 'marquee', selectionModified: true },
    expected: { type: 'open-element' },
  },
  {
    name: '活动工具路径不参与双击打开 → end draw',
    overrides: {
      ...OPEN_BASE,
      activeGesture: 'draw',
      tool: 'select',
    },
    expected: { type: 'end', gesture: 'draw', commit: true },
  },
  {
    name: '取消相位不打开（丢弃）→ end commit=false',
    overrides: { ...OPEN_BASE, phase: 'cancel' },
    expected: { type: 'end', gesture: 'selection-move', commit: false },
  },
  {
    name: '按下相位不做双击判定（桌面在抬起时判定）→ move-selection',
    overrides: {
      ...OPEN_BASE,
      phase: 'down',
      button: 0,
      insideSelectionBounds: true,
    },
    expected: { type: 'move-selection' },
  },
]

describe('decidePointerAction / 双击打开元素（对应桌面 HandleElementClickForMaybeOpen）', () => {
  for (const testCase of OPEN_CASES) {
    it(testCase.name, () => {
      expect(decidePointerAction(context(testCase.overrides))).toEqual(testCase.expected)
    })
  }
})

function wheelContext(overrides: Partial<WheelContext> = {}): WheelContext {
  return {
    delta: 120,
    isHorizontal: false,
    modifiers: NO_MODIFIERS,
    tool: 'pen',
    ...overrides,
  }
}

interface WheelCase {
  readonly name: string
  readonly overrides: Partial<WheelContext>
  readonly expected: 'zoom' | 'pan-horizontal' | 'transform-selection' | 'ignore'
}

const WHEEL_CASES: readonly WheelCase[] = [
  {
    name: '普通滚轮 → zoom（ZoomAboutScreenPoint）',
    overrides: { delta: 120, tool: 'pen' },
    expected: 'zoom',
  },
  {
    name: '反向滚轮 → zoom',
    overrides: { delta: -120, tool: 'pen' },
    expected: 'zoom',
  },
  {
    name: '禁止视口操作 → ignore（第一个判断）',
    overrides: { delta: 120, allowViewportManipulation: false },
    expected: 'ignore',
  },
  {
    name: '存在阻塞交互 → ignore（HasBlockingInteractionForWheel）',
    overrides: { delta: 120, hasBlockingInteraction: true },
    expected: 'ignore',
  },
  {
    name: 'delta 为 0 → ignore',
    overrides: { delta: 0 },
    expected: 'ignore',
  },
  {
    name: '横向滚轮 → pan-horizontal',
    overrides: { delta: 120, isHorizontal: true },
    expected: 'pan-horizontal',
  },
  {
    name: '横向滚轮 + 选择工具 + Ctrl + 有笔迹 → pan-horizontal（横向分支在变换之前）',
    overrides: {
      delta: 120,
      isHorizontal: true,
      tool: 'select',
      hasTransformableStrokes: true,
      modifiers: { ctrl: true, shift: false, alt: false },
    },
    expected: 'pan-horizontal',
  },
  {
    name: '选择工具 + Ctrl + 有笔迹 → transform-selection（缩放笔迹）',
    overrides: {
      delta: 120,
      tool: 'select',
      hasTransformableStrokes: true,
      modifiers: { ctrl: true, shift: false, alt: false },
    },
    expected: 'transform-selection',
  },
  {
    name: '选择工具 + Shift + 有笔迹 → transform-selection（旋转笔迹）',
    overrides: {
      delta: 120,
      tool: 'select',
      hasTransformableStrokes: true,
      modifiers: { ctrl: false, shift: true, alt: false },
    },
    expected: 'transform-selection',
  },
  {
    name: '选择工具 + Ctrl+Shift + 有笔迹 → transform-selection（两者叠加）',
    overrides: {
      delta: 120,
      tool: 'select',
      hasTransformableStrokes: true,
      modifiers: { ctrl: true, shift: true, alt: false },
    },
    expected: 'transform-selection',
  },
  {
    name: '选择工具 + 仅 Alt + 有笔迹 → zoom（Alt 不参与变换）',
    overrides: {
      delta: 120,
      tool: 'select',
      hasTransformableStrokes: true,
      modifiers: { ctrl: false, shift: false, alt: true },
    },
    expected: 'zoom',
  },
  {
    name: '选择工具 + Ctrl + 选中元素 → transform-selection（元素缩放）',
    overrides: {
      delta: 120,
      tool: 'select',
      hasSelectedElement: true,
      modifiers: { ctrl: true, shift: false, alt: false },
    },
    expected: 'transform-selection',
  },
  {
    name: '选择工具 + Shift + 选中元素 → zoom（元素只支持 Ctrl 缩放）',
    overrides: {
      delta: 120,
      tool: 'select',
      hasSelectedElement: true,
      modifiers: { ctrl: false, shift: true, alt: false },
    },
    expected: 'zoom',
  },
  {
    name: '选择工具 + Shift + 笔迹与元素混合选中 → transform-selection（笔迹分支优先）',
    overrides: {
      delta: 120,
      tool: 'select',
      hasTransformableStrokes: true,
      hasSelectedElement: true,
      modifiers: { ctrl: false, shift: true, alt: false },
    },
    expected: 'transform-selection',
  },
  {
    name: '画笔工具 + Ctrl → zoom（只有选择工具才变换选区）',
    overrides: {
      delta: 120,
      tool: 'pen',
      hasTransformableStrokes: true,
      modifiers: { ctrl: true, shift: false, alt: false },
    },
    expected: 'zoom',
  },
  {
    name: '选择工具 + Ctrl + 无选中内容 → zoom',
    overrides: {
      delta: 120,
      tool: 'select',
      modifiers: { ctrl: true, shift: false, alt: false },
    },
    expected: 'zoom',
  },
  {
    name: '选择工具 + 有笔迹但无修饰键 → zoom',
    overrides: { delta: 120, tool: 'select', hasTransformableStrokes: true },
    expected: 'zoom',
  },
]

describe('decideWheelAction（对应桌面 OnCanvasPointerWheelChanged）', () => {
  for (const testCase of WHEEL_CASES) {
    it(testCase.name, () => {
      expect(decideWheelAction(wheelContext(testCase.overrides))).toBe(testCase.expected)
    })
  }
})

describe('桌面常量（与 PointerRoutingDecisions.cs / Open.cs / SelectTool.cs 对齐）', () => {
  it('双击超时 500ms、最大距离 10 DIP', () => {
    expect(ELEMENT_DOUBLE_CLICK_TIMEOUT_MS).toBe(500)
    expect(ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP).toBe(10)
  })

  it('滚轮缩放空闲 150ms、定时器 50ms', () => {
    expect(WHEEL_ZOOM_IDLE_TIMEOUT_MS).toBe(150)
    expect(WHEEL_ZOOM_TIMER_INTERVAL_MS).toBe(50)
  })

  it('横向滚轮每单位 1 DIP、框选点击阈值 6 DIP', () => {
    expect(HORIZONTAL_WHEEL_PAN_DIP_PER_DELTA).toBe(1)
    expect(MARQUEE_CLICK_THRESHOLD_DIP).toBe(6)
  })
})

describe('normalizePressure（对应桌面 NormalizePressure）', () => {
  const cases: ReadonlyArray<readonly [string, Parameters<typeof normalizePressure>, number]> = [
    ['鼠标固定 1.0（浏览器 pressure=0.5 也不影响）', ['mouse', 0.5], 1],
    ['触摸固定 1.0', ['touch', 0.1], 1],
    ['触控笔下界：0.05 → 0.1', ['pen', 0.05], 0.1],
    ['触控笔下界：0.1 保持', ['pen', 0.1], 0.1],
    ['触控笔中间值保持', ['pen', 0.5], 0.5],
    ['触控笔上界：1 保持', ['pen', 1], 1],
    ['触控笔上界：1.5 → 1', ['pen', 1.5], 1],
  ]

  for (const [name, args, expected] of cases) {
    it(name, () => {
      expect(normalizePressure(...args)).toBe(expected)
    })
  }
})

describe('decideWheelZoomTick（对应桌面 EvaluateWheelZoomTick）', () => {
  const cases: ReadonlyArray<readonly [string, readonly [boolean, number, number], string]> = [
    ['未处于滚轮缩放 → stop-idle', [false, 1000, 900], 'stop-idle'],
    ['距上次 149ms → wait（不足 150ms）', [true, 1000, 851], 'wait'],
    ['距上次 150ms → end（到达空闲超时）', [true, 1000, 850], 'end'],
    ['距上次 0ms → wait', [true, 1000, 1000], 'wait'],
    ['距上次远超超时 → end', [true, 1000, 0], 'end'],
  ]

  for (const [name, args, expected] of cases) {
    it(name, () => {
      expect(decideWheelZoomTick(...args)).toBe(expected)
    })
  }
})

describe('decideTouchGestureEnd（对应桌面 ResolveTouchGestureEnd）', () => {
  const cases: ReadonlyArray<
    readonly [string, Parameters<typeof decideTouchGestureEnd>[0], string]
  > = [
    [
      '非触摸来源 → none（不结束笔迹）',
      {
        isStrokeActiveItem: true,
        strokePointCount: 5,
        hasOtherActiveItem: false,
        isErasing: false,
        isTouchOrigin: false,
      },
      'none',
    ],
    [
      '触摸笔迹只有 1 点 → discard-dot-stroke',
      {
        isStrokeActiveItem: true,
        strokePointCount: 1,
        hasOtherActiveItem: false,
        isErasing: false,
        isTouchOrigin: true,
      },
      'discard-dot-stroke',
    ],
    [
      '触摸笔迹 2 点 → commit-stroke',
      {
        isStrokeActiveItem: true,
        strokePointCount: 2,
        hasOtherActiveItem: false,
        isErasing: false,
        isTouchOrigin: true,
      },
      'commit-stroke',
    ],
    [
      '触摸图形 → discard-shape',
      {
        isStrokeActiveItem: false,
        strokePointCount: 0,
        hasOtherActiveItem: true,
        isErasing: false,
        isTouchOrigin: true,
      },
      'discard-shape',
    ],
    [
      '触摸擦除 → commit-eraser',
      {
        isStrokeActiveItem: false,
        strokePointCount: 0,
        hasOtherActiveItem: false,
        isErasing: true,
        isTouchOrigin: true,
      },
      'commit-eraser',
    ],
    [
      '无活动工具操作 → none',
      {
        isStrokeActiveItem: false,
        strokePointCount: 0,
        hasOtherActiveItem: false,
        isErasing: false,
        isTouchOrigin: true,
      },
      'none',
    ],
  ]

  for (const [name, input, expected] of cases) {
    it(name, () => {
      expect(decideTouchGestureEnd(input)).toBe(expected)
    })
  }
})

describe('horizontalWheelPanScreenDelta（对应桌面 ResolveHorizontalWheelPanScreenDelta）', () => {
  it('正增量 → 内容向左（负号来自 PanByScreenDelta 的内容位移语义）', () => {
    expect(horizontalWheelPanScreenDelta(120)).toEqual({ x: -120, y: 0 })
  })

  it('负增量 → 内容向右', () => {
    expect(horizontalWheelPanScreenDelta(-120)).toEqual({ x: 120, y: 0 })
  })

  it('零增量 → 零位移', () => {
    expect(horizontalWheelPanScreenDelta(0)).toEqual({ x: 0, y: 0 })
  })

  it('半格增量按比例换算', () => {
    expect(horizontalWheelPanScreenDelta(60)).toEqual({ x: -60, y: 0 })
  })
})

describe('resolveTouchManipulationTarget（对应桌面 Pointer.cs:53-57）', () => {
  it('点在选中包围盒内 → selection', () => {
    expect(resolveTouchManipulationTarget({ hasSelection: true, insideSelectionBounds: true })).toBe(
      'selection',
    )
  })

  it('点在选中包围盒外 → viewport', () => {
    expect(resolveTouchManipulationTarget({ hasSelection: true, insideSelectionBounds: false })).toBe(
      'viewport',
    )
  })

  it('未注入命中信息时退化为 hasSelection（有选中 → selection）', () => {
    expect(resolveTouchManipulationTarget({ hasSelection: true })).toBe('selection')
  })

  it('无选中 → viewport', () => {
    expect(resolveTouchManipulationTarget({ hasSelection: false })).toBe('viewport')
  })
})
