/**
 * 笔迹域模型（对应桌面 `WindBoard.Board.Stroke` 的持久化形态）。
 *
 * 数值约定（design D3）：所有几何与颜色分量都是 **float32** 语义，
 * 写入时由 `Math.fround` 收敛到单精度可表示值，避免 JSON 里出现 double 尾巴。
 */

/** float32 语义的数值（边界处已 `Math.fround`）。 */
export type F32 = number

/** 二维向量（对应 `System.Numerics.Vector2`，JSON 键为 `x` / `y`）。 */
export interface Vec2 {
  x: F32
  y: F32
}

/** 颜色（对应 `System.Numerics.Vector4`，JSON 键为 `x` / `y` / `z` / `w`）。 */
export interface ColorRgba {
  x: F32
  y: F32
  z: F32
  w: F32
}

/** 笔迹采样点（JSON 键为 `position` + `pressure`）。 */
export interface StrokePoint {
  x: F32
  y: F32
  pressure: F32
}

/** 折线笔迹的 kind 标识（桌面 `BoardInkItemCodec.StrokeKind`）。 */
export const STROKE_KIND = 'stroke'

/** 折线笔迹（对应 `StrokeSnapshot`，JSON 键为 `points` / `colorRgba` / `baseSize` / `enablePressure`）。 */
export interface Stroke {
  kind: typeof STROKE_KIND
  points: StrokePoint[]
  colorRgba: ColorRgba
  baseSize: F32
  enablePressure: boolean
}
