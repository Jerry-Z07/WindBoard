/**
 * 设置窗口（P3.5）：用 Fluent Dialog 承载桌面独立 SettingsWindow 的 NavigationView 结构。
 *
 * 桌面出处：`WindBoard/Settings/SettingsWindow.xaml`（NavigationView：左侧常规 / 外观 /
 * 书写 / 快捷键 / Debug / 关于，Debug 项默认折叠）。网页端用一个模态对话框表达：
 * 左侧竖向导航 + 右侧页面内容；页面清单与文案在 `settings/window-model.ts`，
 * 各页字段与桌面 `settings.json` 同构（见 `settings/model.ts` 文件头）。
 *
 * 尺寸取任务书约定的 900 × 640（桌面窗口默认更大，网页端按可用视口收缩）。
 */

import { Button, Dialog, DialogBody, DialogSurface, DialogTitle } from '@fluentui/react-components'
import {
  Dismiss24Regular,
  Grid24Regular,
  Info24Regular,
  Keyboard24Regular,
  PaintBrush24Regular,
  Pen24Regular,
  Settings24Regular,
} from '@fluentui/react-icons'
import { useState, type ReactElement, type ReactNode } from 'react'
import { AboutPage } from '../../settings/pages/AboutPage.tsx'
import { AppearancePage } from '../../settings/pages/AppearancePage.tsx'
import { DockPage } from '../../settings/pages/DockPage.tsx'
import { GeneralPage } from '../../settings/pages/GeneralPage.tsx'
import { ShortcutsPage } from '../../settings/pages/ShortcutsPage.tsx'
import { WritingPage } from '../../settings/pages/WritingPage.tsx'
import {
  DEFAULT_SETTINGS_PAGE_ID,
  SETTINGS_PAGES,
  findSettingsPage,
  type SettingsPageId,
} from '../../settings/window-model.ts'

/** 属性（固定签名，`AppShell` 传入）。 */
export interface SettingsWindowProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 导航图标：对应桌面 NavigationView 各项的 `FontIcon`/`SymbolIcon`。 */
function pageIcon(id: SettingsPageId): ReactElement {
  switch (id) {
    case 'general':
      return <Settings24Regular />
    case 'appearance':
      return <PaintBrush24Regular />
    case 'writing':
      return <Pen24Regular />
    case 'shortcuts':
      return <Keyboard24Regular />
    case 'dock':
      return <Grid24Regular />
    case 'about':
      return <Info24Regular />
  }
}

/** 按 id 渲染页面组件（与 `window-model.ts` 的页面清单一一对应）。 */
function renderPage(id: SettingsPageId): ReactNode {
  switch (id) {
    case 'general':
      return <GeneralPage />
    case 'appearance':
      return <AppearancePage />
    case 'writing':
      return <WritingPage />
    case 'shortcuts':
      return <ShortcutsPage />
    case 'dock':
      return <DockPage />
    case 'about':
      return <AboutPage />
  }
}

/** 设置窗口。 */
export function SettingsWindow(props: SettingsWindowProps): ReactNode {
  const [pageId, setPageId] = useState<SettingsPageId>(DEFAULT_SETTINGS_PAGE_ID)

  if (!props.open) {
    return null
  }

  const page = findSettingsPage(pageId)

  return (
    <Dialog
      open
      modalType="modal"
      onOpenChange={(_, data) => {
        // Esc / 点击遮罩关闭时通知外壳；右上关闭按钮走同一个回调。
        if (!data.open) {
          props.onClose()
        }
      }}
    >
      <DialogSurface className="wb-settings-surface">
        <DialogBody className="wb-settings-body">
          <DialogTitle
            className="wb-settings-title"
            action={
              <Button
                appearance="subtle"
                icon={<Dismiss24Regular />}
                aria-label="关闭设置"
                onClick={props.onClose}
              />
            }
          >
            设置
          </DialogTitle>

          <div className="wb-settings-layout">
            <nav className="wb-settings-nav" aria-label="设置分类">
              {SETTINGS_PAGES.map((item) => (
                <Button
                  key={item.id}
                  appearance="subtle"
                  className="wb-settings-nav__item"
                  icon={pageIcon(item.id)}
                  aria-current={item.id === pageId ? 'page' : undefined}
                  onClick={() => {
                    setPageId(item.id)
                  }}
                >
                  {item.label}
                </Button>
              ))}
            </nav>

            <div className="wb-settings-content">
              <header className="wb-settings-page-header">
                <h3 className="wb-settings-page-header__title">{page.label}</h3>
                <p className="wb-settings-page-header__description">{page.description}</p>
              </header>
              <div className="wb-settings-page-scroll">{renderPage(pageId)}</div>
            </div>
          </div>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
