/**
 * `openElementExternal`（浏览器里的双击外部打开）单元测试。
 *
 * 全部通过注入的 `openWindow` 断言，**不真的开窗口**；node 环境没有 `window`，
 * 缺省实现（`defaultOpenWindow`）在这里天然降级为「未能发起」，也一并覆盖。
 */

import { describe, expect, it } from 'vitest'
import { createTranslator } from '../../src/i18n/index.ts'
import { formatLocalized, localized } from '../../src/i18n/message.ts'
import type { BoardElement } from '../../src/board/model/element.ts'
import {
  openElementExternal,
  WEB_ONLY_OPEN_TEXTS,
} from '../../src/board/editing/open-element-external.ts'

/** 造一个元素（默认 320×180）。 */
function makeElement(overrides: Partial<BoardElement> & Pick<BoardElement, 'type'>): BoardElement {
  return {
    id: '11111111-1111-4111-8111-111111111111',
    layer: 'aboveInk',
    order: 0,
    positionWorld: { x: 0, y: 0 },
    sizeWorld: { x: 320, y: 180 },
    ...overrides,
  }
}

/** 记录被打开过的 URL，并返回固定结果。 */
function createRecorder(outcome = true): {
  readonly calls: string[]
  readonly openWindow: (url: string) => boolean
} {
  const calls: string[] = []
  return {
    calls,
    openWindow: (url: string): boolean => {
      calls.push(url)
      return outcome
    },
  }
}

describe('链接（桌面 OpenLinkAsync:84-104）', () => {
  it('缺 scheme 时补 https:// 后打开，成功返回 ok', () => {
    const recorder = createRecorder()
    const result = openElementExternal(makeElement({ type: 'link', url: 'example.com' }), {
      openWindow: recorder.openWindow,
    })

    expect(result).toEqual({ ok: true })
    // 传给浏览器的是规整后的绝对 URL（WHATWG URL.href，特殊 scheme 补根路径）。
    expect(recorder.calls).toEqual(['https://example.com/'])
  })

  it('已带 scheme 的 URL 原样交给 openWindow', () => {
    const recorder = createRecorder()
    const result = openElementExternal(
      makeElement({ type: 'link', url: 'https://example.com/a?b=1' }),
      { openWindow: recorder.openWindow },
    )

    expect(result).toEqual({ ok: true })
    expect(recorder.calls).toEqual(['https://example.com/a?b=1'])
  })

  it('mailto: 链接可打开（桌面 LaunchUriAsync 同样支持）', () => {
    const recorder = createRecorder()
    const result = openElementExternal(
      makeElement({ type: 'link', url: 'mailto:someone@example.com' }),
      { openWindow: recorder.openWindow },
    )

    expect(result).toEqual({ ok: true })
    expect(recorder.calls).toEqual(['mailto:someone@example.com'])
  })

  it('被浏览器拦截（openWindow 返回 false）→ Open_LaunchLinkFailed_Message', () => {
    const recorder = createRecorder(false)
    const result = openElementExternal(makeElement({ type: 'link', url: 'https://example.com' }), {
      openWindow: recorder.openWindow,
    })

    expect(result.ok).toBe(false)
    expect(result.error).toEqual({ title: localized('Open_CannotOpenLink_Title'), message: localized('Open_LaunchLinkFailed_Message') })
    // 确实尝试过打开。
    expect(recorder.calls).toEqual(['https://example.com/'])
  })

  it('链接格式非法 → 不调用 openWindow，直接报 Open_InvalidLinkFormat_Fmt', () => {
    const recorder = createRecorder()
    const result = openElementExternal(makeElement({ type: 'link', url: 'ht tp://x' }), {
      openWindow: recorder.openWindow,
    })

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenLink_Title'), message: localized('Open_InvalidLinkFormat_Fmt', 'ht tp://x') },
    })
    expect(recorder.calls).toEqual([])
  })

  it('链接为空 → 不调用 openWindow，报 Open_LinkEmpty_Message', () => {
    const recorder = createRecorder()
    const result = openElementExternal(makeElement({ type: 'link', url: '   ' }), {
      openWindow: recorder.openWindow,
    })

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenLink_Title'), message: localized('Open_LinkEmpty_Message') },
    })
    expect(recorder.calls).toEqual([])
  })

  it('openWindow 抛异常按「未能发起」处理（本模块不向外抛）', () => {
    const result = openElementExternal(makeElement({ type: 'link', url: 'https://example.com' }), {
      openWindow: (): boolean => {
        throw new Error('SecurityError')
      },
    })

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenLink_Title'), message: localized('Open_LaunchLinkFailed_Message') },
    })
  })
})

describe('媒体 / 文件（桌面 OpenFilePathAsync:127-153）', () => {
  it('媒体元素无内嵌资源 → 网页端无法打开本地文件（带显示名）', () => {
    const recorder = createRecorder()
    const result = openElementExternal(
      makeElement({
        type: 'media',
        kind: 'image',
        sourcePath: 'C:/tmp/a.png',
        displayName: 'a.png',
      }),
      { openWindow: recorder.openWindow },
    )

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenFile_Title'), message: localized('Web_OpenLocalFileUnsupported_Fmt', 'a.png') },
    })
    // 文件类目标不会调用 openWindow（浏览器没有本地打开通道）。
    expect(recorder.calls).toEqual([])
  })

  it('显示名为空时用路径兜底', () => {
    const result = openElementExternal(
      makeElement({ type: 'file', sourcePath: 'C:/tmp/a.pdf', displayName: '' }),
    )

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenFile_Title'), message: localized('Web_OpenLocalFileUnsupported_Fmt', 'C:/tmp/a.pdf') },
    })
  })

  it('有 resourceId → 资源尚未加载（P4 资源层就绪前的恒态）', () => {
    const result = openElementExternal(
      makeElement({
        type: 'media',
        kind: 'image',
        sourcePath: 'C:/tmp/a.png',
        displayName: 'a.png',
        resourceId: 'res-1',
      }),
    )

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenFile_Title'), message: localized('Web_OpenResourceUnavailable') },
    })
  })

  it('resourceId 优先于空路径：即使没有 sourcePath 也报「尚未加载」', () => {
    const result = openElementExternal(
      makeElement({ type: 'media', kind: 'image', sourcePath: null, resourceId: 'res-1' }),
    )

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenFile_Title'), message: localized('Web_OpenResourceUnavailable') },
    })
  })

  it('没有 resourceId 且路径为空 → Open_FilePathEmpty_Message（桌面原文）', () => {
    const result = openElementExternal(makeElement({ type: 'file', sourcePath: '   ' }))

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenFile_Title'), message: localized('Open_FilePathEmpty_Message') },
    })
  })
})

describe('文本与未知类型（桌面 OpenElementExternalAsync 的 default 分支:73-74）', () => {
  it('text 元素是显式 no-op：ok 为 true、无错误、不调用 openWindow', () => {
    const recorder = createRecorder()
    const result = openElementExternal(makeElement({ type: 'text', text: '你好' }), {
      openWindow: recorder.openWindow,
    })

    expect(result).toEqual({ ok: true })
    expect(recorder.calls).toEqual([])
  })
})

describe('缺省环境（node 无 window）', () => {
  it('不传 env 时默认实现降级为「未能发起」，不抛异常', () => {
    const result = openElementExternal(makeElement({ type: 'link', url: 'https://example.com' }))

    expect(result).toEqual({
      ok: false,
      error: { title: localized('Open_CannotOpenLink_Title'), message: localized('Open_LaunchLinkFailed_Message') },
    })
  })
})

describe('网页端新增文案', () => {
  it('两条网页独有文案的 key 与渲染结果（桌面 resw 无对应 key）', () => {
    expect(WEB_ONLY_OPEN_TEXTS.localFileUnsupportedFmt('a.png')).toEqual({
      key: 'Web_OpenLocalFileUnsupported_Fmt',
      args: ['a.png'],
    })
    expect(WEB_ONLY_OPEN_TEXTS.resourceUnavailableMessage).toEqual({
      key: 'Web_OpenResourceUnavailable',
    })
    const translateZh = createTranslator('zh-CN')
    expect(formatLocalized(translateZh, WEB_ONLY_OPEN_TEXTS.localFileUnsupportedFmt('a.png'))).toBe(
      '网页端无法打开本地文件：a.png',
    )
    expect(formatLocalized(translateZh, WEB_ONLY_OPEN_TEXTS.resourceUnavailableMessage)).toBe(
      '该元素引用的资源尚未加载到网页端，暂时无法打开。',
    )
  })
})
