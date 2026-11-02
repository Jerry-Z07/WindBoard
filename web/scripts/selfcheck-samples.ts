// 临时自检：读回 web/.interop/produced/ 下的样本，重写后比对 JSON 文本是否逐字节一致。
import { readFileSync } from 'node:fs'
import { unzipSync } from 'fflate'
import { readWbix } from '../src/persistence/wbix/read.ts'
import { writeWbix } from '../src/persistence/wbix/write.ts'

const dir = '.interop/produced/'
const names = [
  '01-empty-page.wbix',
  '02-multi-page.wbix',
  '03-ink-shapes.wbix',
  '04-elements.wbix',
  '05-embedded-image.wbix',
  '06-f32-tricky.wbix',
  '07-unicode-escapes.wbix',
]

let failures = 0
for (const name of names) {
  const bytes = new Uint8Array(readFileSync(dir + name))
  const result = readWbix(bytes)
  const original = unzipSync(bytes)
  const resources = result.manifest.resources.map((entry) => ({ entry, bytes: original[entry.path] }))
  const rewritten = writeWbix(result.workspace, {
    createdUtc: result.manifest.createdUtc ?? undefined,
    createdUtcText: result.manifest.createdUtcText ?? undefined,
    resources,
  })
  const out = unzipSync(rewritten)

  const jsonNames = Object.keys(original).filter((n) => n.endsWith('.json'))
  const identical = jsonNames.every(
    (n) => Buffer.compare(Buffer.from(original[n]), Buffer.from(out[n] ?? new Uint8Array())) === 0,
  )
  const entryOrderSame =
    JSON.stringify(Object.keys(original)) === JSON.stringify(Object.keys(out))

  if (!identical || !entryOrderSame) failures += 1
  console.log(
    `${name}  warn=${result.warnings.length}  jsonIdentical=${identical}  entryOrderSame=${entryOrderSame}  resources=${result.manifest.resources.length}`,
  )
}

console.log(failures === 0 ? '自检通过' : `自检失败：${failures} 个样本`)
process.exit(failures === 0 ? 0 : 1)
