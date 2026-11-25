/**
 * PNG 的 DPI 元数据（pHYs 块）写入。桌面用 WIC 保存 PNG 时会带上分辨率（wicBitmap.SetResolution(dpi, dpi)，BoardRasterExporter.cs:163），导出的图在插入 Word 或打印时尺寸才正确；浏览器的 canvas.toBlob('image/png') 不写 DPI，必须自己补 pHYs。
 * PNG 结构为 8 字节签名加若干块（长度 4B 大端、类型 4B、数据、CRC32 4B），pHYs 规定放在 IHDR 之后、IDAT 之前，数据 9 字节为每米像素 X 4B、每米像素 Y 4B、单位 1B（1 表示米）。
 */

/** PNG 签名（89 50 4E 47 0D 0A 1A 0A）。 */
const PNG_SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a] as const

/** 1 英寸 = 0.0254 米。 */
const METERS_PER_INCH = 0.0254

/** CRC32 查表（PNG 用标准 CRC-32/ISO-HDLC，多项式 0xEDB88320）。 */
const CRC_TABLE: Uint32Array = buildCrcTable()

function buildCrcTable(): Uint32Array {
  const table = new Uint32Array(256)
  for (let i = 0; i < 256; i += 1) {
    let value = i
    for (let bit = 0; bit < 8; bit += 1) {
      value = (value & 1) === 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1
    }
    table[i] = value >>> 0
  }
  return table
}

/** 计算一段字节的 CRC32。 */
export function crc32(bytes: Uint8Array): number {
  let crc = 0xffffffff
  for (const byte of bytes) {
    crc = (CRC_TABLE[(crc ^ byte) & 0xff] as number) ^ (crc >>> 8)
  }
  return (crc ^ 0xffffffff) >>> 0
}

/** 读 32 位大端整数。 */
function readUint32BE(bytes: Uint8Array, offset: number): number {
  return ((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]) >>> 0
}

/** 写 32 位大端整数。 */
function writeUint32BE(target: Uint8Array, offset: number, value: number): void {
  target[offset] = (value >>> 24) & 0xff
  target[offset + 1] = (value >>> 16) & 0xff
  target[offset + 2] = (value >>> 8) & 0xff
  target[offset + 3] = value & 0xff
}

/** 判断是否为合法 PNG（签名匹配）。 */
export function isPng(bytes: Uint8Array): boolean {
  if (bytes.length < PNG_SIGNATURE.length + 8) {
    return false
  }
  return PNG_SIGNATURE.every((byte, index) => bytes[index] === byte)
}

/**
 * 构造 `pHYs` 块（含长度/类型/数据/CRC）。
 *
 * @param dpi 目标 DPI；非有限或 `<= 0` 时按 96 处理。
 */
export function buildPhysChunk(dpi: number): Uint8Array {
  const safeDpi = Number.isFinite(dpi) && dpi > 0 ? dpi : 96
  const pixelsPerMeter = Math.round(safeDpi / METERS_PER_INCH)

  const chunk = new Uint8Array(4 + 4 + 9 + 4)
  writeUint32BE(chunk, 0, 9)
  // 类型 ASCII "pHYs"
  chunk[4] = 0x70
  chunk[5] = 0x48
  chunk[6] = 0x59
  chunk[7] = 0x73
  writeUint32BE(chunk, 8, pixelsPerMeter)
  writeUint32BE(chunk, 12, pixelsPerMeter)
  chunk[16] = 1 // 单位：米
  writeUint32BE(chunk, 17, crc32(chunk.subarray(4, 17)))
  return chunk
}

/**
 * 在 PNG 的 `IHDR` 之后插入 `pHYs` 块；已有 `pHYs` 时先剔除再插入（保持单块）。
 *
 * 返回新字节（不改入参）；不是合法 PNG 时返回 null。
 */
export function embedPngDpi(pngBytes: Uint8Array, dpi: number): Uint8Array | null {
  if (!isPng(pngBytes)) {
    return null
  }

  const phys = buildPhysChunk(dpi)
  const parts: Uint8Array[] = [pngBytes.subarray(0, PNG_SIGNATURE.length)]
  // 注意：`PNG_SIGNATURE` 是 `as const` 元组，`.length` 的类型是字面量 8，必须显式标成 number
  let offset: number = PNG_SIGNATURE.length
  // 记录插入点：读完 IHDR 之后
  let inserted = false
  let alreadyHasPhys = false

  while (offset + 8 <= pngBytes.length) {
    const length = readUint32BE(pngBytes, offset)
    const typeStart = offset + 4
    const type = String.fromCharCode(
      pngBytes[typeStart] as number,
      pngBytes[typeStart + 1] as number,
      pngBytes[typeStart + 2] as number,
      pngBytes[typeStart + 3] as number,
    )
    const chunkEnd = offset + 8 + length + 4
    if (chunkEnd > pngBytes.length) {
      // 截断的文件：原样返回剩余部分（不猜测数据）
      break
    }
    if (type === 'pHYs') {
      alreadyHasPhys = true
      offset = chunkEnd
      continue
    }

    parts.push(pngBytes.subarray(offset, chunkEnd))
    offset = chunkEnd
    if (type === 'IHDR') {
      parts.push(phys)
      inserted = true
    }
  }

  if (!inserted && !alreadyHasPhys) {
    return null
  }

  // 尾部残余（理论上只有 IEND 之后的空白）
  if (offset < pngBytes.length) {
    parts.push(pngBytes.subarray(offset))
  }

  const total = parts.reduce((sum, part) => sum + part.length, 0)
  const merged = new Uint8Array(total)
  let cursor = 0
  for (const part of parts) {
    merged.set(part, cursor)
    cursor += part.length
  }
  return merged
}
