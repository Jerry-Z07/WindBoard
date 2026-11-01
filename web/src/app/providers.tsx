/**
 * React provider 组件（本文件**只导出组件**，以保持 Fast Refresh 可用）。
 */

import type { ReactNode } from 'react'
import type { BoardController } from '../canvas/board-controller.ts'
import { AppServicesContext, BoardControllerContext, ShellActionsContext } from './contexts.ts'
import type { ShellActions } from './contexts.ts'
import type { AppServices } from './services.ts'

/** 提供服务给子树。 */
export function AppServicesProvider(props: { services: AppServices; children: ReactNode }): ReactNode {
  return <AppServicesContext.Provider value={props.services}>{props.children}</AppServicesContext.Provider>
}

/** 提供画布控制器给子树（仅在控制器创建之后渲染子树）。 */
export function BoardControllerProvider(props: {
  controller: BoardController
  children: ReactNode
}): ReactNode {
  return (
    <BoardControllerContext.Provider value={props.controller}>
      {props.children}
    </BoardControllerContext.Provider>
  )
}

/** 提供外壳动作。 */
export function ShellActionsProvider(props: { value: ShellActions; children: ReactNode }): ReactNode {
  return <ShellActionsContext.Provider value={props.value}>{props.children}</ShellActionsContext.Provider>
}
