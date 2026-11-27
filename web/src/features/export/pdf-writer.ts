/**
 * 最小 PDF 写入器（对应桌面 Features/Export/Services/BoardPdfExporter.cs）。桌面是每页一张整页位图，图用 FlateDecode 压缩后塞进 Image XObject（对象布局 :37、image 对象 :98-110、content 与 page 对象 :110-130、xref 与 trailer :216-260），这里逐条复刻，结构一致才谈得上两端导出的 PDF 等价。
 * 对象布局共 2 + pages*3 个对象：1 为 Catalog（/Pages 2 0 R），2 为 Pages（/Kids [...]、/Count），每页三个对象（page、content、image），编号 3+3i、4+3i、5+3i。
 */

import { deflateSync } from 'fflate'

/** 一页的位图数据（RGB，无 alpha；桌面 `WriteImageObject` 也是 DeviceRGB）。 */
export interface PdfPageImage {
  /** 每像素 3 字节，行优先，长度必须等于 `width * height * 3`。 */
  readonly rgb: Uint8Array
  readonly width: number
  readonly height: number
  /** 页面尺寸（PDF 点 = 1/72 英寸）；桌面按 `pixelSize * 72 / dpi` 计算。 */
  readonly pageWidthPt: number
  readonly pageHeightPt: number
}

/** 把数字格式化成 PDF 里安全的十进制串（最多三位小数，去掉多余的 0）。 */
function formatNumber(value: number): string {
  const fixed = Number.isFinite(value) ? value : 0
  const text = fixed.toFixed(3)
  // 去掉末尾 0 与小数点（桌面用 `"0.###"` 格式，结果一致）
  return text.replace(/\.?0+$/, '') === '' ? '0' : text.replace(/\.?0+$/, '')
}

/** 字节串拼接器（记录每个对象的起始偏移，供 xref 使用）。 */
class PdfBuffer {
  private readonly chunks: Uint8Array[] = []
  private length = 0
  /** 对象号 → 起始偏移（1 基）。 */
  private readonly offsets: number[] = []

  /** 已写入的字节数。 */
  get size(): number {
    return this.length
  }

  /** 写 ASCII 文本（PDF 结构关键字都是 ASCII）。 */
  writeText(text: string): void {
    const bytes = new Uint8Array(text.length)
    for (let i = 0; i < text.length; i += 1) {
      bytes[i] = text.charCodeAt(i) & 0xff
    }
    this.writeBytes(bytes)
  }

  /** 写原始字节。 */
  writeBytes(bytes: Uint8Array): void {
    this.chunks.push(bytes)
    this.length += bytes.length
  }

  /** 开始一个间接对象并记录偏移（对象内容由调用方继续写）。 */
  beginObject(objectNumber: number): void {
    if (objectNumber >= this.offsets.length) {
      this.offsets.length = objectNumber + 1
    }
    this.offsets[objectNumber] = this.length
    this.writeText(`${objectNumber} 0 obj\n`)
  }

  /** 结束一个间接对象。 */
  endObject(): void {
    this.writeText('endobj\n')
  }

  /** 取某对象的起始偏移（未写过则为 -1）。 */
  offsetOf(objectNumber: number): number {
    return this.offsets[objectNumber] ?? -1
  }

  /** 合并成连续字节。 */
  toBytes(): Uint8Array {
    const merged = new Uint8Array(this.length)
    let cursor = 0
    for (const chunk of this.chunks) {
      merged.set(chunk, cursor)
      cursor += chunk.length
    }
    return merged
  }
}

/**
 * 生成多页 PDF。图像用 FlateDecode（fflate.deflateSync）；content 流把图铺满整页，桌面用的是 q <w> 0 0 <h> 0 0 cm /Im0 Do Q（BoardPdfExporter.cs:116-130），即先缩放再平移的坐标矩阵，图像左下角对齐页面左下角。
 */
export function buildPdf(pages: readonly PdfPageImage[]): Uint8Array {
  const buffer = new PdfBuffer()
  const totalObjects = 2 + pages.length * 3

  // 头部 + 二进制注释行（告诉传输工具这是二进制文件）
  buffer.writeText('%PDF-1.4\n')
  buffer.writeBytes(new Uint8Array([0x25, 0xe2, 0xe3, 0xcf, 0xd3, 0x0a]))

  const catalogObject = 1
  const pagesObject = 2

  buffer.beginObject(catalogObject)
  buffer.writeText(`<< /Type /Catalog /Pages ${pagesObject} 0 R >>\n`)
  buffer.endObject()

  buffer.beginObject(pagesObject)
  const kids: string[] = []
  for (let i = 0; i < pages.length; i += 1) {
    kids.push(`${3 + i * 3} 0 R`)
  }
  buffer.writeText(`<< /Type /Pages /Count ${pages.length} /Kids [${kids.join(' ')}] >>\n`)
  buffer.endObject()

  for (let i = 0; i < pages.length; i += 1) {
    const page = pages[i]
    const pageObject = 3 + i * 3
    const contentObject = 4 + i * 3
    const imageObject = 5 + i * 3

    const compressed = deflateSync(page.rgb)
    buffer.beginObject(imageObject)
    buffer.writeText('<< /Type /XObject /Subtype /Image ')
    buffer.writeText(`/Width ${page.width} /Height ${page.height} `)
    buffer.writeText('/ColorSpace /DeviceRGB /BitsPerComponent 8 ')
    buffer.writeText(`/Filter /FlateDecode /Length ${compressed.length} >>\n`)
    buffer.writeText('stream\n')
    buffer.writeBytes(compressed)
    buffer.writeText('\nendstream\n')
    buffer.endObject()

    const content = `q\n${formatNumber(page.pageWidthPt)} 0 0 ${formatNumber(page.pageHeightPt)} 0 0 cm\n/Im0 Do\nQ\n`
    const contentBytes = new Uint8Array(content.length)
    for (let c = 0; c < content.length; c += 1) {
      contentBytes[c] = content.charCodeAt(c) & 0xff
    }
    buffer.beginObject(contentObject)
    buffer.writeText(`<< /Length ${contentBytes.length} >>\n`)
    buffer.writeText('stream\n')
    buffer.writeBytes(contentBytes)
    buffer.writeText('endstream\n')
    buffer.endObject()

    buffer.beginObject(pageObject)
    buffer.writeText('<< /Type /Page ')
    buffer.writeText(`/Parent ${pagesObject} 0 R `)
    buffer.writeText(`/MediaBox [0 0 ${formatNumber(page.pageWidthPt)} ${formatNumber(page.pageHeightPt)}] `)
    buffer.writeText(`/Resources << /XObject << /Im0 ${imageObject} 0 R >> >> `)
    buffer.writeText(`/Contents ${contentObject} 0 R >>\n`)
    buffer.endObject()
  }

  const xrefStart = buffer.size
  buffer.writeText('xref\n')
  buffer.writeText(`0 ${totalObjects + 1}\n`)
  buffer.writeText('0000000000 65535 f \n')
  for (let objectNumber = 1; objectNumber <= totalObjects; objectNumber += 1) {
    const offset = buffer.offsetOf(objectNumber)
    buffer.writeText(`${String(Math.max(0, offset)).padStart(10, '0')} 00000 n \n`)
  }
  buffer.writeText('trailer\n')
  buffer.writeText(`<< /Size ${totalObjects + 1} /Root ${catalogObject} 0 R >>\n`)
  buffer.writeText('startxref\n')
  buffer.writeText(`${xrefStart}\n`)
  buffer.writeText('%%EOF\n')

  return buffer.toBytes()
}

/**
 * 像素 → PDF 点。
 *
 * 桌面 `BoardPdfExporter.cs:91-93`：`pt = px * 72 / dpi`。
 */
export function pixelsToPoints(pixels: number, dpi: number): number {
  const safeDpi = Number.isFinite(dpi) && dpi > 0 ? dpi : 96
  return (pixels * 72) / safeDpi
}

/**
 * 把 RGBA 像素转成 PDF 需要的 RGB（丢弃 alpha，按黑底预乘以免透明边变白）。导出用不透明背景（画布背景色），正常情况下 alpha 恒为 255，这里仍做一次合成，避免半透明笔迹在 PDF 里出现白边。
 */
export function rgbaToRgb(rgba: Uint8Array): Uint8Array {
  const pixels = Math.floor(rgba.length / 4)
  const rgb = new Uint8Array(pixels * 3)
  for (let i = 0; i < pixels; i += 1) {
    const r = rgba[i * 4] as number
    const g = rgba[i * 4 + 1] as number
    const b = rgba[i * 4 + 2] as number
    const a = rgba[i * 4 + 3] as number
    rgb[i * 3] = Math.round((r * a) / 255)
    rgb[i * 3 + 1] = Math.round((g * a) / 255)
    rgb[i * 3 + 2] = Math.round((b * a) / 255)
  }
  return rgb
}
