/**
 * 设置服务：单例式的内存状态、订阅与持久化，对应桌面 AppSettingsService（静态单例加事件订阅）。网页端的差异：持久化介质是 localStorage（桌面是 data/settings.json）；不做任何异步 IO，读写在构造与修改时同步完成，因此 UI 不需要处理加载态；存储读写通过注入的 SettingsStorage 完成，测试可直接用内存实现，也可在无 localStorage 的环境（SSR 与 node）下安全降级为只存内存。
 * 本文件只管内存与本地存储；与桌面 settings.json 文件互转时，由 features/settings-file/（P4 实现）在 model.ts 的字段名之上直接序列化。
 */

import {
  DEFAULT_SETTINGS,
  normalizeSettings,
  type AppSettings,
} from './model.ts'

/** 本地存储使用的键名（带版本号，后续结构变更时换键即可实现"旧数据自动失效"）。 */
export const SETTINGS_STORAGE_KEY = 'windboard.settings.v1'

/** 存储适配器：把"文本存取"与"设置语义"解耦，便于测试。 */
export interface SettingsStorage {
  read(): string | null
  write(text: string): void
  clear(): void
}

/** 设置的分区级补丁（每个分区内是浅合并）。 */
export interface SettingsPatch {
  readonly general?: Partial<AppSettings['general']>
  readonly appearance?: Partial<AppSettings['appearance']>
  readonly dock?: Partial<AppSettings['dock']>
  readonly writing?: { readonly pen?: Partial<AppSettings['writing']['pen']> }
  readonly keyboardShortcuts?: Partial<AppSettings['keyboardShortcuts']>
}

/** 设置服务。 */
export interface SettingsService {
  /** 当前设置（不可变快照，引用只在变更时替换，便于 `useSyncExternalStore`）。 */
  get(): AppSettings
  /** 订阅变更，返回取消订阅函数。 */
  subscribe(listener: () => void): () => void
  /** 用完整设置替换（会先归一化）。 */
  replace(next: unknown): void
  /** 分区级补丁（会先归一化）。 */
  patch(patch: SettingsPatch): void
  /** 恢复默认设置。 */
  reset(): void
  /** 取消全部订阅（不清理存储）。 */
  dispose(): void
}

/** 内存存储（无持久化；测试与无 `localStorage` 环境使用）。 */
export function createMemorySettingsStorage(initial: string | null = null): SettingsStorage {
  let current = initial
  return {
    read: () => current,
    write: (text) => {
      current = text
    },
    clear: () => {
      current = null
    },
  }
}

/**
 * localStorage 存储。访问 localStorage 本身可能抛异常（Safari 隐私模式、被策略禁用），因此读写都包了 try/catch 并在失败时静默降级：设置丢失比整个应用起不来要好。
 */
export function createLocalSettingsStorage(key: string = SETTINGS_STORAGE_KEY): SettingsStorage {
  const safeStorage = (): Storage | null => {
    try {
      const candidate = (globalThis as { localStorage?: Storage }).localStorage
      return candidate ?? null
    } catch {
      return null
    }
  }

  return {
    read(): string | null {
      try {
        return safeStorage()?.getItem(key) ?? null
      } catch {
        return null
      }
    },
    write(text: string): void {
      try {
        safeStorage()?.setItem(key, text)
      } catch {
        // 配额满/被禁用：忽略，内存中的设置仍然有效。
      }
    },
    clear(): void {
      try {
        safeStorage()?.removeItem(key)
      } catch {
        // 同上。
      }
    },
  }
}

/** 深合并补丁到当前设置（分区一层、`writing.pen` 两层）。 */
function applyPatch(current: AppSettings, patch: SettingsPatch): unknown {
  return {
    ...current,
    general: { ...current.general, ...patch.general },
    appearance: { ...current.appearance, ...patch.appearance },
    dock: { ...current.dock, ...patch.dock },
    writing: {
      pen: { ...current.writing.pen, ...patch.writing?.pen },
    },
    keyboardShortcuts: { ...current.keyboardShortcuts, ...patch.keyboardShortcuts },
  }
}

/**
 * 创建设置服务。
 *
 * @param storage 持久化适配器，缺省为 `localStorage`。
 */
export function createSettingsService(storage: SettingsStorage = createLocalSettingsStorage()): SettingsService {
  const listeners = new Set<() => void>()

  /** 从存储读出并归一化；任何异常都退化为默认设置。 */
  const load = (): AppSettings => {
    const text = storage.read()
    if (text === null || text.trim() === '') {
      return DEFAULT_SETTINGS
    }
    try {
      return normalizeSettings(JSON.parse(text))
    } catch {
      return DEFAULT_SETTINGS
    }
  }

  let current = load()

  const persist = (): void => {
    try {
      storage.write(JSON.stringify(current))
    } catch {
      // 循环引用等异常理论上不会发生（设置是纯数据），忽略即可。
    }
  }

  const commit = (next: AppSettings): void => {
    current = next
    persist()
    for (const listener of [...listeners]) {
      listener()
    }
  }

  return {
    get: () => current,
    subscribe(listener: () => void): () => void {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    replace(next: unknown): void {
      commit(normalizeSettings(next))
    },
    patch(patch: SettingsPatch): void {
      commit(normalizeSettings(applyPatch(current, patch)))
    },
    reset(): void {
      commit(normalizeSettings(DEFAULT_SETTINGS))
    },
    dispose(): void {
      listeners.clear()
    },
  }
}
