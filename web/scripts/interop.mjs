/**
 * WBIX 双端一致性测试台（P0.5）。
 *
 * 目的：把"网页端与桌面端对同一个 `.wbix` 包的读写语义完全一致"变成可执行的断言。
 * 做法：两端各自把包解析成**规范化 dump JSON**，再逐字段比对；dump 口径见下方「dump 规范」。
 *
 * 五步（任一步失败即非零退出，并打印首个差异路径与两侧取值）：
 *   A：网页端读 `WindBoard.Tests/Fixtures/wbix/files/*.wbix` + `web/.interop/produced/*.wbix`
 *      → `web/.interop/dump-ts/<包名>.json`；
 *   B：桌面端（`WbixInteropDumpTests`，read 模式）读**同一批字节**（脚本先把它们复制到
 *      `web/.interop/stage-read/`）→ `web/.interop/dump-cs/<包名>.json`；
 *      A 与 B 必须逐字段相等（锁「两端读同一份字节得到同一份语义」）；
 *   C：网页端由 `WindBoard.Tests/Fixtures/wbix/model/*.json` 写出 `.wbix` → `web/.interop/produced-ts/`，
 *      并与 `Fixtures/wbix/expected/<model>/**` 逐字节比对（锁「网页端写出的字节与桌面写侧一致」）；
 *   D：桌面端（rewrite 模式）读 `produced-ts/*.wbix` → dump（`web/.interop/dump-cs-model/`），
 *      与「由 model 独立推出的期望 dump」相等；这一步同时就是 P0.4 的验收：
 *      桌面端能读回网页端写出的包；
 *   E：桌面端把 `produced-ts/*.wbix` 重写 → `web/.interop/rewritten-cs/*.wbix`；网页端读回 → dump
 *      （`web/.interop/dump-ts-rewritten/`），与 D 的 dump 相等（锁「桌面端重写后再被网页端读，语义不变」）。
 *
 * dump 规范（两端必须逐条一致；改动时两端同步改，桌面端实现在
 * `WindBoard.Tests/Board/Persistence/WbixInteropDumpTests.cs`）：
 *   1. 顶层结构固定为 `{ format, version, createdUtc, currentIndex, viewport, pages, resources }`；
 *      `viewport = { cameraWorld, zoom, sizeDip }`，其中 `cameraWorld`/`sizeDip` 为 `{x,y}` 或 null，
 *      `zoom` 为 f32 文本或 null。
 *   2. `pages[i] = { id, strokes, elements }`；`strokes` 顺序即 z-order（数组保序，不排序）；
 *      `elements` 为「先全部 belowInk 组、后全部 aboveInk 组」，组内顺序即渲染顺序（保序）。
 *   3. 每个笔迹条目 `{ kind, ... }`：stroke 带 `points[{position,pressure}]`/`colorRgba`/`baseSize`/
 *      `enablePressure`；shape 带 `start`/`end`/`colorRgba`/`width`。
 *   4. 每个元素节点：公共 `{ id, layer, order, positionWorld, sizeWorld, type }` + 类型专有字段：
 *      text → `text`；link → `url`/`title`（可为 null）；media → `kind`/`displayName`/`resourceId`（可为 null）；
 *      file → `sourcePath`/`displayName`。
 *   5. **对象键一律按序数（ordinal）排序**，数组保序；对象键序本身参与比对（顺序不同也算差异）。
 *   6. **所有数值一律写成 float32 最短往返文本字符串**（网页端 `formatF32`，桌面端
 *      `JsonSerializer.Serialize(float)`），例如 `"0.1"`、`"1E-07"`、`"-0"`、`"3.4028235E+38"`；
 *      整数型字段（`version`/`currentIndex`/`order`）保持 JSON number。
 *   7. 不参与比对：
 *      - `media` 元素的 `sourcePath`：桌面读侧会把内嵌资源提取到临时文件并改写该字段，
 *        属平台相关运行时行为，不是格式语义；
 *      - `createdUtc` 的具体值：没有时钟注入点，只锁存在性，统一写成 `"<createdUtc>"`，缺失时为 null。
 *   8. `resources` 按 manifest 顺序（保序）：`{ id, type, path, contentType, meta }`，`meta` 为对象或 null。
 *
 * 反向验证（手工，验证完务必复原）：
 *   - 把下面 `f32Text()` 改成 `value.toFixed(2)` 之类 → A/B/C/D/E 必须失败并给出路径；
 *   - 把 `buildElementDump()` 里某个键挪个位置（键序变化），或让 `buildDumpFromRead()` 的 `pages`
 *     反序（条目顺序变化）→ 同样必须失败。
 */

import { execFileSync } from 'node:child_process'
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  renameSync,
  rmSync,
  writeFileSync,
} from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

import { unzipSync } from 'fflate'

import { DEFAULT_ELEMENT_SIZE } from '../src/board/model/element.ts'
import { f32, formatF32 } from '../src/persistence/wbix/f32-format.ts'
import { WBIX_CURRENT_VERSION, WBIX_FORMAT } from '../src/persistence/wbix/manifest.ts'
import { readPagePayload } from '../src/persistence/wbix/page-payload.ts'
import { readWbix } from '../src/persistence/wbix/read.ts'
import { writeWbix } from '../src/persistence/wbix/write.ts'

// ---------------------------------------------------------------------------
// 路径
// ---------------------------------------------------------------------------

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url))
const WEB_ROOT = path.resolve(SCRIPT_DIR, '..')
const REPO_ROOT = path.resolve(WEB_ROOT, '..')
/** 跨端语料（单一事实源，由桌面端生成）。 */
const FIXTURE_ROOT = path.join(REPO_ROOT, 'WindBoard.Tests', 'Fixtures', 'wbix')
const FIXTURE_FILES_DIR = path.join(FIXTURE_ROOT, 'files')
const FIXTURE_MODELS_DIR = path.join(FIXTURE_ROOT, 'model')
const FIXTURE_EXPECTED_DIR = path.join(FIXTURE_ROOT, 'expected')
/** 本测试台的全部输出（`web/.interop/` 已 gitignore）。 */
const INTEROP_ROOT = path.join(WEB_ROOT, '.interop')
/** 网页端样本目录（由 `pnpm samples` 产出，本脚本只读不改）。 */
const PRODUCED_DIR = path.join(INTEROP_ROOT, 'produced')
/** B 步的输入暂存目录：把 fixtures 与 produced 复制到一起，两端读同一份字节。 */
const STAGE_READ_DIR = path.join(INTEROP_ROOT, 'stage-read')
const DUMP_TS_DIR = path.join(INTEROP_ROOT, 'dump-ts')
const DUMP_CS_DIR = path.join(INTEROP_ROOT, 'dump-cs')
const PRODUCED_TS_DIR = path.join(INTEROP_ROOT, 'produced-ts')
const DUMP_CS_MODEL_DIR = path.join(INTEROP_ROOT, 'dump-cs-model')
const REWRITTEN_CS_DIR = path.join(INTEROP_ROOT, 'rewritten-cs')
const DUMP_TS_REWRITTEN_DIR = path.join(INTEROP_ROOT, 'dump-ts-rewritten')

// ---------------------------------------------------------------------------
// 常量
// ---------------------------------------------------------------------------

/** 桌面端 dump 生成器读取的环境变量。 */
const INTEROP_DIR_VARIABLE = 'WINDBOARD_WBIX_INTEROP_DIR'
const INTEROP_MODE_VARIABLE = 'WINDBOARD_WBIX_INTEROP_MODE'
const READ_MODE = 'read'
const REWRITE_MODE = 'rewrite'
/** 桌面端 dump 生成器的输出子目录（与 `WbixInteropDumpTests` 保持一致）。 */
const CS_DUMP_FOLDER = 'dump-cs'
const CS_REWRITTEN_FOLDER = 'rewritten-cs'
/** `createdUtc` 占位符（语料与 dump 共用同一写法）。 */
const CREATED_UTC_PLACEHOLDER = '<createdUtc>'
/**
 * 写样本时使用的固定 `createdUtc` 文本。
 *
 * 取 7 位小数 + `+00:00`，与桌面写侧 `Utf8JsonWriter` 的形态一致；
 * `writeManifestJson` 会把该文本原样写入，因此与 `expected/**` 的 `<createdUtc>`
 * 占位符替换后可逐字节比对。
 */
const CREATED_UTC_TEXT = '2026-10-31T13:45:12.1230000+00:00'
/** 期望文本目录里的条目清单文件名。 */
const ENTRIES_FILE = 'entries.txt'

// ---------------------------------------------------------------------------
// 通用工具
// ---------------------------------------------------------------------------

/** 可预期的失败：带一段已排好版的说明，由 `main()` 统一打印后以非零码退出。 */
class InteropFailure extends Error {}

/** 序数比较（与 .NET `StringComparer.Ordinal` 等价）。 */
function compareOrdinal(left, right) {
  return left < right ? -1 : left > right ? 1 : 0
}

/** 判断是否为普通对象（排除数组与 null）。 */
function isPlainObject(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** 构造键序数排序的对象（JS 对象保持插入顺序，`JSON.stringify` 随之输出）。 */
function sortedObject(entries) {
  const result = {}
  for (const key of Object.keys(entries).sort(compareOrdinal)) {
    result[key] = entries[key]
  }

  return result
}

/** float32 最短往返文本（dump 里所有数值的统一形态）。 */
function f32Text(value) {
  return formatF32(value)
}

/** 构造 `{x,y}` 数值节点。 */
function buildVec2Dump(value) {
  const point = isPlainObject(value) ? value : { x: 0, y: 0 }
  return sortedObject({ x: f32Text(point.x), y: f32Text(point.y) })
}

/** 构造 `{x,y,z,w}` 数值节点。 */
function buildVec4Dump(value) {
  const color = isPlainObject(value) ? value : { x: 0, y: 0, z: 0, w: 0 }
  return sortedObject({
    x: f32Text(color.x),
    y: f32Text(color.y),
    z: f32Text(color.z),
    w: f32Text(color.w),
  })
}

/** `currentIndex` 的钳制规则（与两端读侧一致）。 */
function clampCurrentIndex(value, pageCount) {
  return Math.min(Math.max(value, 0), Math.max(0, pageCount - 1))
}

/** 列目录下指定后缀的文件（序数排序，保证可重复运行）。 */
function listFiles(directory, suffix) {
  if (!existsSync(directory)) {
    return []
  }

  return readdirSync(directory)
    .filter((name) => name.toLowerCase().endsWith(suffix))
    .sort(compareOrdinal)
    .map((name) => path.join(directory, name))
}

/** 确保目录存在。 */
function ensureDir(directory) {
  mkdirSync(directory, { recursive: true })
}

/** 清空目录（不存在则忽略），保证脚本可重复运行。 */
function resetDir(directory) {
  rmSync(directory, { recursive: true, force: true })
  ensureDir(directory)
}

/** 把目录整体移动到目标位置（先清目标，Windows 下 rename 不能覆盖已存在目录）。 */
function moveDir(source, target) {
  rmSync(target, { recursive: true, force: true })
  renameSync(source, target)
}

/** 写 JSON 文件（带缩进，便于人工查看差异）。 */
function writeJsonFile(filePath, value) {
  writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
}

/** 读 JSON 文件。 */
function readJsonFile(filePath) {
  return JSON.parse(readFileSync(filePath, 'utf8'))
}

/** 取输出末尾若干行（dotnet 失败时定位用）。 */
function tailLines(text, count) {
  const lines = String(text ?? '')
    .split(/\r?\n/)
    .filter((line) => line.trim().length > 0)

  return lines.slice(Math.max(0, lines.length - count)).join('\n')
}

/** 把任意值格式化成单行文本（差异打印用）。 */
function formatValue(value) {
  if (value === undefined) {
    return '<缺失>'
  }

  const text = JSON.stringify(value) ?? String(value)
  return text.length > 300 ? `${text.slice(0, 300)}…` : text
}

/**
 * 按 zip 本地文件头顺序列出条目名。
 *
 * 与 `web/tests/conformance/desktop-golden.test.ts` 的口径一致：直接扫 `0x04034b50`
 * 本地文件头，避免依赖中央目录顺序（fflate 的 `unzipSync` 会丢顺序信息）。
 */
function listLocalEntryNames(bytes) {
  const names = []
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)

  for (let offset = 0; offset + 30 <= bytes.byteLength; offset += 1) {
    if (view.getUint32(offset, true) !== 0x04034b50) {
      continue
    }

    const nameLength = view.getUint16(offset + 26, true)
    const nameStart = offset + 30
    const nameEnd = nameStart + nameLength
    if (nameEnd > bytes.byteLength) {
      break
    }

    names.push(new TextDecoder('utf-8').decode(bytes.subarray(nameStart, nameEnd)))
    offset = nameEnd - 1
  }

  return names
}

// ---------------------------------------------------------------------------
// dump 构建：网页端（读结果 → dump）
// ---------------------------------------------------------------------------

/** 由 `readWbix` 的结果构造规范化 dump。 */
function buildDumpFromRead(result) {
  const { workspace, manifest } = result

  return sortedObject({
    format: manifest.format,
    version: manifest.version,
    createdUtc: manifest.createdUtc === null ? null : CREATED_UTC_PLACEHOLDER,
    currentIndex: workspace.currentIndex,
    viewport: buildViewportDump(
      workspace.viewportCameraWorld,
      workspace.viewportZoom,
      workspace.viewportSizeDip,
    ),
    pages: workspace.pages.map((page) =>
      sortedObject({
        id: page.id,
        strokes: page.strokes.map(buildInkItemDump),
        elements: page.elements.map(buildElementDump),
      }),
    ),
    resources: manifest.resources.map(buildResourceDump),
  })
}

/** 构造 `viewport` 节点。 */
function buildViewportDump(cameraWorld, zoom, sizeDip) {
  return sortedObject({
    cameraWorld: cameraWorld === null || cameraWorld === undefined ? null : buildVec2Dump(cameraWorld),
    zoom: zoom === null || zoom === undefined ? null : f32Text(zoom),
    sizeDip: sizeDip === null || sizeDip === undefined ? null : buildVec2Dump(sizeDip),
  })
}

/** 构造单个墨迹条目节点。 */
function buildInkItemDump(item) {
  if (item.kind === 'stroke') {
    return sortedObject({
      kind: item.kind,
      points: item.points.map((point) =>
        sortedObject({
          position: buildVec2Dump(point),
          pressure: f32Text(point.pressure),
        }),
      ),
      colorRgba: buildVec4Dump(item.colorRgba),
      baseSize: f32Text(item.baseSize),
      enablePressure: item.enablePressure,
    })
  }

  return sortedObject({
    kind: item.kind,
    start: buildVec2Dump(item.start),
    end: buildVec2Dump(item.end),
    colorRgba: buildVec4Dump(item.colorRgba),
    width: f32Text(item.width),
  })
}

/**
 * 构造单个元素节点。
 *
 * 注意：media 的 `sourcePath` 不输出（见文件头 dump 规范第 7 条），`resourceId` 保留。
 */
function buildElementDump(element) {
  const base = {
    id: element.id,
    layer: element.layer,
    order: element.order,
    positionWorld: buildVec2Dump(element.positionWorld),
    sizeWorld: buildVec2Dump(element.sizeWorld),
  }

  switch (element.type) {
    case 'text':
      return sortedObject({ ...base, type: 'text', text: element.text ?? '' })

    case 'link':
      return sortedObject({ ...base, type: 'link', url: element.url ?? '', title: element.title ?? null })

    case 'media':
      return sortedObject({
        ...base,
        type: 'media',
        kind: element.kind ?? 'video',
        displayName: element.displayName ?? '',
        resourceId: element.resourceId ?? null,
      })

    case 'file':
      return sortedObject({
        ...base,
        type: 'file',
        sourcePath: element.sourcePath ?? '',
        displayName: element.displayName ?? '',
      })

    default:
      return sortedObject({ ...base, type: 'unknown' })
  }
}

/** 构造单个资源节点。 */
function buildResourceDump(entry) {
  return sortedObject({
    id: entry.id,
    type: entry.type,
    path: entry.path,
    contentType: entry.contentType,
    meta: entry.meta === null || entry.meta === undefined ? null : sortedObject(entry.meta),
  })
}

// ---------------------------------------------------------------------------
// 语料 model → 工作区（C 步：网页端写包）
// ---------------------------------------------------------------------------

/** 由 model 的 `resources` 构造可写资源列表（含 base64 解码后的字节）。 */
function modelResources(model) {
  const list = Array.isArray(model.resources) ? model.resources : []

  return list.map((resource) => {
    const entry = {
      id: typeof resource.id === 'string' ? resource.id : '',
      type: typeof resource.type === 'string' ? resource.type : '',
      path: typeof resource.path === 'string' ? resource.path : '',
      contentType: typeof resource.contentType === 'string' ? resource.contentType : '',
      meta: isPlainObject(resource.meta) ? { ...resource.meta } : null,
    }
    const bytes = typeof resource.base64 === 'string' ? Buffer.from(resource.base64, 'base64') : Buffer.alloc(0)

    return { entry, bytes: new Uint8Array(bytes) }
  })
}

/** 取元素的 `data` 节点（缺失时按空对象处理，与两端读侧一致）。 */
function elementData(element) {
  return isPlainObject(element?.data) ? element.data : {}
}

/** 取元素所在层（缺省 belowInk，与两端读侧一致）。 */
function elementLayer(data) {
  const text = typeof data.layer === 'string' ? data.layer.trim() : ''
  return text.toLowerCase() === 'aboveink' ? 'aboveInk' : 'belowInk'
}

/**
 * 把 model 里 `order < 0` 的元素预规范化为「该层数组下标」。
 *
 * 为什么要这一步：两端写侧都有 `order = order < 0 ? 该层数组下标 : order` 的兜底
 * （桌面 `WbixWorkspaceSerializer.Save.cs` 的 `AppendElements`、网页端 `writeElements`）。
 * 语料 model 是"直接构造的快照"，而读侧会按 `(order, 原始下标)` 排序——若不先规范化，
 * `order: -1` 的元素会被读侧排到该层最前，与桌面端 golden 文本（层内保持 model 数组顺序）
 * 不一致。先按层内下标把负数抹平，读侧排序就退化为恒等，从而与桌面端快照顺序一致。
 */
function normalizeNegativeElementOrders(elements) {
  const groupIndex = { belowInk: 0, aboveInk: 0 }

  return elements.map((element, index) => {
    const data = elementData(element)
    const layer = elementLayer(data)
    const positionInLayer = groupIndex[layer]
    groupIndex[layer] += 1

    const order = typeof data.order === 'number' ? data.order : index
    if (order >= 0) {
      return element
    }

    return { ...element, data: { ...data, order: positionInLayer } }
  })
}

/** 由 model 的页面 payload 构造网页端工作区。 */
function workspaceFromModel(model) {
  const resources = modelResources(model)
  const resourceById = new Map(
    resources.map((resource) => [resource.entry.id.toLowerCase(), { ...resource.entry, bytes: resource.bytes }]),
  )
  const embeddedImageIds = embeddedImageResourceIds(resources)
  const warnings = []
  const context = {
    version: WBIX_CURRENT_VERSION,
    resolveResource: (resourceId) => resourceById.get(String(resourceId).toLowerCase()) ?? null,
    warn: (code, detail) => warnings.push(`${code}: ${detail}`),
  }

  const modelPages = Array.isArray(model.pages) ? model.pages : []
  const pages = modelPages.map((page) => {
    const payload = { ...page, elements: normalizeNegativeElementOrders(Array.isArray(page.elements) ? page.elements : []) }
    const parsed = readPagePayload(JSON.stringify(payload), context)

    return {
      ...parsed,
      elements: applyEmbeddedMediaResourceIds(parsed.elements, embeddedImageIds),
    }
  })

  if (warnings.length > 0) {
    throw new InteropFailure(`✗ C 失败：语料 model 解析出现告警，说明适配层与读侧口径不一致\n  ${warnings.join('\n  ')}`)
  }

  return {
    pages,
    currentIndex: clampCurrentIndex(typeof model.currentIndex === 'number' ? model.currentIndex : 0, pages.length),
    viewportCameraWorld: optionalVec2(model.viewportCameraWorld),
    viewportZoom: optionalF32(model.viewportZoom),
    viewportSizeDip: optionalVec2(model.viewportSizeDip),
  }
}

/** 可选 `{x,y}`：null/undefined 保持 null，其余转 float32。 */
function optionalVec2(value) {
  if (value === null || value === undefined) {
    return null
  }

  return { x: f32(value.x), y: f32(value.y) }
}

/** 可选 float32。 */
function optionalF32(value) {
  return value === null || value === undefined ? null : f32(value)
}

/**
 * 按资源 `meta` 推导「内嵌图片 → resourceId」映射。
 *
 * 镜像桌面写侧 `WbixWorkspaceSerializer.Save.cs` 的 `BuildEmbeddedImageResourceIdMap`：
 * `meta.role == "elementImage"`（忽略大小写）+ `meta.elementId` 命中元素 id 时，
 * 该元素写成 `resourceId = 资源 id` 且 `sourcePath = null`。
 * 网页端 `writeWbix` 不做这层推导（它只负责序列化工作区），因此适配层必须补齐。
 */
function embeddedImageResourceIds(resources) {
  const map = new Map()

  for (const resource of resources) {
    const meta = resource.entry.meta
    if (meta === null) {
      continue
    }

    const role = meta.role
    const elementId = meta.elementId
    if (typeof role !== 'string' || role.toLowerCase() !== 'elementimage' || typeof elementId !== 'string') {
      continue
    }

    map.set(elementId.toLowerCase(), resource.entry.id)
  }

  return map
}

/** 把内嵌图片元素的 `resourceId`/`sourcePath` 对齐桌面写侧语义。 */
function applyEmbeddedMediaResourceIds(elements, embeddedImageIds) {
  return elements.map((element) => {
    if (element.type !== 'media' || element.kind !== 'image') {
      return element
    }

    const resourceId = embeddedImageIds.get(element.id.toLowerCase())
    if (resourceId === undefined) {
      return element
    }

    return { ...element, resourceId, sourcePath: null }
  })
}

// ---------------------------------------------------------------------------
// dump 构建：由 model 独立推出期望 dump（D 步的基准）
// ---------------------------------------------------------------------------

/**
 * 由 model 直接推出"桌面端读网页端写出的包"应当得到的 dump。
 *
 * 刻意不复用 `readPagePayload`：这条路径是**独立实现**，用于交叉验证两端读侧
 * 是否把 model 表达的语义原样读了出来（例如 `order` 归一、层内顺序、resourceId 推导）。
 */
function expectedDumpFromModel(model) {
  const resources = modelResources(model)
  const embeddedImageIds = embeddedImageResourceIds(resources)
  const modelPages = Array.isArray(model.pages) ? model.pages : []

  return sortedObject({
    format: WBIX_FORMAT,
    version: WBIX_CURRENT_VERSION,
    createdUtc: CREATED_UTC_PLACEHOLDER,
    currentIndex: clampCurrentIndex(typeof model.currentIndex === 'number' ? model.currentIndex : 0, modelPages.length),
    viewport: buildViewportDump(
      optionalVec2(model.viewportCameraWorld),
      optionalF32(model.viewportZoom),
      optionalVec2(model.viewportSizeDip),
    ),
    pages: modelPages.map((page) =>
      sortedObject({
        id: String(page.id ?? '').toLowerCase(),
        strokes: (Array.isArray(page.strokes) ? page.strokes : []).map(buildModelInkItemDump),
        elements: orderedModelElements(Array.isArray(page.elements) ? page.elements : [], embeddedImageIds),
      }),
    ),
    resources: resources.map((resource) => buildResourceDump(resource.entry)),
  })
}

/** 由 model 的包装态笔迹条目构造 dump 节点。 */
function buildModelInkItemDump(item) {
  const kind = typeof item?.kind === 'string' ? item.kind : ''

  if (isPlainObject(item?.stroke)) {
    const stroke = item.stroke
    const points = Array.isArray(stroke.points) ? stroke.points : []

    return sortedObject({
      kind,
      points: points.map((point) =>
        sortedObject({
          position: buildVec2Dump(point?.position),
          pressure: f32Text(point?.pressure),
        }),
      ),
      colorRgba: buildVec4Dump(stroke.colorRgba),
      baseSize: f32Text(stroke.baseSize),
      enablePressure: stroke.enablePressure === true,
    })
  }

  const shape = isPlainObject(item?.shape) ? item.shape : {}
  return sortedObject({
    kind,
    start: buildVec2Dump(shape.start),
    end: buildVec2Dump(shape.end),
    colorRgba: buildVec4Dump(shape.colorRgba),
    width: f32Text(shape.width),
  })
}

/** 按「先 belowInk 后 aboveInk、层内 (order, 原始下标) 排序」排列 model 元素并构造 dump 节点。 */
function orderedModelElements(elements, embeddedImageIds) {
  const normalized = normalizeNegativeElementOrders(elements)
  const below = []
  const above = []

  normalized.forEach((element, index) => {
    const data = elementData(element)
    const layer = elementLayer(data)
    const order = typeof data.order === 'number' ? data.order : index
    const pending = { element, data, index, order, layer }
    if (layer === 'aboveInk') {
      above.push(pending)
    } else {
      below.push(pending)
    }
  })

  const byOrderThenIndex = (left, right) => left.order - right.order || left.index - right.index
  below.sort(byOrderThenIndex)
  above.sort(byOrderThenIndex)

  return [...below, ...above].map((pending) =>
    buildModelElementDump(pending.element, pending.data, pending.layer, pending.order, embeddedImageIds),
  )
}

/** 由 model 元素构造 dump 节点（media 的 sourcePath 不输出）。 */
function buildModelElementDump(element, data, layer, order, embeddedImageIds) {
  const type = typeof element?.type === 'string' ? element.type.trim().toLowerCase() : ''
  const id = String(data.id ?? '').toLowerCase()
  const base = {
    id,
    layer,
    order,
    positionWorld: buildVec2Dump(data.positionWorld ?? { x: 0, y: 0 }),
    sizeWorld: buildVec2Dump(data.sizeWorld ?? DEFAULT_ELEMENT_SIZE),
  }

  switch (type) {
    case 'text':
      return sortedObject({ ...base, type, text: typeof data.text === 'string' ? data.text : '' })

    case 'link':
      return sortedObject({
        ...base,
        type,
        url: typeof data.url === 'string' ? data.url : '',
        title: typeof data.title === 'string' ? data.title : null,
      })

    case 'media': {
      const derivedResourceId = embeddedImageIds.get(id)
      const resourceId =
        derivedResourceId ?? (typeof data.resourceId === 'string' ? data.resourceId : null)
      return sortedObject({
        ...base,
        type,
        kind: mediaKindText(data.kind),
        displayName: typeof data.displayName === 'string' ? data.displayName : '',
        resourceId,
      })
    }

    case 'file':
      return sortedObject({
        ...base,
        type,
        sourcePath: typeof data.sourcePath === 'string' ? data.sourcePath : '',
        displayName: typeof data.displayName === 'string' ? data.displayName : '',
      })

    default:
      return sortedObject({ ...base, type: 'unknown' })
  }
}

/** 媒体种类文本（未知值按桌面读侧兜底为 video）。 */
function mediaKindText(value) {
  const text = typeof value === 'string' ? value.trim().toLowerCase() : ''
  return text === 'image' || text === 'audio' ? text : 'video'
}

// ---------------------------------------------------------------------------
// 差异比对
// ---------------------------------------------------------------------------

/** 构造一条差异记录。 */
function createDiff(at, actual, expected) {
  return { path: at.length === 0 ? '(根)' : at, actual, expected }
}

/**
 * 递归比对两个 dump，返回第一条差异（无差异返回 null）。
 *
 * 口径：对象键集合与键序都比对，数组按序逐项比对，标量用 `Object.is`（`-0` 与 `0` 视为不同）。
 */
function compareValues(actual, expected, at) {
  const actualIsArray = Array.isArray(actual)
  const expectedIsArray = Array.isArray(expected)

  if (actualIsArray || expectedIsArray) {
    if (!actualIsArray || !expectedIsArray) {
      return createDiff(at, actual, expected)
    }

    if (actual.length !== expected.length) {
      return createDiff(`${at}.length`, actual.length, expected.length)
    }

    for (let index = 0; index < expected.length; index += 1) {
      const diff = compareValues(actual[index], expected[index], `${at}[${index}]`)
      if (diff !== null) {
        return diff
      }
    }

    return null
  }

  const actualIsObject = isPlainObject(actual)
  const expectedIsObject = isPlainObject(expected)

  if (actualIsObject || expectedIsObject) {
    if (!actualIsObject || !expectedIsObject) {
      return createDiff(at, actual, expected)
    }

    const actualKeys = Object.keys(actual)
    const expectedKeys = Object.keys(expected)
    if (actualKeys.join('\u0000') !== expectedKeys.join('\u0000')) {
      // 键集合不同，或键序不同（dump 规范要求键序数排序，键序本身参与比对）。
      const missing = expectedKeys.filter((key) => !actualKeys.includes(key))
      const extra = actualKeys.filter((key) => !expectedKeys.includes(key))
      const notes = [
        missing.length === 0 ? '' : `缺失 ${missing.join(',')}`,
        extra.length === 0 ? '' : `多出 ${extra.join(',')}`,
      ].filter((note) => note.length > 0)

      return {
        path: at.length === 0 ? '(根) 的键序' : `${at} 的键序`,
        actual: notes.length === 0 ? actualKeys.join(',') : `${actualKeys.join(',')}（${notes.join('；')}）`,
        expected: expectedKeys.join(','),
      }
    }

    for (const key of expectedKeys) {
      const diff = compareValues(actual[key], expected[key], at.length === 0 ? key : `${at}.${key}`)
      if (diff !== null) {
        return diff
      }
    }

    return null
  }

  if (!Object.is(actual, expected)) {
    return createDiff(at, actual, expected)
  }

  return null
}

/** 断言两个 dump 相等，否则抛出带路径与两侧取值的失败。 */
function assertDumpsEqual(step, packageFileName, actual, expected, actualLabel, expectedLabel) {
  const diff = compareValues(actual, expected, '')
  if (diff === null) {
    return
  }

  const lines = [
    `✗ ${step} 失败：${packageFileName}`,
    `  路径：${diff.path}`,
    `  ${actualLabel}：${formatValue(diff.actual)}`,
    `  ${expectedLabel}：${formatValue(diff.expected)}`,
  ]

  throw new InteropFailure(lines.join('\n'))
}

// ---------------------------------------------------------------------------
// 桌面端调用
// ---------------------------------------------------------------------------

/** 构建测试程序集（`--no-build` 的前提）。 */
function buildTestAssembly() {
  console.log('· 构建 WindBoard.Tests（Debug / x64）…')
  try {
    execFileSync(
      'dotnet',
      ['build', 'WindBoard.slnx', '-p:Platform=x64', '-v', 'quiet', '--nologo'],
      { cwd: REPO_ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 * 1024 * 1024 },
    )
  } catch (error) {
    const detail = `${error.stdout ?? ''}${error.stderr ?? ''}`.trim() || error.message
    throw new InteropFailure(`✗ 构建失败：dotnet build WindBoard.slnx\n${tailLines(detail, 40)}`)
  }
}

/**
 * 调用桌面端 dump 生成器。
 *
 * @param {'read'|'rewrite'} mode
 * @param {string} directory 待处理包所在目录（绝对路径；桌面端要求 Windows 形态）
 */
function runDotnetDump(mode, directory) {
  const args = [
    'test',
    'WindBoard.slnx',
    '--filter',
    'FullyQualifiedName~WbixInteropDumpTests',
    '--no-build',
    '--nologo',
    '-v',
    'quiet',
    '-p:Platform=x64',
  ]
  const env = {
    ...process.env,
    [INTEROP_DIR_VARIABLE]: directory,
    [INTEROP_MODE_VARIABLE]: mode,
  }

  let output = ''
  try {
    output = execFileSync('dotnet', args, {
      cwd: REPO_ROOT,
      env,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      maxBuffer: 64 * 1024 * 1024,
    })
  } catch (error) {
    const detail = `${error.stdout ?? ''}${error.stderr ?? ''}`.trim() || error.message
    throw new InteropFailure(`✗ 桌面端 dump 生成失败（mode=${mode}）：\n${tailLines(detail, 40)}`)
  }

  const dumpDir = path.join(directory, CS_DUMP_FOLDER)
  if (!existsSync(dumpDir)) {
    throw new InteropFailure(
      `✗ 桌面端未产出 ${CS_DUMP_FOLDER}/（mode=${mode}）：测试可能被跳过，检查 ${INTEROP_DIR_VARIABLE} 是否传入\n${tailLines(output, 20)}`,
    )
  }

  return output
}

// ---------------------------------------------------------------------------
// 步骤
// ---------------------------------------------------------------------------

/** 把 fixtures 与 produced 的包复制到同一目录，保证两端读的是同一份字节。 */
function stageInputs() {
  resetDir(STAGE_READ_DIR)
  const sources = [...listFiles(FIXTURE_FILES_DIR, '.wbix'), ...listFiles(PRODUCED_DIR, '.wbix')]
  if (sources.length === 0) {
    throw new InteropFailure('✗ 没有可用的输入包：语料目录与 web/.interop/produced/ 都为空')
  }

  const names = []
  for (const source of sources) {
    const name = path.basename(source)
    if (names.includes(name)) {
      throw new InteropFailure(`✗ 输入包重名：${name}（fixtures 与 produced 下不能同名）`)
    }

    names.push(name)
    copyFileSync(source, path.join(STAGE_READ_DIR, name))
  }

  return names
}

/** A：网页端读包 → dump-ts。 */
function stepA(packageNames) {
  const dumps = new Map()
  resetDir(DUMP_TS_DIR)

  for (const name of packageNames) {
    const bytes = new Uint8Array(readFileSync(path.join(STAGE_READ_DIR, name)))
    const dump = buildDumpFromRead(readWbix(bytes))
    dumps.set(name, dump)
    writeJsonFile(path.join(DUMP_TS_DIR, `${path.parse(name).name}.json`), dump)
  }

  console.log(`✓ A 网页端读取 ${packageNames.length} 个包 → dump-ts/`)
  return dumps
}

/** B：桌面端读同一批包 → dump-cs，与 A 逐字段比对。 */
function stepB(packageNames, dumpsTs) {
  runDotnetDump(READ_MODE, STAGE_READ_DIR)
  moveDir(path.join(STAGE_READ_DIR, CS_DUMP_FOLDER), DUMP_CS_DIR)

  const dumpsCs = new Map()
  for (const name of packageNames) {
    const dumpPath = path.join(DUMP_CS_DIR, `${path.parse(name).name}.json`)
    if (!existsSync(dumpPath)) {
      throw new InteropFailure(`✗ B 失败：桌面端没有产出 ${name} 的 dump（${dumpPath}）`)
    }

    dumpsCs.set(name, readJsonFile(dumpPath))
  }

  for (const name of packageNames) {
    assertDumpsEqual('B', name, dumpsCs.get(name), dumpsTs.get(name), 'C#', 'TS')
  }

  console.log(`✓ B 桌面端读取同 ${packageNames.length} 个包，dump 与 A 逐字段一致`)
  return dumpsCs
}

/**
 * 与 `expected/<model>/**` 逐字节比对（C 步的附加断言）。
 *
 * 口径：条目名与顺序必须完全一致；`expected/**` 里存在的条目逐字节比较，
 * JSON 文本先把 `<createdUtc>` 占位符替换成本次写入的固定文本。
 */
function assertMatchesExpectedText(modelName, bytes) {
  const expectedDir = path.join(FIXTURE_EXPECTED_DIR, modelName)
  const entriesFile = path.join(expectedDir, ENTRIES_FILE)
  if (!existsSync(entriesFile)) {
    return false
  }

  const expectedEntries = readFileSync(entriesFile, 'utf8')
    .split(/\r?\n/)
    .filter((line) => line.trim().length > 0)
  const actualEntries = listLocalEntryNames(bytes)
  if (actualEntries.join('\n') !== expectedEntries.join('\n')) {
    throw new InteropFailure(
      [
        `✗ C 失败：${modelName}.wbix 的 zip 条目名/顺序与 expected/${modelName}/entries.txt 不一致`,
        `  实际：${actualEntries.join(' | ')}`,
        `  期望：${expectedEntries.join(' | ')}`,
      ].join('\n'),
    )
  }

  const entryBytes = unzipSync(bytes)
  for (const entryName of expectedEntries) {
    const expectedPath = path.join(expectedDir, ...entryName.split('/'))
    if (!existsSync(expectedPath)) {
      // 二进制资源（assets/**）不在 expected/ 里，跳过内容比对。
      continue
    }

    const expectedText = readFileSync(expectedPath, 'utf8')
    const normalized = entryName.toLowerCase().endsWith('.json')
      ? expectedText.split(CREATED_UTC_PLACEHOLDER).join(CREATED_UTC_TEXT)
      : expectedText
    const expectedBytes = Buffer.from(normalized, 'utf8')
    const actual = Buffer.from(entryBytes[entryName] ?? new Uint8Array())

    if (!actual.equals(expectedBytes)) {
      const limit = Math.min(actual.length, expectedBytes.length)
      let offset = 0
      while (offset < limit && actual[offset] === expectedBytes[offset]) {
        offset += 1
      }

      throw new InteropFailure(
        [
          `✗ C 失败：${modelName}.wbix 的 ${entryName} 与 expected/${modelName}/${entryName} 不一致`,
          `  首个差异字节偏移：${offset}`,
          `  实际：${formatValue(actual.subarray(Math.max(0, offset - 40), offset + 60).toString('utf8'))}`,
          `  期望：${formatValue(expectedBytes.subarray(Math.max(0, offset - 40), offset + 60).toString('utf8'))}`,
        ].join('\n'),
      )
    }
  }

  return true
}

/** C：网页端由 model 写包 → produced-ts，并与 expected/** 逐字节比对。 */
function stepC() {
  const modelFiles = listFiles(FIXTURE_MODELS_DIR, '.json')
  if (modelFiles.length === 0) {
    throw new InteropFailure(`✗ C 失败：${FIXTURE_MODELS_DIR} 下没有 model 文件`)
  }

  resetDir(PRODUCED_TS_DIR)
  const produced = new Map()
  let compared = 0

  for (const modelFile of modelFiles) {
    const modelName = path.parse(modelFile).name
    const model = readJsonFile(modelFile)
    const resources = modelResources(model)
    const workspace = workspaceFromModel(model)
    const bytes = writeWbix(workspace, {
      createdUtcText: CREATED_UTC_TEXT,
      resources: resources.map((resource) => ({ entry: resource.entry, bytes: resource.bytes })),
    })

    writeFileSync(path.join(PRODUCED_TS_DIR, `${modelName}.wbix`), bytes)
    produced.set(modelName, bytes)

    if (assertMatchesExpectedText(modelName, bytes)) {
      compared += 1
    }
  }

  console.log(
    `✓ C 网页端由 ${modelFiles.length} 个 model 写出 produced-ts/（其中 ${compared} 个与 expected/** 逐字节一致）`,
  )
  return produced
}

/** D：桌面端读 produced-ts → dump，与由 model 推出的期望 dump 比对。 */
function stepD(produced) {
  runDotnetDump(REWRITE_MODE, PRODUCED_TS_DIR)
  moveDir(path.join(PRODUCED_TS_DIR, CS_DUMP_FOLDER), DUMP_CS_MODEL_DIR)
  moveDir(path.join(PRODUCED_TS_DIR, CS_REWRITTEN_FOLDER), REWRITTEN_CS_DIR)

  const dumpsCs = new Map()
  for (const modelName of produced.keys()) {
    const dumpPath = path.join(DUMP_CS_MODEL_DIR, `${modelName}.json`)
    if (!existsSync(dumpPath)) {
      throw new InteropFailure(`✗ D 失败：桌面端没有产出 ${modelName}.wbix 的 dump（${dumpPath}）`)
    }

    const dump = readJsonFile(dumpPath)
    dumpsCs.set(modelName, dump)

    const model = readJsonFile(path.join(FIXTURE_MODELS_DIR, `${modelName}.json`))
    assertDumpsEqual('D', `${modelName}.wbix`, dump, expectedDumpFromModel(model), 'C#', '期望')
  }

  console.log(`✓ D 桌面端读回 ${produced.size} 个网页端写出的包，dump 与由 model 推出的期望一致（P0.4）`)
  return dumpsCs
}

/** E：网页端读桌面端重写后的包 → dump，与 D 的 dump 比对。 */
function stepE(dumpsCs) {
  resetDir(DUMP_TS_REWRITTEN_DIR)

  for (const [modelName, expectedDump] of dumpsCs) {
    const packagePath = path.join(REWRITTEN_CS_DIR, `${modelName}.wbix`)
    if (!existsSync(packagePath)) {
      throw new InteropFailure(`✗ E 失败：桌面端没有产出 ${packagePath}`)
    }

    const dump = buildDumpFromRead(readWbix(new Uint8Array(readFileSync(packagePath))))
    writeJsonFile(path.join(DUMP_TS_REWRITTEN_DIR, `${modelName}.json`), dump)
    assertDumpsEqual('E', `${modelName}.wbix`, dump, expectedDump, 'TS', 'C#')
  }

  console.log(`✓ E 网页端读回 ${dumpsCs.size} 个桌面端重写包，dump 与 D 一致`)
}

// ---------------------------------------------------------------------------
// 入口
// ---------------------------------------------------------------------------

/** 清理本脚本自己的输出目录（不触碰 `web/.interop/produced/`）。 */
function cleanOutputs() {
  for (const directory of [
    STAGE_READ_DIR,
    DUMP_TS_DIR,
    DUMP_CS_DIR,
    DUMP_CS_MODEL_DIR,
    PRODUCED_TS_DIR,
    REWRITTEN_CS_DIR,
    DUMP_TS_REWRITTEN_DIR,
  ]) {
    rmSync(directory, { recursive: true, force: true })
  }
}

/** 主流程。 */
function main() {
  const startedAt = Date.now()

  if (listFiles(PRODUCED_DIR, '.wbix').length === 0) {
    throw new InteropFailure('✗ web/.interop/produced/ 下没有样本，请先运行 `pnpm samples`')
  }

  cleanOutputs()
  ensureDir(INTEROP_ROOT)

  if (process.env.WINDBOARD_INTEROP_SKIP_BUILD === '1') {
    console.log('· 跳过 dotnet build（WINDBOARD_INTEROP_SKIP_BUILD=1）')
  } else {
    buildTestAssembly()
  }

  const packageNames = stageInputs()
  const dumpsTs = stepA(packageNames)
  stepB(packageNames, dumpsTs)
  const produced = stepC()
  const dumpsCs = stepD(produced)
  stepE(dumpsCs)

  const seconds = ((Date.now() - startedAt) / 1000).toFixed(1)
  console.log(`✓ 全部通过：A~E 五步（${seconds}s）`)
}

try {
  main()
} catch (error) {
  if (error instanceof InteropFailure) {
    console.error(error.message)
  } else {
    console.error(`✗ 未预期的错误：${error instanceof Error ? error.stack ?? error.message : String(error)}`)
  }

  process.exitCode = 1
}
