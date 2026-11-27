/**
 * React 上下文对象。单独放在 .ts 文件里，是为了让 provider 文件（.tsx）只导出组件，否则 Fast Refresh 会因为"同一文件既导出组件又导出常量/函数"而失效（oxlint react/only-export-components）。
 */

import { createContext } from 'react'
import type { BoardController } from '../canvas/board-controller.ts'
import type { AppServices } from './services.ts'

/** 外壳动作集合：Dock 上的入口会触发窗口级 UI。 */
export interface ShellActions {
  readonly isFullscreen: boolean
  /** 打开设置窗口（更多菜单 → 设置）。 */
  openSettings(): void
  /** 打开导出对话框（更多菜单 → 导出）。 */
  openExport(): void
  /** 打开导入对话框（左 Dock → 导入）。 */
  openImport(): void
  /** 切换全屏（更多菜单 → 全屏切换）；桌面全屏时隐藏标题栏。 */
  toggleFullscreen(): void
}

/** 应用服务上下文。 */
export const AppServicesContext = createContext<AppServices | null>(null)

/** 画布控制器上下文。 */
export const BoardControllerContext = createContext<BoardController | null>(null)

/** 外壳动作上下文。 */
export const ShellActionsContext = createContext<ShellActions | null>(null)
