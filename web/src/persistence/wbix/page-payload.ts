import { DEFAULT_ELEMENT_SIZE } from '../../board/model/element.ts'
import type { BoardElement, Layer, MediaKind } from '../../board/model/element.ts'
import { parseMediaKind } from '../../board/model/element.ts'
import { formatGuid, isGuid, newGuid, parseGuid } from '../../board/model/ids.ts'
import type { Guid } from '../../board/model/ids.ts'
import type { Vec2 } from '../../board/model/stroke.ts'
import type { PageSnapshot } from '../../board/model/workspace.ts'
import { WbixError } from './errors.ts'
import type { WbixWarningCode } from './errors.ts'
import { decodeInkItem, writeInkItem } from './ink-codec.ts'
import { JsonTextWriter, getJsonProperty, isPlainObject, parseJsonTolerant } from './json-options.ts'
import { fileNameOf } from './paths.ts'

/**
 * 页面 payload（pages/page-000.json）编解码，形态与桌面 WbixPagePayload 一致：`{ "id": Guid, "strokes": [...], "elements": [...] }`。
 * 读侧容错逐条对齐 WbixWorkspaceSerializer.Load.cs：strokes 缺失或为 null 时取空数组，单条为 null 或未知 kind 跳过；elements 缺失或为 null 时取空数组，type 空白或未知跳过，单元素解析异常也跳过；元素 data 内字段按大小写敏感查找（桌面走 JsonElement.TryGetProperty），缺失时取桌面同样的默认值（id 新 Guid、位置 (0,0)、尺寸 (320,180)、order 取数组下标、layer 取 belowInk）。
 */

/** 元素层级字面量。 */
const LAYER_BELOW = 'belowInk'
const LAYER_ABOVE = 'aboveInk'

/** 读侧已解析的资源（由 `read.ts` 提供，避免本模块依赖 zip 细节）。 */
export interface ResolvedResource {
  id: string
  /** 归一化后的 zip 条目路径（用于 `displayName` 兜底）。 */
  path: string
  type: string
  contentType: string
  meta: Record<string, string> | null
  bytes: Uint8Array
}

/** 页面 payload 读取上下文。 */
export interface PagePayloadReadContext {
  /** manifest 的 `version`（当前 1/2/3；读侧形态自适应，此值仅用于分派与日志）。 */
  version: number
  /** 按 `resourceId` 解析内嵌资源；失败返回 null（实现方负责记告警）。 */
  resolveResource(resourceId: string): ResolvedResource | null
  /** 记录容忍告警。 */
  warn(code: WbixWarningCode, detail: string): void
  /**
   * v3 文件里出现 v1/v2 扁平笔迹条目时的回调（对齐桌面
   * `WbixLegacyFlatItemWarningGate`：由实现方保证每个文件最多告警一次）。
   */
  onLegacyFlatItem?(): void
}

/**
 * 读取页面 payload。
 * @throws WbixError `page-parse-failed`（payload 结构非法，桌面侧会让整包导入失败）
 * @throws WbixError `ink-item-invalid`（笔迹条目结构非法，桌面侧会让整页解析失败）
 */
export function readPagePayload(text: string, context: PagePayloadReadContext): PageSnapshot {
  let parsed: unknown
  try {
    parsed = parseJsonTolerant(text)
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    throw new WbixError('page-parse-failed', `页面 payload 解析失败：${reason}`)
  }

  if (!isPlainObject(parsed)) {
    throw new WbixError('page-parse-failed', '页面 payload 必须是 JSON 对象')
  }

  const id = readPayloadId(parsed)
  const strokes = readStrokes(parsed, context)
  const elements = readElements(parsed, context)

  return { id, strokes, elements }
}

function readPayloadId(payload: Record<string, unknown>): Guid {
  const { found, value } = getJsonProperty(payload, 'id')
  const id = found ? parseGuid(value) : null
  if (id === null) {
    // 桌面 `WbixPagePayload.Id` 是 Guid 值类型，缺失或非法会让整页反序列化失败。
    throw new WbixError('page-parse-failed', '页面 payload 缺少合法的 id')
  }

  return id
}

function readStrokes(payload: Record<string, unknown>, context: PagePayloadReadContext): PageSnapshot['strokes'] {
  const { found, value } = getJsonProperty(payload, 'strokes')
  if (!found || value === null) {
    // 桌面 `Strokes ?? empty`。
    return []
  }

  if (!Array.isArray(value)) {
    throw new WbixError('page-parse-failed', '页面 payload 的 strokes 必须是数组')
  }

  const strokes: PageSnapshot['strokes'] = []
  for (let index = 0; index < value.length; index += 1) {
    const decoded = decodeInkItem(value[index])
    if (decoded.status === 'skipped') {
      if (decoded.reason === 'null') {
        context.warn('ink-item-null', `strokes[${index}] 为 null，已跳过`)
      } else {
        context.warn('ink-item-unknown-kind', `strokes[${index}] 的 kind='${decoded.kind ?? ''}' 未知，已跳过`)
      }
      continue
    }

    strokes.push(decoded.item)

    if (decoded.legacyFlat && context.version >= 3) {
      // v3 文件里出现扁平条目属于格式倒退：兜底读取，每个文件只告警一次。
      context.onLegacyFlatItem?.()
    }
  }

  return strokes
}

/** 待排序元素（保留原始下标，用于 order 相同时的稳定排序）。 */
interface PendingElement {
  element: BoardElement
  index: number
}

function readElements(payload: Record<string, unknown>, context: PagePayloadReadContext): BoardElement[] {
  const { found, value } = getJsonProperty(payload, 'elements')
  if (!found || value === null) {
    return []
  }

  if (!Array.isArray(value)) {
    throw new WbixError('page-parse-failed', '页面 payload 的 elements 必须是数组')
  }

  const below: PendingElement[] = []
  const above: PendingElement[] = []

  for (let index = 0; index < value.length; index += 1) {
    const raw = value[index]

    if (raw === null) {
      context.warn('element-null', `elements[${index}] 为 null，已跳过`)
      continue
    }

    if (!isPlainObject(raw)) {
      // 桌面 `WbixPageElement` 反序列化失败会让整页失败。
      throw new WbixError('page-parse-failed', `elements[${index}] 必须是 JSON 对象`)
    }

    try {
      const parsed = parseElement(raw, index, context)
      if (parsed === null) {
        continue
      }

      const pending: PendingElement = { element: parsed.element, index }
      if (parsed.aboveInk) {
        above.push(pending)
      } else {
        below.push(pending)
      }
    } catch (error) {
      if (error instanceof WbixError) {
        throw error
      }

      // 对齐桌面：单元素解析异常只 Warn 后跳过，不阻断整页。
      const reason = error instanceof Error ? error.message : String(error)
      context.warn('element-parse-failed', `elements[${index}] 解析失败：${reason}`)
    }
  }

  sortByOrderThenIndex(below)
  sortByOrderThenIndex(above)

  return [...below, ...above].map((pending) => pending.element)
}

function sortByOrderThenIndex(items: PendingElement[]): void {
  items.sort((left, right) => {
    const byOrder = left.element.order - right.element.order
    return byOrder !== 0 ? byOrder : left.index - right.index
  })
}

function parseElement(
  raw: Record<string, unknown>,
  fallbackOrder: number,
  context: PagePayloadReadContext,
): { element: BoardElement; aboveInk: boolean } | null {
  // `type` 走 STJ 大小写不敏感匹配（`WbixPageElement.Type` 是 string 属性）。
  const typeProperty = getJsonProperty(raw, 'type')
  if (typeProperty.found && typeProperty.value !== null && typeof typeProperty.value !== 'string') {
    throw new WbixError('page-parse-failed', `elements[${fallbackOrder}].type 必须是字符串`)
  }

  const typeText = typeof typeProperty.value === 'string' ? typeProperty.value.trim() : ''
  if (typeText.length === 0) {
    context.warn('element-type-empty', `elements[${fallbackOrder}] 缺少 type，已跳过`)
    return null
  }

  // `data` 非对象时，桌面侧所有 `TryGet*` 都返回 null，等价于"全取默认值"。
  const dataProperty = getJsonProperty(raw, 'data')
  const data = isPlainObject(dataProperty.value) ? dataProperty.value : {}

  const id = readGuidField(data, 'id') ?? newGuid()
  const positionWorld = readVec2Field(data, 'positionWorld') ?? { x: 0, y: 0 }
  const sizeWorld = readVec2Field(data, 'sizeWorld') ?? { ...DEFAULT_ELEMENT_SIZE }
  const order = readInt32Field(data, 'order') ?? fallbackOrder
  const layerText = (readStringField(data, 'layer') ?? LAYER_BELOW).trim()
  const aboveInk = layerText.toLowerCase() === LAYER_ABOVE.toLowerCase()
  const layer: Layer = aboveInk ? LAYER_ABOVE : LAYER_BELOW

  const base = { id, layer, order, positionWorld, sizeWorld }

  switch (typeText.toLowerCase()) {
    case 'text':
      return {
        element: { ...base, type: 'text', text: readStringField(data, 'text') ?? '' },
        aboveInk,
      }

    case 'link':
      return {
        element: {
          ...base,
          type: 'link',
          url: readStringField(data, 'url') ?? '',
          title: readStringField(data, 'title'),
        },
        aboveInk,
      }

    case 'media':
      return { element: parseMediaElement(base, data, context), aboveInk }

    case 'file':
      return { element: parseFileElement(base, data), aboveInk }

    default:
      // 桌面 `TryParseElement` 的 default 分支静默跳过（无 Warn）。
      context.warn('element-unknown-type', `elements[${fallbackOrder}] 的 type='${typeText}' 未知，已跳过`)
      return null
  }
}

type ElementBase = Pick<BoardElement, 'id' | 'layer' | 'order' | 'positionWorld' | 'sizeWorld'>

function parseMediaElement(
  base: ElementBase,
  data: Record<string, unknown>,
  context: PagePayloadReadContext,
): BoardElement {
  const kindText = readStringField(data, 'kind') ?? 'video'
  const kind: MediaKind = parseMediaKind(kindText)

  let displayName = readStringField(data, 'displayName') ?? ''
  const sourcePath = readStringField(data, 'sourcePath')
  const resourceId = readStringField(data, 'resourceId')

  let resolved: ResolvedResource | null = null
  if (resourceId !== null && resourceId.trim().length > 0) {
    resolved = context.resolveResource(resourceId)
    if (resolved !== null && displayName.trim().length === 0) {
      // 桌面用"提取到临时目录后的文件名"兜底，这里用 zip 条目文件名，语义一致。
      displayName = fileNameOf(resolved.path)
    }
  }

  if (displayName.trim().length === 0) {
    displayName = kind === 'image' ? '图片' : kind === 'audio' ? '音频' : '视频'
  }

  return {
    ...base,
    type: 'media',
    kind,
    // 与桌面一致：内嵌图片的 sourcePath 在导出/再导出时保持 null，不做本地路径回填。
    sourcePath,
    displayName,
    resourceId,
  }
}

function parseFileElement(base: ElementBase, data: Record<string, unknown>): BoardElement {
  const sourcePath = readStringField(data, 'sourcePath') ?? ''
  let displayName = readStringField(data, 'displayName') ?? ''

  if (displayName.trim().length === 0) {
    displayName = sourcePath.trim().length === 0 ? '文件' : fileNameOf(sourcePath)
  }

  return { ...base, type: 'file', sourcePath, displayName }
}

/** 大小写敏感读取成员（对齐 `JsonElement.TryGetProperty`）。 */
function getExactProperty(
  obj: Record<string, unknown>,
  propertyName: string,
): { found: boolean; value: unknown } {
  return Object.prototype.hasOwnProperty.call(obj, propertyName)
    ? { found: true, value: obj[propertyName] }
    : { found: false, value: undefined }
}

function readStringField(obj: Record<string, unknown>, propertyName: string): string | null {
  const { found, value } = getExactProperty(obj, propertyName)
  if (!found || typeof value !== 'string') {
    return null
  }

  return value
}

function readGuidField(obj: Record<string, unknown>, propertyName: string): Guid | null {
  return parseGuid(readStringField(obj, propertyName))
}

function readInt32Field(obj: Record<string, unknown>, propertyName: string): number | null {
  const { found, value } = getExactProperty(obj, propertyName)
  if (!found || typeof value !== 'number' || !Number.isInteger(value)) {
    return null
  }

  return value >= -2147483648 && value <= 2147483647 ? value : null
}

function readVec2Field(obj: Record<string, unknown>, propertyName: string): Vec2 | null {
  const { found, value } = getExactProperty(obj, propertyName)
  if (!found || !isPlainObject(value)) {
    return null
  }

  const x = readSingleField(value, 'x')
  const y = readSingleField(value, 'y')
  if (x === null || y === null) {
    return null
  }

  return { x, y }
}

function readSingleField(obj: Record<string, unknown>, propertyName: string): number | null {
  const { found, value } = getExactProperty(obj, propertyName)
  if (!found || typeof value !== 'number' || !Number.isFinite(value)) {
    return null
  }

  return Math.fround(value)
}

/**
 * 写出页面 payload 文本（固定 v3 形态）。元素顺序对齐桌面 CreateWbixPageElements：先 belowInk 再 aboveInk，层内保持数组顺序；每个元素的 order 取自身值，order < 0 时用层内下标替代。
 */
export function writePagePayloadJson(page: PageSnapshot): string {
  if (!isGuid(page.id)) {
    throw new WbixError('invalid-value', `页面 id 不是合法 Guid：${page.id}`)
  }

  const writer = new JsonTextWriter()
  writer.openObject()

  writer.stringProperty('id', formatGuid(page.id))

  writer.name('strokes')
  writer.openArray()
  for (const item of page.strokes) {
    writeInkItem(writer, item)
  }
  writer.closeArray()

  writer.name('elements')
  writeElements(writer, page.elements)

  writer.closeObject()
  return writer.text()
}

function writeElements(writer: JsonTextWriter, elements: BoardElement[]): void {
  const below = elements.filter((element) => element.layer !== LAYER_ABOVE)
  const above = elements.filter((element) => element.layer === LAYER_ABOVE)

  writer.openArray()
  appendElements(writer, below, LAYER_BELOW)
  appendElements(writer, above, LAYER_ABOVE)
  writer.closeArray()
}

function appendElements(writer: JsonTextWriter, elements: BoardElement[], layer: Layer): void {
  for (let index = 0; index < elements.length; index += 1) {
    const element = elements[index]
    const order = element.order < 0 ? index : element.order
    writeElement(writer, element, layer, order)
  }
}

function writeElement(writer: JsonTextWriter, element: BoardElement, layer: Layer, order: number): void {
  writer.openObject()
  writer.stringProperty('type', element.type)
  writer.name('data')
  writer.openObject()

  // 字段顺序对齐桌面匿名对象：id, layer, positionWorld, sizeWorld, order, 类型专有字段。
  writer.stringProperty('id', formatGuid(element.id))
  writer.stringProperty('layer', layer)
  writer.name('positionWorld')
  writeVec2(writer, element.positionWorld)
  writer.name('sizeWorld')
  writeVec2(writer, element.sizeWorld)
  writer.intProperty('order', order)

  switch (element.type) {
    case 'text':
      writer.stringProperty('text', element.text ?? '')
      break

    case 'link':
      writer.stringProperty('url', element.url ?? '')
      writeNullableString(writer, 'title', element.title)
      break

    case 'media':
      writer.stringProperty('kind', element.kind ?? 'video')
      writeNullableString(writer, 'sourcePath', element.sourcePath)
      writer.stringProperty('displayName', element.displayName ?? '')
      writeNullableString(writer, 'resourceId', element.resourceId)
      break

    case 'file':
      writer.stringProperty('sourcePath', element.sourcePath ?? '')
      writer.stringProperty('displayName', element.displayName ?? '')
      break
  }

  writer.closeObject()
  writer.closeObject()
}

function writeNullableString(writer: JsonTextWriter, propertyName: string, value: string | null | undefined): void {
  writer.name(propertyName)
  if (value === null || value === undefined) {
    writer.nullValue()
    return
  }

  writer.stringValue(value)
}

function writeVec2(writer: JsonTextWriter, value: Vec2): void {
  writer.openObject()
  writer.f32Property('x', value.x)
  writer.f32Property('y', value.y)
  writer.closeObject()
}
