/**
 * WBIX 导入（读取侧）单元测试。
 *
 * 覆盖：
 * - 真实语料 `WindBoard.Tests/Fixtures/wbix/files/standard-v3.wbix`（桌面写侧产物）的解析结果；
 * - 空 / 损坏 / 缺 manifest 字节的致命错误；
 * - `applyImportedWorkspace` 的追加语义（页数、当前页、撤销栈）。
 */

import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { strToU8, zipSync } from 'fflate'
import { describe, expect, it } from 'vitest'
import { AddInkItemCommand } from '../../src/board/commands.ts'
import type { PageSnapshot } from '../../src/board/model/workspace.ts'
import { BoardWorkspace } from '../../src/board/workspace.ts'
import { applyImportedWorkspace, parseWbixForImport } from '../../src/features/import/wbix-import.ts'
import type { ImportedWorkspace } from '../../src/features/import/wbix-import.ts'
import { WbixError } from '../../src/persistence/wbix/errors.ts'

const FIXTURE_ROOT = fileURLToPath(new URL('../../../WindBoard.Tests/Fixtures/wbix/', import.meta.url))
const HAS_FIXTURES = existsSync(FIXTURE_ROOT)

/** 读取语料文件为字节。 */
function readFixture(name: string): Uint8Array {
  return new Uint8Array(readFileSync(`${FIXTURE_ROOT}files/${name}`))
}

/** 造一个空页快照。 */
function emptyPage(id: string): PageSnapshot {
  return { id, strokes: [], elements: [] }
}

/** 造一条最小笔迹（用于让现有页的撤销栈非空）。 */
function createStroke(): PageSnapshot['strokes'][number] {
  return {
    kind: 'stroke',
    points: [
      { x: 0, y: 0, pressure: 1 },
      { x: 10, y: 10, pressure: 1 },
    ],
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: true,
  }
}

/** 捕获期望的 `WbixError`。 */
function captureWbixError(action: () => void): WbixError {
  try {
    action()
  } catch (error) {
    if (error instanceof WbixError) {
      return error
    }

    throw error
  }

  throw new Error('预期抛出 WbixError，但没有抛错')
}

describe.skipIf(!HAS_FIXTURES)('parseWbixForImport（standard-v3 真实字节）', () => {
  it('解析出 3 页、当前页 1、无容忍告警', () => {
    const imported = parseWbixForImport(readFixture('standard-v3.wbix'))

    expect(imported.snapshots).toHaveLength(3)
    expect(imported.currentIndex).toBe(1)
    expect(imported.warnings).toEqual([])
  })

  it('页 id 与页序来自 manifest（按 index 稳定排序）', () => {
    const imported = parseWbixForImport(readFixture('standard-v3.wbix'))

    expect(imported.snapshots.map((page) => page.id)).toEqual([
      '11111111-1111-4111-8111-111111111111',
      '22222222-2222-4222-8222-222222222222',
      '88888888-8888-4888-8888-888888888888',
    ])
  })

  it('页内容随快照读出（page-000：5 笔迹 + 5 元素）', () => {
    const imported = parseWbixForImport(readFixture('standard-v3.wbix'))
    const first = imported.snapshots[0]

    expect(first.strokes).toHaveLength(5)
    expect(first.elements).toHaveLength(5)
    expect(imported.snapshots[1].strokes).toHaveLength(2)
    expect(imported.snapshots[1].elements).toHaveLength(0)
    expect(imported.snapshots[2].strokes).toHaveLength(1)
  })
})

describe('parseWbixForImport 致命错误（对齐 persistence 错误约定）', () => {
  it('空字节 → zip-read-failed', () => {
    const error = captureWbixError(() => {
      parseWbixForImport(new Uint8Array(0))
    })

    expect(error.code).toBe('zip-read-failed')
  })

  it('非 zip 字节 → zip-read-failed', () => {
    const error = captureWbixError(() => {
      parseWbixForImport(strToU8('这不是一个 zip 文件'))
    })

    expect(error.code).toBe('zip-read-failed')
  })

  it('zip 里缺 manifest.json → missing-manifest', () => {
    const bytes = zipSync({ 'pages/page-000.json': strToU8('{}') })
    const error = captureWbixError(() => {
      parseWbixForImport(bytes)
    })

    expect(error.code).toBe('missing-manifest')
  })
})

describe('applyImportedWorkspace（桌面 AppendAfterLastPage 语义）', () => {
  /** 3 页的导入结果，导入包记录的当前页是第 2 页（相对下标 1）。 */
  function createImported(): ImportedWorkspace {
    return {
      snapshots: [emptyPage('imported-a'), emptyPage('imported-b'), emptyPage('imported-c')],
      currentIndex: 1,
      warnings: [],
    }
  }

  it('追加到最后一页之后，当前页切到「第一张导入页 + 导入包记录的下标」', () => {
    const workspace = new BoardWorkspace()
    applyImportedWorkspace(workspace, createImported())

    // 现有 1 页 + 导入 3 页。
    expect(workspace.pages).toHaveLength(4)
    expect(workspace.pages.map((page) => page.id)).toEqual([
      workspace.pages[0].id,
      'imported-a',
      'imported-b',
      'imported-c',
    ])
    // startIndex=1、importCurrent=1 → 2，且当前页就是导入包记录的那一页。
    expect(workspace.currentIndex).toBe(2)
    expect(workspace.currentPage.id).toBe('imported-b')
  })

  it('现有页对象原样保留（id / 内容 / 会话都不变）', () => {
    const workspace = new BoardWorkspace()
    const originalId = workspace.currentPage.id
    const originalPage = workspace.currentPage
    workspace.currentPage.session.execute(new AddInkItemCommand(createStroke()))
    expect(workspace.currentPage.session.document.items).toHaveLength(1)

    applyImportedWorkspace(workspace, createImported())

    expect(workspace.pages[0].id).toBe(originalId)
    expect(workspace.pages[0].session.document.items).toHaveLength(1)
    // 桌面 `AppendPages` 保留已有页对象（连带会话与撤销栈），web `appendPages` 同语义。
    expect(workspace.pages[0]).toBe(originalPage)
  })

  it('导入后已有页的撤销栈仍可用（桌面 AppendPages 的关键语义）', () => {
    const workspace = new BoardWorkspace()
    workspace.currentPage.session.execute(new AddInkItemCommand(createStroke()))
    const pageWithHistory = workspace.pages[0]
    expect(pageWithHistory.session.canUndo).toBe(true)

    applyImportedWorkspace(workspace, createImported())

    // 已有页没被重建：撤销栈还在，undo() 真的回滚了那一笔。
    expect(pageWithHistory.session.canUndo).toBe(true)
    expect(pageWithHistory.session.undo()).toBe(true)
    expect(pageWithHistory.session.document.items).toHaveLength(0)
    expect(pageWithHistory.session.canRedo).toBe(true)
  })

  it('导入页是新建会话，撤销栈为空', () => {
    const workspace = new BoardWorkspace()
    applyImportedWorkspace(workspace, createImported())

    // 第 0 页是原有页；1..3 是导入页。
    for (const page of workspace.pages.slice(1)) {
      expect(page.session.canUndo).toBe(false)
      expect(page.session.canRedo).toBe(false)
    }
  })

  it('两种初始状态都按「原页数 + 导入页数」落位当前索引', () => {
    // 初始状态 1：工作区只有 1 个空页。
    const single = new BoardWorkspace()
    applyImportedWorkspace(single, createImported())
    expect(single.pages).toHaveLength(1 + 3)
    expect(single.currentIndex).toBe(1 + 1)
    expect(single.currentPage.id).toBe('imported-b')

    // 初始状态 2：工作区已有 3 页（当前页停在最后一页）。
    const multiple = new BoardWorkspace()
    multiple.addPage()
    multiple.addPage()
    expect(multiple.pages).toHaveLength(3)
    expect(multiple.currentIndex).toBe(2)

    applyImportedWorkspace(multiple, createImported())
    expect(multiple.pages).toHaveLength(3 + 3)
    expect(multiple.currentIndex).toBe(3 + 1)
    expect(multiple.currentPage.id).toBe('imported-b')
  })

  it('重复导入按次累加页数', () => {
    const workspace = new BoardWorkspace()
    applyImportedWorkspace(workspace, createImported())
    applyImportedWorkspace(workspace, createImported())

    expect(workspace.pages).toHaveLength(7)
    // 第二次导入的 startIndex=4，importCurrent=1 → 5。
    expect(workspace.currentIndex).toBe(5)
  })

  it('空导入是空操作（桌面 AppendPages([]) 同样什么都不做）', () => {
    const workspace = new BoardWorkspace()
    const beforeId = workspace.currentPage.id
    applyImportedWorkspace(workspace, { snapshots: [], currentIndex: 2, warnings: [] })

    expect(workspace.pages).toHaveLength(1)
    expect(workspace.currentIndex).toBe(0)
    expect(workspace.currentPage.id).toBe(beforeId)
  })

  it('导入包 currentIndex 越界时夹到最后一页', () => {
    const workspace = new BoardWorkspace()
    applyImportedWorkspace(workspace, {
      snapshots: [emptyPage('imported-a'), emptyPage('imported-b')],
      currentIndex: 99,
      warnings: [],
    })

    // startIndex=1 + clamp(99, 0, 1)=1 → 2（即最后一页）。
    expect(workspace.currentIndex).toBe(2)
    expect(workspace.currentPage.id).toBe('imported-b')
  })
})
