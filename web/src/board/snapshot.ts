/**
 * 运行时工作区与持久化快照的转换。BoardWorkspace 与 BoardDocument 是可变的运行时对象（命令直接在上面增删条目），WorkspaceSnapshot 与 PageSnapshot 是持久化形状（persistence/wbix 只认这一层）；这一层是两个世界的唯一桥梁，对应桌面的 BoardWorkspaceSnapshotConverter（运行到快照）与 BoardWorkspaceSnapshotApplier（快照到运行）。
 * 快照与运行时共享条目对象（浅拷贝数组），因此转换不复制笔迹点，成本只与页数相关。
 */

import type { WorkspaceSnapshot } from './model/workspace.ts'
import type { BoardViewport } from './viewport.ts'
import { BoardWorkspace } from './workspace.ts'

/**
 * 运行时工作区 → 持久化快照。
 * @param workspace 工作区
 * @param viewport 视口；缺省或传 null 时三个视口字段写 null（桌面同样允许缺省）
 */
export function workspaceToSnapshot(
  workspace: BoardWorkspace,
  viewport?: BoardViewport | null,
): WorkspaceSnapshot {
  const pages = workspace.pages.map((page) => page.session.document.toSnapshot(page.id))

  return {
    pages,
    currentIndex: workspace.currentIndex,
    viewportCameraWorld:
      viewport === undefined || viewport === null
        ? null
        : { x: viewport.cameraWorld.x, y: viewport.cameraWorld.y },
    viewportZoom: viewport === undefined || viewport === null ? null : viewport.zoom,
    viewportSizeDip:
      viewport === undefined || viewport === null
        ? null
        : { x: viewport.sizeDip.x, y: viewport.sizeDip.y },
  }
}

/**
 * 持久化快照 → 新工作区（导入 .wbix 用）。每页重建独立会话（撤销与重做栈为空），currentIndex 由 replaceWithSnapshots 夹到合法区间。
 * @throws RangeError snapshot.pages 为空
 */
export function createWorkspaceFromSnapshot(snapshot: WorkspaceSnapshot): BoardWorkspace {
  const workspace = new BoardWorkspace()
  workspace.replaceWithSnapshots(snapshot.pages, snapshot.currentIndex)
  return workspace
}

/**
 * 把快照里的视口元数据应用到视口。语义同桌面 WbixWorkspaceSerializer.Load 的读侧处理：三个字段都可缺省，缺省时保持视口原状；viewportSizeDip 不参与，视口尺寸永远由画布实际尺寸决定（桌面也只在保存时记录，加载时用于恢复窗口尺寸，web 侧由 ResizeObserver 负责）。
 */
export function applySnapshotViewport(viewport: BoardViewport, snapshot: WorkspaceSnapshot): void {
  const camera = snapshot.viewportCameraWorld
  const zoom = snapshot.viewportZoom
  if (camera === null || zoom === null || zoom <= 0) {
    return
  }

  viewport.setView(camera, zoom)
}
