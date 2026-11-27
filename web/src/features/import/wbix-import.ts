/**
 * WBIX 导入（读取侧）纯逻辑。桌面唯一活路径是 MainWindow.Import.cs 调 ImportFlow.StartAsync（ImportFlow.cs:42-60）：选中项含 .wbix/.wbi 时一律按 AppendAfterLastPage 导入，追加到最后一页之后再把当前页切到导入包记录的那一页（相对偏移），不破坏现有页；ReplaceCurrentPage 只由已下线的 WBIX 对话框提供，其 resw key 在 C# 里已无引用，web 只实现 append。
 * 追加走 appendPages，对应桌面 AppendPages（ImportFlow.cs:252）：已有页对象与其撤销栈原样保留，只有新追加的页是空会话；第二参是绝对下标，故把桌面的 AppendPages 加 SetCurrentIndex 合并成一次调用。导入包的视口元数据不应用（桌面 ImportFlow.cs:206-256 全程未触碰 viewport）。
 */
import type { PageSnapshot } from '../../board/model/workspace.ts'
import type { BoardWorkspace } from '../../board/workspace.ts'
import { readWbix } from '../../persistence/wbix/read.ts'

/** 解析后的导入结果。 */
export interface ImportedWorkspace {
  /** 导入包里的页面快照（数组顺序即页序）。 */
  readonly snapshots: readonly PageSnapshot[]
  /** 导入包记录的当前页下标（`manifest.currentIndex`，已由读侧夹到合法区间）。 */
  readonly currentIndex: number
  /** 容忍告警的中文说明（来自 persistence 层；被跳过的内容不影响其余内容导入）。 */
  readonly warnings: readonly string[]
}

/**
 * 解析 .wbix 字节为页面快照，只读取与汇总，不修改任何运行时对象；失败按 persistence 约定抛 WbixError，由调用方转成用户提示（桌面 ImportFlow.cs:258-262 把任何异常都显示为 Import_Failed_Title 加 Import_Wbix_ParseFailed_Message）。
 * @throws WbixError 致命错误（见 persistence/wbix/errors.ts）
 */
export function parseWbixForImport(bytes: Uint8Array): ImportedWorkspace {
  const result = readWbix(bytes)

  return {
    snapshots: result.workspace.pages,
    currentIndex: result.workspace.currentIndex,
    // 桌面侧告警只进日志（`AppLog.Warn`），这里保留中文说明供 UI 提示；`code` 留给直接调用 `readWbix` 的诊断场景。
    warnings: result.warnings.map((warning) => warning.detail),
  }
}

/**
 * 把导入结果应用到现有工作区（桌面 AppendAfterLastPage 语义，ImportFlow.cs:252-255）：追加所有导入页，并把当前页切到第一张导入页加导入包记录的相对下标；web 的 appendPages 第二参就是绝对下标且内部同样夹取，故 SetCurrentIndex 合并进同一次调用。
 * 导入包没有任何页时是空操作（桌面 AppendPages([]) 同样什么都不做，ImportFlow.cs:230-234 的 pages=0 分支只记日志）。
 * @param workspace 现有工作区（原地追加并通知订阅者；现有页对象与其撤销栈保持不变）
 * @param imported parseWbixForImport 的结果
 */
export function applyImportedWorkspace(
  workspace: BoardWorkspace,
  imported: ImportedWorkspace,
): void {
  if (imported.snapshots.length === 0) {
    return
  }

  const importCurrent = clampIndex(imported.currentIndex, imported.snapshots.length)
  // 第一张新页的绝对下标 = 追加前的页数（与 `appendPages` 的返回值一致）。
  const targetIndex = workspace.pages.length + importCurrent
  workspace.appendPages(imported.snapshots, targetIndex)
}

/** 把下标夹到 `[0, count - 1]`（`count <= 0` 时返回 0，与桌面 `Math.Clamp(..., 0, Math.Max(0, n - 1))` 等价）。 */
function clampIndex(index: number, count: number): number {
  const upper = Math.max(0, count - 1)
  return Math.min(Math.max(Math.trunc(index) || 0, 0), upper)
}
