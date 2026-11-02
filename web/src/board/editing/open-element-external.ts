/**
 * 浏览器里的「双击元素外部打开」执行层。
 *
 * 桌面 `BoardInputController.Open.cs` 的判定与文案已由 `element-open.ts` 移植；本模块只负责
 * **真正把东西打开**，并把结果收敛成 `OpenElementResult`（不抛异常、不弹窗）。
 *
 * 桌面 → 网页的语义映射（逐条对应 `Open.cs`）：
 * - 链接（`OpenLinkAsync`，`:84-104`）：桌面把 `Uri` 交给 `Windows.System.Launcher.LaunchUriAsync`；
 *   网页用新标签打开该 URL。桌面「系统未能打开该链接」对应网页「弹出窗口被拦截」。
 * - 媒体 / 文件（`OpenFilePathAsync`，`:127-153`）：桌面用 `StorageFile.GetFileFromPathAsync` +
 *   `Launcher.LaunchFileAsync` 启动本地默认程序。**浏览器没有等价能力**：网页拿不到本地文件系统路径，
 *   也不能启动本机程序。因此这里返回明确的「网页端无法打开本地文件」提示，而不是假装成功。
 *   将来 P4 资源层就绪后，可用 `resourceId` 解析出内嵌资源并打开 blob URL（见下方 `TODO`）。
 * - 文本（`OpenElementExternalAsync` 的 default 分支，`:73-74`）：桌面什么都不做，这里返回
 *   `{ ok: true }`（没有错误需要提示），由调用方决定是否忽略。
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
 * 网页端新增文案（桌面 resw 里没有对应 key，属于浏览器语义差异的补充说明）。
 *
 * 理由：桌面能启动本地程序，网页不能。若沿用桌面的 `Open_FileNotFound_Fmt` / `Open_Unauthorized_Fmt`
 * 会误导用户以为"文件丢了/没权限"，实际是"浏览器打不开本地文件"。
 * key 落在 `src/i18n/web-strings/element.json`。
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
 * 浏览器里的「双击外部打开」。
 *
 * 行为（按元素类型）：
 * - link：规整 URL 后新标签打开；被拦截 → `Open_LaunchLinkFailed_Message`；格式非法 → `Open_InvalidLinkFormat_Fmt`。
 * - media / file：有 `resourceId` → 资源未加载；路径为空 → `Open_FilePathEmpty_Message`；否则 → 网页端不支持。
 * - text / 未知类型：no-op，返回 `{ ok: true }`。
 *
 * ## 打开链接时的取舍（不是疏漏，改前先读）
 *
 * 1. **不能与 `noopener,noreferrer` 同用**：按规范（MDN `Window.open`），features 里出现 `noopener`
 *    （`noreferrer` 隐含 `noopener`）时 `window.open` 无论是否成功都返回 `null`，于是「返回 null = 被拦截」
 *    这个判据失效 —— 照字面实现会让**每次成功打开都弹「系统未能打开该链接」**。因此缺省实现改为
 *    `window.open(url, '_blank')` 拿句柄（`null` 即被拦）后立刻 `handle.opener = null`，隔离效果与
 *    `noopener` 等价，代价是失去 `noreferrer`。
 * 2. **隐私影响范围**：不带 `noreferrer` 时仍受浏览器缺省 Referrer-Policy 约束
 *    （`strict-origin-when-cross-origin`）——跨源只发送**源站 origin**（如 `https://app.example/`），
 *    不含路径、查询串与片段；HTTPS 页面跳 HTTP 目标时不发送 Referer。故泄漏面仅为「用户来自哪个站点」。
 * 3. **若将来要求彻底不发 Referer**：改用 `<a href target="_blank" rel="noopener noreferrer">` 的程序化点击
 *    导航（`anchor.click()`），Referer 与 opener 都能完全按 `rel` 控制；代价是**拿不到任何返回值**，
 *    无法检测弹窗是否被拦截，只能一律视为成功（或另配"未响应"兜底提示）。
 *
 * @param element 被双击的元素
 * @param env 可注入的浏览器环境（单测用），缺省走 `window.open`
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
 * 打开媒体 / 文件目标。
 *
 * 浏览器能力边界（顺序有意如此）：
 * 1. 有 `resourceId`：网页端的内嵌资源由 P4 资源层解析成 blob URL 后打开；当前还没有该层，
 *    因此明确告知「尚未加载」。这一分支**先于**路径校验 —— WBIX 内嵌图片可能只有 `resourceId`
 *    而没有 `sourcePath`，此时报"文件路径为空"会误导用户（桌面侧导入时会把内嵌资源落成临时文件，
 *    网页端没有这一步）。
 * 2. 没有 `resourceId`：桌面会按 `SourcePath` 启动本地程序，浏览器**做不到**，返回网页端新增文案。
 *
 * TODO(P4)：资源层提供 `resolveResourceUrl(resourceId): string | null` 后，在此处打开返回的 blob URL
 * （`openWindow(blobUrl)` 即可），并保留失败分支。
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
 * 缺省实现：用新标签打开 URL。
 *
 * 这里**没有**把 `noopener` 写进 features，原因（MDN `Window.open` 的 noopener 说明 + 本机 Chrome 实测）：
 * 一旦带上 `noopener`（`noreferrer` 同样隐含 noopener），规范要求 `window.open` 无论是否成功都返回
 * `null`，于是「返回 null = 被拦截」这个判据就失效了 —— 用户成功打开链接时反而会看到
 * 「系统未能打开该链接」。实测（Chrome）：带 `noopener,noreferrer` 返回 null；不带则返回句柄。
 * 折中做法是先普通打开拿到句柄（`null` 即被拦截，判据可靠），再立刻把 `opener` 置空，
 * 达到与 `noopener` 相同的隔离效果（目标页读不到 `window.opener`；实测跨源赋值不抛异常、置空后读回为 null）。
 * 代价是不带 `noreferrer`（会向目标站发送本站 URL 作为 Referer）；若更看重隐私，可改回
 * `noopener,noreferrer` 并接受"无法区分被拦截"。
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
