/**
 * 记录型假 2D 上下文（渲染层单测用）。
 *
 * 不引入 canvas / jsdom 依赖：这里只记录渲染层实际调用的操作
 * （路径命令、填充/描边、文本、变换、状态栈），足以断言绘制顺序与几何参数。
 */

import type { RenderContext2D } from '../../../src/render/canvas-types.ts'

/** 变换矩阵快照。 */
export interface FakeTransform {
  a: number
  b: number
  c: number
  d: number
  e: number
  f: number
}

/** 路径命令（参数按 Canvas2D 同名方法的顺序记录）。 */
export type FakePathCommand =
  | { op: 'moveTo'; args: [number, number] }
  | { op: 'lineTo'; args: [number, number] }
  | { op: 'rect'; args: [number, number, number, number] }
  | { op: 'arc'; args: [number, number, number, number, number] }
  | { op: 'ellipse'; args: [number, number, number, number, number, number, number] }
  | { op: 'roundRect'; args: [number, number, number, number, number] }
  | { op: 'closePath'; args: [] }

/** 一条操作记录。 */
export interface FakeOp {
  /** 操作名（与 Canvas2D 方法同名）。 */
  readonly op: string
  /** 数值参数。 */
  readonly args?: readonly number[]
  /** 绘制时刻的路径副本（`fill` / `stroke` / `clip`）。 */
  readonly path?: readonly FakePathCommand[]
  /** 绘制时刻的关键样式快照。 */
  readonly style?: Readonly<Record<string, string | number>>
  /** 文本（`fillText`）。 */
  readonly text?: string
  /** 绘制时刻的变换。 */
  readonly transform?: FakeTransform
}

/** 保存/恢复用的样式快照。 */
interface FakeStyleState {
  fillStyle: string | object
  strokeStyle: string | object
  lineWidth: number
  lineCap: 'butt' | 'round' | 'square'
  lineJoin: 'bevel' | 'round' | 'miter'
  miterLimit: number
  font: string
  textAlign: 'center' | 'end' | 'left' | 'right' | 'start'
  textBaseline: 'alphabetic' | 'bottom' | 'hanging' | 'ideographic' | 'middle' | 'top'
  transform: FakeTransform
}

/** 记录型假上下文。 */
export class FakeContext2D implements RenderContext2D {
  fillStyle: string | object = ''
  strokeStyle: string | object = ''
  lineWidth = 1
  lineCap: 'butt' | 'round' | 'square' = 'butt'
  lineJoin: 'bevel' | 'round' | 'miter' = 'miter'
  miterLimit = 10
  font = '10px sans-serif'
  textAlign: 'center' | 'end' | 'left' | 'right' | 'start' = 'start'
  textBaseline: 'alphabetic' | 'bottom' | 'hanging' | 'ideographic' | 'middle' | 'top' = 'alphabetic'

  /** 圆角矩形实现；置为 `undefined` 可模拟不支持 `roundRect` 的旧环境。 */
  roundRect:
    | ((x: number, y: number, width: number, height: number, radii?: number | number[]) => void)
    | undefined

  /** 全部操作（按调用顺序）。 */
  readonly ops: FakeOp[] = []

  private _path: FakePathCommand[] = []
  private _transform: FakeTransform = { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 }
  private readonly _saved: FakeStyleState[] = []

  constructor() {
    this.roundRect = (x, y, width, height, radii) => {
      const radius = typeof radii === 'number' ? radii : 0
      this._path.push({ op: 'roundRect', args: [x, y, width, height, radius] })
      this.ops.push({ op: 'roundRect', args: [x, y, width, height, radius] })
    }
  }

  /** 当前路径（最近一次 `beginPath` 之后的命令）。 */
  get path(): readonly FakePathCommand[] {
    return this._path
  }

  /** 当前变换。 */
  get transform(): FakeTransform {
    return this._transform
  }

  /** 状态栈深度（`save` 与 `restore` 的差）。 */
  get saveDepth(): number {
    return this._saved.length
  }

  setTransform(a: number, b: number, c: number, d: number, e: number, f: number): void {
    this._transform = { a, b, c, d, e, f }
    this.ops.push({ op: 'setTransform', args: [a, b, c, d, e, f] })
  }

  save(): void {
    this._saved.push(this.snapshotStyle())
    this.ops.push({ op: 'save' })
  }

  restore(): void {
    const state = this._saved.pop()
    if (state === undefined) {
      return
    }
    this.fillStyle = state.fillStyle
    this.strokeStyle = state.strokeStyle
    this.lineWidth = state.lineWidth
    this.lineCap = state.lineCap
    this.lineJoin = state.lineJoin
    this.miterLimit = state.miterLimit
    this.font = state.font
    this.textAlign = state.textAlign
    this.textBaseline = state.textBaseline
    this._transform = { ...state.transform }
    this.ops.push({ op: 'restore' })
  }

  beginPath(): void {
    this._path = []
    this.ops.push({ op: 'beginPath' })
  }

  closePath(): void {
    this._path.push({ op: 'closePath', args: [] })
    this.ops.push({ op: 'closePath' })
  }

  moveTo(x: number, y: number): void {
    this._path.push({ op: 'moveTo', args: [x, y] })
    this.ops.push({ op: 'moveTo', args: [x, y] })
  }

  lineTo(x: number, y: number): void {
    this._path.push({ op: 'lineTo', args: [x, y] })
    this.ops.push({ op: 'lineTo', args: [x, y] })
  }

  rect(x: number, y: number, width: number, height: number): void {
    this._path.push({ op: 'rect', args: [x, y, width, height] })
    this.ops.push({ op: 'rect', args: [x, y, width, height] })
  }

  arc(
    x: number,
    y: number,
    radius: number,
    startAngle: number,
    endAngle: number,
    _counterclockwise?: boolean,
  ): void {
    this._path.push({ op: 'arc', args: [x, y, radius, startAngle, endAngle] })
    this.ops.push({ op: 'arc', args: [x, y, radius] })
  }

  ellipse(
    x: number,
    y: number,
    radiusX: number,
    radiusY: number,
    rotation: number,
    startAngle: number,
    endAngle: number,
    _counterclockwise?: boolean,
  ): void {
    this._path.push({ op: 'ellipse', args: [x, y, radiusX, radiusY, rotation, startAngle, endAngle] })
    this.ops.push({ op: 'ellipse', args: [x, y, radiusX, radiusY] })
  }

  fill(): void {
    this.ops.push({
      op: 'fill',
      path: copyPath(this._path),
      style: { fillStyle: String(this.fillStyle) },
      transform: { ...this._transform },
    })
  }

  stroke(): void {
    this.ops.push({
      op: 'stroke',
      path: copyPath(this._path),
      style: {
        strokeStyle: String(this.strokeStyle),
        lineWidth: this.lineWidth,
        lineCap: this.lineCap,
        lineJoin: this.lineJoin,
        miterLimit: this.miterLimit,
      },
      transform: { ...this._transform },
    })
  }

  clip(): void {
    this.ops.push({ op: 'clip', path: copyPath(this._path) })
  }

  fillRect(x: number, y: number, width: number, height: number): void {
    this.ops.push({
      op: 'fillRect',
      args: [x, y, width, height],
      style: { fillStyle: String(this.fillStyle) },
    })
  }

  fillText(text: string, x: number, y: number): void {
    this.ops.push({
      op: 'fillText',
      text,
      args: [x, y],
      style: {
        font: this.font,
        fillStyle: String(this.fillStyle),
        textAlign: this.textAlign,
        textBaseline: this.textBaseline,
      },
      transform: { ...this._transform },
    })
  }

  /** 清空已记录的操作（便于“再渲染一帧后对比”）。 */
  clearOps(): void {
    this.ops.length = 0
  }

  /** 取指定操作类型的记录（保持调用顺序）。 */
  opsOfType(type: string): FakeOp[] {
    return this.ops.filter((entry) => entry.op === type)
  }

  /** 取最近一次指定操作。 */
  lastOp(type: string): FakeOp | undefined {
    for (let index = this.ops.length - 1; index >= 0; index -= 1) {
      if (this.ops[index].op === type) {
        return this.ops[index]
      }
    }
    return undefined
  }

  private snapshotStyle(): FakeStyleState {
    return {
      fillStyle: this.fillStyle,
      strokeStyle: this.strokeStyle,
      lineWidth: this.lineWidth,
      lineCap: this.lineCap,
      lineJoin: this.lineJoin,
      miterLimit: this.miterLimit,
      font: this.font,
      textAlign: this.textAlign,
      textBaseline: this.textBaseline,
      transform: { ...this._transform },
    }
  }
}

/** 深拷贝路径（`fill` / `stroke` 记录时刻的快照，之后路径可能继续变化）。 */
function copyPath(path: readonly FakePathCommand[]): FakePathCommand[] {
  return path.map((command) => ({ ...command, args: [...command.args] }) as FakePathCommand)
}

/** 提取路径中的多边形顶点（`moveTo` / `lineTo`），用于断言笔迹轮廓几何。 */
export function collectPolygonPoints(
  path: readonly FakePathCommand[],
): Array<{ x: number; y: number }> {
  const points: Array<{ x: number; y: number }> = []
  for (const command of path) {
    if (command.op === 'moveTo' || command.op === 'lineTo') {
      points.push({ x: command.args[0], y: command.args[1] })
    }
  }
  return points
}
