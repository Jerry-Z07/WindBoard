/**
 * 浏览器里的“双击元素外部打开”执行层：判定与文案由 element-open.ts 移植，这里只负责真正打开并把结果收敛成 OpenElementResult（不抛异常、不弹窗）。
 * 语义映射：链接用新标签打开（桌面交给 LaunchUriAsync，桌面“系统未能打开该链接”对应网页“弹窗被拦截”）；媒体/文件拿不到本地路径，返回网页端无法打开的提示；文本与未知类型返回 { ok: true }。
 */

import type { BoardElement } from '../model/element.ts'
import type { ElementOpenError, ElementOpenTarget } from './element-open.ts'
import { localized, type LocalizedMessage } from '../../i18n/message.ts'
import {
  describeElementOpenError,
  ELEMENT_OPEN_TEXTS,
  normalizeOpenUrl,
  resolveElementOpenTarget,
} from './element-open.ts'

/**
 * 网页端新增文案（桌面 resw 里没有对应 key）：桌面能启动本地程序，网页不能，沿用 Open_FileNotFound_Fmt / Open_Unauthorized_Fmt 会让用户以为文件丢了或没权限。key 落在 src/i18n/web-strings/element.json。
 */
export const WEB_ONLY_OPEN_TEXTS = {
  /** 本地文件在浏览器里没有打开通道（`{0}` 为显示名，缺失时用路径）。 */
  localFileUnsupportedFmt: (displayName: string): LocalizedMessage =>
    localized('Web_OpenLocalFileUnsupported_Fmt', displayName),
  /** 元素引用的内嵌资源尚未加载到网页端（P4 资源层就绪前恒为此状态）。 */
  resourceUnavailableMessage: localized('Web_OpenResourceUnavailable'),
} as const

/** 外部打开的结果。`ok` 为 false 时 `error` 一定有值，调用方直接弹提示即可。 */
export interface OpenElementResult {
  readonly ok: boolean
  readonly error?: ElementOpenError
}

/** 文件类目标（`resolveElementOpenTarget` 结果里 `kind === 'file'` 的那一支）。 */
type FileOpenTarget = Extract<NonNullable<ElementOpenTarget>, { readonly kind: 'file' }>

/** 打开链接的注入点（单测替换，默认实现包 `window.open`）。 */
export interface OpenElementEnv {
  /** 打开 URL；返回 false 表示未能发起（例如被浏览器拦截）。 */
  readonly openWindow?: (url: string) => boolean
}

/**
 * 浏览器里的“双击外部打开”：link 规整 URL 后新标签打开（被拦截给 Open_LaunchLinkFailed_Message，格式非法给 Open_InvalidLinkFormat_Fmt）；media/file 有 resourceId 报资源未加载，路径为空报 Open_FilePathEmpty_Message，否则报网页端不支持；text 与未知类型返回 { ok: true }。
 * window.open 的 features 不能带 noopener（noreferrer 隐含 noopener），规范要求那时无论成败都返回 null，“返回 null 即被拦截”的判据失效；折中是普通打开拿句柄后立刻把 opener 置空，隔离等价但失去 noreferrer（跨源只按缺省 Referrer-Policy 泄漏来源 origin），改 anchor.click 则拿不到返回值、无法检测拦截。
 * @param element 被双击的元素
 * @param env 可注入的浏览器环境（单测用），缺省走 window.open
 */
export function openElementExternal(
  element: BoardElement,
  env: OpenElementEnv = {},
): OpenElementResult {
  const target = resolveElementOpenTarget(element)
  // 桌面 default 分支直接 return（`:73-74`）：文本元素不打开任何东西，也不提示。
  if (target === null) {
    return { ok: true }
  }

  if (target.kind === 'url') {
    // 先做可静态判定的校验（链接空/非法），与桌面在打开前的检查顺序一致。
    const staticError = describeElementOpenError(target)
    if (staticError !== null) {
      return { ok: false, error: staticError }
    }

    // 校验已保证能规整；这里再取一次是为了拿到补全 scheme 后的可打开地址。
    const url = normalizeOpenUrl(target.url)
    if (url === null) {
      return { ok: false, error: invalidLinkError(target.url) }
    }

    if (!openUrl(url, env)) {
      // 桌面 `OpenLinkAsync` 在 `LaunchUriAsync` 返回 false 时提示该文案（`:99-102`）。
      return { ok: false, error: launchLinkFailedError() }
    }

    return { ok: true }
  }

  return openFileTarget(element, target)
}

/**
 * 打开媒体 / 文件目标：有 resourceId 先报“尚未加载”（P4 资源层未就绪，且 WBIX 内嵌图片可能只有 resourceId 没有 sourcePath，先校验路径会误导用户），没有 resourceId 则返回网页端无法打开本地文件。
 * TODO(P4)：资源层提供 resolveResourceUrl(resourceId) 后在此打开返回的 blob URL（openWindow(blobUrl) 即可），并保留失败分支。
 */
function openFileTarget(element: BoardElement, target: FileOpenTarget): OpenElementResult {
  const resourceId = (element.resourceId ?? '').trim()
  if (resourceId.length > 0) {
    return {
      ok: false,
      error: {
        title: ELEMENT_OPEN_TEXTS.cannotOpenFileTitle,
        message: WEB_ONLY_OPEN_TEXTS.resourceUnavailableMessage,
      },
    }
  }

  // 没有内嵌资源时才做桌面原有的路径校验（`Open.cs:129-133`）。
  const staticError = describeElementOpenError(target)
  if (staticError !== null) {
    return { ok: false, error: staticError }
  }

  const displayName = target.displayName.trim().length > 0 ? target.displayName : target.path
  return {
    ok: false,
    error: {
      title: ELEMENT_OPEN_TEXTS.cannotOpenFileTitle,
      message: WEB_ONLY_OPEN_TEXTS.localFileUnsupportedFmt(displayName),
    },
  }
}

/** 调用注入的打开函数；抛异常按「未能发起」处理（本模块的契约是不向外抛）。 */
function openUrl(url: string, env: OpenElementEnv): boolean {
  const openWindow = env.openWindow ?? defaultOpenWindow
  try {
    return openWindow(url)
  } catch {
    return false
  }
}

/**
 * 缺省实现：用新标签打开 URL。这里不把 noopener 写进 features：带上后（noreferrer 同样隐含 noopener）规范要求 window.open 无论成败都返回 null，“返回 null 即被拦截”的判据失效，用户成功打开反而会看到失败提示（Chrome 实测）。
 * 折中是普通打开拿到句柄后立刻把 opener 置空，目标页读不到 window.opener，隔离效果与 noopener 相同；代价是不发 noreferrer，目标站会收到本站 URL 作为 Referer。
 */
function defaultOpenWindow(url: string): boolean {
  // 用 `globalThis` 而不是直接写 `window`：单测环境（node）没有 `window`，直接引用会抛 ReferenceError。
  const candidate = (globalThis as { window?: unknown }).window
  if (candidate === undefined || candidate === null) {
    return false
  }

  const browserWindow = candidate as {
    open(targetUrl: string, target: string): { opener: unknown } | null
  }
  const handle = browserWindow.open(url, '_blank')
  if (handle === null) {
    return false
  }

  try {
    // 立刻切断 opener：此时新窗口还是同源的初始 about:blank，赋值一定生效；
    // 目标页的脚本在此之后才执行，因此读不到 window.opener。
    handle.opener = null
  } catch {
    // 个别实现禁止跨源写 opener：忽略，窗口已经打开了。
  }

  return true
}

/** 链接格式非法（桌面 `Open_InvalidLinkFormat_Fmt`，`{0}` 为 trim 后的原文）。 */
function invalidLinkError(raw: string): ElementOpenError {
  return {
    title: ELEMENT_OPEN_TEXTS.cannotOpenLinkTitle,
    message: ELEMENT_OPEN_TEXTS.invalidLinkFormatFmt(raw.trim()),
  }
}

/** 链接未能发起（桌面 `Open_LaunchLinkFailed_Message`）。 */
function launchLinkFailedError(): ElementOpenError {
  return {
    title: ELEMENT_OPEN_TEXTS.cannotOpenLinkTitle,
    message: ELEMENT_OPEN_TEXTS.launchLinkFailedMessage,
  }
}
