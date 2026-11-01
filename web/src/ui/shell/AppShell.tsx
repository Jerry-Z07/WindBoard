/**
 * 应用外壳：把标题栏、画布、提醒栈、Dock 与窗口级对话框摆到桌面的对应位置。
 *
 * 结构对应 `WindBoard/MainWindow.xaml`：
 * ```
 * Row0 TitleBar（全屏隐藏）
 * Row1 Grid {
 *        BoardCanvasControl
 *        右上 ReminderBannerStackPanel（Margin 14,14,14,0）
 *        底部 Grid（Margin 14,0,14,14，RequestedTheme=Light）
 *          左 Dock（3 项） / 中部 Dock（工具 + 撤销重做） / 右 页面 Dock
 *      }
 * ```
 * Dock 的三块浮层**分别**对齐左/中/右，这样主 Dock 才能始终以窗口居中——不能用等距 flex 代替。
 */

import { useMemo, useState, type ReactNode } from 'react'
import { useFullscreen } from '../../app/use-fullscreen.ts'
import type { ShellActions } from '../../app/contexts.ts'
import { ShellActionsProvider } from '../../app/providers.tsx'
import { BoardSurface } from './BoardSurface.tsx'
import { DockBar } from './DockBar.tsx'
import { ReminderStack } from './ReminderStack.tsx'
import { TitleBar } from './TitleBar.tsx'
import { SettingsWindow } from '../dialogs/SettingsWindow.tsx'
import { ExportDialog } from '../dialogs/ExportDialog.tsx'
import { ImportDialog } from '../dialogs/ImportDialog.tsx'

/** 应用外壳。 */
export function AppShell(): ReactNode {
  const { isFullscreen, toggle: toggleFullscreen } = useFullscreen()
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [exportOpen, setExportOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)

  const actions = useMemo<ShellActions>(
    () => ({
      isFullscreen,
      openSettings: () => {
        setSettingsOpen(true)
      },
      openExport: () => {
        setExportOpen(true)
      },
      openImport: () => {
        setImportOpen(true)
      },
      toggleFullscreen,
    }),
    [isFullscreen, toggleFullscreen],
  )

  return (
    <ShellActionsProvider value={actions}>
      <div className="wb-app">
        <TitleBar visible={!isFullscreen} />
        <div className="wb-body">
          <BoardSurface />
          <ReminderStack />
          <div className="wb-dock-layer">
            <DockBar placement="left" />
            <div className="wb-dock-layer__center">
              <DockBar placement="center" />
            </div>
            <DockBar placement="right" />
          </div>
        </div>
        <SettingsWindow
          open={settingsOpen}
          onClose={() => {
            setSettingsOpen(false)
          }}
        />
        <ExportDialog
          open={exportOpen}
          onClose={() => {
            setExportOpen(false)
          }}
        />
        <ImportDialog
          open={importOpen}
          onClose={() => {
            setImportOpen(false)
          }}
        />
      </div>
    </ShellActionsProvider>
  )
}
