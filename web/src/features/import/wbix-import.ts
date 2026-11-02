/**
 * WBIX 导入（读取侧）纯逻辑。
 *
 * ## 桌面导入语义（先确认源码再实现，结论如下）
 *
 * 唯一活路径：`WindBoard/UI/MainWindow/MainWindow.Import.cs:18-51` → `ImportFlow.StartAsync`
 * （`WindBoard/Features/Import/ImportFlow.cs:42-60`）：
 * 1. 多选文件（`:52-56`）；
 * 2. 只要选中项里含 `.wbix/.wbi`，就**一律**按 `ImportWbixMode.AppendAfterLastPage` 提交
 *    （`:99-101`；同时选到其它文件时只提示 `ImportDialog_WorkspaceExclusive_Message`，仍按工作区导入）；
 * 3. 落到工作区（`:252-255`）：
 *    ```csharp
 *    int startIndex = _workspace.AppendPages(pages, switchToFirstAppendedPage: false);
 *    int importCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
 *    int targetIndex = Math.Clamp(startIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
 *    _workspace.SetCurrentIndex(targetIndex);
 *    ```
 *
 * **结论：导入 = 追加到最后一页之后，不破坏现有页内容；当前页切到导入包记录的那一页（相对偏移）。**
 *
 * 另一个模式 `ReplaceCurrentPage`（`ImportFlow.cs:228-250`：用导入文件第 1 页替换当前页、
 * 其余页插到其后，且需先弹风险确认 `:402-415`）在桌面侧只由 WBIX 对话框提供，而该对话框已下线：
 * `Import_Wbix_InsertMethod_Label` / `Import_Wbix_Mode_*` / `Import_Risk_ReplaceWorkspace_Content`
 * 在 C# 里**没有任何引用**（只剩 resw）。因此 web 首版只实现 append，不做"覆盖"。
 *
 * ## 追加用 `BoardWorkspace.appendPages`（与桌面同语义）
 *
 * web 的 `BoardWorkspace.appendPages(snapshots, switchToIndex?)` 对应桌面 `AppendPages`
 * （`Features/Import/ImportFlow.cs:252`）：**已有页对象原样保留**，各自的会话与撤销/重做栈不受影响，
 * 只有新追加的页是新建会话（撤销栈为空）。第二参是**绝对**页下标，因此桌面
 * "先 AppendPages 再 SetCurrentIndex"（`:252-255`）在这里合并成一次调用。
 *
 * 视口：桌面导入只追加页、不应用导入包的视口元数据（`ImportFlow.cs:206-256` 全程未触碰 viewport），
 * 本模块同样不处理视口。
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
 * 解析 `.wbix` 字节 → 页面快照。
 *
 * 只做「读取 + 汇总」，不修改任何运行时对象；导入失败按现有 persistence 约定抛
 * `WbixError`（`zip-read-failed` / `missing-manifest` / `page-parse-failed` 等），
 * 由调用方转成用户提示（桌面 `ImportFlow.cs:258-262` 把任何异常都显示
 * `Import_Failed_Title` + `Import_Wbix_ParseFailed_Message`）。
 *
 * @throws WbixError 致命错误（见 `persistence/wbix/errors.ts`）
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
 * 把导入结果应用到现有工作区（桌面 `AppendAfterLastPage` 语义，`ImportFlow.cs:252-255`）。
 *
 * 追加所有导入页，并把当前页切到「第一张导入页 + 导入包记录的相对下标」：
 * ```csharp
 * int startIndex = _workspace.AppendPages(pages, switchToFirstAppendedPage: false);   // :252
 * int importCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
 * int targetIndex = Math.Clamp(startIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
 * _workspace.SetCurrentIndex(targetIndex);                                            // :255
 * ```
 * web 的 `appendPages` 第二参就是绝对下标，所以这里把 `SetCurrentIndex` 合并进同一次调用
 * （`appendPages` 内部对下标做同样的夹取）。
 *
 * 导入包没有任何页时是**空操作**（桌面 `AppendPages([])` 同样什么都不做；`ImportFlow.cs:230-234`
 * 的 `pages=0` 分支只记日志）。
 *
 * @param workspace 现有工作区（原地追加并通知订阅者；现有页对象与其撤销栈保持不变）
 * @param imported `parseWbixForImport` 的结果
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
