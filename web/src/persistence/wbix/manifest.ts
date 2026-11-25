import { formatGuid, isGuid, parseGuid } from '../../board/model/ids.ts'
import type { Guid } from '../../board/model/ids.ts'
import type { Vec2 } from '../../board/model/stroke.ts'
import { WbixError } from './errors.ts'
import {
  JsonTextWriter,
  getJsonProperty,
  isPlainObject,
  parseDateTimeOffset,
  parseJsonTolerant,
} from './json-options.ts'

/**
 * manifest.json 编解码。形态同桌面 WbixManifest，record 参数顺序即 JSON 键序：format, version, createdUtc, currentIndex, pages, resources, viewportCameraWorld, viewportZoom, viewportSizeDip。桌面 JsonSerializerOptions 未设置 DefaultIgnoreCondition，所有成员都会输出，视口三项在无数据时写 null。
 */

/** 格式标识（`WbixWorkspaceSerializer.FormatName`）。 */
export const WBIX_FORMAT = 'wbix'

/** 写侧固定版本（`WbixWorkspaceSerializer.CurrentVersion`）。 */
export const WBIX_CURRENT_VERSION = 3

/** 读侧支持的最低版本。 */
export const WBIX_MIN_VERSION = 1

/** manifest 条目名。 */
export const WBIX_MANIFEST_ENTRY = 'manifest.json'

/** 页目录名。 */
export const WBIX_PAGES_FOLDER = 'pages'

/** 资源目录名。 */
export const WBIX_ASSETS_FOLDER = 'assets'

/** manifest 中的页条目。 */
export interface WbixManifestPage {
  id: Guid
  index: number
  path: string
}

/** manifest 中的资源条目。 */
export interface WbixResourceEntry {
  id: string
  type: string
  path: string
  contentType: string
  meta: Record<string, string> | null
}

/** manifest 数据。 */
export interface WbixManifest {
  format: string
  version: number
  /**
   * 创建时间（毫秒精度）。桌面写侧取 DateTimeOffset.UtcNow，JSON 里最多带 7 位小数（100ns 精度），而 JS Date 只有毫秒精度，读入 7 位小数的值会丢失亚毫秒部分；需要逐字节往返时改用 {@link createdUtcText}。
   */
  createdUtc: Date | null
  /**
   * 读入时 `createdUtc` 的原始文本（无损，含亚毫秒小数位）；写出时由调用方决定是否复用。
   *
   * 只有在读入路径上才会填充，`null` 表示"没有原始文本"（新建的 manifest）。
   */
  createdUtcText: string | null
  currentIndex: number
  pages: WbixManifestPage[]
  resources: WbixResourceEntry[]
  viewportCameraWorld: Vec2 | null
  viewportZoom: number | null
  viewportSizeDip: Vec2 | null
}

/** 页 zip 条目名（对齐 `page-{i:000}.json`）。 */
export function pageEntryName(index: number): string {
  return `${WBIX_PAGES_FOLDER}/page-${String(index).padStart(3, '0')}.json`
}

/**
 * 读取 manifest。
 *
 * @throws WbixError `manifest-parse-failed` / `format-mismatch` / `version-not-supported`
 */
export function readManifest(text: string): WbixManifest {
  let parsed: unknown
  try {
    parsed = parseJsonTolerant(text)
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    throw new WbixError('manifest-parse-failed', `manifest 解析失败：${reason}`)
  }

  if (!isPlainObject(parsed)) {
    throw new WbixError('manifest-parse-failed', 'manifest 必须是 JSON 对象')
  }

  const formatValue = getJsonProperty(parsed, 'format').value
  const format = typeof formatValue === 'string' ? formatValue : ''
  if (format.toLowerCase() !== WBIX_FORMAT) {
    // 桌面：`string.Equals(manifest.Format, "wbix", OrdinalIgnoreCase)` 不成立即失败。
    throw new WbixError('format-mismatch', `不是 WBIX 文件：format='${format}'`)
  }

  const version = readVersion(parsed)

  const pagesValue = getJsonProperty(parsed, 'pages').value
  if (!Array.isArray(pagesValue)) {
    throw new WbixError('manifest-parse-failed', 'manifest.pages 必须是数组')
  }

  const pages = pagesValue.map((value, index) => readManifestPage(value, index))
  const resources = readResources(parsed)
  const currentIndex = readInteger(parsed, 'currentIndex', 0, 'manifest.currentIndex')
  const createdUtc = readCreatedUtc(parsed)

  return {
    format,
    version,
    createdUtc: createdUtc.value,
    createdUtcText: createdUtc.text,
    currentIndex,
    pages,
    resources,
    viewportCameraWorld: readOptionalVec2(parsed, 'viewportCameraWorld'),
    viewportZoom: readOptionalF32(parsed, 'viewportZoom'),
    viewportSizeDip: readOptionalVec2(parsed, 'viewportSizeDip'),
  }
}

function readVersion(manifest: Record<string, unknown>): number {
  const { found, value } = getJsonProperty(manifest, 'version')

  if (!found) {
    // 桌面 int 缺省为 0，随后被版本门禁拒绝。
    throw new WbixError('version-not-supported', 'manifest 缺少 version')
  }

  if (value === null || typeof value !== 'number' || !Number.isInteger(value)) {
    throw new WbixError('manifest-parse-failed', 'manifest.version 必须是整数')
  }

  if (value < WBIX_MIN_VERSION || value > WBIX_CURRENT_VERSION) {
    throw new WbixError(
      'version-not-supported',
      `不支持的 WBIX 版本：${value}（支持 ${WBIX_MIN_VERSION}–${WBIX_CURRENT_VERSION}）`,
    )
  }

  return value
}

function readManifestPage(value: unknown, index: number): WbixManifestPage {
  if (!isPlainObject(value)) {
    throw new WbixError('manifest-parse-failed', `manifest.pages[${index}] 必须是对象`)
  }

  const id = parseGuid(getJsonProperty(value, 'id').value)
  if (id === null) {
    throw new WbixError('manifest-parse-failed', `manifest.pages[${index}].id 不是合法 Guid`)
  }

  const pageIndex = readInteger(value, 'index', 0, `manifest.pages[${index}].index`)

  const pathValue = getJsonProperty(value, 'path').value
  if (typeof pathValue !== 'string') {
    throw new WbixError('manifest-parse-failed', `manifest.pages[${index}].path 必须是字符串`)
  }

  return { id, index: pageIndex, path: pathValue }
}

function readResources(manifest: Record<string, unknown>): WbixResourceEntry[] {
  const { found, value } = getJsonProperty(manifest, 'resources')
  if (!found || value === null) {
    return []
  }

  if (!Array.isArray(value)) {
    throw new WbixError('manifest-parse-failed', 'manifest.resources 必须是数组')
  }

  return value.map((item, index) => readResource(item, index))
}

function readResource(value: unknown, index: number): WbixResourceEntry {
  if (!isPlainObject(value)) {
    throw new WbixError('manifest-parse-failed', `manifest.resources[${index}] 必须是对象`)
  }

  return {
    id: readStringOrEmpty(value, 'id'),
    type: readStringOrEmpty(value, 'type'),
    path: readStringOrEmpty(value, 'path'),
    contentType: readStringOrEmpty(value, 'contentType'),
    meta: readMeta(value),
  }
}

function readMeta(resource: Record<string, unknown>): Record<string, string> | null {
  const { found, value } = getJsonProperty(resource, 'meta')
  if (!found || value === null) {
    return null
  }

  if (!isPlainObject(value)) {
    throw new WbixError('manifest-parse-failed', 'manifest.resources[].meta 必须是对象')
  }

  const meta: Record<string, string> = {}
  for (const [key, item] of Object.entries(value)) {
    if (typeof item !== 'string') {
      throw new WbixError('manifest-parse-failed', `manifest.resources[].meta.${key} 必须是字符串`)
    }
    meta[key] = item
  }

  return meta
}

function readStringOrEmpty(obj: Record<string, unknown>, propertyName: string): string {
  const { value } = getJsonProperty(obj, propertyName)
  return typeof value === 'string' ? value : ''
}

function readInteger(
  obj: Record<string, unknown>,
  propertyName: string,
  fallback: number,
  path: string,
): number {
  const { found, value } = getJsonProperty(obj, propertyName)
  if (!found || value === null) {
    return fallback
  }

  if (typeof value !== 'number' || !Number.isInteger(value)) {
    throw new WbixError('manifest-parse-failed', `${path} 必须是整数`)
  }

  return value
}

function readCreatedUtc(manifest: Record<string, unknown>): { value: Date | null; text: string | null } {
  const { found, value } = getJsonProperty(manifest, 'createdUtc')
  if (!found || value === null) {
    return { value: null, text: null }
  }

  const parsed = parseDateTimeOffset(value)
  if (parsed === null) {
    // 桌面 `DateTimeOffset` 反序列化失败会让整个 manifest 解析失败。
    throw new WbixError('manifest-parse-failed', 'manifest.createdUtc 不是合法时间')
  }

  return { value: parsed, text: typeof value === 'string' ? value : null }
}

/** `DateTimeOffset` 文本形态（.NET `O` 格式去掉可选小数位）。 */
// 桌面端写侧（`Utf8JsonWriter.WriteString(DateTimeOffset)`）产出的小数位是 **0~7 位可变**（`TrimDateTimeOffset` 会去掉末尾的 0，秒为整时连小数点一起去掉），偏移恒为 `+00:00`。读侧这里对偏移放宽到任意 `±HH:MM`：手改或第三方工具产出的文件同样要能原样往返，只有写回去时才依赖原文本本身（不做任何重排）。
const DATE_TIME_OFFSET_TEXT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?[+-]\d{2}:\d{2}$/

function isDateTimeOffsetText(text: string): boolean {
  return DATE_TIME_OFFSET_TEXT.test(text)
}

function readOptionalF32(manifest: Record<string, unknown>, propertyName: string): number | null {
  const { found, value } = getJsonProperty(manifest, propertyName)
  if (!found || value === null) {
    return null
  }

  if (typeof value !== 'number') {
    throw new WbixError('manifest-parse-failed', `manifest.${propertyName} 必须是数字`)
  }

  return Math.fround(value)
}

function readOptionalVec2(manifest: Record<string, unknown>, propertyName: string): Vec2 | null {
  const { found, value } = getJsonProperty(manifest, propertyName)
  if (!found || value === null) {
    return null
  }

  if (!isPlainObject(value)) {
    throw new WbixError('manifest-parse-failed', `manifest.${propertyName} 必须是对象`)
  }

  const x = readOptionalF32(value, 'x')
  const y = readOptionalF32(value, 'y')
  if (x === null || y === null) {
    throw new WbixError('manifest-parse-failed', `manifest.${propertyName} 缺少 x/y`)
  }

  return { x, y }
}

/**
 * 写出 manifest 文本。
 *
 * @throws WbixError `invalid-value` 当取值无法表达为合法 manifest
 */
export function writeManifestJson(manifest: WbixManifest, createdUtcText?: string | null): string {
  if (!Number.isInteger(manifest.version)) {
    throw new WbixError('invalid-value', `manifest.version 必须是整数：${String(manifest.version)}`)
  }

  if (!(manifest.createdUtc instanceof Date) || Number.isNaN(manifest.createdUtc.getTime())) {
    throw new WbixError('invalid-value', 'manifest.createdUtc 必须是有效时间')
  }

  // 优先原样复用读入时的文本，这样 7 位小数的 createdUtc 也能逐字节往返；
  // 未提供时才按 `Date`（毫秒精度）格式化。
  const createdUtcRaw = createdUtcText ?? null
  if (createdUtcRaw !== null && !isDateTimeOffsetText(createdUtcRaw)) {
    throw new WbixError('invalid-value', `createdUtc 文本不是合法 DateTimeOffset：${createdUtcRaw}`)
  }

  const writer = new JsonTextWriter()
  writer.openObject()

  writer.stringProperty('format', WBIX_FORMAT)
  writer.intProperty('version', manifest.version)
  if (createdUtcRaw === null) {
    writer.dateTimeOffsetProperty('createdUtc', manifest.createdUtc)
  } else {
    writer.dateTimeOffsetTextProperty('createdUtc', createdUtcRaw)
  }
  writer.intProperty('currentIndex', manifest.currentIndex)

  writer.name('pages')
  writer.openArray()
  for (const page of manifest.pages) {
    if (!isGuid(page.id)) {
      throw new WbixError('invalid-value', `页 id 不是合法 Guid：${page.id}`)
    }

    writer.openObject()
    writer.stringProperty('id', formatGuid(page.id))
    writer.intProperty('index', page.index)
    writer.stringProperty('path', page.path)
    writer.closeObject()
  }
  writer.closeArray()

  writer.name('resources')
  writer.openArray()
  for (const resource of manifest.resources) {
    writeResource(writer, resource)
  }
  writer.closeArray()

  writeNullableVec2(writer, 'viewportCameraWorld', manifest.viewportCameraWorld)
  writeNullableF32(writer, 'viewportZoom', manifest.viewportZoom)
  writeNullableVec2(writer, 'viewportSizeDip', manifest.viewportSizeDip)

  writer.closeObject()
  return writer.text()
}

function writeResource(writer: JsonTextWriter, resource: WbixResourceEntry): void {
  writer.openObject()
  writer.stringProperty('id', resource.id)
  writer.stringProperty('type', resource.type)
  writer.stringProperty('path', resource.path)
  writer.stringProperty('contentType', resource.contentType)

  writer.name('meta')
  if (resource.meta === null) {
    writer.nullValue()
  } else {
    writer.openObject()
    for (const [key, value] of Object.entries(resource.meta)) {
      writer.stringProperty(key, value)
    }
    writer.closeObject()
  }

  writer.closeObject()
}

function writeNullableVec2(writer: JsonTextWriter, propertyName: string, value: Vec2 | null): void {
  writer.name(propertyName)
  if (value === null) {
    writer.nullValue()
    return
  }

  writer.openObject()
  writer.f32Property('x', value.x)
  writer.f32Property('y', value.y)
  writer.closeObject()
}

function writeNullableF32(writer: JsonTextWriter, propertyName: string, value: number | null): void {
  writer.name(propertyName)
  if (value === null) {
    writer.nullValue()
    return
  }

  writer.f32Value(value)
}
