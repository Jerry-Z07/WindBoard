import { createHash } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { BoardElement } from '../src/board/model/element.ts'
import type { WorkspaceSnapshot } from '../src/board/model/workspace.ts'
import { f32 } from '../src/persistence/wbix/f32-format.ts'
import type { WbixWriteResource } from '../src/persistence/wbix/write.ts'
import { writeWbix } from '../src/persistence/wbix/write.ts'

/**
 * 生成 P0.4 的互操作样本（`web/.interop/produced/*.wbix`）。
 *
 * 用途：交给桌面端 `WbixWorkspaceSerializer` 读回，验证 web 写出的包在桌面侧
 * 语义无损（implement.md P0.5 的 D 步：C# 读 produced）。
 *
 * 约定：
 * - 所有样本用**固定 GUID** 与**固定 createdUtc**，保证重复运行产出文本逐字节一致；
 * - 数值一律先过 `f32()`（域模型约定：进入持久化层的数值已是 float32）；
 * - 每个样本在控制台打印字节数与 SHA-256，便于和桌面端重写结果对比。
 *
 * 运行：`pnpm samples`（等价 `node scripts/produce-samples.ts`）
 */

const OUTPUT_DIR = fileURLToPath(new URL('../.interop/produced/', import.meta.url))
const CREATED_UTC = new Date('2026-10-31T13:45:12.123Z')

function pageId(n: number): string {
  return `aaaaaaaa-0000-4000-8000-00000000000${n}`
}

function elementId(n: number): string {
  return `bbbbbbbb-0000-4000-8000-00000000000${n}`
}

function emptyPage(n: number) {
  return { id: pageId(n), strokes: [], elements: [] }
}

function workspace(pages: WorkspaceSnapshot['pages'], overrides: Partial<WorkspaceSnapshot> = {}): WorkspaceSnapshot {
  return {
    pages,
    currentIndex: 0,
    viewportCameraWorld: null,
    viewportZoom: null,
    viewportSizeDip: null,
    ...overrides,
  }
}

function textElement(n: number, text: string, overrides: Partial<BoardElement> = {}): BoardElement {
  return {
    id: elementId(n),
    layer: 'belowInk',
    order: 0,
    positionWorld: { x: 10, y: 20 },
    sizeWorld: { x: 320, y: 180 },
    type: 'text',
    text,
    ...overrides,
  }
}

interface Sample {
  fileName: string
  /** 样本意图（写进控制台与 INDEX.md）。 */
  purpose: string
  workspace: WorkspaceSnapshot
  resources?: WbixWriteResource[]
}

const samples: Sample[] = [
  {
    fileName: '01-empty-page.wbix',
    purpose: '最小包：单页空内容、无视口元数据（验证 resources: [] 与空数组同行）',
    workspace: workspace([emptyPage(1)]),
  },
  {
    fileName: '02-multi-page.wbix',
    purpose: '多页 + currentIndex 非 0 + 视口三项（验证页排序、currentIndex、viewport 键序）',
    workspace: workspace(
      [
        {
          id: pageId(1),
          strokes: [
            {
              kind: 'stroke',
              points: [
                { x: f32(12.5), y: f32(30.25), pressure: f32(0.5) },
                { x: f32(20), y: f32(40), pressure: 1 },
              ],
              colorRgba: { x: 0, y: 0, z: 0, w: 1 },
              baseSize: 4,
              enablePressure: true,
            },
          ],
          elements: [],
        },
        { id: pageId(2), strokes: [], elements: [] },
        { id: pageId(3), strokes: [], elements: [] },
      ],
      {
        currentIndex: 1,
        viewportCameraWorld: { x: f32(-120.5), y: f32(64.25) },
        viewportZoom: f32(1.75),
        viewportSizeDip: { x: 1280, y: 720 },
      },
    ),
  },
  {
    fileName: '03-ink-shapes.wbix',
    purpose: '四种形状 + 一条折线笔迹（验证 v3 包装 kind/shape 与 stroke 的键序）',
    workspace: workspace([
      {
        id: pageId(1),
        strokes: [
          {
            kind: 'stroke',
            points: [
              { x: 0, y: 0, pressure: f32(0.25) },
              { x: f32(50.5), y: f32(50.5), pressure: 1 },
            ],
            colorRgba: { x: 0, y: 0, z: 1, w: 1 },
            baseSize: f32(2.5),
            enablePressure: true,
          },
          {
            kind: 'line',
            start: { x: 0, y: 0 },
            end: { x: 100, y: 100 },
            colorRgba: { x: 1, y: 0, z: 0, w: 1 },
            width: 2,
          },
          {
            kind: 'rect',
            start: { x: f32(-10.5), y: f32(-20.25) },
            end: { x: 30, y: 40 },
            colorRgba: { x: 0, y: 1, z: 0, w: 1 },
            width: f32(1.5),
          },
          {
            kind: 'ellipse',
            start: { x: 5, y: 5 },
            end: { x: 25, y: 15 },
            colorRgba: { x: 0, y: 0, z: 0, w: f32(0.5) },
            width: 3,
          },
          {
            kind: 'arrow',
            start: { x: 0, y: 0 },
            end: { x: f32(80.125), y: f32(-40.0625) },
            colorRgba: { x: 0, y: 0, z: 0, w: 1 },
            width: 2,
          },
        ],
        elements: [],
      },
    ]),
  },
  {
    fileName: '04-elements.wbix',
    purpose: 'text/link/media/file 四类元素 + below/above 分层 + order 含负数（验证分层排序与字段序）',
    workspace: workspace([
      {
        id: pageId(1),
        strokes: [],
        elements: [
          textElement(1, '下方文字'),
          {
            id: elementId(2),
            layer: 'belowInk',
            order: 2,
            positionWorld: { x: f32(100.5), y: f32(200.25) },
            sizeWorld: { x: 400, y: 60 },
            type: 'link',
            url: 'https://example.com/a?b=1&c=2',
            title: null,
          },
          {
            id: elementId(3),
            layer: 'belowInk',
            order: 3,
            positionWorld: { x: 0, y: 0 },
            sizeWorld: { x: 320, y: 180 },
            type: 'media',
            kind: 'video',
            sourcePath: 'D:\\media\\演示.mp4',
            displayName: '演示.mp4',
            resourceId: null,
          },
          {
            id: elementId(4),
            layer: 'aboveInk',
            order: -1,
            positionWorld: { x: 50, y: 60 },
            sizeWorld: { x: 200, y: 40 },
            type: 'text',
            text: '上方文字',
          },
          {
            id: elementId(5),
            layer: 'aboveInk',
            order: -1,
            positionWorld: { x: 0, y: 0 },
            sizeWorld: { x: 320, y: 180 },
            type: 'file',
            sourcePath: 'D:\\docs\\报告.pdf',
            displayName: '报告.pdf',
          },
        ],
      },
    ]),
  },
  {
    fileName: '05-embedded-image.wbix',
    purpose: '内嵌图片：media.sourcePath=null + resourceId + assets/elements 资源（验证资源条目与 meta）',
    workspace: workspace([
      {
        id: pageId(1),
        strokes: [],
        elements: [
          {
            id: elementId(1),
            layer: 'belowInk',
            order: 0,
            positionWorld: { x: f32(16.5), y: f32(24.25) },
            sizeWorld: { x: 640, y: 360 },
            type: 'media',
            kind: 'image',
            sourcePath: null,
            displayName: '图片',
            resourceId: 'res-cover',
          },
        ],
      },
    ]),
    resources: [
      {
        entry: {
          id: 'res-cover',
          type: 'image',
          path: `assets/elements/${elementId(1).replace(/-/g, '')}.png`,
          contentType: 'image/png',
          meta: { role: 'elementImage', elementId: elementId(1) },
        },
        // 1x1 透明 PNG（最小合法 PNG，桌面端只做字节搬运，不校验内容）
        bytes: Uint8Array.from(
          atob(
            'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==',
          ),
          (character) => character.charCodeAt(0),
        ),
      },
    ],
  },
  {
    fileName: '06-f32-tricky.wbix',
    purpose: 'float32 文本边界：1E-07 / 3.4028235E+38 / 123456790 / -0 / 1E-45（验证 formatF32 全部分支）',
    workspace: workspace(
      [
        {
          id: pageId(1),
          strokes: [
            {
              kind: 'stroke',
              points: [
                { x: f32(1e-7), y: f32(3.4028235e38), pressure: f32(-0) },
                { x: f32(123456790), y: f32(1e-45), pressure: f32(0.1) },
                { x: f32(1 / 3), y: f32(-12.75), pressure: f32(16777216) },
              ],
              colorRgba: { x: f32(0.1), y: f32(0.2), z: f32(0.3), w: f32(0.4) },
              baseSize: f32(1e-7),
              enablePressure: true,
            },
          ],
          elements: [],
        },
      ],
      { viewportZoom: f32(1e-7), viewportCameraWorld: { x: f32(1e9), y: f32(-1e9) } },
    ),
  },
  {
    fileName: '07-unicode-escapes.wbix',
    purpose: '非 ASCII 与 HTML 敏感字符转义：中文 / emoji / < > & \' " + / 反斜杠 / 制表符（验证 escapeJsonString）',
    workspace: workspace([
      {
        id: pageId(1),
        strokes: [],
        elements: [
          textElement(1, '中文笔迹'),
          textElement(2, 'a<b>c&d\'e"f+g', { order: 1 }),
          textElement(3, '😀 增补平面', { order: 2 }),
          textElement(4, 'C:\\path\\to\t制表符', { order: 3 }),
          textElement(5, '换行\n第二行', { order: 4 }),
        ],
      },
    ]),
  },
]

function sha256(bytes: Uint8Array): string {
  return createHash('sha256').update(bytes).digest('hex')
}

function main(): void {
  mkdirSync(OUTPUT_DIR, { recursive: true })

  const indexLines = [
    '# 互操作样本索引',
    '',
    '由 `pnpm samples`（`web/scripts/produce-samples.ts`）生成，用于双端读回验证。',
    '所有样本使用固定 GUID 与固定 `createdUtc`（2026-10-31T13:45:12.123+00:00），重复运行文本逐字节一致。',
    '',
    '| 文件 | 字节数 | SHA-256 | 意图 |',
    '| --- | --- | --- | --- |',
  ]

  for (const sample of samples) {
    const bytes = writeWbix(sample.workspace, {
      createdUtc: CREATED_UTC,
      resources: sample.resources,
    })
    const target = join(OUTPUT_DIR, sample.fileName)
    writeFileSync(target, bytes)

    const digest = sha256(bytes)
    console.log(`${sample.fileName}  ${bytes.byteLength} B  ${digest}`)
    console.log(`  ${sample.purpose}`)

    indexLines.push(`| ${sample.fileName} | ${bytes.byteLength} | ${digest} | ${sample.purpose} |`)
  }

  writeFileSync(join(OUTPUT_DIR, 'INDEX.md'), `${indexLines.join('\n')}\n`)
  console.log(`\n已写入 ${samples.length} 个样本到 ${OUTPUT_DIR}`)
}

main()
