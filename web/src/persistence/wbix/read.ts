import { unzipSync } from 'fflate'
import type { WorkspaceSnapshot } from '../../board/model/workspace.ts'
import type { PageSnapshot } from '../../board/model/workspace.ts'
import { WbixError } from './errors.ts'
import type { WbixWarning, WbixWarningCode } from './errors.ts'
import { decodeUtf8 } from './json-options.ts'
import {
  WBIX_ASSETS_FOLDER,
  WBIX_MANIFEST_ENTRY,
  WBIX_PAGES_FOLDER,
  readManifest,
} from './manifest.ts'
import type { WbixManifest, WbixManifestPage, WbixResourceEntry } from './manifest.ts'
import { readPagePayload } from './page-payload.ts'
import type { PagePayloadReadContext, ResolvedResource } from './page-payload.ts'
import { isSafeZipPath, normalizeZipPath } from './paths.ts'

/**
 * `.wbix` 读取入口（zip → manifest → 页 → 资源）。
 *
 * 读取顺序与致命/容忍判定逐条对齐桌面 `WbixWorkspaceSerializer.LoadAsync`：
 * 1. 缺 `manifest.json` → 致命；zip 无法解析 → 致命；
 * 2. `format` 不是 `wbix` → 致命；`version` 越界 → 致命（版本契约唯一落点）；
 * 3. 页按 `manifest.pages[].index` 稳定排序后逐个读取；
 * 4. 页路径归一化 + 安全校验（必须 `pages/` 前缀、无 `..`）失败或条目缺失 → 致命；
 * 5. 页 payload 非法 → 致命；单笔迹条目 / 单元素问题按容忍策略跳过并记录告警。
 *
 * 说明：本实现一次性把 zip 解压到内存（P0 阶段的语料与真实文件都在可接受范围内），
 * 资源字节按需解析并缓存。
 */

/** 单个资源的大小上限（`MaxResourceBytes`，32 MiB）。 */
export const MAX_RESOURCE_BYTES = 32 * 1024 * 1024

/** 单次读取的资源总量上限（`MaxTotalExtractedBytes`，256 MiB）。 */
export const MAX_TOTAL_RESOURCE_BYTES = 256 * 1024 * 1024

/** 读取结果。 */
export interface WbixReadResult {
  workspace: WorkspaceSnapshot
  manifest: WbixManifest
  /** 已解析的资源（按 manifest 里的 id 大小写不敏感索引）。 */
  resources: Map<string, ResolvedResource>
  /** 容忍告警（按出现顺序）。 */
  warnings: WbixWarning[]
}

/** 页读取器（design D11：按 `manifest.version` 分派，注册顺序即匹配顺序）。 */
interface WbixPageReader {
  minVersion: number
  maxVersion: number
  read(text: string, context: PagePayloadReadContext): PageSnapshot
}

/**
 * 版本 → 页读取器。
 *
 * 说明：桌面侧 `WbixPageReaderV1V2` 与 `WbixPageReaderV3` 的差别只在
 * `InkItemReadMode`（是否对"v3 文件里的扁平条目"告警），两种模式都接受两种形态，
 * 因此这里共用同一自适应解码器，只按版本决定是否告警（见 `onLegacyFlatItem`）。
 */
const PAGE_READERS: readonly WbixPageReader[] = [
  { minVersion: 1, maxVersion: 2, read: readPagePayload },
  { minVersion: 3, maxVersion: 3, read: readPagePayload },
]

function pageReaderFor(version: number): WbixPageReader {
  for (const reader of PAGE_READERS) {
    if (version >= reader.minVersion && version <= reader.maxVersion) {
      return reader
    }
  }

  throw new WbixError('version-not-supported', `没有支持版本 ${version} 的页读取器`)
}

/**
 * 读取 `.wbix` 字节。
 *
 * @throws WbixError 致命错误（见模块注释）
 */
export function readWbix(bytes: Uint8Array): WbixReadResult {
  const entries = unzipEntries(bytes)
  const warnings: WbixWarning[] = []
  const warn = (code: WbixWarningCode, detail: string): void => {
    warnings.push({ code, detail })
  }

  const manifestBytes = entries.get(WBIX_MANIFEST_ENTRY)
  if (manifestBytes === undefined) {
    throw new WbixError('missing-manifest', `缺少 ${WBIX_MANIFEST_ENTRY}`)
  }

  const manifest = readManifest(decodeUtf8(manifestBytes))
  const pageReader = pageReaderFor(manifest.version)

  const resourceIndex = buildResourceIndex(manifest.resources)
  const resources = new Map<string, ResolvedResource>()
  let extractedBytes = 0
  let legacyFlatWarned = false

  const resolveResource = (resourceId: string): ResolvedResource | null => {
    const cached = resources.get(resourceId.toLowerCase())
    if (cached !== undefined) {
      return cached
    }

    const entry = resourceIndex.get(resourceId.toLowerCase())
    if (entry === undefined) {
      return null
    }

    if (entry.path.trim().length === 0) {
      return null
    }

    const zipPath = normalizeZipPath(entry.path)
    if (!isSafeZipPath(zipPath, `${WBIX_ASSETS_FOLDER}/`)) {
      warn('resource-path-unsafe', `资源路径不安全，已忽略：path='${entry.path}'`)
      return null
    }

    const data = entries.get(zipPath)
    if (data === undefined) {
      warn('resource-missing', `资源条目不存在，已忽略：path='${zipPath}'`)
      return null
    }

    if (data.byteLength <= 0 || data.byteLength > MAX_RESOURCE_BYTES) {
      warn('resource-too-large', `资源大小超限，已忽略：path='${zipPath}', bytes=${data.byteLength}`)
      return null
    }

    if (extractedBytes + data.byteLength > MAX_TOTAL_RESOURCE_BYTES) {
      warn(
        'resource-total-limit',
        `资源总大小超限，已忽略：path='${zipPath}', bytes=${data.byteLength}, total=${extractedBytes}`,
      )
      return null
    }

    extractedBytes += data.byteLength
    const resolved: ResolvedResource = {
      id: entry.id,
      path: zipPath,
      type: entry.type,
      contentType: entry.contentType,
      meta: entry.meta,
      bytes: data,
    }
    resources.set(resourceId.toLowerCase(), resolved)
    return resolved
  }

  const pages: PageSnapshot[] = []
  for (const pageEntry of sortPagesByIndex(manifest.pages)) {
    const pagePath = normalizeZipPath(pageEntry.path)
    if (!isSafeZipPath(pagePath, `${WBIX_PAGES_FOLDER}/`)) {
      throw new WbixError('missing-page-file', `页路径不安全：path='${pageEntry.path}'`)
    }

    const pageBytes = entries.get(pagePath)
    if (pageBytes === undefined) {
      throw new WbixError('missing-page-file', `页条目不存在：path='${pagePath}'`)
    }

    pages.push(
      pageReader.read(decodeUtf8(pageBytes), {
        version: manifest.version,
        resolveResource,
        warn,
        onLegacyFlatItem: () => {
          if (legacyFlatWarned) {
            return
          }
          legacyFlatWarned = true
          warn('ink-item-legacy-flat', 'v3 文件的笔迹条目中出现扁平形态，已按 kind=stroke 兼容读取')
        },
      }),
    )
  }

  const currentIndex = Math.min(Math.max(manifest.currentIndex, 0), Math.max(0, pages.length - 1))

  return {
    workspace: {
      pages,
      currentIndex,
      viewportCameraWorld: manifest.viewportCameraWorld,
      viewportZoom: manifest.viewportZoom,
      viewportSizeDip: manifest.viewportSizeDip,
    },
    manifest,
    resources,
    warnings,
  }
}

/** 解压 zip 为"条目名 → 字节"（条目名保持原样，查找大小写敏感）。 */
function unzipEntries(bytes: Uint8Array): Map<string, Uint8Array> {
  let unzipped: Record<string, Uint8Array>
  try {
    unzipped = unzipSync(bytes)
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    throw new WbixError('zip-read-failed', `zip 解析失败：${reason}`)
  }

  return new Map(Object.entries(unzipped))
}

/** 按 `index` 稳定排序（对齐桌面 `OrderBy(p => p.Index)`）。 */
function sortPagesByIndex(pages: WbixManifestPage[]): WbixManifestPage[] {
  return pages
    .map((page, position) => ({ page, position }))
    .sort((left, right) => {
      const byIndex = left.page.index - right.page.index
      return byIndex !== 0 ? byIndex : left.position - right.position
    })
    .map((item) => item.page)
}

/** 资源索引：id 大小写不敏感，重复保留第一个，空 id / 空路径跳过。 */
function buildResourceIndex(resources: WbixResourceEntry[]): Map<string, WbixResourceEntry> {
  const index = new Map<string, WbixResourceEntry>()
  for (const resource of resources) {
    if (resource.id.trim().length === 0 || resource.path.trim().length === 0) {
      continue
    }

    const key = resource.id.toLowerCase()
    if (!index.has(key)) {
      index.set(key, resource)
    }
  }

  return index
}
