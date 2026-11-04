/**
 * 设备像素网格对齐的单测。
 *
 * 背景（浏览器实测）：DPR 1.25 / 1.5 这类**小数缩放**下，浏览器会把整张画布合成层
 * 重采样一次——后备存储按四舍五入的整数 CSS 尺寸算会与布局盒差 1 行（约 0.1% 缩放），
 * 画布原点落在半设备像素上时更是直接双线性插值，边缘从 1 个设备像素的过渡变成 2 个。
 * 桌面端 D2D 直绘在设备像素上，没有这两级重采样，表现就是「网页端笔迹更糊」。
 *
 * 这里锁住对齐算法：后备存储取整设备像素、CSS 尺寸 = 设备像素 / DPR、
 * 原点位移 = 把设备坐标凑成整数所需的补偿，且连续多帧稳定。
 */

import { afterEach, describe, expect, it } from 'vitest'

import {
  computeDeviceGridAlignment,
  createBoardRenderHandle,
  snapToDevicePixel,
  type BoardRenderHandle,
  type RenderSources,
} from '../../src/render/render-handle.ts'
import { installFakeBrowserClock, type FakeBrowserClock } from './helpers/fake-browser.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'
import { createDocument, createViewport } from './helpers/render-fixtures.ts'

const cleanups: Array<() => void> = []

afterEach(() => {
  while (cleanups.length > 0) {
    cleanups.pop()?.()
  }
})

interface Harness {
  handle: BoardRenderHandle
  canvas: FakeCanvas
  clock: FakeBrowserClock
  sources: RenderSources
}

interface HarnessOptions {
  dpr?: number
  clientWidth?: number
  clientHeight?: number
  rectWidth?: number
  rectHeight?: number
  rectTop?: number
}

/** 造一个「假画布 + 假浏览器时钟 + 渲染句柄」夹具；布局矩形可单独指定小数尺寸。 */
function createHarness(options: HarnessOptions = {}): Harness {
  const clock = installFakeBrowserClock()
  cleanups.push(() => {
    clock.restore()
  })
  if (options.dpr !== undefined) {
    clock.setDevicePixelRatio(options.dpr)
  }
  const canvas = new FakeCanvas({
    clientWidth: options.clientWidth ?? 800,
    clientHeight: options.clientHeight ?? 600,
  })
  if (options.rectWidth !== undefined) {
    canvas.rectWidth = options.rectWidth
  }
  if (options.rectHeight !== undefined) {
    canvas.rectHeight = options.rectHeight
  }
  if (options.rectTop !== undefined) {
    canvas.rectTop = options.rectTop
  }
  const viewport = createViewport({ x: canvas.clientWidth, y: canvas.clientHeight })
  const sources: RenderSources = {
    document: createDocument(),
    viewport,
    background: '',
    liveStroke: null,
    liveShape: null,
  }
  const handle = createBoardRenderHandle(canvas, sources)
  cleanups.push(() => {
    handle.dispose()
  })
  return { handle, canvas, clock, sources }
}

describe('snapToDevicePixel', () => {
  it('整数设备位置不需要位移', () => {
    expect(snapToDevicePixel(0, 1)).toBe(0)
    expect(snapToDevicePixel(66, 1.5)).toBe(0)
    expect(snapToDevicePixel(41.6, 1.25)).toBe(0)
  })

  it('小数设备位置给出把坐标凑成整数所需的补偿（绝对值不超过半个设备像素）', () => {
    // 33 CSS × 1.5 = 49.5 设备像素 → 补 0.5 设备像素 = 1/3 CSS 像素
    expect(snapToDevicePixel(33, 1.5)).toBeCloseTo(1 / 3, 12)
    // 33 CSS × 1.25 = 41.25 设备像素 → 就近取整到 41，回补 -0.25 设备像素 = -0.2 CSS 像素
    expect(snapToDevicePixel(33, 1.25)).toBeCloseTo(-0.2, 12)
    for (const [offset, ratio] of [
      [33, 1.5],
      [33, 1.25],
      [12.5, 1.75],
      [0.3, 2.5],
    ] as const) {
      const shift = snapToDevicePixel(offset, ratio)
      const device = offset * ratio + shift * ratio
      expect(Math.abs(device - Math.round(device))).toBeLessThan(1e-9)
      expect(Math.abs(shift * ratio)).toBeLessThanOrEqual(ratio / 2 + 1e-9)
    }
  })

  it('非法输入不产生位移', () => {
    expect(snapToDevicePixel(Number.NaN, 2)).toBe(0)
    expect(snapToDevicePixel(33, 0)).toBe(0)
    expect(snapToDevicePixel(33, Number.POSITIVE_INFINITY)).toBe(0)
  })
})

describe('computeDeviceGridAlignment', () => {
  it('DPR 1 下尺寸与布局盒一致、无需位移', () => {
    const alignment = computeDeviceGridAlignment({ left: 0, top: 33, width: 800, height: 600 }, 1)
    expect(alignment.backing).toEqual({ width: 800, height: 600 })
    expect(alignment.widthDip).toBe(800)
    expect(alignment.heightDip).toBe(600)
    expect(alignment.translateDip).toEqual({ x: 0, y: 0 })
  })

  it('DPR 1.5 下把小数布局盒抬到整设备像素', () => {
    // 真实读数：应用窗口 1100×780、DSF 1.5 时画布布局盒 1086×710.6667、顶边 32.66667
    const alignment = computeDeviceGridAlignment(
      { left: 0, top: 32.66666793823242, width: 1086, height: 710.6666870117188 },
      1.5,
    )
    // 710.6667 × 1.5 = 1066.00003 → 就近取整 1066，与真实设备行数一致
    expect(alignment.backing).toEqual({ width: 1629, height: 1066 })
    expect(alignment.widthDip * 1.5).toBeCloseTo(1629, 9)
    expect(alignment.heightDip * 1.5).toBeCloseTo(1066, 9)
    // 原点已经落在整设备像素附近（32.6667 × 1.5 ≈ 49），只需极小的微调
    expect(Math.abs(alignment.translateDip.y)).toBeLessThan(1e-4)
  })

  it('半设备像素原点给出半个设备像素的补偿', () => {
    const alignment = computeDeviceGridAlignment({ left: 0, top: 33, width: 1000, height: 666 }, 1.5)
    expect(alignment.backing).toEqual({ width: 1500, height: 999 })
    expect(alignment.translateDip.y * 1.5).toBeCloseTo(0.5, 12)
    expect(alignment.translateDip.x).toBe(0)
  })

  it('后备存储与布局盒的设备像素数相差不超过半像素，且 CSS 尺寸乘回 DPR 等于设备像素', () => {
    for (const ratio of [1, 1.25, 1.5, 2, 2.5, 3]) {
      const alignment = computeDeviceGridAlignment(
        { left: 12.3, top: 7.7, width: 1000.4, height: 666.6 },
        ratio,
      )
      expect(Math.abs(alignment.backing.width - 1000.4 * ratio)).toBeLessThanOrEqual(ratio / 2)
      expect(Math.abs(alignment.backing.height - 666.6 * ratio)).toBeLessThanOrEqual(ratio / 2)
      expect(alignment.widthDip * ratio).toBeCloseTo(alignment.backing.width, 9)
      expect(alignment.heightDip * ratio).toBeCloseTo(alignment.backing.height, 9)
    }
  })

  it('尺寸换算是幂等的（把结果写回布局盒再算一次不会变大）', () => {
    for (const ratio of [1, 1.25, 1.5, 1.732, 2.5]) {
      const first = computeDeviceGridAlignment(
        { left: 0, top: 0, width: 1086, height: 710.6666870117188 },
        ratio,
      )
      const second = computeDeviceGridAlignment(
        { left: 0, top: 0, width: first.widthDip, height: first.heightDip },
        ratio,
      )
      expect(second.backing).toEqual(first.backing)
    }
  })
})

describe('渲染句柄的设备像素对齐', () => {
  it('按小数布局矩形写后备存储、内联尺寸与位移', () => {
    const { handle, canvas } = createHarness({
      dpr: 1.5,
      clientWidth: 1000,
      clientHeight: 667,
      rectWidth: 1000,
      rectHeight: 666.6666870117188,
      rectTop: 33,
    })
    handle.renderNow()

    expect(canvas.width).toBe(1500)
    // 666.6667 × 1.5 = 1000.00003 → 就近取整 1000（与真实设备行数一致）
    expect(canvas.height).toBe(1000)
    expect(canvas.style.width).toBe('1000px')
    expect(canvas.style.height).toBe('666.6666666666666px')
    // 顶边 33 CSS × 1.5 = 49.5 设备像素 → 向下补 0.5 设备像素（= 1/3 CSS）
    expect(canvas.style.transform).toBe('translate(0px, 0.3333333333333333px)')
  })

  it('连续多帧结果稳定（不会因为读到自己写入的位移而反复横跳）', () => {
    const { handle, canvas } = createHarness({
      dpr: 1.5,
      clientWidth: 1000,
      clientHeight: 667,
      rectWidth: 1000,
      rectHeight: 666.6666870117188,
      rectTop: 33,
    })
    handle.renderNow()
    const first = { ...canvas.style }
    // 浏览器里位移会体现在下一帧的 getBoundingClientRect 上，这里照样模拟
    canvas.rectTop += 1 / 3
    handle.renderNow()
    handle.renderNow()
    expect({ ...canvas.style }).toEqual(first)
  })

  it('视口尺寸取对齐后的 CSS 尺寸（= 设备像素 / DPR）', () => {
    const { handle, sources } = createHarness({
      dpr: 1,
      clientWidth: 1000,
      clientHeight: 666,
      rectWidth: 1000.4,
      rectHeight: 666.6,
    })
    handle.renderNow()
    // 1000.4 → 就近取整 1000 设备像素；666.6 → 667
    expect(sources.viewport.sizeDip.x).toBe(1000)
    expect(sources.viewport.sizeDip.y).toBe(667)
  })

  it('DPR 1 下内联样式仍被写入，但不产生位移', () => {
    const { handle, canvas } = createHarness({
      dpr: 1,
      clientWidth: 800,
      clientHeight: 600,
      rectTop: 33,
    })
    handle.renderNow()
    expect(canvas.style.width).toBe('800px')
    expect(canvas.style.transform).toBe('translate(0px, 0px)')
  })

  it('画布尚未布局（尺寸为 0）时沿用视口尺寸，不把视口压成 1×1', () => {
    const { handle, canvas, sources } = createHarness({
      clientWidth: 0,
      clientHeight: 0,
      rectWidth: 0,
      rectHeight: 0,
    })
    sources.viewport.updateViewportSize({ x: 900, y: 700 })
    handle.renderNow()
    expect(sources.viewport.sizeDip.x).toBe(900)
    expect(canvas.width).toBe(900)
  })
})
