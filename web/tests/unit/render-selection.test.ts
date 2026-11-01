/**
 * 选择覆盖层单测：手柄几何 / 命中 / 游标 / 绘制调用序列 / 与渲染句柄的接线。
 *
 * 期望值全部来自桌面真源（见 `src/render/selection-handles.ts` 与 `src/render/selection.ts`
 * 文件头的行号），这里只做"实现是否照抄"的断言。
 */

import { afterEach, describe, expect, it } from 'vitest'
import {
  computeSelectionHandles,
  hitTestSelectionHandle,
  SELECTION_HANDLE_HEIGHT,
  SELECTION_HANDLE_WIDTH,
  selectionHandleCursor,
  type ScreenBounds,
  type SelectionHandleId,
} from '../../src/render/selection-handles.ts'
import {
  drawSelectionOverlay,
  MARQUEE_FILL_COLOR,
  SELECTION_ACCENT_COLOR,
  SELECTION_BORDER_CORNER_RADIUS,
  SELECTION_BORDER_WIDTH,
  SELECTION_HANDLE_BORDER_WIDTH,
  SELECTION_HANDLE_CORNER_RADIUS,
  SELECTION_HANDLE_FILL_COLOR,
  type SelectionOverlay,
} from '../../src/render/selection.ts'
import {
  createBoardRenderHandle,
  type BoardRenderHandle,
  type RenderSources,
} from '../../src/render/render-handle.ts'
import type { SceneSurface } from '../../src/render/scene.ts'
import { FakeContext2D } from './helpers/fake-2d-context.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'
import { installFakeBrowserClock } from './helpers/fake-browser.ts'
import { createDocument, createViewport } from './helpers/render-fixtures.ts'

/** 常规包围盒（100,50)–(300,150)：宽 200、高 100。 */
const BOUNDS: ScreenBounds = { min: { x: 100, y: 50 }, max: { x: 300, y: 150 } }

/** 画布后备存储参数（DPR 2，便于断言"覆盖层按 DPR 放大"）。 */
const SURFACE: SceneSurface = { pixelWidth: 1600, pixelHeight: 1200, devicePixelRatio: 2 }

/** 取某个手柄的中心点。 */
function centerOf(bounds: ScreenBounds, id: SelectionHandleId): { x: number; y: number } {
  const handle = computeSelectionHandles(bounds).find((candidate) => candidate.id === id)
  if (handle === undefined) {
    throw new Error(`没有找到手柄 ${id}`)
  }
  return handle.centerScreen
}

/** 断言某个路径命令是圆角矩形（参数按 `roundRect(x, y, w, h, radius)`）。 */
function expectRoundRect(
  args: readonly number[] | undefined,
  expected: { x: number; y: number; width: number; height: number; radius: number },
): void {
  expect(args).toBeDefined()
  expect(args).toHaveLength(5)
  expect(args?.[0]).toBeCloseTo(expected.x)
  expect(args?.[1]).toBeCloseTo(expected.y)
  expect(args?.[2]).toBeCloseTo(expected.width)
  expect(args?.[3]).toBeCloseTo(expected.height)
  expect(args?.[4]).toBeCloseTo(expected.radius)
}

describe('选择手柄几何（selection-handles）', () => {
  it('尺寸与桌面 SelectionHandleSizeDip 一致', () => {
    // `BoardCanvasControl.SelectionHandles.cs:19` `SelectionHandleSizeDip = 14.0f`
    expect(SELECTION_HANDLE_WIDTH).toBe(14)
    expect(SELECTION_HANDLE_HEIGHT).toBe(14)
  })

  it('8 个手柄按桌面 XAML 子元素顺序给出（TL、T、TR、L、R、BL、B、BR）', () => {
    // 顺序出处：`BoardCanvasControl.xaml:56-71`
    const ids = computeSelectionHandles(BOUNDS).map((handle) => handle.id)
    expect(ids).toEqual(['nw', 'n', 'ne', 'w', 'e', 'sw', 's', 'se'])
  })

  it('手柄中心落在角点与边中点上', () => {
    // 定位出处：`SelectionHandles.cs:196-227`（以中心点摆放，左上角 = 中心 − 7）
    expect(centerOf(BOUNDS, 'nw')).toEqual({ x: 100, y: 50 })
    expect(centerOf(BOUNDS, 'n')).toEqual({ x: 200, y: 50 })
    expect(centerOf(BOUNDS, 'ne')).toEqual({ x: 300, y: 50 })
    expect(centerOf(BOUNDS, 'w')).toEqual({ x: 100, y: 100 })
    expect(centerOf(BOUNDS, 'e')).toEqual({ x: 300, y: 100 })
    expect(centerOf(BOUNDS, 'sw')).toEqual({ x: 100, y: 150 })
    expect(centerOf(BOUNDS, 's')).toEqual({ x: 200, y: 150 })
    expect(centerOf(BOUNDS, 'se')).toEqual({ x: 300, y: 150 })
  })

  it('退化包围盒（宽为 0）时左右两侧手柄重合，不产生 NaN', () => {
    const degenerate: ScreenBounds = { min: { x: 100, y: 50 }, max: { x: 100, y: 150 } }
    const handles = computeSelectionHandles(degenerate)
    expect(handles).toHaveLength(8)
    for (const handle of handles) {
      expect(Number.isFinite(handle.centerScreen.x)).toBe(true)
      expect(Number.isFinite(handle.centerScreen.y)).toBe(true)
    }
    expect(centerOf(degenerate, 'nw')).toEqual({ x: 100, y: 50 })
    expect(centerOf(degenerate, 'n')).toEqual({ x: 100, y: 50 })
    expect(centerOf(degenerate, 'e')).toEqual({ x: 100, y: 100 })
    expect(centerOf(degenerate, 'se')).toEqual({ x: 100, y: 150 })
  })

  it('退化包围盒（高为 0）时上下两侧手柄重合', () => {
    const degenerate: ScreenBounds = { min: { x: 100, y: 50 }, max: { x: 300, y: 50 } }
    expect(centerOf(degenerate, 'nw')).toEqual({ x: 100, y: 50 })
    expect(centerOf(degenerate, 'w')).toEqual({ x: 100, y: 50 })
    expect(centerOf(degenerate, 'e')).toEqual({ x: 300, y: 50 })
    expect(centerOf(degenerate, 'se')).toEqual({ x: 300, y: 50 })
  })
})

describe('选择手柄命中（selection-handles）', () => {
  it('命中角点与边中点', () => {
    expect(hitTestSelectionHandle(BOUNDS, { x: 100, y: 50 })).toBe('nw')
    expect(hitTestSelectionHandle(BOUNDS, { x: 200, y: 50 })).toBe('n')
    expect(hitTestSelectionHandle(BOUNDS, { x: 300, y: 50 })).toBe('ne')
    expect(hitTestSelectionHandle(BOUNDS, { x: 100, y: 100 })).toBe('w')
    expect(hitTestSelectionHandle(BOUNDS, { x: 300, y: 100 })).toBe('e')
    expect(hitTestSelectionHandle(BOUNDS, { x: 100, y: 150 })).toBe('sw')
    expect(hitTestSelectionHandle(BOUNDS, { x: 200, y: 150 })).toBe('s')
    expect(hitTestSelectionHandle(BOUNDS, { x: 300, y: 150 })).toBe('se')
  })

  it('命中区为手柄自身 14×14（含边界，无额外容差）', () => {
    // nw 中心 (100,50)，半宽/半高 7
    expect(hitTestSelectionHandle(BOUNDS, { x: 107, y: 57 })).toBe('nw')
    expect(hitTestSelectionHandle(BOUNDS, { x: 93, y: 43 })).toBe('nw')
    expect(hitTestSelectionHandle(BOUNDS, { x: 107.5, y: 50 })).toBeNull()
    expect(hitTestSelectionHandle(BOUNDS, { x: 100, y: 57.5 })).toBeNull()
  })

  it('角优先于边：小包围盒上重叠区域返回角手柄', () => {
    const small: ScreenBounds = { min: { x: 0, y: 0 }, max: { x: 20, y: 20 } }
    // (7,7) 同时落在 nw（中心 0,0）与 n（中心 10,0）的命中区内
    expect(hitTestSelectionHandle(small, { x: 7, y: 7 })).toBe('nw')
    // (10,7) 只在 n 的命中区内
    expect(hitTestSelectionHandle(small, { x: 10, y: 7 })).toBe('n')
    // (13,13) 同时落在 se（中心 20,20）与 e/s 内 → 角优先
    expect(hitTestSelectionHandle(small, { x: 13, y: 13 })).toBe('se')
  })

  it('远离包围盒的点不命中', () => {
    expect(hitTestSelectionHandle(BOUNDS, { x: 0, y: 0 })).toBeNull()
    expect(hitTestSelectionHandle(BOUNDS, { x: 200, y: 100 })).toBeNull()
  })
})

describe('选择手柄游标（selection-handles）', () => {
  it('按方向映射到 CSS 缩放光标', () => {
    // 桌面 `SelectionHandleThumbStyle` 没设 Cursor（`BoardCanvasControl.xaml:13-33`），
    // 网页端按缩放手柄的通行做法给出方向光标。
    expect(selectionHandleCursor('nw')).toBe('nwse-resize')
    expect(selectionHandleCursor('se')).toBe('nwse-resize')
    expect(selectionHandleCursor('ne')).toBe('nesw-resize')
    expect(selectionHandleCursor('sw')).toBe('nesw-resize')
    expect(selectionHandleCursor('n')).toBe('ns-resize')
    expect(selectionHandleCursor('s')).toBe('ns-resize')
    expect(selectionHandleCursor('e')).toBe('ew-resize')
    expect(selectionHandleCursor('w')).toBe('ew-resize')
  })
})

describe('选择覆盖层绘制（selection）', () => {
  it('无选中且无框选时不产生任何绘制调用', () => {
    const ctx = new FakeContext2D()
    drawSelectionOverlay(ctx, { boundsScreen: null, marqueeScreen: null }, SURFACE)
    expect(ctx.ops).toHaveLength(0)
  })

  it('有选中：先画选择框，再画 8 个手柄，最后还原变换', () => {
    const ctx = new FakeContext2D()
    drawSelectionOverlay(ctx, { boundsScreen: BOUNDS, marqueeScreen: null, showHandles: true }, SURFACE)

    const transforms = ctx.opsOfType('setTransform')
    // 覆盖层坐标是 DIP：先按 DPR 放大，结束后还原单位矩阵
    expect(transforms[0]?.args).toEqual([2, 0, 0, 2, 0, 0])
    expect(transforms[transforms.length - 1]?.args).toEqual([1, 0, 0, 1, 0, 0])

    // 1 次选择框描边 + 8 次手柄描边；手柄各一次填充
    expect(ctx.opsOfType('stroke')).toHaveLength(9)
    expect(ctx.opsOfType('fill')).toHaveLength(8)

    // 选择框：内缩半个线宽的圆角矩形（XAML 是内描边，Canvas 是居中描边）
    const borderStroke = ctx.opsOfType('stroke')[0]
    expect(borderStroke.style?.strokeStyle).toBe(SELECTION_ACCENT_COLOR)
    expect(borderStroke.style?.lineWidth).toBe(SELECTION_BORDER_WIDTH)
    expectRoundRect(borderStroke.path?.[0]?.args, {
      x: BOUNDS.min.x + SELECTION_BORDER_WIDTH / 2,
      y: BOUNDS.min.y + SELECTION_BORDER_WIDTH / 2,
      width: 200 - SELECTION_BORDER_WIDTH,
      height: 100 - SELECTION_BORDER_WIDTH,
      radius: SELECTION_BORDER_CORNER_RADIUS,
    })

    // 第一个手柄（nw）：白底 + 强调色 2px 描边 + 圆角 3，外沿正好 14×14
    const handleFill = ctx.opsOfType('fill')[0]
    expect(handleFill.style?.fillStyle).toBe(SELECTION_HANDLE_FILL_COLOR)
    const inset = SELECTION_HANDLE_BORDER_WIDTH / 2
    expectRoundRect(handleFill.path?.[0]?.args, {
      x: BOUNDS.min.x - SELECTION_HANDLE_WIDTH / 2 + inset,
      y: BOUNDS.min.y - SELECTION_HANDLE_HEIGHT / 2 + inset,
      width: SELECTION_HANDLE_WIDTH - inset * 2,
      height: SELECTION_HANDLE_HEIGHT - inset * 2,
      radius: SELECTION_HANDLE_CORNER_RADIUS,
    })
    const handleStroke = ctx.opsOfType('stroke')[1]
    expect(handleStroke.style?.strokeStyle).toBe(SELECTION_ACCENT_COLOR)
    expect(handleStroke.style?.lineWidth).toBe(SELECTION_HANDLE_BORDER_WIDTH)
  })

  it('showHandles 为 true 时画 8 个手柄', () => {
    const ctx = new FakeContext2D()
    drawSelectionOverlay(ctx, { boundsScreen: BOUNDS, marqueeScreen: null, showHandles: true }, SURFACE)
    expect(ctx.opsOfType('fill')).toHaveLength(8)
    expect(ctx.opsOfType('stroke')).toHaveLength(9) // 1 选择框 + 8 手柄
  })

  it('showHandles 缺省（或 false）时只画选择框，不画手柄', () => {
    // 桌面规则：选中笔迹只画框、选中单个元素才画手柄 → 默认不画手柄
    const withoutField = new FakeContext2D()
    drawSelectionOverlay(withoutField, { boundsScreen: BOUNDS, marqueeScreen: null }, SURFACE)
    expect(withoutField.opsOfType('fill')).toHaveLength(0)
    expect(withoutField.opsOfType('stroke')).toHaveLength(1)

    const explicitFalse = new FakeContext2D()
    drawSelectionOverlay(
      explicitFalse,
      { boundsScreen: BOUNDS, marqueeScreen: null, showHandles: false },
      SURFACE,
    )
    expect(explicitFalse.opsOfType('fill')).toHaveLength(0)
    expect(explicitFalse.opsOfType('stroke')).toHaveLength(1)
  })

  it('仅框选：一次淡填充 + 一次强调色描边，没有手柄', () => {
    const ctx = new FakeContext2D()
    drawSelectionOverlay(
      ctx,
      { boundsScreen: null, marqueeScreen: { min: { x: 10, y: 20 }, max: { x: 110, y: 120 } } },
      SURFACE,
    )

    expect(ctx.opsOfType('fill')).toHaveLength(1)
    expect(ctx.opsOfType('stroke')).toHaveLength(1)
    expect(ctx.opsOfType('fill')[0]?.style?.fillStyle).toBe(MARQUEE_FILL_COLOR)

    const stroke = ctx.opsOfType('stroke')[0]
    expect(stroke.style?.strokeStyle).toBe(SELECTION_ACCENT_COLOR)
    expect(stroke.style?.lineWidth).toBe(SELECTION_BORDER_WIDTH)
    expectRoundRect(stroke.path?.[0]?.args, {
      x: 10 + SELECTION_BORDER_WIDTH / 2,
      y: 20 + SELECTION_BORDER_WIDTH / 2,
      width: 100 - SELECTION_BORDER_WIDTH,
      height: 100 - SELECTION_BORDER_WIDTH,
      radius: SELECTION_BORDER_CORNER_RADIUS,
    })
  })

  it('同时有选中与框选时，框选矩形画在最后（盖住手柄）', () => {
    const ctx = new FakeContext2D()
    drawSelectionOverlay(
      ctx,
      { boundsScreen: BOUNDS, marqueeScreen: { min: { x: 10, y: 20 }, max: { x: 110, y: 120 } }, showHandles: true },
      SURFACE,
    )

    const fills = ctx.opsOfType('fill')
    expect(fills).toHaveLength(9) // 8 个手柄 + 1 个框选填充
    expect(fills[fills.length - 1]?.style?.fillStyle).toBe(MARQUEE_FILL_COLOR)
  })

  it('框选矩形反向输入（min > max）时先规范化再绘制', () => {
    const ctx = new FakeContext2D()
    drawSelectionOverlay(
      ctx,
      { boundsScreen: null, marqueeScreen: { min: { x: 110, y: 120 }, max: { x: 10, y: 20 } } },
      SURFACE,
    )
    expectRoundRect(ctx.opsOfType('stroke')[0]?.path?.[0]?.args, {
      x: 11,
      y: 21,
      width: 98,
      height: 98,
      radius: SELECTION_BORDER_CORNER_RADIUS,
    })
  })

  it('旧环境没有 roundRect 时退化为直角矩形', () => {
    const ctx = new FakeContext2D()
    ctx.roundRect = undefined
    drawSelectionOverlay(ctx, { boundsScreen: BOUNDS, marqueeScreen: null, showHandles: true }, SURFACE)
    expect(ctx.opsOfType('roundRect')).toHaveLength(0)
    expect(ctx.opsOfType('rect')).toHaveLength(9)
  })
})

describe('渲染句柄接线（selection）', () => {
  const cleanups: Array<() => void> = []

  afterEach(() => {
    while (cleanups.length > 0) {
      cleanups.pop()?.()
    }
  })

  interface Harness {
    handle: BoardRenderHandle
    context: FakeContext2D
    sources: RenderSources
    advance(): number
  }

  function createHarness(): Harness {
    const clock = installFakeBrowserClock()
    cleanups.push(() => {
      clock.restore()
    })
    clock.setDevicePixelRatio(2)

    const context = new FakeContext2D()
    const canvas = new FakeCanvas({ clientWidth: 800, clientHeight: 600, context })
    const sources: RenderSources = {
      document: createDocument(),
      viewport: createViewport({ x: 800, y: 600 }),
      background: '#2e2f33',
      liveStroke: null,
      liveShape: null,
    }
    const handle = createBoardRenderHandle(canvas, sources)
    cleanups.push(() => {
      handle.dispose()
    })
    return { handle, context, sources, advance: () => clock.advance() }
  }

  it('覆盖层画在场景之后（场景以单位矩阵收尾，覆盖层随后按 DPR 放大）', () => {
    const { handle, context, advance } = createHarness()
    const overlay: SelectionOverlay = { boundsScreen: BOUNDS, marqueeScreen: null, showHandles: true }

    handle.setSelection(overlay)
    advance()

    const ops = context.ops
    // 场景的世界变换带平移（视口中心 400,300 → e/f = 800/600），
    // 覆盖层的 DIP 变换是纯缩放 (2,0,0,2,0,0)，可以据此定位覆盖层起点。
    const overlayTransformIndex = ops.findIndex(
      (op) => op.op === 'setTransform' && op.args?.join(',') === '2,0,0,2,0,0',
    )
    const backgroundIndex = ops.findIndex((op) => op.op === 'fillRect')
    const firstStrokeIndex = ops.findIndex((op) => op.op === 'stroke')
    expect(backgroundIndex).toBeGreaterThan(-1)
    expect(overlayTransformIndex).toBeGreaterThan(backgroundIndex)
    expect(firstStrokeIndex).toBeGreaterThan(overlayTransformIndex)
    expect(context.opsOfType('stroke')).toHaveLength(9)
  })

  it('setSelection(null) 后不再绘制覆盖层', () => {
    const { handle, context, advance } = createHarness()
    handle.setSelection({ boundsScreen: BOUNDS, marqueeScreen: null, showHandles: true })
    advance()
    expect(context.opsOfType('stroke')).toHaveLength(9)

    context.clearOps()
    handle.setSelection(null)
    advance()

    // 场景仍然绘制（背景 fillRect 至少一次），但没有任何手柄/选择框描边
    expect(context.opsOfType('fillRect').length).toBeGreaterThan(0)
    expect(context.opsOfType('stroke')).toHaveLength(0)
    expect(context.opsOfType('fill')).toHaveLength(0)
  })

  it('renderNow 也会绘制已设置的选择覆盖层', () => {
    const { handle, context, sources } = createHarness()
    // 直接写 sources 的等价路径：句柄创建时就能带上 selection
    sources.selection = { boundsScreen: BOUNDS, marqueeScreen: null, showHandles: true }
    handle.renderNow()
    expect(context.opsOfType('stroke')).toHaveLength(9)
  })

  it('dispose 后 setSelection 不再触发绘制', () => {
    const { handle, context, advance } = createHarness()
    handle.dispose()
    context.clearOps()
    handle.setSelection({ boundsScreen: BOUNDS, marqueeScreen: null, showHandles: true })
    advance()
    expect(context.ops).toHaveLength(0)
  })
})
