/**
 * Dock 与弹出面板的**纯逻辑**（不依赖 React / DOM，可直接在 node 环境测试）。
 *
 * 这里的每个常量与算法都对应桌面端的一处源码，改动前请先看注释里的出处：
 * - 色板网格列数：`WindBoard/MainWindow.xaml.cs:528-533 ComputePaletteColumns`
 * - 色板槽位排布：`WindBoard/MainWindow.xaml.cs:413-447 ApplyPenPaletteToFlyout`
 * - 更多菜单：`WindBoard/MainWindow.xaml:92-124`
 * - Dock 顺序：`WindBoard/Features/Dock/Models/DockItemIds.cs` +
 *   `WindBoard/Settings/AppSettingsStore.cs` 的 `NormalizeOrder`
 * - 滑动清屏：`WindBoard/UI/Common/ClearCanvasSlideController.cs:12-14`
 * - 页码指示：`WindBoard/UI/MainWindow/MainWindow.Pages.cs`
 */

import type { DockItemId } from '../../settings/model.ts'
import type { PenColor } from '../../tools/tool-settings.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'

/**
 * Dock 三块浮层的位置（`MainWindow.xaml:54-70`：左/中/右三块分别对齐）。
 *
 * `narrow` 是网页端为手机新增的第四种：窄屏下三段横排会互相覆盖（实测视口 <900 即压叠），
 * 因此合并成单段紧凑栏，详见 `ui/shell/use-narrow-layout.ts` 的头注释。
 */
export type DockPlacement = 'left' | 'center' | 'right' | 'narrow'

// ===== 色板网格 =====

/** 色板列数下限（`MainWindow.xaml.cs:531` 的 `Math.Clamp(columns, 3, 6)`）。 */
export const PALETTE_MIN_COLUMNS = 3

/** 色板列数上限（同上）。 */
export const PALETTE_MAX_COLUMNS = 6

/**
 * 计算色板网格列数：`ceil(sqrt(count))`，再夹到 `[3, 6]`。
 *
 * 桌面出处：`MainWindow.xaml.cs:528-533`。例如 9 色 → 3 列、16 色 → 4 列、49 色 → 6 列。
 */
export function computePaletteColumns(count: number): number {
  if (!Number.isFinite(count) || count <= 0) {
    return PALETTE_MIN_COLUMNS
  }
  const columns = Math.ceil(Math.sqrt(count))
  return Math.min(PALETTE_MAX_COLUMNS, Math.max(PALETTE_MIN_COLUMNS, columns))
}

/** 一个色板槽位在网格中的位置。 */
export interface PaletteSlot {
  /** 归一化后的颜色（`#RRGGBB`，桌面 `ColorHex.ToHexRgb` 已丢掉 alpha）。 */
  readonly hex: string
  /** 行号（0 起）。 */
  readonly row: number
  /** 列号（0 起）。 */
  readonly column: number
}

/**
 * 把色板展开成网格槽位（行优先，与桌面 `row = i / columns`、`col = i % columns` 一致）。
 *
 * **空槽位（`null` / 空白）直接跳过**：桌面会渲染成禁用的空圆
 * （`MainWindow.xaml.cs:455-462`），网页端按任务约定不渲染占位槽。
 */
export function layoutPaletteSlots(paletteHexes: readonly (string | null)[]): readonly PaletteSlot[] {
  const hexes = paletteHexes.filter(
    (hex): hex is string => typeof hex === 'string' && hex.trim().length > 0,
  )
  const columns = computePaletteColumns(hexes.length)
  return hexes.map((hex, index) => ({
    hex,
    row: Math.floor(index / columns),
    column: index % columns,
  }))
}

/**
 * 解析 `#RRGGBB` / `RRGGBB` / `#AARRGGBB` / `AARRGGBB` 为 0~1 浮点颜色。
 *
 * 与桌面 `WindBoard/Settings/ColorHex.cs:TryParse` 一致：长度只认 6 或 8；
 * 6 位时 alpha 视为不透明（`0xFF`）；8 位是 **ARGB**（alpha 在前）。
 * 解析失败返回 `null`（对应桌面 `TryParse` 返回 `false`）。
 */
export function hexToPenColor(hex: string): PenColor | null {
  const text = hex.trim().replace(/^#/, '')
  if (text.length !== 6 && text.length !== 8) {
    return null
  }
  if (!/^[0-9a-fA-F]+$/.test(text)) {
    return null
  }

  const hasAlpha = text.length === 8
  const alpha = hasAlpha ? Number.parseInt(text.slice(0, 2), 16) : 0xff
  const offset = hasAlpha ? 2 : 0
  return {
    r: Number.parseInt(text.slice(offset, offset + 2), 16) / 255,
    g: Number.parseInt(text.slice(offset + 2, offset + 4), 16) / 255,
    b: Number.parseInt(text.slice(offset + 4, offset + 6), 16) / 255,
    a: alpha / 255,
  }
}

/** 0~1 浮点分量 → 0~255 字节（四舍五入，与渲染层 `render/color.ts` 的量化一致）。 */
function toByte(value: number): number {
  if (!Number.isFinite(value)) {
    return 0
  }
  return Math.round(Math.min(1, Math.max(0, value)) * 255)
}

/**
 * 当前画笔颜色是否等于某个色板颜色（用于高亮选中色块）。
 *
 * 桌面用 `_currentPenHex` 字符串比较（`MainWindow.xaml.cs:437-441`）；网页端画笔颜色是
 * 0~1 浮点，因此按字节量化后比较，避免浮点误差导致"明明选了却不亮"。
 */
export function isActivePenHex(color: PenColor, hex: string): boolean {
  const parsed = hexToPenColor(hex)
  if (parsed === null) {
    return false
  }
  return (
    toByte(color.r) === toByte(parsed.r) &&
    toByte(color.g) === toByte(parsed.g) &&
    toByte(color.b) === toByte(parsed.b) &&
    toByte(color.a) === toByte(parsed.a)
  )
}

// ===== 更多菜单 =====

/** 更多菜单里可用的动作（桌面 `MainWindow.xaml:92-124` 去掉"屏幕批注"与"退出"）。 */
export type MoreMenuActionId = 'settings' | 'export' | 'fullscreen'

/** 更多菜单的一项：普通菜单项或分隔线（`MenuFlyoutSeparator`）。 */
export type MoreMenuEntry =
  | { readonly kind: 'item'; readonly id: MoreMenuActionId; readonly labelKey: MessageKey }
  | { readonly kind: 'divider' }

/**
 * 构建更多菜单（桌面 `MainWindow.xaml:92-124` 的顺序：设置 / 分隔 / 导出 / 全屏）。
 *
 * 与桌面的差异：桌面还有"屏幕批注"（`:109-117`，默认 `Collapsed`）与"退出"（`:119-123`），
 * 浏览器没有对应能力，按任务约定不实现。
 *
 * @param isFullscreen 当前是否全屏；决定最后一项的文案（`Common_FullScreen` / `Common_ExitFullScreen`）
 */
export function buildMoreMenuEntries(isFullscreen: boolean): readonly MoreMenuEntry[] {
  return [
    // 文案 key 与桌面 `Common.resw` 同名，渲染由 `DockBar` 用 `t()` 完成。
    { kind: 'item', id: 'settings', labelKey: 'Common_Settings' },
    { kind: 'divider' },
    { kind: 'item', id: 'export', labelKey: 'Common_Export' },
    {
      kind: 'item',
      id: 'fullscreen',
      labelKey: isFullscreen ? 'Common_ExitFullScreen' : 'Common_FullScreen',
    },
  ]
}

// ===== Dock 顺序 =====

/**
 * 按设置里的顺序排列 Dock 项。
 *
 * 桌面语义（`AppSettingsStore.NormalizeOrder`）：过滤掉未知项、去重保留首次出现、
 * 缺失项按默认顺序补齐。这里的输入已经是"当前渲染的项"，因此只做：
 * 按 `order` 依次取走匹配项 → 未在 `order` 里出现的项追加到末尾（防御性，正常不会发生）。
 */
export function applyDockOrder<T>(
  items: readonly T[],
  idOf: (item: T) => DockItemId,
  order: readonly DockItemId[],
): readonly T[] {
  const remaining = [...items]
  const ordered: T[] = []
  for (const id of order) {
    const index = remaining.findIndex((item) => idOf(item) === id)
    if (index >= 0) {
      ordered.push(...remaining.splice(index, 1))
    }
  }
  ordered.push(...remaining)
  return ordered
}

// ===== 页码 =====

/** 把页码夹到 `[0, pageCount - 1]`；页数为 0 时返回 0（桌面始终至少一页）。 */
export function clampPageIndex(index: number, pageCount: number): number {
  if (!Number.isFinite(index)) {
    return 0
  }
  const last = Math.max(0, pageCount - 1)
  return Math.min(last, Math.max(0, Math.trunc(index)))
}

/**
 * 翻页：`delta` 为 ±1。
 *
 * 桌面的上一页/下一页按钮在边界处不再移动（`MainWindow.Pages.cs` 的 `PreviousPage`/`NextPage`），
 * 因此这里同样夹取而不是循环。
 */
export function stepPageIndex(current: number, delta: number, pageCount: number): number {
  return clampPageIndex(clampPageIndex(current, pageCount) + delta, pageCount)
}

// ===== 滑动清屏 =====

/** 把手与底板左右各留 6 DIP（`ClearCanvasSlideController.cs:12 ThumbInset`）。 */
export const CLEAR_SLIDE_THUMB_INSET = 6

/** 拖到 90% 以上才算确认清屏（`ClearCanvasSlideController.cs:13 CompleteRatio`）。 */
export const CLEAR_SLIDE_COMPLETE_RATIO = 0.9

/**
 * 把手可移动的最大水平位移（`ClearCanvasSlideController.cs:203-212 GetThumbMaxX`）：
 * `max(0, hostWidth - thumbWidth - ThumbInset * 2)`。
 */
export function clearSlideThumbMaxOffset(hostWidth: number, thumbWidth: number): number {
  if (!Number.isFinite(hostWidth) || !Number.isFinite(thumbWidth)) {
    return 0
  }
  return Math.max(0, hostWidth - thumbWidth - CLEAR_SLIDE_THUMB_INSET * 2)
}

/**
 * 松手时是否达到清屏阈值（`ClearCanvasSlideController.cs:190-196 CompleteGesture`）：
 * `maxOffset > 0 && offset >= maxOffset * 0.9`。
 */
export function isClearSlideComplete(offset: number, maxOffset: number): boolean {
  if (!Number.isFinite(offset) || !Number.isFinite(maxOffset) || maxOffset <= 0) {
    return false
  }
  return offset >= maxOffset * CLEAR_SLIDE_COMPLETE_RATIO
}

/**
 * 把拖动位移换成 0~1 的进度（用于把手的视觉位置）。
 * `maxOffset` 为 0 时返回 0，避免除零。
 */
export function clearSlideProgress(offset: number, maxOffset: number): number {
  if (!Number.isFinite(offset) || !Number.isFinite(maxOffset) || maxOffset <= 0) {
    return 0
  }
  return Math.min(1, Math.max(0, offset / maxOffset))
}
