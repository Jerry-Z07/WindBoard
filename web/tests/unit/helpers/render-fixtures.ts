/** 渲染测试用的域对象工厂（笔迹 / 形状 / 元素 / 文档 / 视口）。 */

import { BoardDocument } from '../../../src/board/document.ts'
import type { BoardElement } from '../../../src/board/model/element.ts'
import type { Shape } from '../../../src/board/model/shape.ts'
import type { ColorRgba, Stroke, Vec2 } from '../../../src/board/model/stroke.ts'
import type { InkItem } from '../../../src/board/model/workspace.ts'
import { BoardViewport } from '../../../src/board/viewport.ts'

/** 不透明红色（笔迹 / 形状默认色）。 */
export const RED: ColorRgba = { x: 1, y: 0, z: 0, w: 1 }

/** 不透明绿色。 */
export const GREEN: ColorRgba = { x: 0, y: 1, z: 0, w: 1 }

/** 笔迹点输入。 */
export interface StrokePointInput {
  x: number
  y: number
  pressure?: number
}

/** 创建笔迹（默认 baseSize 4、不启用压感）。 */
export function createStroke(
  points: readonly StrokePointInput[],
  options: Partial<Stroke> = {},
): Stroke {
  return {
    kind: 'stroke',
    points: points.map((point) => ({ x: point.x, y: point.y, pressure: point.pressure ?? 0.5 })),
    colorRgba: RED,
    baseSize: 4,
    enablePressure: false,
    ...options,
  }
}

/** 创建形状。 */
export function createShape(shape: Shape): Shape {
  return shape
}

/** 创建元素（未给出的字段使用合理默认值）。 */
export function createElement(
  overrides: Partial<BoardElement> & Pick<BoardElement, 'id'>,
): BoardElement {
  return {
    layer: 'belowInk',
    order: 0,
    positionWorld: { x: 0, y: 0 },
    sizeWorld: { x: 320, y: 180 },
    type: 'text',
    ...overrides,
  }
}

/** 创建文档（直接塞入条目与元素，跳过命令层）。 */
export function createDocument(
  items: readonly InkItem[] = [],
  elements: readonly BoardElement[] = [],
): BoardDocument {
  const document = new BoardDocument()
  document.items.push(...items)
  document.elements.push(...elements)
  return document
}

/** 创建视口（默认 800×600、相机原点、缩放 1）。 */
export function createViewport(
  size: Vec2 = { x: 800, y: 600 },
  cameraWorld: Vec2 = { x: 0, y: 0 },
  zoom = 1,
): BoardViewport {
  const viewport = new BoardViewport()
  viewport.updateViewportSize(size)
  viewport.setView(cameraWorld, zoom)
  return viewport
}
