/**
 * 生成 PWA 图标（PNG），零依赖：自绘 + 自带 PNG 编码器（zlib 用 Node 内置模块）。
 *
 * 为什么不用 canvas 库：仓库不引第三方图形依赖，而图标只有「圆角方块 + 一笔曲线」，
 * 用解析式覆盖度直接算比装 `canvas`/`sharp` 便宜得多；产物又是二进制，放进 `public/`
 * 后跟源码一起受版本控制，平时不需要重新生成。
 *
 * 图形与 `public/favicon.svg` 同一套语言：品牌蓝底 `#1976D2` + 白色贝塞尔笔迹。
 * 运行：`node scripts/generate-icons.mjs`（`pnpm icons`）。
 */

import { deflateSync } from 'node:zlib'
import { writeFileSync, mkdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const PUBLIC_DIR = resolve(dirname(fileURLToPath(import.meta.url)), '../public')

/** 品牌蓝（与 Dock 选中态 `#1976D2` 一致）。 */
const BRAND = [0x19, 0x76, 0xd2]
/** 笔迹颜色。 */
const INK = [0xff, 0xff, 0xff]

/** 圆角半径占边长比例（与 favicon 的观感一致）。 */
const CORNER_RADIUS_RATIO = 0.22
/** 笔迹线宽占边长比例。 */
const STROKE_WIDTH_RATIO = 0.075

/**
 * 笔迹曲线控制点（归一化坐标）。
 *
 * 纵向振幅刻意压到 ±0.28：振幅再大（例如 0.28→0.72）曲线在中段曲率半径会小于线宽，
 * 描出来的"管子"会在拐点自相交出一块台阶状的缺口。
 */
const CURVE = [
  [0.24, 0.66],
  [0.40, 0.36],
  [0.60, 0.64],
  [0.76, 0.34],
]

/**
 * 计算像素到圆角矩形的覆盖度（0~1），用 0.5px 过渡做抗锯齿。
 * @param {number} x 像素中心 x
 * @param {number} y 像素中心 y
 * @param {number} size 边长
 * @param {number} radius 圆角半径
 * @returns {number}
 */
function roundedRectCoverage(x, y, size, radius) {
  // 折到第一象限：矩形对称，圆角中心在 (radius, radius)
  const dx = Math.max(radius - x, x - (size - radius), 0)
  const dy = Math.max(radius - y, y - (size - radius), 0)
  const distance = Math.hypot(dx, dy) - radius
  return clamp01(0.5 - distance)
}

/**
 * 三次贝塞尔上某点的坐标。
 * @param {number} t 参数 0~1
 * @returns {{x: number, y: number}}
 */
function cubicAt(t) {
  const mt = 1 - t
  const a = mt * mt * mt
  const b = 3 * mt * mt * t
  const c = 3 * mt * t * t
  const d = t * t * t
  return {
    x: a * CURVE[0][0] + b * CURVE[1][0] + c * CURVE[2][0] + d * CURVE[3][0],
    y: a * CURVE[0][1] + b * CURVE[1][1] + c * CURVE[2][1] + d * CURVE[3][1],
  }
}

/**
 * 把值夹到 0~1。
 * @param {number} value 输入
 * @returns {number}
 */
function clamp01(value) {
  return value < 0 ? 0 : value > 1 ? 1 : value
}

/**
 * 渲染一枚图标。
 * @param {number} size 输出边长（像素）
 * @param {{maskable?: boolean}} [options] `maskable` 时整块铺满（不留圆角），供平台自行裁形
 * @returns {Uint8Array} RGBA 像素
 */
function renderIcon(size, options = {}) {
  const maskable = options.maskable === true
  // maskable 图标要把内容收进安全区：平台可能裁成圆形，四周 10% 会被切掉
  const contentScale = maskable ? 0.8 : 1
  const offset = (size - size * contentScale) / 2
  const radius = size * CORNER_RADIUS_RATIO
  const halfStroke = (size * STROKE_WIDTH_RATIO) / 2

  /** @type {Uint8Array} */
  const pixels = new Uint8Array(size * size * 4)
  // 先铺背景与圆角覆盖度
  for (let y = 0; y < size; y += 1) {
    for (let x = 0; x < size; x += 1) {
      const px = x + 0.5
      const py = y + 0.5
      const bg = maskable ? 1 : roundedRectCoverage(px, py, size, radius)
      const index = (y * size + x) * 4
      pixels[index] = BRAND[0]
      pixels[index + 1] = BRAND[1]
      pixels[index + 2] = BRAND[2]
      pixels[index + 3] = Math.round(255 * bg)
    }
  }

  // 再盖笔迹：沿曲线等距撒点，按点到线宽边缘的覆盖度取最大值
  const samples = Math.max(256, Math.round(size * 4))
  for (let i = 0; i <= samples; i += 1) {
    const point = cubicAt(i / samples)
    const cx = offset + point.x * size * contentScale
    const cy = offset + point.y * size * contentScale
    const r = halfStroke * contentScale
    const minX = Math.max(0, Math.floor(cx - r - 1))
    const maxX = Math.min(size - 1, Math.ceil(cx + r + 1))
    const minY = Math.max(0, Math.floor(cy - r - 1))
    const maxY = Math.min(size - 1, Math.ceil(cy + r + 1))
    for (let y = minY; y <= maxY; y += 1) {
      for (let x = minX; x <= maxX; x += 1) {
        const distance = Math.hypot(x + 0.5 - cx, y + 0.5 - cy)
        const coverage = clamp01(0.5 - (distance - r))
        if (coverage <= 0) continue
        const index = (y * size + x) * 4
        const alpha = Math.round(255 * coverage)
        if (alpha <= pixels[index + 3] && pixels[index] === INK[0]) {
          // 已经是不透明的白色，无需再叠加
          if (coverage < 1) continue
        }
        pixels[index] = INK[0]
        pixels[index + 1] = INK[1]
        pixels[index + 2] = INK[2]
        pixels[index + 3] = Math.max(pixels[index + 3], alpha)
      }
    }
  }
  return pixels
}

/** CRC32 查表（PNG 每个 chunk 都要）。 */
const CRC_TABLE = (() => {
  const table = new Uint32Array(256)
  for (let i = 0; i < 256; i += 1) {
    let value = i
    for (let bit = 0; bit < 8; bit += 1) {
      value = (value & 1) === 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1
    }
    table[i] = value >>> 0
  }
  return table
})()

/**
 * 计算 CRC32。
 * @param {Uint8Array} bytes 输入
 * @returns {number}
 */
function crc32(bytes) {
  let crc = 0xffffffff
  for (const byte of bytes) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8)
  return (crc ^ 0xffffffff) >>> 0
}

/**
 * 拼一个 PNG chunk。
 * @param {string} type 四字符类型
 * @param {Uint8Array} data 负载
 * @returns {Uint8Array}
 */
function chunk(type, data) {
  const out = new Uint8Array(12 + data.length)
  const view = new DataView(out.buffer)
  view.setUint32(0, data.length)
  for (let i = 0; i < 4; i += 1) out[4 + i] = type.charCodeAt(i)
  out.set(data, 8)
  const crcInput = out.subarray(4, 8 + data.length)
  view.setUint32(8 + data.length, crc32(crcInput))
  return out
}

/**
 * 把 RGBA 像素编码成 PNG（8 位、真彩 + alpha、无隔行）。
 * @param {Uint8Array} pixels RGBA 像素
 * @param {number} size 边长
 * @returns {Uint8Array}
 */
function encodePng(pixels, size) {
  const ihdr = new Uint8Array(13)
  const view = new DataView(ihdr.buffer)
  view.setUint32(0, size)
  view.setUint32(4, size)
  ihdr[8] = 8 // 位深
  ihdr[9] = 6 // 颜色类型：RGBA
  ihdr[10] = 0 // 压缩方法
  ihdr[11] = 0 // 过滤方法
  ihdr[12] = 0 // 非隔行

  // 每行前置一个过滤字节 0（None）
  const stride = size * 4
  const raw = new Uint8Array((stride + 1) * size)
  for (let y = 0; y < size; y += 1) {
    raw[y * (stride + 1)] = 0
    raw.set(pixels.subarray(y * stride, (y + 1) * stride), y * (stride + 1) + 1)
  }

  const signature = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
  const parts = [
    signature,
    chunk('IHDR', ihdr),
    chunk('IDAT', new Uint8Array(deflateSync(raw, { level: 9 }))),
    chunk('IEND', new Uint8Array(0)),
  ]
  const total = parts.reduce((sum, part) => sum + part.length, 0)
  const out = new Uint8Array(total)
  let cursor = 0
  for (const part of parts) {
    out.set(part, cursor)
    cursor += part.length
  }
  return out
}

mkdirSync(PUBLIC_DIR, { recursive: true })

const targets = [
  { file: 'icon-192.png', size: 192 },
  { file: 'icon-512.png', size: 512 },
  // maskable：整块铺满、内容收进安全区，平台裁成圆形/水滴形时不会被切到笔迹
  { file: 'icon-maskable-512.png', size: 512, maskable: true },
  // iOS 的 apple-touch-icon 也叫 180/167，这里统一给 180
  { file: 'apple-touch-icon.png', size: 180 },
]

for (const target of targets) {
  const pixels = renderIcon(target.size, { maskable: target.maskable === true })
  const png = encodePng(pixels, target.size)
  const path = resolve(PUBLIC_DIR, target.file)
  writeFileSync(path, png)
  console.log(`${target.file}: ${png.length} bytes (${target.size}x${target.size})`)
}
