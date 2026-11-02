import type { ColorRgba, F32, Vec2 } from './stroke.ts'

/**
 * 两点式形状域模型（对应桌面 `WindBoard.Board.Items.BoardShape`，design E）。
 *
 * 说明：矩形 / 椭圆按**对角点**存储（`start` / `end`），读侧负责规范化为 Min/Max；
 * `width` 是世界坐标线宽，形状不支持压感。
 */

/** 形状 kind 集合（桌面 `BoardInkItemCodec` 的 line/rect/ellipse/arrow）。 */
export const SHAPE_KINDS = ['line', 'rect', 'ellipse', 'arrow'] as const

/** 形状类型标识。 */
export type ShapeKind = (typeof SHAPE_KINDS)[number]

/** 是否为合法形状 kind。 */
export function isShapeKind(value: unknown): value is ShapeKind {
  return typeof value === 'string' && (SHAPE_KINDS as readonly string[]).includes(value)
}

/** 形状（对应 `ShapeSnapshot`，JSON 键为 `start` / `end` / `colorRgba` / `width`）。 */
export interface Shape {
  kind: ShapeKind
  start: Vec2
  end: Vec2
  colorRgba: ColorRgba
  width: F32
}
