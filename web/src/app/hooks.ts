/**
 * 应用级 React hooks：全部通过 `useSyncExternalStore` 订阅服务快照。
 *
 * 之所以统一用 `useSyncExternalStore`：服务的快照引用只在真正变更时替换，
 * 这样组件不会因为"每次调用都返回新对象"而无限重渲染（`useState` + 手动订阅做不到）。
 */

import { useContext, useMemo, useSyncExternalStore } from 'react'
import type { BoardController, BoardControllerSnapshot } from '../canvas/board-controller.ts'
import type { AppSettings } from '../settings/model.ts'
import type { SettingsService } from '../settings/service.ts'
import type { FlyoutId, FlyoutMachine } from '../ui/flyouts/flyout-machine.ts'
import type { Reminder, ReminderService } from '../ui/reminders/reminder-store.ts'
import { AppServicesContext, BoardControllerContext, ShellActionsContext } from './contexts.ts'
import type { ShellActions } from './contexts.ts'
import type { AppServices } from './services.ts'

/** 取服务集合；未包裹 provider 时直接抛错（这是接线错误，不是运行期状态）。 */
export function useAppServices(): AppServices {
  const services = useContext(AppServicesContext)
  if (services === null) {
    throw new Error('useAppServices 必须在 AppServicesProvider 内使用')
  }
  return services
}

/** 取画布控制器；未包裹 provider 时抛错。 */
export function useBoardController(): BoardController {
  const controller = useContext(BoardControllerContext)
  if (controller === null) {
    throw new Error('useBoardController 必须在 BoardControllerProvider 内使用')
  }
  return controller
}

/** 取外壳动作；未包裹 provider 时抛错。 */
export function useShellActions(): ShellActions {
  const actions = useContext(ShellActionsContext)
  if (actions === null) {
    throw new Error('useShellActions 必须在 ShellActionsProvider 内使用')
  }
  return actions
}

/** 订阅设置：返回当前设置快照（引用只在变更时替换）。 */
export function useSettings(): AppSettings {
  const { settings } = useAppServices()
  return useSyncExternalStore(settings.subscribe, settings.get)
}

/** 订阅设置服务本身（写设置时用）。 */
export function useSettingsService(): SettingsService {
  return useAppServices().settings
}

/** 订阅提醒列表（下标 0 是最新）。 */
export function useReminders(): readonly Reminder[] {
  const { reminders } = useAppServices()
  return useSyncExternalStore(reminders.subscribe, reminders.get)
}

/** 订阅提醒服务本身（推入/关闭时用）。 */
export function useReminderService(): ReminderService {
  return useAppServices().reminders
}

/** 订阅当前打开的面板。 */
export function useOpenFlyout(): FlyoutId | null {
  const { flyouts } = useAppServices()
  return useSyncExternalStore(flyouts.subscribe, flyouts.get)
}

/** 订阅面板状态机本身（打开/关闭时用）。 */
export function useFlyoutMachine(): FlyoutMachine {
  return useAppServices().flyouts
}

/** 订阅画布控制器快照。 */
export function useBoardSnapshot(): BoardControllerSnapshot {
  const controller = useBoardController()
  return useSyncExternalStore(controller.subscribe, controller.getSnapshot)
}

/** 页面指示文本（桌面 `"1 / 3"`；单页时也返回 `"1 / 1"`，是否显示由调用方决定）。 */
export function usePageIndicatorText(): string {
  const snapshot = useBoardSnapshot()
  const total = Math.max(snapshot.pageCount, 1)
  const current = Math.min(Math.max(snapshot.currentPageIndex + 1, 1), total)
  return `${current} / ${total}`
}

/**
 * 是否显示"翻页三件套"（上一页/指示器/下一页）。
 *
 * 单页时只隐藏这三项，**「+」始终可见**（`UI/MainWindow/MainWindow.Pages.cs:81-101`：
 * `showPager = total > 1` 只折叠 `PagePrevButton`/`PageNextButton`/`PageIndicatorButton`）。
 * 若把整个右侧 Dock 一起隐藏，用户就没有任何入口新增第二页。
 */
export function useShowPagePager(): boolean {
  const snapshot = useBoardSnapshot()
  return snapshot.pageCount > 1
}

/** 当前色板/粗细预设视图（来自设置）。 */
export function usePenPalette(): {
  paletteHexes: readonly (string | null)[]
  thicknessPresets: readonly [number, number, number]
  useThicknessSlider: boolean
} {
  const settings = useSettings()
  return useMemo(
    () => ({
      paletteHexes: settings.writing.pen.paletteHexes,
      thicknessPresets: settings.writing.pen.thicknessPresets,
      useThicknessSlider: settings.writing.pen.useThicknessSlider,
    }),
    [settings.writing.pen],
  )
}
