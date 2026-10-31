/**
 * 元素卡片绘制。
 *
 * 对应桌面 `BoardSceneRenderer.DrawCardElement`（`WindBoard/Rendering/Board/BoardSceneRenderer.cs:554-634`）：
 * 圆角底板 + 1.5 描边 + 左上角圆形类型徽标 + 标题 + 副标题（裁剪到各自矩形）。
 *
 * P1.3 只做到“结构一致”：
 * - 桌面用 Segoe MDL2 字形做徽标、用 DirectWrite 排版；网页端用 Unicode 字符占位 + `fillText`；
 * - 图片元素一律走占位卡片（桌面无位图时同样回退卡片，`:539-552`），真正的位图/缩略图绘制留待资源层；
 * - 文案来自桌面 L10n key，网页端 P1 尚无本地化层，先用中文常量占位（P3 接入 i18n 后替换）。
 */

import type { BoardElement } from '../board/model/element.ts'
import type { WorldBounds } from '../board/viewport.ts'
import { intersectsWorldBounds } from './bounds.ts'
import type { RenderContext2D } from './canvas-types.ts'

/** 卡片圆角（世界坐标 DIP）：桌面 `const float corner = 14.0f`（`:568`）。 */
export const ELEMENT_CARD_CORNER_RADIUS = 14

/** 卡片内边距：桌面 `pad = 12.0f`（`:569`）。 */
export const ELEMENT_CARD_PADDING = 12

/** 徽标尺寸：桌面 `iconSize = 32.0f`（`:570`）。 */
export const ELEMENT_CARD_ICON_SIZE = 32

/** 徽标与文字间距：桌面 `iconGap = 12.0f`（`:571`）。 */
export const ELEMENT_CARD_ICON_GAP = 12

/** 标题行高：桌面 `titleHeight = 22.0f`（`:572`）。 */
export const ELEMENT_CARD_TITLE_HEIGHT = 22

/** 标题与正文间距：桌面 `titleGap = 4.0f`（`:573`）。 */
export const ELEMENT_CARD_TITLE_GAP = 4

/** 卡片描边宽度：桌面 `DrawRoundedRectangle(..., strokeWidth: 1.5f)`（`:587`）。 */
export const ELEMENT_CARD_BORDER_WIDTH = 1.5

/** 标题字号：桌面 Segoe UI SemiBold 15.0（`:885-892`）。 */
export const ELEMENT_CARD_TITLE_FONT_SIZE = 15

/** 正文字号：桌面 Segoe UI Normal 13.0（`:898-906`）。 */
export const ELEMENT_CARD_BODY_FONT_SIZE = 13

/** 徽标字号：桌面 16.0（`:908-916`）。 */
export const ELEMENT_CARD_ICON_FONT_SIZE = 16

/** 行高系数（桌面 DirectWrite 默认行距的近似值，仅影响多行正文的行间距）。 */
const LINE_HEIGHT_FACTOR = 1.35

/** 文本元素预览的最大字符数（桌面 `MaxTextPreviewChars = 4096`）。 */
const MAX_TEXT_PREVIEW_CHARS = 4096

// 深色主题配色（桌面默认 `ElementCardTheme.Dark`，`BoardSceneRenderer.cs:64` 与 `:74-80`）。
/** 卡片底色 #202126。 */
const CARD_BACKGROUND = '#202126'
/** 卡片描边 #3A3B40。 */
const CARD_BORDER = '#3A3B40'
/** 标题文字 #EDEDED。 */
const CARD_TEXT = '#EDEDED'
/** 副标题文字 #BDBDBD。 */
const CARD_SECONDARY_TEXT = '#BDBDBD'
/** 徽标文字 #EDEDED。 */
const CARD_ICON_TEXT = '#EDEDED'

// 类型徽标强调色（桌面 `GetElementCardVisual`，`:744-750`；0~1 浮点 → 字节）。
const ACCENT_BLUE = 'rgb(10, 133, 255)'
const ACCENT_ORANGE = 'rgb(255, 148, 0)'
const ACCENT_PURPLE = 'rgb(176, 82, 222)'
const ACCENT_GREEN = 'rgb(51, 199, 89)'
const ACCENT_GRAY = 'rgb(140, 140, 148)'
const ACCENT_CYAN = 'rgb(89, 199, 250)'

// 字体：桌面标题 SemiBold 15、正文 Normal 13、徽标 16（`:885-916`）。
const TITLE_FONT = `600 ${ELEMENT_CARD_TITLE_FONT_SIZE}px "Segoe UI", system-ui, sans-serif`
const BODY_FONT = `${ELEMENT_CARD_BODY_FONT_SIZE}px "Segoe UI", system-ui, sans-serif`
const ICON_FONT = `${ELEMENT_CARD_ICON_FONT_SIZE}px "Segoe UI", system-ui, sans-serif`

// 文案占位（桌面 L10n key 的中文值，P3 接入本地化后替换）。
const TEXT_ELEMENT_TITLE = '文本'
const LINK_OPEN_HINT = '双击打开链接'
const EXTERNAL_OPEN_HINT = '双击打开'
const UNTITLED_NAME = '未命名'
const MEDIA_LABEL = '媒体'
const AUDIO_LABEL = '音频'
const VIDEO_LABEL = '视频'
const IMAGE_NO_PREVIEW_LABEL = '图片（暂无预览）'
const ARCHIVE_LABEL = '压缩包'

/** 已知扩展名 → 文件类型标签（桌面 `GetFileKindLabel`，`:853-864`）。 */
const FILE_KIND_LABELS: Readonly<Record<string, string>> = {
  '.pdf': 'PDF',
  '.doc': 'Word',
  '.docx': 'Word',
  '.docm': 'Word',
  '.ppt': 'PowerPoint',
  '.pptx': 'PowerPoint',
  '.pptm': 'PowerPoint',
  '.xls': 'Excel',
  '.xlsx': 'Excel',
  '.xlsm': 'Excel',
  '.csv': 'CSV',
  '.rtf': 'RTF',
  '.epub': 'EPUB',
  '.zip': ARCHIVE_LABEL,
  '.7z': ARCHIVE_LABEL,
  '.rar': ARCHIVE_LABEL,
}

/** 元素卡片在世界坐标下的矩形。 */
export interface ElementCardRect {
  left: number
  top: number
  width: number
  height: number
}

/** 一张卡片的视觉参数（类型决定强调色、占位字形与文案）。 */
interface ElementCardVisual {
  accentColor: string
  glyph: string
  title: string
  secondary: string
}

/**
 * 按层绘制可见元素卡片。
 *
 * @param elements 同层元素，数组顺序即绘制顺序（桌面读入时已按 `order` 排好，渲染不再排序）
 * @param visibleWorld 当前可见世界包围盒（用于 AABB 剔除，`BoardSceneRenderer.cs:501-537`）
 */
export function drawVisibleElementCards(
  ctx: RenderContext2D,
  elements: readonly BoardElement[],
  visibleWorld: WorldBounds,
): void {
  for (const element of elements) {
    const rect = getElementCardRect(element)
    if (!intersectsWorldBounds(toWorldBounds(rect), visibleWorld)) {
      continue
    }
    drawElementCard(ctx, element, rect)
  }
}

/** 元素世界矩形：`positionWorld` / `sizeWorld` 归一成非负宽高。 */
export function getElementCardRect(element: BoardElement): ElementCardRect {
  const { positionWorld, sizeWorld } = element
  return {
    left: Math.min(positionWorld.x, positionWorld.x + sizeWorld.x),
    top: Math.min(positionWorld.y, positionWorld.y + sizeWorld.y),
    width: Math.abs(sizeWorld.x),
    height: Math.abs(sizeWorld.y),
  }
}

/**
 * 绘制单张元素卡片（调用方负责可见性剔除）。
 *
 * 几何完全按桌面常量：圆角 14、内边距 12、徽标 32、徽标间距 12、标题高 22、标题间距 4、描边 1.5。
 */
export function drawElementCard(
  ctx: RenderContext2D,
  element: BoardElement,
  rect: ElementCardRect,
): void {
  const visual = getElementCardVisual(element)

  // 1. 底板：圆角矩形填充 + 描边（`:582-587`）。
  traceRoundedRectPath(ctx, rect.left, rect.top, rect.width, rect.height, ELEMENT_CARD_CORNER_RADIUS)
  ctx.fillStyle = CARD_BACKGROUND
  ctx.fill()
  ctx.strokeStyle = CARD_BORDER
  ctx.lineWidth = ELEMENT_CARD_BORDER_WIDTH
  ctx.stroke()

  // 2. 徽标：左上角圆形色块 + 占位字形（`:591-605`）。
  const iconLeft = rect.left + ELEMENT_CARD_PADDING
  const iconTop = rect.top + ELEMENT_CARD_PADDING
  const iconRadius = ELEMENT_CARD_ICON_SIZE / 2
  if (visual.glyph.length > 0) {
    ctx.beginPath()
    ctx.arc(iconLeft + iconRadius, iconTop + iconRadius, iconRadius, 0, Math.PI * 2)
    ctx.fillStyle = visual.accentColor
    ctx.fill()
    drawCenteredText(
      ctx,
      visual.glyph,
      iconLeft + iconRadius,
      iconTop + iconRadius,
      ICON_FONT,
      CARD_ICON_TEXT,
    )
  }

  // 3. 文字区：徽标右侧到右边距（`:607-621`）。
  const textLeft = iconLeft + ELEMENT_CARD_ICON_SIZE + ELEMENT_CARD_ICON_GAP
  const textWidth = rect.left + rect.width - ELEMENT_CARD_PADDING - textLeft
  const titleTop = rect.top + ELEMENT_CARD_PADDING
  if (visual.title.length > 0) {
    drawClippedLines(ctx, [visual.title], {
      left: textLeft,
      top: titleTop,
      width: textWidth,
      height: ELEMENT_CARD_TITLE_HEIGHT,
      fontSize: ELEMENT_CARD_TITLE_FONT_SIZE,
      font: TITLE_FONT,
      color: CARD_TEXT,
    })
  }

  const bodyTop = titleTop + ELEMENT_CARD_TITLE_HEIGHT + ELEMENT_CARD_TITLE_GAP
  const bodyHeight = rect.top + rect.height - ELEMENT_CARD_PADDING - bodyTop
  if (visual.secondary.length > 0 && bodyHeight > 0) {
    drawClippedLines(ctx, visual.secondary.split('\n'), {
      left: textLeft,
      top: bodyTop,
      width: textWidth,
      height: bodyHeight,
      fontSize: ELEMENT_CARD_BODY_FONT_SIZE,
      font: BODY_FONT,
      color: CARD_SECONDARY_TEXT,
    })
  }
}

/** 裁剪矩形内绘制多行文本（桌面用 `DrawText` + `DrawTextOptions.Clip`，`:623-633`）。 */
interface ClippedTextLayout {
  left: number
  top: number
  width: number
  height: number
  fontSize: number
  font: string
  color: string
}

function drawClippedLines(
  ctx: RenderContext2D,
  lines: readonly string[],
  layout: ClippedTextLayout,
): void {
  if (layout.width <= 0 || layout.height <= 0) {
    return
  }
  ctx.save()
  ctx.beginPath()
  ctx.rect(layout.left, layout.top, layout.width, layout.height)
  ctx.clip()
  ctx.font = layout.font
  ctx.fillStyle = layout.color
  ctx.textAlign = 'left'
  ctx.textBaseline = 'top'
  const lineHeight = layout.fontSize * LINE_HEIGHT_FACTOR
  for (let index = 0; index < lines.length; index += 1) {
    ctx.fillText(lines[index], layout.left, layout.top + index * lineHeight)
  }
  ctx.restore()
}

/** 居中单行文本（徽标字形）。 */
function drawCenteredText(
  ctx: RenderContext2D,
  text: string,
  centerX: number,
  centerY: number,
  font: string,
  color: string,
): void {
  ctx.font = font
  ctx.fillStyle = color
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(text, centerX, centerY)
}

/** 圆角矩形路径（桌面 `RoundedRectangle(corner, corner)`）。 */
function traceRoundedRectPath(
  ctx: RenderContext2D,
  left: number,
  top: number,
  width: number,
  height: number,
  radius: number,
): void {
  ctx.beginPath()
  if (typeof ctx.roundRect === 'function') {
    ctx.roundRect(left, top, width, height, radius)
    return
  }
  // 退化：不支持 roundRect 的环境画直角矩形（布局一致，仅缺圆角）。
  ctx.rect(left, top, width, height)
}

/** 按元素类型推导卡片视觉参数（桌面 `GetElementCardVisual`，`:742-799`）。 */
function getElementCardVisual(element: BoardElement): ElementCardVisual {
  switch (element.type) {
    case 'link': {
      const url = (element.url ?? '').trim()
      const title = (element.title ?? '').trim()
      const heading = title.length > 0 ? title : url
      const secondary =
        title.length > 0 && url.length > 0 ? `${url}\n${LINK_OPEN_HINT}` : LINK_OPEN_HINT
      return { accentColor: ACCENT_BLUE, glyph: '↗', title: heading, secondary }
    }
    case 'media': {
      const name = getBestDisplayName(element.displayName, element.sourcePath)
      switch (element.kind) {
        case 'audio':
          return {
            accentColor: ACCENT_ORANGE,
            glyph: '♪',
            title: name,
            secondary: `${AUDIO_LABEL}\n${EXTERNAL_OPEN_HINT}`,
          }
        case 'video':
          return {
            accentColor: ACCENT_PURPLE,
            glyph: '▶',
            title: name,
            secondary: `${VIDEO_LABEL}\n${EXTERNAL_OPEN_HINT}`,
          }
        case 'image':
          return {
            accentColor: ACCENT_CYAN,
            glyph: '▣',
            title: name,
            secondary: `${IMAGE_NO_PREVIEW_LABEL}\n${EXTERNAL_OPEN_HINT}`,
          }
        default:
          return {
            accentColor: ACCENT_GRAY,
            glyph: '◇',
            title: name,
            secondary: `${MEDIA_LABEL}\n${EXTERNAL_OPEN_HINT}`,
          }
      }
    }
    case 'file': {
      const name = getBestDisplayName(element.displayName, element.sourcePath)
      const kindLabel = getFileKindLabel(name)
      if (kindLabel === null) {
        return {
          accentColor: ACCENT_GRAY,
          glyph: '◇',
          title: name,
          secondary: EXTERNAL_OPEN_HINT,
        }
      }
      return {
        accentColor: ACCENT_GREEN,
        glyph: '▤',
        title: name,
        secondary: `${kindLabel}\n${EXTERNAL_OPEN_HINT}`,
      }
    }
    case 'text':
      return {
        accentColor: ACCENT_GRAY,
        glyph: '✎',
        title: TEXT_ELEMENT_TITLE,
        secondary: buildTextPreview(element.text),
      }
    default:
      // 未知类型（损坏或未来版本数据）：桌面同样回退到灰色占位（`:798`）。
      return { accentColor: ACCENT_GRAY, glyph: '?', title: '', secondary: '' }
  }
}

/** 显示名：`displayName` → 路径文件名 → “未命名”（桌面 `GetBestDisplayName`）。 */
function getBestDisplayName(
  displayName: string | undefined,
  sourcePath: string | null | undefined,
): string {
  const trimmed = displayName?.trim() ?? ''
  if (trimmed.length > 0) {
    return trimmed
  }
  const fileName = getFileName(sourcePath)
  return fileName.length > 0 ? fileName : UNTITLED_NAME
}

/** 取路径最后一段（同时兼容 `\` 与 `/`）。 */
function getFileName(path: string | null | undefined): string {
  if (path === null || path === undefined) {
    return ''
  }
  const normalized = path.replace(/\\/g, '/')
  const index = normalized.lastIndexOf('/')
  return index >= 0 ? normalized.slice(index + 1) : normalized
}

/** 已知扩展名 → 类型标签；未知返回 `null`（桌面 known=false 分支）。 */
function getFileKindLabel(fileName: string): string | null {
  const dot = fileName.lastIndexOf('.')
  if (dot < 0) {
    return null
  }
  const extension = fileName.slice(dot).toLowerCase()
  return FILE_KIND_LABELS[extension] ?? null
}

/** 文本元素预览：截断到 4096 字符并补省略号（桌面 `MaxTextPreviewChars`）。 */
function buildTextPreview(text: string | undefined): string {
  const trimmed = text?.trim() ?? ''
  if (trimmed.length <= MAX_TEXT_PREVIEW_CHARS) {
    return trimmed
  }
  return `${trimmed.slice(0, MAX_TEXT_PREVIEW_CHARS)}…`
}

/** `ElementCardRect` → 世界包围盒。 */
function toWorldBounds(rect: ElementCardRect): WorldBounds {
  return {
    min: { x: rect.left, y: rect.top },
    max: { x: rect.left + rect.width, y: rect.top + rect.height },
  }
}
