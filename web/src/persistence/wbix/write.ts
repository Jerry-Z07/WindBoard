import { zipSync } from 'fflate'
import type { WorkspaceSnapshot } from '../../board/model/workspace.ts'
import { WbixError } from './errors.ts'
import { encodeUtf8 } from './json-options.ts'
import {
  WBIX_ASSETS_FOLDER,
  WBIX_CURRENT_VERSION,
  WBIX_FORMAT,
  WBIX_MANIFEST_ENTRY,
  pageEntryName,
  writeManifestJson,
} from './manifest.ts'
import type { WbixManifest, WbixResourceEntry } from './manifest.ts'
import { writePagePayloadJson } from './page-payload.ts'
import { isSafeZipPath, normalizeZipPath } from './paths.ts'

/**
 * `.wbix` 写出入口。
 *
 * 契约（design §9，对齐桌面 `WbixWorkspaceSerializer.SaveAsync`）：
 * - 固定写 `version = 3`；
 * - zip 条目顺序：全部 `pages/page-XXX.json` → `assets/...` → **`manifest.json` 最后**；
 * - 页 payload 与 manifest 的文本形态由 `json-options` / `f32-format` 逐字符控制；
 * - 空资源写 `"resources": []`。
 */

/** 附加资源（P0 阶段不产出内嵌图片，接口预留给 P4 的媒体导入）。 */
export interface WbixWriteResource {
  entry: WbixResourceEntry
  bytes: Uint8Array
}

/** 写出选项。 */
export interface WbixWriteOptions {
  /** `manifest.createdUtc`（默认取当前时间，便于测试固定值）。 */
  createdUtc?: Date
  /**
   * 直接写入的 `createdUtc` 原始文本（含亚毫秒小数位）。
   *
   * 用途：读入桌面写出的文件后再写回时，`Date` 的毫秒精度会丢掉后 4 位小数，
   * 传 `manifest.createdUtcText` 即可逐字节还原。提供时以本字段为准。
   */
  createdUtcText?: string
  /** 需要写入 `assets/` 的资源。 */
  resources?: WbixWriteResource[]
}

/**
 * zip 压缩级别。
 *
 * 说明：桌面用 `CompressionLevel.Optimal`（.NET 内部约等于级别 6），
 * 但 zip 容器的元数据（时间戳、压缩实现）本来就不保证字节一致，
 * 因此这里只求"体积合理 + 桌面可读"，字节级一致由 JSON 文本层保证。
 */
const ZIP_LEVEL = 6

/**
 * 写出 `.wbix` 字节。
 *
 * @throws WbixError `invalid-value`（页 id 非法、资源路径不安全、条目重名等）
 */
export function writeWbix(workspace: WorkspaceSnapshot, options: WbixWriteOptions = {}): Uint8Array {
  const files: Record<string, Uint8Array> = {}
  const addEntry = (entryName: string, data: Uint8Array): void => {
    if (Object.prototype.hasOwnProperty.call(files, entryName)) {
      throw new WbixError('invalid-value', `zip 条目重名：${entryName}`)
    }
    files[entryName] = data
  }

  // 1) 页（顺序即 manifest.pages 顺序）
  for (let index = 0; index < workspace.pages.length; index += 1) {
    addEntry(pageEntryName(index), encodeUtf8(writePagePayloadJson(workspace.pages[index])))
  }

  // 2) 资源（assets/ 下）
  const resources = options.resources ?? []
  for (const resource of resources) {
    const zipPath = normalizeZipPath(resource.entry.path)
    if (!isSafeZipPath(zipPath, `${WBIX_ASSETS_FOLDER}/`)) {
      throw new WbixError('invalid-value', `资源路径不安全：path='${resource.entry.path}'`)
    }
    addEntry(zipPath, resource.bytes)
  }

  // 3) manifest（必须最后写）
  const createdUtc = options.createdUtc ?? new Date()
  const manifest: WbixManifest = {
    format: WBIX_FORMAT,
    version: WBIX_CURRENT_VERSION,
    createdUtc,
    createdUtcText: options.createdUtcText ?? null,
    currentIndex: workspace.currentIndex,
    pages: workspace.pages.map((page, index) => ({
      id: page.id,
      index,
      path: pageEntryName(index),
    })),
    resources: resources.map((resource) => resource.entry),
    viewportCameraWorld: workspace.viewportCameraWorld,
    viewportZoom: workspace.viewportZoom,
    viewportSizeDip: workspace.viewportSizeDip,
  }
  addEntry(WBIX_MANIFEST_ENTRY, encodeUtf8(writeManifestJson(manifest, options.createdUtcText ?? null)))

  return zipSync(files, { level: ZIP_LEVEL })
}
