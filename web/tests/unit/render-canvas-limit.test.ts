import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  MAX_CANVAS_AREA_PIXELS,
  MAX_CANVAS_BACKING_AREA_PIXELS,
  MAX_CANVAS_BACKING_DIMENSION,
  isLikelyIos,
  resolveCanvasDevicePixelRatio,
  resolveDevicePixelRatio,
} from '../../src/render/render-handle.ts'

/**
 * 后备存储上限：DPR 不再有固定上限（固定上限会让高 DPI 屏幕的画布被浏览器放大，
 * 笔迹发糊），改成按 Canvas2D 的实际能力（单边 16384 / 面积）反算。
 * iOS 另有更小的面积硬限制（16.7M），越过之后画布直接空白且不报错。
 */

function stubNavigator(value: unknown): void {
  vi.stubGlobal('navigator', value)
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('isLikelyIos', () => {
  it('识别 iPhone / iPad / iPod 的 UA', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)' })
    expect(isLikelyIos()).toBe(true)
    stubNavigator({ userAgent: 'Mozilla/5.0 (iPad; CPU OS 16_6 like Mac OS X)' })
    expect(isLikelyIos()).toBe(true)
  })

  it('iPadOS 13 起的桌面版 UA 靠触摸点数识别', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', platform: 'MacIntel', maxTouchPoints: 5 })
    expect(isLikelyIos()).toBe(true)
  })

  it('普通 Mac / Windows 不算 iOS', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', platform: 'MacIntel', maxTouchPoints: 0 })
    expect(isLikelyIos()).toBe(false)
    stubNavigator({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', platform: 'Win32', maxTouchPoints: 10 })
    expect(isLikelyIos()).toBe(false)
  })

  it('没有 navigator 时返回 false（node 环境）', () => {
    stubNavigator(undefined)
    expect(isLikelyIos()).toBe(false)
  })
})

describe('resolveCanvasDevicePixelRatio', () => {
  it('常规窗口不夹取 DPR：DPR 3 照原样使用', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', platform: 'Win32' })
    // 1920×1080 × 3² = 18.7M 像素，远低于桌面面积上限
    expect(resolveCanvasDevicePixelRatio(3, { x: 1920, y: 1080 })).toBe(3)
    expect(resolveCanvasDevicePixelRatio(2.5, { x: 1512, y: 982 })).toBe(2.5)
  })

  it('非 iOS 平台只受单边与面积上限约束', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', platform: 'Win32' })
    // 3000×3000 × 2² = 36M < 67.1M，不受（更小的）iOS 面积上限影响
    expect(resolveCanvasDevicePixelRatio(2, { x: 3000, y: 3000 })).toBe(2)
    const huge = resolveCanvasDevicePixelRatio(3, { x: 8000, y: 8000 })
    expect(8000 * huge).toBeLessThanOrEqual(MAX_CANVAS_BACKING_DIMENSION + 1)
    expect(huge * huge * 8000 * 8000).toBeLessThanOrEqual(MAX_CANVAS_BACKING_AREA_PIXELS + 1)
  })

  it('单边超过 16384 时按边长反算 DPR', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', platform: 'Win32' })
    // 细长窗口：面积不超但单边超了
    const sizeDip = { x: 9000, y: 400 }
    const dpr = resolveCanvasDevicePixelRatio(3, sizeDip)
    expect(sizeDip.x * dpr).toBeLessThanOrEqual(MAX_CANVAS_BACKING_DIMENSION + 1)
    expect(dpr).toBeCloseTo(MAX_CANVAS_BACKING_DIMENSION / 9000, 6)
  })

  it('iOS 上未超面积上限时保持原 DPR', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)' })
    // 1366×1024 × 2² ≈ 5.6M 像素，远低于 16.7M
    expect(resolveCanvasDevicePixelRatio(2, { x: 1366, y: 1024 })).toBe(2)
  })

  it('iOS 上超出面积上限时按面积反算 DPR', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)' })
    const sizeDip = { x: 3000, y: 3000 }
    const dpr = resolveCanvasDevicePixelRatio(2, sizeDip)
    const area = sizeDip.x * sizeDip.y * dpr * dpr
    expect(area).toBeLessThanOrEqual(MAX_CANVAS_AREA_PIXELS + 1)
    expect(dpr).toBeLessThan(2)
    // 反算值就是 sqrt(上限 / DIP 面积)
    expect(dpr).toBeCloseTo(Math.sqrt(MAX_CANVAS_AREA_PIXELS / (3000 * 3000)), 6)
  })

  it('iOS 上夹取结果不低于 1（画面可能变糊，但不能退化成 0 倍）', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)' })
    // 极端尺寸：即便降到 DPR 1 也会超过上限，此时就停在 1
    const dpr = resolveCanvasDevicePixelRatio(3, { x: 8000, y: 8000 })
    expect(dpr).toBe(1)
  })

  it('退回原始 DPR 上限：不用面积换算把小于 1 的值放大', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)' })
    expect(resolveCanvasDevicePixelRatio(0.8, { x: 100, y: 100 })).toBe(0.8)
    expect(resolveDevicePixelRatio(0.8)).toBe(0.8)
  })

  it('尺寸未知（0 或非有限值）时不做面积夹取', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)' })
    expect(resolveCanvasDevicePixelRatio(2, { x: 0, y: 0 })).toBe(2)
    expect(resolveCanvasDevicePixelRatio(2, { x: Number.NaN, y: 100 })).toBe(2)
  })
})
