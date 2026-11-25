import { isShapeKind } from '../../board/model/shape.ts'
import type { Shape, ShapeKind } from '../../board/model/shape.ts'
import { STROKE_KIND } from '../../board/model/stroke.ts'
import type { ColorRgba, Stroke, StrokePoint, Vec2 } from '../../board/model/stroke.ts'
import type { InkItem } from '../../board/model/workspace.ts'
import { WbixError } from './errors.ts'
import type { JsonTextWriter } from './json-options.ts'
import { getJsonProperty, isPlainObject } from './json-options.ts'

/**
 * 笔迹条目编解码（对齐桌面 InkItemSnapshotJsonConverter 与 BoardInkItemCodec）。写侧固定 v3 包装形态：笔迹为 `{ "kind": "stroke", "stroke": { points, colorRgba, baseSize, enablePressure } }`，形状为 `{ "kind": "line", "shape": { start, end, colorRgba, width } }`。
 * 读侧形态自适应且与桌面同一套规则：有 stroke 对象按 v3 笔迹读，有 shape 对象按 v3 形状读，两者都没有则整个对象按 v1/v2 扁平 StrokeSnapshot 读；stroke 或 shape 为 null 或非对象属致命（桌面抛 JsonException，整页失败）；未知 kind 跳过单条（桌面 BoardInkItemCodec.ToItem 记 Warn 后返回 null）；形状 kind 缺 shape 数据或笔迹 kind 缺 stroke 数据属致命（桌面抛 ArgumentException）。
 */

const ZERO_VEC2: Vec2 = { x: 0, y: 0 }
const ZERO_VEC4: ColorRgba = { x: 0, y: 0, z: 0, w: 0 }

/** 解码结果：成功、或跳过（null 条目 / 未知 kind）。 */
export type InkItemDecodeResult =
  | {
      status: 'ok'
      item: InkItem
      /** 是否为 v1/v2 扁平形态（v3 文件里出现属于格式倒退，由调用方决定是否告警）。 */
      legacyFlat: boolean
    }
  | { status: 'skipped'; reason: 'null' | 'unknown-kind'; kind?: string }

function invalid(message: string): never {
  throw new WbixError('ink-item-invalid', message)
}

/** 读取 float32 字段（缺失取默认值；类型不符则致命）。 */
function readF32Field(
  obj: Record<string, unknown>,
  propertyName: string,
  fallback: number,
  path: string,
): number {
  const { found, value } = getJsonProperty(obj, propertyName)
  if (!found) {
    return fallback
  }

  if (typeof value !== 'number') {
    invalid(`${path}.${propertyName} 必须是数字`)
  }

  return Math.fround(value)
}

/** 读取布尔字段（缺失取默认值；类型不符则致命）。 */
function readBooleanField(
  obj: Record<string, unknown>,
  propertyName: string,
  fallback: boolean,
  path: string,
): boolean {
  const { found, value } = getJsonProperty(obj, propertyName)
  if (!found) {
    return fallback
  }

  if (typeof value !== 'boolean') {
    invalid(`${path}.${propertyName} 必须是布尔值`)
  }

  return value
}

/** 读取 Vector2 字段（缺失取默认值；null/非对象则致命）。 */
function readVec2Field(
  obj: Record<string, unknown>,
  propertyName: string,
  fallback: Vec2,
  path: string,
): Vec2 {
  const { found, value } = getJsonProperty(obj, propertyName)
  if (!found) {
    return fallback
  }

  if (!isPlainObject(value)) {
    invalid(`${path}.${propertyName} 必须是对象`)
  }

  const componentPath = `${path}.${propertyName}`
  return {
    x: readF32Field(value, 'x', 0, componentPath),
    y: readF32Field(value, 'y', 0, componentPath),
  }
}

/** 读取 Vector4 字段（缺失取默认值；null/非对象则致命）。 */
function readVec4Field(
  obj: Record<string, unknown>,
  propertyName: string,
  fallback: ColorRgba,
  path: string,
): ColorRgba {
  const { found, value } = getJsonProperty(obj, propertyName)
  if (!found) {
    return fallback
  }

  if (!isPlainObject(value)) {
    invalid(`${path}.${propertyName} 必须是对象`)
  }

  const componentPath = `${path}.${propertyName}`
  return {
    x: readF32Field(value, 'x', 0, componentPath),
    y: readF32Field(value, 'y', 0, componentPath),
    z: readF32Field(value, 'z', 0, componentPath),
    w: readF32Field(value, 'w', 0, componentPath),
  }
}

/** 读取 `points` 数组（缺失/null 致命；元素必须是对象，对齐 record struct）。 */
function readPointsField(obj: Record<string, unknown>, path: string): StrokePoint[] {
  const { found, value } = getJsonProperty(obj, 'points')
  if (!found || value === null) {
    invalid(`${path} 缺少 points 数据`)
  }

  if (!Array.isArray(value)) {
    invalid(`${path}.points 必须是数组`)
  }

  const points: StrokePoint[] = []
  for (let index = 0; index < value.length; index += 1) {
    const element = value[index]
    if (!isPlainObject(element)) {
      invalid(`${path}.points[${index}] 必须是对象`)
    }

    const pointPath = `${path}.points[${index}]`
    const position = readVec2Field(element, 'position', ZERO_VEC2, pointPath)
    points.push({
      x: position.x,
      y: position.y,
      pressure: readF32Field(element, 'pressure', 0, pointPath),
    })
  }

  return points
}

/** 读取扁平 `StrokeSnapshot`。 */
function readStrokeSnapshot(obj: Record<string, unknown>, path: string): Stroke {
  return {
    kind: STROKE_KIND,
    points: readPointsField(obj, path),
    colorRgba: readVec4Field(obj, 'colorRgba', ZERO_VEC4, path),
    baseSize: readF32Field(obj, 'baseSize', 0, path),
    enablePressure: readBooleanField(obj, 'enablePressure', false, path),
  }
}

/** 读取扁平 `ShapeSnapshot`（kind 来自外层包装）。 */
function readShapeSnapshot(obj: Record<string, unknown>, kind: ShapeKind, path: string): Shape {
  return {
    kind,
    start: readVec2Field(obj, 'start', ZERO_VEC2, path),
    end: readVec2Field(obj, 'end', ZERO_VEC2, path),
    colorRgba: readVec4Field(obj, 'colorRgba', ZERO_VEC4, path),
    width: readF32Field(obj, 'width', 0, path),
  }
}

/** kind 归一（对齐 `NormalizeKind`：空白 → `"stroke"`，否则 trim）。 */
function normalizeKind(kind: string | null): string {
  const trimmed = kind === null ? '' : kind.trim()
  return trimmed.length === 0 ? STROKE_KIND : trimmed
}

/** 数据形态（尚未按 kind 分发）。 */
type InkItemForm =
  | { form: 'stroke'; kind: string; stroke: Stroke }
  | { form: 'shape'; kind: string; shapeData: Record<string, unknown> }
  | { form: 'flat'; kind: string; stroke: Stroke }

/** 形态适配：只判断 v3 包装 / v1-v2 扁平，不做 kind 语义分发。 */
function readInkItemForm(value: Record<string, unknown>, path: string): InkItemForm {
  let hasKind = false
  let kindText: string | null = null
  let strokeValue: Record<string, unknown> | null = null
  let shapeValue: Record<string, unknown> | null = null

  for (const key of Object.keys(value)) {
    const lowerKey = key.toLowerCase()

    if (lowerKey === 'kind') {
      hasKind = true
      const kindCandidate = value[key]
      // 非字符串（含 null）不覆盖 kindText，交由缺省归一处理。
      if (typeof kindCandidate === 'string') {
        kindText = kindCandidate
      }
      continue
    }

    if (lowerKey === 'stroke') {
      if (!isPlainObject(value[key])) {
        invalid(`${path}.stroke 必须是对象`)
      }
      strokeValue = value[key]
      continue
    }

    if (lowerKey === 'shape') {
      if (!isPlainObject(value[key])) {
        invalid(`${path}.shape 必须是对象`)
      }
      shapeValue = value[key]
    }
  }

  if (strokeValue !== null) {
    return {
      form: 'stroke',
      kind: normalizeKind(hasKind ? kindText : null),
      stroke: readStrokeSnapshot(strokeValue, `${path}.stroke`),
    }
  }

  if (shapeValue !== null) {
    return {
      // 形状分支不归一 kind：缺失给空串，由分发逻辑判定（与桌面一致）。
      form: 'shape',
      kind: hasKind && kindText !== null ? kindText : '',
      shapeData: shapeValue,
    }
  }

  return {
    form: 'flat',
    kind: normalizeKind(hasKind ? kindText : null),
    stroke: readStrokeSnapshot(value, path),
  }
}

/**
 * 解码单个笔迹条目。
 *
 * @throws WbixError `ink-item-invalid` 结构非法（桌面侧会让整页解析失败）
 */
export function decodeInkItem(value: unknown): InkItemDecodeResult {
  if (value === null) {
    return { status: 'skipped', reason: 'null' }
  }

  if (!isPlainObject(value)) {
    invalid('笔迹条目必须是 JSON 对象')
  }

  const decoded = readInkItemForm(value, 'ink')
  const kind = decoded.kind.trim()
  const lowerKind = kind.toLowerCase()

  if (isShapeKind(lowerKind)) {
    if (decoded.form !== 'shape') {
      // 形状 kind 却带 stroke 包装/扁平数据：桌面 Codec 抛 ArgumentException。
      invalid(`kind='${kind}' 的条目缺少 shape 数据`)
    }
    return {
      status: 'ok',
      item: readShapeSnapshot(decoded.shapeData, lowerKind, 'ink.shape'),
      legacyFlat: false,
    }
  }

  if (lowerKind === STROKE_KIND) {
    if (decoded.form === 'shape') {
      invalid('kind=stroke 的条目缺少 stroke 数据')
    }
    return { status: 'ok', item: decoded.stroke, legacyFlat: decoded.form === 'flat' }
  }

  return { status: 'skipped', reason: 'unknown-kind', kind }
}

/** 写单个笔迹条目（固定 v3 包装形态）。 */
export function writeInkItem(writer: JsonTextWriter, item: InkItem): void {
  if (item.kind === STROKE_KIND) {
    writer.openObject()
    writer.stringProperty('kind', STROKE_KIND)
    writer.name('stroke')
    writer.openObject()

    writer.name('points')
    writer.openArray()
    for (const point of item.points) {
      writer.openObject()
      writer.name('position')
      writeVec2(writer, { x: point.x, y: point.y })
      writer.f32Property('pressure', point.pressure)
      writer.closeObject()
    }
    writer.closeArray()

    writer.name('colorRgba')
    writeColor(writer, item.colorRgba)
    writer.f32Property('baseSize', item.baseSize)
    writer.booleanProperty('enablePressure', item.enablePressure)

    writer.closeObject()
    writer.closeObject()
    return
  }

  if (!isShapeKind(item.kind)) {
    // 未知 kind 写入会让桌面读侧丢数据，属于编程错误，宁可写失败。
    throw new WbixError('invalid-value', `无法写出未知 kind 的笔迹条目：'${String(item.kind)}'`)
  }

  writer.openObject()
  writer.stringProperty('kind', item.kind)
  writer.name('shape')
  writer.openObject()
  writer.name('start')
  writeVec2(writer, item.start)
  writer.name('end')
  writeVec2(writer, item.end)
  writer.name('colorRgba')
  writeColor(writer, item.colorRgba)
  writer.f32Property('width', item.width)
  writer.closeObject()
  writer.closeObject()
}

function writeVec2(writer: JsonTextWriter, value: Vec2): void {
  writer.openObject()
  writer.f32Property('x', value.x)
  writer.f32Property('y', value.y)
  writer.closeObject()
}

function writeColor(writer: JsonTextWriter, value: ColorRgba): void {
  writer.openObject()
  writer.f32Property('x', value.x)
  writer.f32Property('y', value.y)
  writer.f32Property('z', value.z)
  writer.f32Property('w', value.w)
  writer.closeObject()
}
