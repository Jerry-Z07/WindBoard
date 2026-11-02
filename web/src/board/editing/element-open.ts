/**
 * 元素的「双击外部打开」判定与静态校验（纯函数，node 环境可测）。
 *
 * 移植自桌面 `WindBoard/Interaction/BoardInputController/BoardInputController.Open.cs`（181 行）：
 * - 双击判定 `HandleElementClickForMaybeOpen`（:26-53）：同一元素 id + 500ms 内 + 屏幕距离 ≤ 10 DIP；
 * - 打开分派 `OpenElementExternalAsync`（:55-82）：link → 打开 URL；media/file → 打开文件；其余（text）不处理；
 * - URL 规整 `TryCreateUri`（:106-125）：先按绝对 URI 解析，失败且原文不含 `://` 时兜底补 `https://`；
 * - 文件路径 `OpenFilePathAsync`（:127-153）：trim 后为空即报错，其余错误（找不到/无权限/启动失败）依赖真实 IO。
 *
 * 与桌面一致的两处「调用方职责」，本模块**不**代劳：
 * 1. 只有选择工具下才处理双击（`Open.cs:28-30`）：调用方需自行判断工具类型后再喂点击；
 * 2. 文件打开的真实错误分类（`FileNotFoundException` / `UnauthorizedAccessException` / 启动失败）
 *    需要实际 IO，纯函数无法区分，由真正执行打开的调用方负责，文案见 `ELEMENT_OPEN_TEXTS`。
 */

import { localized, type LocalizedMessage } from '../../i18n/message.ts'
import type { BoardElement } from '../model/element.ts'
import type { Vec2 } from '../model/stroke.ts'

/** 双击判定的时间窗口（桌面 `ElementDoubleClickTimeoutMs`，`Open.cs:19`）。 */
export const ELEMENT_DOUBLE_CLICK_TIMEOUT_MS = 500

/** 双击判定的最大屏幕距离（DIP，桌面 `ElementDoubleClickMaxDistanceDip`，`Open.cs:20`）。 */
export const ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP = 10

/** 双击距离阈值的平方（避免开方；桌面同样比较 `DistanceSquared`，`Open.cs:37`）。 */
const MAX_DISTANCE_SQUARED =
  ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP * ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP

/**
 * 打开相关文案：只给 **key**，中英文由 UI 用 `t()` 渲染（见 `i18n/message.ts`）。
 *
 * key 逐字取自 `WindBoard/Strings/zh-CN/Open.resw` 与 `Common.resw`（注释标注来源）。
 * 纯函数只产出其中 4 条（链接为空 / 链接格式无效 / 文件路径为空 及其标题），
 * 其余 8 条登记在此供真正执行打开的调用方复用，避免 web 端另造文案。
 */
export const ELEMENT_OPEN_TEXTS = {
  /** `Open_CannotOpenLink_Title` */
  cannotOpenLinkTitle: localized('Open_CannotOpenLink_Title'),
  /** `Open_LinkEmpty_Message` */
  linkEmptyMessage: localized('Open_LinkEmpty_Message'),
  /** `Open_InvalidLinkFormat_Fmt`（`{0}` = 链接原文） */
  invalidLinkFormatFmt: (raw: string): LocalizedMessage =>
    localized('Open_InvalidLinkFormat_Fmt', raw),
  /** `Open_LaunchLinkFailed_Message` */
  launchLinkFailedMessage: localized('Open_LaunchLinkFailed_Message'),
  /** `Open_CannotOpenFile_Title` */
  cannotOpenFileTitle: localized('Open_CannotOpenFile_Title'),
  /** `Open_FilePathEmpty_Message` */
  filePathEmptyMessage: localized('Open_FilePathEmpty_Message'),
  /** `Open_FileNotFound_Title` */
  fileNotFoundTitle: localized('Open_FileNotFound_Title'),
  /** `Open_FileNotFound_Fmt`（`{0}` = 元素显示名） */
  fileNotFoundFmt: (name: string): LocalizedMessage => localized('Open_FileNotFound_Fmt', name),
  /** `Open_Unauthorized_Fmt`（`{0}` = 元素显示名） */
  unauthorizedFmt: (name: string): LocalizedMessage => localized('Open_Unauthorized_Fmt', name),
  /** `Open_LaunchFileFailed_Fmt`（`{0}` = 元素显示名） */
  launchFileFailedFmt: (name: string): LocalizedMessage =>
    localized('Open_LaunchFileFailed_Fmt', name),
  /** `Common_OpenFailed_Title` */
  openFailedTitle: localized('Common_OpenFailed_Title'),
  /** `Common_Close` */
  close: localized('Common_Close'),
} as const

/**
 * 双击判定器：内部维护「上一次元素点击」记录（桌面 `_lastElementClick*` 三个字段，`Open.cs:22-24`）。
 *
 * 使用方式：每次在元素上按下（点击）时调用 `handleClick`，返回 true 即触发外部打开。
 * 注意调用方需先保证当前是选择工具（`Open.cs:28-30`），本判定器不做工具判断。
 */
export interface ElementDoubleClickTracker {
  /**
   * 记录一次元素点击，返回本次是否构成「双击同一元素」。
   *
   * @param elementId 被点击元素的 id（桌面比较 `Guid` 相等）
   * @param screenDip 点击位置的屏幕坐标（DIP，不是世界坐标）
   * @param nowMs 当前时间（毫秒，单调递增的任意基准即可）
   */
  handleClick(elementId: string, screenDip: Vec2, nowMs: number): boolean

  /** 清空记录（切换工具、开始其它手势时调用，避免跨场景误判为双击）。 */
  reset(): void
}

/**
 * 创建双击判定器。
 *
 * 行为逐条对齐 `HandleElementClickForMaybeOpen`（`Open.cs:26-53`）：
 * 1. 用**旧记录**判定本次是否双击（同一 id + 时间差 ≤ 500ms + 屏幕距离平方 ≤ 100）；
 * 2. 无论是否双击，都先把记录更新为本次点击；
 * 3. 判定为双击后**清空记录**（`Open.cs:49-51`），避免连击反复触发。
 */
export function createElementDoubleClickTracker(): ElementDoubleClickTracker {
  let lastElementId: string | null = null
  let lastClickAtMs = 0
  let lastScreenDip: Vec2 = { x: 0, y: 0 }

  return {
    handleClick(elementId: string, screenDip: Vec2, nowMs: number): boolean {
      const isDoubleClick =
        lastElementId !== null &&
        lastElementId === elementId &&
        // 桌面用 `(now - last).TotalMilliseconds <= 500`；时间源倒退时该差值为负也会通过，
        // 这里保持同样的比较式（调用方应传入单调递增时间）。
        nowMs - lastClickAtMs <= ELEMENT_DOUBLE_CLICK_TIMEOUT_MS &&
        squaredDistance(screenDip, lastScreenDip) <= MAX_DISTANCE_SQUARED

      // 先记录本次点击（桌面 :39-41，`Vector2` 为值类型，相当于拷贝一份坐标）。
      lastElementId = elementId
      lastClickAtMs = nowMs
      lastScreenDip = { x: screenDip.x, y: screenDip.y }

      if (!isDoubleClick) {
        return false
      }

      // 命中后清空记录，避免连续多次点击反复触发（桌面 :48-51）。
      lastElementId = null
      lastClickAtMs = 0
      return true
    },

    reset(): void {
      lastElementId = null
      lastClickAtMs = 0
      lastScreenDip = { x: 0, y: 0 }
    },
  }
}

/** 双击打开的目标类型（`null` 表示该元素不支持外部打开，例如文本元素）。 */
export type ElementOpenTarget =
  | { readonly kind: 'url'; readonly url: string }
  | { readonly kind: 'file'; readonly path: string; readonly displayName: string }
  | null

/**
 * 解析元素的双击打开目标。
 *
 * 对应桌面 `OpenElementExternalAsync` 的类型分派（`Open.cs:59-75`）：
 * - link → `{ kind: 'url' }`，`url` 为 trim 后的原文；能规整时已补全 scheme（见 `normalizeOpenUrl`）；
 * - media / file → `{ kind: 'file' }`，`path` 为 trim 后的 `sourcePath`（桌面在 `OpenFilePathAsync` 里 trim，`:129`）；
 * - text（及其它未知类型）→ `null`（桌面 default 分支什么都不做，`:73-74`）。
 *
 * 约定：`url` / `path` 一定是 trim 后的字符串（可能为空串），空串与非法值由
 * `describeElementOpenError` 转成用户可读的提示；`displayName` 保持原值（桌面不 trim，用于错误文案的 `{0}`）。
 */
export function resolveElementOpenTarget(element: BoardElement): ElementOpenTarget {
  switch (element.type) {
    case 'link': {
      const raw = (element.url ?? '').trim()
      // 能规整就返回规整结果（调用方在无错误时可直接打开该 url），否则保留原文供错误提示使用。
      return { kind: 'url', url: normalizeOpenUrl(raw) ?? raw }
    }

    case 'media':
    case 'file': {
      return {
        kind: 'file',
        path: (element.sourcePath ?? '').trim(),
        displayName: element.displayName ?? '',
      }
    }

    case 'text':
    default:
      return null
  }
}

/**
 * URL 规整：返回可交给浏览器打开的绝对 URL；无法规整时返回 `null`。
 *
 * 与桌面 `TryCreateUri`（`Open.cs:106-125`）等价：
 * 1. 先按绝对 URI 解析；
 * 2. 失败且原文**不含** `://`（大小写不敏感）时，补 `https://` 再解析一次；
 * 3. 都失败 → 非法。
 *
 * 两个实现差异（都不影响桌面语义，桌面把 `Uri` 对象直接交给 `Launcher`，字符串形态不可观测）：
 * - 返回值用 WHATWG `URL.href`，因此特殊 scheme（http/https/ftp/ws/wss）会补根路径 `/`、scheme 与主机小写化，
 *   这与 .NET `Uri.ToString()` 的表现一致（`new Uri("http://x").ToString()` 也是 `http://x/`）；
 * - 原文首尾空白会被 trim（桌面在 `OpenLinkAsync:86` 先 trim 再调 `TryCreateUri`，等价）。
 */
export function normalizeOpenUrl(raw: string): string | null {
  const trimmed = raw.trim()
  if (trimmed.length === 0) {
    return null
  }

  const absolute = tryCreateAbsoluteUrl(trimmed)
  if (absolute !== null) {
    return absolute
  }

  // 用户可能省略 scheme（例如输入 example.com），这里兜底补 https://（桌面 :116-122）。
  // 注意：只要原文含 "://" 就不兜底，因此 "://example.com" 这类畸形串会直接判为非法（与桌面一致）。
  if (!trimmed.toLowerCase().includes('://')) {
    const withHttps = tryCreateAbsoluteUrl(`https://${trimmed}`)
    if (withHttps !== null) {
      return withHttps
    }
  }

  return null
}

/**
 * 打开失败的提示（标题 + 正文），文案来自 `ELEMENT_OPEN_TEXTS`；
 * 只带 key + 实参，渲染由调用方用 `formatLocalized(t, …)` 完成。
 */
export interface ElementOpenError {
  readonly title: LocalizedMessage
  readonly message: LocalizedMessage
}

/**
 * 对打开目标做**可静态判定**的校验，返回应提示给用户的错误；无需提示时返回 `null`。
 *
 * 对应桌面 `OpenLinkAsync`（`Open.cs:84-104`）与 `OpenFilePathAsync`（`:127-134`）里的前置校验：
 * - 链接：空 → `Open_CannotOpenLink_Title` + `Open_LinkEmpty_Message`；
 *   非法 → `Open_CannotOpenLink_Title` + `Open_InvalidLinkFormat_Fmt`（`{0}` 为 trim 后的原文）；
 * - 文件：路径空 → `Open_CannotOpenFile_Title` + `Open_FilePathEmpty_Message`。
 *
 * 文件「不存在 / 无权限 / 启动失败」需要真实 IO，本函数**不**返回这些错误（返回 `null`），
 * 由执行打开动作的调用方用 `ELEMENT_OPEN_TEXTS` 里的对应文案自行提示。
 *
 * 返回值约定：`null` 表示可以尝试打开；此时 url 目标一定是已规整的绝对 URL
 * （因为 `resolveElementOpenTarget` 已做过规整，本函数再次校验保持幂等）。
 */
export function describeElementOpenError(
  target: NonNullable<ElementOpenTarget>,
): ElementOpenError | null {
  if (target.kind === 'url') {
    const raw = target.url.trim()
    if (raw.length === 0) {
      return {
        title: ELEMENT_OPEN_TEXTS.cannotOpenLinkTitle,
        message: ELEMENT_OPEN_TEXTS.linkEmptyMessage,
      }
    }

    if (normalizeOpenUrl(raw) === null) {
      return {
        title: ELEMENT_OPEN_TEXTS.cannotOpenLinkTitle,
        message: ELEMENT_OPEN_TEXTS.invalidLinkFormatFmt(raw),
      }
    }

    return null
  }

  if (target.path.trim().length === 0) {
    return {
      title: ELEMENT_OPEN_TEXTS.cannotOpenFileTitle,
      message: ELEMENT_OPEN_TEXTS.filePathEmptyMessage,
    }
  }

  return null
}

/** 两点距离平方（桌面 `Vector2.DistanceSquared`）。 */
function squaredDistance(a: Vec2, b: Vec2): number {
  const dx = a.x - b.x
  const dy = a.y - b.y
  return dx * dx + dy * dy
}

/**
 * 等价于 .NET `Uri.TryCreate(raw, UriKind.Absolute, out _)`：能解析成绝对 URL 则返回规整后的
 * `href` 字符串，否则返回 `null`。返回字符串而非 `URL` 对象，避免本模块的类型签名依赖 DOM lib
 * （实现上仍用 WHATWG `URL` 解析，Node 与浏览器都提供该全局构造器）。
 *
 * 已核对的关键行为（与 .NET 一致）：`mailto:` / `ftp://` 可解析；`//host/path`、`example.com`、空串
 * 都因缺少 scheme 而失败（`//host` 由调用方的 https 兜底分支处理，见 `normalizeOpenUrl`）。
 */
function tryCreateAbsoluteUrl(raw: string): string | null {
  try {
    return new URL(raw).href
  } catch {
    return null
  }
}
