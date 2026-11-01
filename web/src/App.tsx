/**
 * 应用根组件：只负责创建服务并交给外壳，不做布局（布局在 `ui/shell/AppShell.tsx`）。
 *
 * 主题策略（与桌面一致）：
 * - 外壳与设置窗口跟随系统深浅色（`FluentProvider` 的 App 级主题）；
 * - **底部 Dock 强制浅色**（桌面 `MainWindow.xaml:54` 的 `RequestedTheme="Light"`），
 *   由 `DockBar` 内部的嵌套 `FluentProvider` 单独包一层；
 * - 画布背景由设置决定（默认 #2E2F33），不受主题影响。
 */

import { useEffect, useMemo, type ReactNode } from 'react'
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components'
import { AppServicesProvider } from './app/providers.tsx'
import { createAppServices, disposeAppServices } from './app/services.ts'
import { AppShell } from './ui/shell/AppShell.tsx'

/** 跟随系统主题选 App 级 Fluent 主题。 */
function useSystemTheme(): typeof webLightTheme {
  const isDark = useMemo(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return false
    }
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  }, [])
  useEffect(() => {
    if (typeof document === 'undefined') {
      return
    }
    document.documentElement.dataset.theme = isDark ? 'dark' : 'light'
  }, [isDark])
  return isDark ? webDarkTheme : webLightTheme
}

/** 应用根组件。 */
function App(): ReactNode {
  const services = useMemo(() => createAppServices(), [])
  const theme = useSystemTheme()

  useEffect(() => () => disposeAppServices(services), [services])

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <AppServicesProvider services={services}>
        <AppShell />
      </AppServicesProvider>
    </FluentProvider>
  )
}

export default App
