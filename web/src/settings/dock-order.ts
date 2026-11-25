/**
 * Dock 顺序计算（纯函数）。桌面出处：DockSettingsPage.xaml:47-229 的四组 ListView 都启用 CanDragItems 与 CanReorderItems，拖动结束触发 DragItemsCompleted；DockSettingsPage.xaml.cs:395-412（OnDockItemsDragCompleted → PersistOrdersToSettings）把四组当前顺序整体写回 Dock.LeftOrder / Dock.ToolsOrder / Dock.UndoRedoOrder / Dock.PagesOrder；读设置时的兜底规则见 settings/model.ts 的 normalizeDockOrder（剔除未知 id、去重、按默认顺序补齐缺失项），本模块 normalizeOrder 与它同语义，供落盘前自检或外部数据进来时先规整。
 * 只做数组怎么变的纯计算，不依赖 React / DOM，便于表驱动单测；所有函数都不修改入参并始终返回新数组，需要判断是否真的变了时用内容比较（isSameOrder），不要用引用比较。
 */

/** 下标是否是 `list` 的合法位置（整数且落在 [0, length) 内）。 */
function isIndexInRange(list: readonly unknown[], index: number): boolean {
  return Number.isInteger(index) && index >= 0 && index < list.length
}

/**
 * 把 fromIndex 处的元素移动到 toIndex。语义对齐桌面 ListView 拖动落点：移动后 toIndex 就是该元素的新位置，其余元素依次让位；越界、非整数下标或原地移动都视为无操作，返回等值副本。
 */
export function moveItem<T>(list: readonly T[], fromIndex: number, toIndex: number): readonly T[] {
  const next = list.slice()
  if (!isIndexInRange(list, fromIndex) || !isIndexInRange(list, toIndex) || fromIndex === toIndex) {
    return next
  }
  const moved = next.splice(fromIndex, 1)[0]
  next.splice(toIndex, 0, moved)
  return next
}

/** 上移一位；已在首位时无操作。 */
export function moveItemUp<T>(list: readonly T[], index: number): readonly T[] {
  return moveItem(list, index, index - 1)
}

/** 下移一位；已在末位时无操作。 */
export function moveItemDown<T>(list: readonly T[], index: number): readonly T[] {
  return moveItem(list, index, index + 1)
}

/**
 * 规整顺序：保留 order 中合法且不重复的 id（重复时保留第一次出现的位置），然后按 allIds 的顺序补齐缺失项。语义与 model.ts 的 normalizeDockOrder 一致：未知 id 丢弃、重复 id 去重、缺失 id 补齐。
 */
export function normalizeOrder<T extends string>(
  order: readonly T[],
  allIds: readonly T[],
): readonly T[] {
  const allowed = new Set<T>(allIds)
  const seen = new Set<T>()
  const result: T[] = []
  for (const id of order) {
    if (!allowed.has(id) || seen.has(id)) {
      continue
    }
    seen.add(id)
    result.push(id)
  }
  for (const id of allIds) {
    if (seen.has(id)) {
      continue
    }
    seen.add(id)
    result.push(id)
  }
  return result
}

/** 两个顺序数组内容是否完全一致（用于跳过无变化的设置写入）。 */
export function isSameOrder(a: readonly string[], b: readonly string[]): boolean {
  if (a.length !== b.length) {
    return false
  }
  return a.every((id, index) => id === b[index])
}
