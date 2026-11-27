import type { Guid } from './ids.ts'
import type { Vec2 } from './stroke.ts'

/**
 * 页面元素域模型（对应桌面 BoardElementSnapshot 家族）。WBIX 里元素是 { type, data }，data 内的字段随类型不同（见 persistence/wbix/page-payload.ts）；layer 决定元素在笔迹下方还是上方，order 是层内绘制与命中顺序；媒体元素的内嵌图片通过 resourceId 引用 manifest.resources 里的资源条目。
 */

/** 元素层级（桌面 `"belowInk"` / `"aboveInk"`）。 */
export const ELEMENT_LAYERS = ['belowInk', 'aboveInk'] as const

/** 元素层级类型。 */
export type Layer = (typeof ELEMENT_LAYERS)[number]

/** 元素类型集合（WBIX 当前覆盖 text/link/media/file）。 */
export const ELEMENT_TYPES = ['text', 'link', 'media', 'file'] as const

/** 元素类型。 */
export type ElementType = (typeof ELEMENT_TYPES)[number]

/** 媒体种类（桌面 `BoardMediaKind`）。 */
export const MEDIA_KINDS = ['image', 'audio', 'video'] as const

/** 媒体种类类型。 */
export type MediaKind = (typeof MEDIA_KINDS)[number]

/** 是否为合法层级。 */
export function isLayer(value: unknown): value is Layer {
  return typeof value === 'string' && (ELEMENT_LAYERS as readonly string[]).includes(value)
}

/** 是否为合法元素类型。 */
export function isElementType(value: unknown): value is ElementType {
  return typeof value === 'string' && (ELEMENT_TYPES as readonly string[]).includes(value)
}

/** 是否为合法媒体种类。 */
export function isMediaKind(value: unknown): value is MediaKind {
  return typeof value === 'string' && (MEDIA_KINDS as readonly string[]).includes(value)
}

/**
 * 解析媒体种类文本（对齐桌面 `ParseMediaKind`：image/audio/video，其它一律 video）。
 */
export function parseMediaKind(value: unknown): MediaKind {
  if (typeof value !== 'string') {
    return 'video'
  }

  switch (value.trim().toLowerCase()) {
    case 'image':
      return 'image'
    case 'audio':
      return 'audio'
    case 'video':
    default:
      return 'video'
  }
}

/** 元素默认尺寸（桌面读侧缺省 `new Vector2(320f, 180f)`）。 */
export const DEFAULT_ELEMENT_SIZE: Readonly<Vec2> = { x: 320, y: 180 }

/** 页面元素。 */
export interface BoardElement {
  id: Guid
  layer: Layer
  /** 层内顺序；写侧 `order < 0` 时用数组下标替代。 */
  order: number
  positionWorld: Vec2
  sizeWorld: Vec2
  type: ElementType
  /** type=text。 */
  text?: string
  /** type=link。 */
  url?: string
  /** type=link；缺省为 null。 */
  title?: string | null
  /** type=media。 */
  kind?: MediaKind
  /** type=media/file；内嵌图片时桌面写侧输出 null。 */
  sourcePath?: string | null
  /** type=media/file。 */
  displayName?: string
  /** type=media；指向 `manifest.resources[].id`。 */
  resourceId?: string | null
}
