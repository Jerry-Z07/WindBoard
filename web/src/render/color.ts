/** 渲染层颜色工具：把域模型的 0~1 浮点颜色转成 Canvas2D 可用的 CSS 文本。 */

import type { ColorRgba } from '../board/model/stroke.ts'

/**
 * ColorRgba（各分量 0~1，对应桌面 Color4）→ rgba(r, g, b, a) 文本。与桌面一致使用非预乘 alpha（Color4 语义），Canvas2D 的 rgba() 同样是非预乘；越界值按 0~1 收敛，避免损坏数据让画布整体失效。
 */
export function colorToCss(color: ColorRgba): string {
  return `rgba(${toByte(color.x)}, ${toByte(color.y)}, ${toByte(color.z)}, ${formatAlpha(color.w)})`
}

/** 0~1 浮点 → 0~255 整数（四舍五入，与桌面 D2D 的字节量化一致）。 */
function toByte(value: number): number {
  if (!Number.isFinite(value)) {
    return 0
  }
  return Math.round(Math.min(1, Math.max(0, value)) * 255)
}

/** alpha 保留原始精度（`1` / `0.92`），便于测试与桌面快照逐值比对。 */
function formatAlpha(value: number): string {
  if (!Number.isFinite(value)) {
    return '1'
  }
  return String(Math.min(1, Math.max(0, value)))
}
