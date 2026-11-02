/**
 * 系统级「用 WindBoard 打开 .wbix」支持（PWA 的 `file_handlers`）。
 *
 * 链路：`public/manifest.webmanifest` 的 `file_handlers` 声明可打开的扩展名 →
 * 用户双击 `.wbix` 时浏览器把文件通过 `window.launchQueue` 递进来 →
 * 本模块读出字节 → 交给宿主（`BoardSurface`）导入。
 *
 * 为什么走「注册回调」而不是直接调用导入逻辑：
 * 启动顺序不确定（`launchQueue` 可能在 React 挂载前就派发），所以这里既存
 * 「待取队列」也存「消费者」，两边谁先到都能接上；而且 `platform/` 层不依赖
 * `board/` 与 React，便于单测。
 */

/** 从系统"打开方式"传进来的一个文件。 */
export interface LaunchedFile {
  /** 原始文件名（含扩展名）。 */
  readonly name: string
  /** 文件字节。 */
  readonly bytes: Uint8Array
}

/** 消费者：拿到一批文件后做什么由宿主决定。 */
export type LaunchFileHandler = (files: readonly LaunchedFile[]) => void

/** `launchQueue` 的最小结构（TS 内置 lib 里没有这部分定义）。 */
interface LaunchQueueLike {
  setConsumer(consumer: (params: { readonly files: readonly FileSystemFileHandleLike[] }) => void): void
}

/** `FileSystemFileHandle` 的最小结构。 */
interface FileSystemFileHandleLike {
  getFile(): Promise<File>
}

/** 已到达但还没有消费者的文件。 */
const pendingFiles: LaunchedFile[] = []

/** 当前消费者。 */
let currentHandler: LaunchFileHandler | null = null

/**
 * 取 `window.launchQueue`（非 Chromium 或被禁用时为 `undefined`）。
 * @returns {LaunchQueueLike | undefined}
 */
function getLaunchQueue(): LaunchQueueLike | undefined {
  const scope = globalThis as { launchQueue?: LaunchQueueLike }
  return scope.launchQueue
}

/**
 * 把一批文件交给消费者；没有消费者就先排队。
 * @param {readonly LaunchedFile[]} files 文件
 * @returns {void}
 */
function deliver(files: readonly LaunchedFile[]): void {
  if (files.length === 0) {
    return
  }
  if (currentHandler === null) {
    pendingFiles.push(...files)
    return
  }
  currentHandler(files)
}

/**
 * 挂上 `launchQueue` 消费者。整个应用只需在入口调用一次。
 *
 * 不支持该 API 时静默返回——「双击 .wbix 打开」是增强能力，不是可用性前提。
 * @returns {void}
 */
export function attachLaunchQueue(): void {
  const launchQueue = getLaunchQueue()
  if (launchQueue === undefined) {
    return
  }
  launchQueue.setConsumer((params) => {
    void (async () => {
      const files: LaunchedFile[] = []
      for (const handle of params.files) {
        try {
          const file = await handle.getFile()
          const buffer = await file.arrayBuffer()
          files.push({ name: file.name, bytes: new Uint8Array(buffer) })
        } catch (error) {
          // 单个文件读失败不应吞掉其余文件；留给调用方看控制台即可
          console.warn('读取系统传入的文件失败', error)
        }
      }
      deliver(files)
    })()
  })
}

/**
 * 注册（或清除）文件消费者；注册时会把此前排队等着的文件立刻交出去。
 * @param {LaunchFileHandler | null} handler 消费者，`null` 表示清除
 * @returns {void}
 */
export function setLaunchFileHandler(handler: LaunchFileHandler | null): void {
  currentHandler = handler
  if (handler === null || pendingFiles.length === 0) {
    return
  }
  const files = pendingFiles.splice(0, pendingFiles.length)
  handler(files)
}

/**
 * 仅供测试：清空队列与消费者。
 * @returns {void}
 */
export function resetLaunchFileState(): void {
  pendingFiles.length = 0
  currentHandler = null
}
