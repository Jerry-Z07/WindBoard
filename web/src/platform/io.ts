/**
 * 浏览器文件能力（PlatformIO 的网页实现）。
 *
 * 设计要点：
 * - **能力分级**：优先 File System Access API（`showSaveFilePicker` / `showDirectoryPicker`，
 *   Chromium 系支持，用户可指定文件名与目录）；不支持时回退到 `<a download>` / `<input type=file>`，
 *   行为上等价（用户仍能拿到文件），只是无法指定目标目录、也无法读回写入结果。
 * - **取消与失败必须区分**：用户关掉系统选择器时抛 `AbortError`，这不是错误，
 *   返回 `{ ok: false, cancelled: true }`，调用方据此静默返回而不是弹错误提示。
 * - **node/SSR 安全**：所有能力探测都走 `typeof window`/特性检测，没有 DOM 时返回失败结果而不抛，
 *   这样纯逻辑与单测可以在 node 环境里跑。
 * - 不引入依赖：ZIP 降级复用已有的 `fflate`。
 */

import { zipSync } from 'fflate'

/** 保存单个文件的请求。 */
export interface SaveFileRequest {
  /** 建议文件名（**含扩展名**；FSA 会用它作为默认名，`<a download>` 直接用它）。 */
  readonly suggestedName: string
  /** MIME 类型（`image/png` / `application/pdf` / `application/zip` …）。 */
  readonly mimeType: string
  readonly bytes: Uint8Array
}

/** 保存结果。`cancelled` 与 `error` 互斥：前者是用户主动取消，不该报错。 */
export interface SaveResult {
  readonly ok: boolean
  readonly cancelled?: boolean
  readonly path?: string
  readonly error?: string
}

/** 选择并读取的文件。 */
export interface PickedFile {
  readonly name: string
  readonly bytes: Uint8Array
}

/** 文件系统句柄的最小类型（避免依赖 DOM lib 的具体声明，jsdom 里也能编译）。 */
interface WritableLike {
  write(data: Uint8Array): Promise<void>
  close(): Promise<void>
}
interface FileHandleLike {
  createWritable(): Promise<WritableLike>
}
interface SavePickerWindow {
  showSaveFilePicker?: (options: {
    suggestedName?: string
    types?: { description: string; accept: Record<string, string[]> }[]
  }) => Promise<FileHandleLike>
  showDirectoryPicker?: () => Promise<{
    name: string
    getDirectoryHandle(name: string, options?: { create?: boolean }): Promise<{
      getFileHandle(name: string, options?: { create?: boolean }): Promise<FileHandleLike>
    }>
  }>
}

/** 取窗口对象；没有 DOM 时返回 null（node/SSR）。 */
function getWindow(): (Window & typeof globalThis & SavePickerWindow) | null {
  if (typeof window === 'undefined') {
    return null
  }
  return window as Window & typeof globalThis & SavePickerWindow
}

/** 判断是否是用户取消（File System Access API 用 `AbortError`）。 */
function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'name' in error && (error as { name: unknown }).name === 'AbortError'
}

/**
 * 把任意异常转成人类可读的中文错误文本。
 *
 * 浏览器的 `DOMException` 只有英文 `message`（如 `File picker already active.`），
 * 直接展示给用户很突兀，所以按 `name` 归类给出中文前缀，再附原始信息便于排查。
 */
function describeError(error: unknown): string {
  if (typeof error === 'object' && error !== null && 'name' in error) {
    const name = (error as { name: unknown }).name
    const detail = error instanceof Error ? error.message : String(error)
    switch (name) {
      case 'SecurityError':
        return `浏览器拒绝了文件访问（需要用户手势或安全上下文）：${detail}`
      case 'NotAllowedError':
        return `没有文件访问权限：${detail}`
      case 'NotFoundError':
        return `目标位置不存在：${detail}`
      case 'InvalidStateError':
        return `文件选择器正忙（上一次对话框还没关闭）：${detail}`
      case 'NotSupportedError':
        return `当前浏览器不支持该文件操作：${detail}`
      default:
        return detail
    }
  }
  return String(error)
}

/** 由 MIME 生成 picker 的 `types` 描述（FSA 要求 `accept` 是 `{ mime: [ext] }`）。 */
function pickerTypes(suggestedName: string, mimeType: string): { description: string; accept: Record<string, string[]> }[] {
  const dot = suggestedName.lastIndexOf('.')
  const extension = dot >= 0 ? suggestedName.slice(dot) : ''
  if (extension === '') {
    return []
  }
  return [{ description: extension.slice(1).toUpperCase(), accept: { [mimeType]: [extension] } }]
}

/**
 * 保存单个文件。
 *
 * - 支持 FSA：弹系统保存对话框，用户可改名/换目录；取消返回 `cancelled`；
 * - 否则回退 `<a download>`：直接下载到浏览器默认目录，**无法得知是否成功**，
 *   只要触发就返回 `ok: true`（这是回退路径的固有局限，注释里说明）。
 */
export async function saveFile(request: SaveFileRequest): Promise<SaveResult> {
  const scope = getWindow()
  if (scope === null) {
    return { ok: false, error: '当前环境不支持文件保存' }
  }

  if (typeof scope.showSaveFilePicker === 'function') {
    try {
      const handle = await scope.showSaveFilePicker({
        suggestedName: request.suggestedName,
        types: pickerTypes(request.suggestedName, request.mimeType),
      })
      const writable = await handle.createWritable()
      await writable.write(request.bytes)
      await writable.close()
      return { ok: true, path: request.suggestedName }
    } catch (error) {
      if (isAbortError(error)) {
        return { ok: false, cancelled: true }
      }
      return { ok: false, error: describeError(error) }
    }
  }

  try {
    downloadBytes(scope, request.suggestedName, request.mimeType, request.bytes)
    return { ok: true, path: request.suggestedName }
  } catch (error) {
    return { ok: false, error: describeError(error) }
  }
}

/**
 * 保存多个文件到同一目录（多页 PNG）。
 *
 * - 支持 `showDirectoryPicker`：在用户选的目录下建 `folderName` 子目录并逐页写入；
 * - 否则打成 ZIP（`fflate.zipSync`，store 模式即可——PNG 本身已压缩）后走 `saveFile`。
 */
export async function saveFilesToFolder(
  folderName: string,
  files: readonly SaveFileRequest[],
): Promise<SaveResult> {
  if (files.length === 0) {
    return { ok: false, error: '没有可保存的文件' }
  }

  const scope = getWindow()
  if (scope === null) {
    return { ok: false, error: '当前环境不支持文件保存' }
  }

  if (typeof scope.showDirectoryPicker === 'function') {
    try {
      const root = await scope.showDirectoryPicker()
      const folder = await root.getDirectoryHandle(folderName, { create: true })
      for (const file of files) {
        const handle = await folder.getFileHandle(file.suggestedName, { create: true })
        const writable = await handle.createWritable()
        await writable.write(file.bytes)
        await writable.close()
      }
      return { ok: true, path: `${root.name}/${folderName}` }
    } catch (error) {
      if (isAbortError(error)) {
        return { ok: false, cancelled: true }
      }
      return { ok: false, error: describeError(error) }
    }
  }

  // 回退：打成一个 ZIP 下载（目录层级用 `folderName/` 前缀保留）
  try {
    const entries: Record<string, Uint8Array> = {}
    for (const file of files) {
      entries[`${folderName}/${file.suggestedName}`] = file.bytes
    }
    const zipBytes = zipSync(entries, { level: 0 })
    return await saveFile({
      suggestedName: `${folderName}.zip`,
      mimeType: 'application/zip',
      bytes: zipBytes,
    })
  } catch (error) {
    return { ok: false, error: describeError(error) }
  }
}

/** 用 `<a download>` 触发下载（回退路径）。 */
function downloadBytes(
  scope: Window & typeof globalThis,
  fileName: string,
  mimeType: string,
  bytes: Uint8Array,
): void {
  // `Uint8Array<ArrayBufferLike>` 不能直接当 `BlobPart`（TS 5.7 起收紧了 SharedArrayBuffer 的区分），
  // 复制到独立的 ArrayBuffer 视图即可，代价是一次拷贝（导出文件本来就要整块落盘）。
  const blob = new Blob([new Uint8Array(bytes)], { type: mimeType })
  const url = scope.URL.createObjectURL(blob)
  try {
    const anchor = scope.document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    anchor.rel = 'noopener'
    scope.document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    // 下载是异步读取 blob 的，立刻 revoke 会让部分浏览器拿不到数据；延后一拍释放。
    scope.setTimeout(() => {
      scope.URL.revokeObjectURL(url)
    }, 0)
  }
}

/**
 * 选择并读取一个文件。
 *
 * @param accept `accept` 属性的值（如 `.wbix`）；取消返回 null。
 */
export async function pickFile(accept: string): Promise<PickedFile | null> {
  const scope = getWindow()
  if (scope === null) {
    return null
  }

  return await new Promise<PickedFile | null>((resolve, reject) => {
    const input = scope.document.createElement('input')
    input.type = 'file'
    input.accept = accept
    input.style.display = 'none'

    let settled = false
    const finish = (value: PickedFile | null): void => {
      if (settled) {
        return
      }
      settled = true
      input.remove()
      resolve(value)
    }

    input.addEventListener('change', () => {
      const file = input.files?.[0]
      if (file === undefined) {
        finish(null)
        return
      }
      void file
        .arrayBuffer()
        .then((buffer) => {
          finish({ name: file.name, bytes: new Uint8Array(buffer) })
        })
        .catch((error: unknown) => {
          input.remove()
          reject(error instanceof Error ? error : new Error(String(error)))
        })
    })

    // 用户直接关掉选择器时浏览器**不会**派发事件（各浏览器行为不一致），
    // 因此这里不做超时兜底：拿不到 change 就停在 pending，不会误报错误。
    input.addEventListener('cancel', () => {
      finish(null)
    })

    scope.document.body.appendChild(input)
    input.click()
  })
}
