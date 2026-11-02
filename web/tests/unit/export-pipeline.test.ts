/**
 * P4 导出计算层单测：画幅计算、PNG 的 DPI 块、PDF 字节结构。
 *
 * 这三块都是纯函数（不碰 DOM），所以放在 node 环境里跑；
 * 真机渲染（OffscreenCanvas）由浏览器人工验收覆盖。
 */

import { describe, expect, it } from 'vitest'
import {
  DEFAULT_MAX_EDGE_PIXELS,
  FIXED_FRAME_DPI,
  computeExportFrame,
  computePageContentBounds,
  constrainDpi,
} from '../../src/features/export/raster-frame.ts'
import { buildPhysChunk, crc32, embedPngDpi, isPng } from '../../src/features/export/png-encode.ts'
import { buildPdf, pixelsToPoints, rgbaToRgb } from '../../src/features/export/pdf-writer.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'

/** 造一条两点笔迹。 */
function strokeAt(x0: number, y0: number, x1: number, y1: number): Stroke {
  return {
    kind: 'stroke',
    points: [
      { x: x0, y: y0, pressure: 1 },
      { x: x1, y: y1, pressure: 1 },
    ],
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: false,
  }
}

describe('computeExportFrame', () => {
  const bounds = { min: { x: 0, y: 0 }, max: { x: 200, y: 100 } }

  it('固定画幅：dpi 恒 96，内容等比缩放后居中', () => {
    const frame = computeExportFrame(bounds, {
      paddingDip: 24,
      dpi: 192,
      fixedFrame: { pixelWidth: 1920, pixelHeight: 1080 },
      fallbackViewportSizeDip: { x: 800, y: 600 },
    })
    expect(frame.pixelWidth).toBe(1920)
    expect(frame.pixelHeight).toBe(1080)
    expect(frame.dpi).toBe(FIXED_FRAME_DPI)
    expect(frame.widthDip).toBe(1920)
    expect(frame.cameraWorld).toEqual({ x: 100, y: 50 })
    // 可用区 1920-48=1872 × 1080-48=1032 → 缩放取 min(1872/200, 1032/100)=9.36
    expect(frame.zoom).toBeCloseTo(9.36, 5)
  })

  it('固定画幅：空内容时相机在原点上、不缩放', () => {
    const frame = computeExportFrame(null, {
      paddingDip: 24,
      dpi: 96,
      fixedFrame: { pixelWidth: 720, pixelHeight: 720 },
      fallbackViewportSizeDip: { x: 800, y: 600 },
    })
    expect(frame.cameraWorld).toEqual({ x: 0, y: 0 })
    expect(frame.zoom).toBe(1)
  })

  it('自适应画幅：尺寸 = 内容 + 2×留白，并按 DPI 折算像素', () => {
    const frame = computeExportFrame(bounds, {
      paddingDip: 24,
      dpi: 192,
      fixedFrame: null,
      fallbackViewportSizeDip: { x: 800, y: 600 },
    })
    expect(frame.widthDip).toBe(248)
    expect(frame.heightDip).toBe(148)
    expect(frame.dpi).toBe(192)
    // ceil(248*192/96)=496、ceil(148*192/96)=296
    expect(frame.pixelWidth).toBe(496)
    expect(frame.pixelHeight).toBe(296)
    expect(frame.zoom).toBe(1)
  })

  it('自适应画幅：空内容用兜底视口尺寸', () => {
    const frame = computeExportFrame(null, {
      paddingDip: 24,
      dpi: 0,
      fixedFrame: null,
      fallbackViewportSizeDip: { x: 800, y: 600 },
    })
    expect(frame.widthDip).toBe(800)
    expect(frame.heightDip).toBe(600)
    expect(frame.dpi).toBe(96)
  })

  it('留白负数按 0 处理', () => {
    const frame = computeExportFrame(bounds, {
      paddingDip: -50,
      dpi: 96,
      fixedFrame: null,
      fallbackViewportSizeDip: { x: 800, y: 600 },
    })
    expect(frame.widthDip).toBe(200)
    expect(frame.heightDip).toBe(100)
  })

  it('constrainDpi：按最大边反算上限，下限 12', () => {
    expect(constrainDpi(384, 1000, 1000, 2000)).toBeCloseTo(192, 5)
    // 100000 DIP 宽在 16384 像素上限下只能到 15.7 DPI，仍高于 12 的下限
    expect(constrainDpi(96, 100000, 100000, DEFAULT_MAX_EDGE_PIXELS)).toBeCloseTo(15.72864, 4)
    // 足够大的画幅才会触到 12 DPI 的下限
    expect(constrainDpi(96, 1000000, 1000000, DEFAULT_MAX_EDGE_PIXELS)).toBe(12)
    expect(constrainDpi(96, 100, 100, 0)).toBe(96)
  })

  it('固定画幅的像素尺寸被 maxEdge 钳制', () => {
    const frame = computeExportFrame(bounds, {
      paddingDip: 0,
      dpi: 96,
      fixedFrame: { pixelWidth: 30000, pixelHeight: 10 },
      fallbackViewportSizeDip: { x: 800, y: 600 },
      maxEdgePixels: 4096,
    })
    expect(frame.pixelWidth).toBe(4096)
    expect(frame.pixelHeight).toBe(10)
  })
})

describe('computePageContentBounds', () => {
  it('只统计笔迹（桌面 TryGetDocumentBounds 同口径）', () => {
    const bounds = computePageContentBounds([
      strokeAt(10, 20, 30, 40),
      {
        kind: 'rect',
        start: { x: -100, y: -100 },
        end: { x: 900, y: 900 },
        colorRgba: { x: 0, y: 0, z: 0, w: 1 },
        width: 3,
      },
    ])
    // 形状的 -100/900 不应扩大包围盒
    expect(bounds?.min.x).toBeGreaterThanOrEqual(10 - 2)
    expect(bounds?.max.x).toBeLessThanOrEqual(30 + 2)
  })

  it('没有笔迹时返回 null', () => {
    expect(computePageContentBounds([])).toBeNull()
  })
})

describe('png-encode', () => {
  /** 造一个最小的合法 PNG（签名 + IHDR + IEND，不含 IDAT）。 */
  function minimalPng(): Uint8Array {
    const ihdrData = new Uint8Array(13)
    // 宽 1、高 1、位深 8、颜色类型 6（RGBA）、压缩/过滤/交错都为 0
    ihdrData.set([0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0])
    const ihdr = new Uint8Array(4 + 4 + ihdrData.length + 4)
    ihdr.set([0, 0, 0, 13], 0)
    ihdr.set([0x49, 0x48, 0x44, 0x52], 4)
    ihdr.set(ihdrData, 8)
    const crcInput = ihdr.subarray(4, 8 + ihdrData.length)
    const crc = crc32(crcInput)
    ihdr.set([(crc >>> 24) & 0xff, (crc >>> 16) & 0xff, (crc >>> 8) & 0xff, crc & 0xff], 8 + ihdrData.length)

    const iend = new Uint8Array([0, 0, 0, 0, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82])

    const png = new Uint8Array(8 + ihdr.length + iend.length)
    png.set([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], 0)
    png.set(ihdr, 8)
    png.set(iend, 8 + ihdr.length)
    return png
  }

  it('crc32 与已知值一致', () => {
    // "123456789" 的 CRC32 标准值
    const bytes = new TextEncoder().encode('123456789')
    expect(crc32(bytes)).toBe(0xcbf43926)
  })

  it('buildPhysChunk：96 DPI → 3780 像素每米', () => {
    const chunk = buildPhysChunk(96)
    expect(chunk.length).toBe(21)
    expect(chunk[4]).toBe(0x70) // p
    expect(chunk[5]).toBe(0x48) // H
    expect(chunk[6]).toBe(0x59) // Y
    expect(chunk[7]).toBe(0x73) // s
    const pixelsPerMeter = ((chunk[8] << 24) | (chunk[9] << 16) | (chunk[10] << 8) | chunk[11]) >>> 0
    expect(pixelsPerMeter).toBe(3780)
    expect(chunk[16]).toBe(1)
  })

  it('embedPngDpi 把 pHYs 插到 IHDR 之后，且可重复执行（不叠加）', () => {
    const png = minimalPng()
    expect(isPng(png)).toBe(true)

    const once = embedPngDpi(png, 192)
    expect(once).not.toBeNull()
    const bytes = once as Uint8Array
    // 签名 8 + IHDR 25 → pHYs 从 33 开始
    expect(String.fromCharCode(...bytes.subarray(37, 41))).toBe('pHYs')

    const twice = embedPngDpi(bytes, 192)
    expect(twice).not.toBeNull()
    const text = new TextDecoder('latin1').decode(twice as Uint8Array)
    expect(text.split('pHYs').length - 1).toBe(1)
  })

  it('非法 PNG 返回 null', () => {
    expect(embedPngDpi(new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9]), 96)).toBeNull()
  })
})

describe('pdf-writer', () => {
  it('空页列表也能生成结构完整的 PDF', () => {
    const pdf = buildPdf([])
    const text = new TextDecoder('latin1').decode(pdf)
    expect(text.startsWith('%PDF-1.4')).toBe(true)
    expect(text).toContain('/Count 0')
    expect(text.trimEnd().endsWith('%%EOF')).toBe(true)
  })

  it('两页 PDF：对象数、MediaBox 与 xref 偏移正确', () => {
    const page = {
      rgb: new Uint8Array(4 * 4 * 3),
      width: 4,
      height: 4,
      pageWidthPt: 288,
      pageHeightPt: 144,
    }
    const pdf = buildPdf([page, page])
    const text = new TextDecoder('latin1').decode(pdf)

    // 对象总数 = 2 + 2*3 = 8 → xref 表头 "0 9"
    expect(text).toContain('0 9\n')
    expect(text).toContain('/Count 2')
    expect(text).toContain('/MediaBox [0 0 288 144]')
    expect(text).toContain('/Im0 Do')

    // xref 里的每个偏移必须真的指向 "<n> 0 obj"
    const startxrefMatch = /startxref\n(\d+)\n/.exec(text)
    expect(startxrefMatch).not.toBeNull()
    const xrefStart = Number(startxrefMatch?.[1])
    expect(text.startsWith('xref', xrefStart)).toBe(true)

    const entries = text.slice(xrefStart).split('\n')
    // entries[0]='xref', [1]='0 9', [2]='0000000000 65535 f '
    for (let objectNumber = 1; objectNumber <= 8; objectNumber += 1) {
      const entry = entries[2 + objectNumber] as string
      const offset = Number(entry.slice(0, 10))
      expect(text.startsWith(`${objectNumber} 0 obj`, offset)).toBe(true)
    }
  })

  it('pixelsToPoints 与桌面换算一致（px*72/dpi）', () => {
    expect(pixelsToPoints(1920, 192)).toBe(720)
    expect(pixelsToPoints(4, 0)).toBe(3)
  })

  it('rgbaToRgb 丢弃 alpha 并按黑底合成', () => {
    const rgba = new Uint8Array([255, 0, 0, 255, 255, 255, 255, 128, 0, 0, 0, 0])
    const rgb = rgbaToRgb(rgba)
    expect(Array.from(rgb)).toEqual([255, 0, 0, 128, 128, 128, 0, 0, 0])
  })
})
