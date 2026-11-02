/**
 * 应用级服务的创建与释放。
 *
 * 这一层刻意**不含 React**：服务本身是纯 TS 单例，可脱离组件树直接测试；
 * 与 React 的桥接（context / provider / hooks）在 `contexts.ts`、`providers.tsx`、`hooks.ts`。
 */

import type { AppSettings } from '../settings/model.ts'
import { createSettingsService, type SettingsService } from '../settings/service.ts'
import { createFlyoutMachine, type FlyoutMachine } from '../ui/flyouts/flyout-machine.ts'
import { createReminderService, type ReminderService } from '../ui/reminders/reminder-store.ts'

/** 与 React 无关的应用服务集合。 */
export interface AppServices {
  readonly settings: SettingsService
  readonly reminders: ReminderService
  readonly flyouts: FlyoutMachine
}

/** 创建一套服务（每次挂载应用创建一个；测试可自行构造）。 */
export function createAppServices(): AppServices {
  return {
    settings: createSettingsService(),
    reminders: createReminderService(),
    flyouts: createFlyoutMachine(),
  }
}

/** 释放服务（取消订阅与计时器）。 */
export function disposeAppServices(services: AppServices): void {
  services.settings.dispose()
  services.reminders.dispose()
}

/** 便捷：取当前画布背景色（设置缺字段时由模型保证有默认值）。 */
export function canvasBackgroundOf(settings: AppSettings): string {
  return settings.appearance.canvasBackgroundHex
}
