/**
 * `platform/file-handlers.ts` 的单测：系统「用 WindBoard 打开 .wbix」的接线。
 *
 * 只测纯逻辑（注入假的 `globalThis.launchQueue`），不碰真实浏览器 API——
 * 真实链路靠手工验证：打包后安装 PWA，双击 `.wbix` 应把页面追加到最后一页之后。
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  attachLaunchQueue,
  resetLaunchFileState,
  setLaunchFileHandler,
  type LaunchedFile,
} from '../../src/platform/file-handlers.ts'

/** 假的 `FileSystemFileHandle`：只需要 `getFile()`。 */
function fakeHandle(name: string, bytes: number[]): { getFile(): Promise<File> } {
  return {
    getFile: () => Promise.resolve(new File([new Uint8Array(bytes)], name)),
  }
}

/** 假的 `launchQueue`：记住 consumer，方便测试主动派发。 */
function installFakeLaunchQueue(): {
  dispatch: (handles: readonly { getFile(): Promise<File> }[]) => void
} {
  let consumer: ((params: { readonly files: readonly { getFile(): Promise<File> }[] }) => void) | null = null
  ;(globalThis as { launchQueue?: unknown }).launchQueue = {
    setConsumer: (next: (params: { readonly files: readonly { getFile(): Promise<File> }[] }) => void) => {
      consumer = next
    },
  }
  return {
    dispatch: (handles) => {
      expect(consumer).not.toBeNull()
      consumer?.({ files: handles })
    },
  }
}

/** 等异步读取跑完：`setConsumer` 里是 for 循环 + `await getFile` + `await arrayBuffer`，
 *  多个文件时微任务层数随文件数增长，用一轮宏任务统一收尾最稳。 */
async function flushMicrotasks(): Promise<void> {
  await new Promise<void>((resolve) => {
    setTimeout(resolve, 0)
  })
}

describe('file-handlers', () => {
  beforeEach(() => {
    resetLaunchFileState()
  })

  afterEach(() => {
    resetLaunchFileState()
    delete (globalThis as { launchQueue?: unknown }).launchQueue
  })

  it('没有 launchQueue 时 attachLaunchQueue 不抛错', () => {
    expect(() => {
      attachLaunchQueue()
    }).not.toThrow()
  })

  it('把系统递进来的文件读成字节交给消费者', async () => {
    const queue = installFakeLaunchQueue()
    attachLaunchQueue()
    /** @type {LaunchedFile[][]} */
    const received: LaunchedFile[][] = []
    setLaunchFileHandler((files) => {
      received.push([...files])
    })

    queue.dispatch([fakeHandle('a.wbix', [1, 2, 3])])
    await flushMicrotasks()

    expect(received).toHaveLength(1)
    expect(received[0]).toHaveLength(1)
    expect(received[0][0].name).toBe('a.wbix')
    expect([...received[0][0].bytes]).toEqual([1, 2, 3])
  })

  it('消费者未注册时先排队，注册后立刻收到', async () => {
    const queue = installFakeLaunchQueue()
    attachLaunchQueue()
    queue.dispatch([fakeHandle('early.wbix', [9])])
    await flushMicrotasks()

    /** @type {LaunchedFile[][]} */
    const received: LaunchedFile[][] = []
    setLaunchFileHandler((files) => {
      received.push([...files])
    })

    expect(received).toHaveLength(1)
    expect(received[0][0].name).toBe('early.wbix')
  })

  it('一次派发多个文件时按原顺序交出', async () => {
    const queue = installFakeLaunchQueue()
    attachLaunchQueue()
    const names: string[] = []
    setLaunchFileHandler((files) => {
      for (const file of files) names.push(file.name)
    })

    queue.dispatch([fakeHandle('1.wbix', [1]), fakeHandle('2.wbix', [2])])
    await flushMicrotasks()

    expect(names).toEqual(['1.wbix', '2.wbix'])
  })

  it('单个文件读取失败不影响其余文件', async () => {
    const queue = installFakeLaunchQueue()
    attachLaunchQueue()
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const names: string[] = []
    setLaunchFileHandler((files) => {
      for (const file of files) names.push(file.name)
    })

    queue.dispatch([
      {
        getFile: () => Promise.reject(new Error('权限被拒绝')),
      },
      fakeHandle('ok.wbix', [7]),
    ])
    await flushMicrotasks()

    expect(names).toEqual(['ok.wbix'])
    expect(warn).toHaveBeenCalled()
    warn.mockRestore()
  })

  it('清除消费者后，新文件回到队列而不是丢失', async () => {
    const queue = installFakeLaunchQueue()
    attachLaunchQueue()
    setLaunchFileHandler(null)

    queue.dispatch([fakeHandle('later.wbix', [5])])
    await flushMicrotasks()

    const names: string[] = []
    setLaunchFileHandler((files) => {
      for (const file of files) names.push(file.name)
    })
    expect(names).toEqual(['later.wbix'])
  })

  it('空文件列表不触发消费者', async () => {
    const queue = installFakeLaunchQueue()
    attachLaunchQueue()
    const handler = vi.fn()
    setLaunchFileHandler(handler)

    queue.dispatch([])
    await flushMicrotasks()

    expect(handler).not.toHaveBeenCalled()
  })
})
