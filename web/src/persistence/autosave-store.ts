/**
 * 自动保存的存储层（网页端增强，桌面没有对应功能，桌面只在用户显式保存时写文件）。浏览器标签页可能被系统回收、崩溃或误关，白板内容只在内存里就会全丢；设计（见 design.md D8）为 WBIX 字节存 OPFS、元数据存 IndexedDB，两者都不可用时降级为仅内存（不报错，只是不提供恢复能力）。
 * 数据形态刻意与导出文件一致（整包 WBIX 字节），恢复就等于导入自己刚存的存档，不需要第二套序列化逻辑，也不会有格式漂移。
 */

/** 自动保存的元数据（用于启动时判断"有没有可恢复的存档"）。 */
export interface AutosaveMeta {
  /** 保存时刻（毫秒时间戳）。 */
  readonly savedAt: number
  readonly pageCount: number
  readonly itemCount: number
  /** 字节数（便于判断存档是否完整，也用于 UI 提示）。 */
  readonly byteLength: number
}

/** 自动保存存储。 */
export interface AutosaveStore {
  /** 写入存档与元数据。 */
  save(bytes: Uint8Array, meta: AutosaveMeta): Promise<void>
  /** 读存档字节；没有时返回 null。 */
  readBytes(): Promise<Uint8Array | null>
  /** 读元数据；没有时返回 null。 */
  readMeta(): Promise<AutosaveMeta | null>
  /** 清除存档（用户恢复或丢弃后调用）。 */
  clear(): Promise<void>
  /** 是否具备持久化能力（false 表示只是内存占位，UI 不该给"可恢复"的承诺）。 */
  readonly durable: boolean
}

/** OPFS 里存档文件名。 */
const OPFS_FILE_NAME = 'autosave.wbix'
/** OPFS 里元数据文件名（与字节分开存，避免读元数据时把整包读进内存）。 */
const OPFS_META_FILE_NAME = 'autosave.meta.json'

/** OPFS 目录句柄的最小结构（避免依赖 DOM lib 的具体声明）。 */
interface OpfsDirectory {
  getFileHandle(name: string, options?: { create?: boolean }): Promise<{
    getFile(): Promise<File>
    createWritable(): Promise<{ write(data: Uint8Array): Promise<void>; close(): Promise<void> }>
  }>
  removeEntry(name: string): Promise<void>
}
interface OpfsScope {
  storage?: { getDirectory?: () => Promise<OpfsDirectory> }
}

/** 取 OPFS 根目录；不可用时返回 null。 */
async function getOpfsRoot(): Promise<OpfsDirectory | null> {
  if (typeof navigator === 'undefined') {
    return null
  }
  const scope = navigator as Navigator & OpfsScope
  const getDirectory = scope.storage?.getDirectory
  if (typeof getDirectory !== 'function') {
    return null
  }
  try {
    return await getDirectory.call(scope.storage)
  } catch {
    return null
  }
}

/** 内存存储（无持久化能力；测试与降级用）。 */
export function createMemoryAutosaveStore(): AutosaveStore {
  let bytes: Uint8Array | null = null
  let meta: AutosaveMeta | null = null
  return {
    durable: false,
    save: async (nextBytes, nextMeta) => {
      bytes = new Uint8Array(nextBytes)
      meta = nextMeta
    },
    readBytes: async () => (bytes === null ? null : new Uint8Array(bytes)),
    readMeta: async () => meta,
    clear: async () => {
      bytes = null
      meta = null
    },
  }
}

/** 浏览器存储（OPFS 优先；不可用时降级为内存）。 */
export async function createBrowserAutosaveStore(): Promise<AutosaveStore> {
  const root = await getOpfsRoot()
  if (root === null) {
    return createMemoryAutosaveStore()
  }

  return {
    durable: true,
    async save(bytes, meta) {
      const bytesHandle = await root.getFileHandle(OPFS_FILE_NAME, { create: true })
      const bytesWriter = await bytesHandle.createWritable()
      await bytesWriter.write(bytes)
      await bytesWriter.close()

      // 元数据后写：字节写成功才更新元数据，避免出现"元数据说有存档但字节是旧的"。
      const metaHandle = await root.getFileHandle(OPFS_META_FILE_NAME, { create: true })
      const metaWriter = await metaHandle.createWritable()
      await metaWriter.write(new TextEncoder().encode(JSON.stringify(meta)))
      await metaWriter.close()
    },
    async readBytes() {
      const handle = await root.getFileHandle(OPFS_FILE_NAME)
      const file = await handle.getFile()
      return new Uint8Array(await file.arrayBuffer())
    },
    async readMeta() {
      try {
        const handle = await root.getFileHandle(OPFS_META_FILE_NAME)
        const file = await handle.getFile()
        const parsed: unknown = JSON.parse(await file.text())
        if (typeof parsed !== 'object' || parsed === null) {
          return null
        }
        const record = parsed as Record<string, unknown>
        if (typeof record.savedAt !== 'number' || typeof record.byteLength !== 'number') {
          return null
        }
        return {
          savedAt: record.savedAt,
          pageCount: typeof record.pageCount === 'number' ? record.pageCount : 1,
          itemCount: typeof record.itemCount === 'number' ? record.itemCount : 0,
          byteLength: record.byteLength,
        }
      } catch {
        // 元数据不存在（首次运行）或损坏：都当作"没有可恢复存档"。
        return null
      }
    },
    async clear() {
      await Promise.all([
        root.removeEntry(OPFS_FILE_NAME).catch(() => undefined),
        root.removeEntry(OPFS_META_FILE_NAME).catch(() => undefined),
      ])
    },
  }
}
