/**
 * 应用根组件：只负责创建服务并交给外壳，不做布局（布局在 ui/shell/AppShell.tsx）。主题策略与桌面一致：外壳与设置窗口跟随系统深浅色（FluentProvider 的 App 级主题）；底部 Dock 强制浅色（桌面 MainWindow.xaml:54 的 RequestedTheme="Light"），由 DockBar 内部的嵌套 FluentProvider 单独包一层；画布背景由设置决定（默认 #2E2F33），不受主题影响。
 */

import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { FluentProvider, type Theme } from '@fluentui/react-components'
import { winuiDarkTheme, winuiLightTheme } from './app/theme.ts'
import { AppServicesProvider } from './app/providers.tsx'
import { createAppServices, disposeAppServices } from './app/services.ts'
import { I18nProvider } from './i18n/I18nProvider.tsx'
import { AppShell } from './ui/shell/AppShell.tsx'

/** 深色偏好查询串（`tokens.css` 的深色变量块走 `data-theme`，与本判定同源）。 */
const DARK_SCHEME_QUERY = '(prefers-color-scheme: dark)'

/** 当前系统是否偏好深色（没有 `matchMedia` 的环境按浅色处理）。 */
function prefersDarkScheme(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return false
  }
  return window.matchMedia(DARK_SCHEME_QUERY).matches
}

/**
 * 跟随系统主题选 App 级 Fluent 主题。必须订阅 matchMedia 的 change 事件：桌面 WinUI 在系统切深浅色时会实时跟随，网页端若只在挂载时读一次，用户改了系统主题后界面会一直停在旧主题。
 */
function useSystemTheme(): Theme {
  const [isDark, setIsDark] = useState(prefersDarkScheme)

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return
    }
    const query = window.matchMedia(DARK_SCHEME_QUERY)
    const onChange = (event: MediaQueryListEvent): void => {
      setIsDark(event.matches)
    }
    // 初始值已由 `useState(prefersDarkScheme)` 读好，这里只订阅后续变化
    query.addEventListener('change', onChange)
    return () => {
      query.removeEventListener('change', onChange)
    }
  }, [])

  useEffect(() => {
    if (typeof document === 'undefined') {
      return
    }
    document.documentElement.dataset.theme = isDark ? 'dark' : 'light'
  }, [isDark])

  return isDark ? winuiDarkTheme : winuiLightTheme
}

/** 应用根组件。 */
function App(): ReactNode {
  const services = useMemo(() => createAppServices(), [])
  const theme = useSystemTheme()

  useEffect(() => () => disposeAppServices(services), [services])

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <AppServicesProvider services={services}>
        <I18nProvider>
          <AppShell />
        </I18nProvider>
      </AppServicesProvider>
    </FluentProvider>
  )
}

export default App
