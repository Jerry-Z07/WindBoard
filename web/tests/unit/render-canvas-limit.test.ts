import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  MAX_CANVAS_AREA_PIXELS,
  MAX_DEVICE_PIXEL_RATIO,
  isLikelyIos,
  resolveCanvasDevicePixelRatio,
  resolveDevicePixelRatio,
} from '../../src/render/render-handle.ts'

/**
 * iOS 画布面积上限（P5.3）：WebKit 对单张 canvas 的像素总数有硬限制，
 * 超过后画布直接空白且不报错。这里验证上限只在 iOS 生效、且不放大 DPR。
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
  it('非 iOS 平台完全沿用原有 DPR 规则', () => {
    stubNavigator({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', platform: 'Win32' })
    // 这个尺寸在 iOS 上会被夹，但 Windows 上不该受影响
    expect(resolveCanvasDevicePixelRatio(2, { x: 3000, y: 3000 })).toBe(
      MAX_DEVICE_PIXEL_RATIO,
    )
    expect(resolveCanvasDevicePixelRatio(1.5, { x: 3000, y: 3000 })).toBe(1.5)
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
