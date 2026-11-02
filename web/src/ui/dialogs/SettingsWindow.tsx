/**
 * 设置窗口（P3.5）：用 Fluent Dialog 承载桌面独立 SettingsWindow 的 NavigationView 结构。
 *
 * 桌面出处：`WindBoard/Settings/SettingsWindow.xaml`（NavigationView：左侧常规 / 外观 /
 * 书写 / 快捷键 / Debug / 关于，Debug 项默认折叠）。网页端用一个模态对话框表达：
 * 左侧竖向导航 + 右侧页面内容；页面清单与文案在 `settings/window-model.ts`，
 * 各页字段与桌面 `settings.json` 同构（见 `settings/model.ts` 文件头）。
 *
 * 标题行复刻桌面 `SettingsWindow.xaml:23-44` 的 TitleBar：左边标题，右边
 * `AutoSuggestBox`（本文件用 `SettingsSearchBox`）+ 关闭按钮。搜索选中后
 * 切到目标设置页、把对应卡片滚到视野中央并短暂高亮（桌面是
 * `NavigateToSearchTarget` `:429-443` + `TryBringPendingElementIntoView` 的"跳页并聚焦"）。
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
import { useEffect, useRef, useState, type ReactElement, type ReactNode } from 'react'
import { useI18n } from '../../i18n/context.ts'
import { AboutPage } from '../../settings/pages/AboutPage.tsx'
import { AppearancePage } from '../../settings/pages/AppearancePage.tsx'
import { DockPage } from '../../settings/pages/DockPage.tsx'
import { GeneralPage } from '../../settings/pages/GeneralPage.tsx'
import { ShortcutsPage } from '../../settings/pages/ShortcutsPage.tsx'
import { WritingPage } from '../../settings/pages/WritingPage.tsx'
import type { SettingsSearchTarget } from '../../settings/search-targets.ts'
import {
  DEFAULT_SETTINGS_PAGE_ID,
  SETTINGS_PAGES,
  findSettingsPage,
  type SettingsPageId,
} from '../../settings/window-model.ts'
import { SettingsSearchBox } from '../settings/SettingsSearchBox.tsx'

/** 属性（固定签名，`AppShell` 传入）。 */
export interface SettingsWindowProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 搜索定位后的高亮时长（1.2s 后移除 class，由 `settings-search.css` 的动画淡出）。 */
const SEARCH_HIGHLIGHT_DURATION_MS = 1200

/** 高亮 class（定义在 `styles/settings-search.css`）。 */
const SEARCH_HIGHLIGHT_CLASS = 'wb-settings-card--highlight'

/** 一次待定位的搜索目标；`seq` 保证连续选同一条时也能重新触发定位。 */
interface PendingSearchTarget {
  readonly target: SettingsSearchTarget
  readonly seq: number
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

/**
 * 按 id 渲染页面组件（与 `window-model.ts` 的页面清单一一对应）。
 *
 * `onNavigate` 供页面内部跳转到另一页（桌面 Outlook 风格设置页里的"跳页卡片"，
 * 例如外观页的 `DockSettingsCard` → `Frame.Navigate(typeof(DockSettingsPage))`）。
 */
function renderPage(id: SettingsPageId, onNavigate: (pageId: SettingsPageId) => void): ReactNode {
  switch (id) {
    case 'general':
      return <GeneralPage />
    case 'appearance':
      return <AppearancePage onNavigate={onNavigate} />
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
  const { t } = useI18n()
  const [pageId, setPageId] = useState<SettingsPageId>(DEFAULT_SETTINGS_PAGE_ID)
  const [pending, setPending] = useState<PendingSearchTarget | null>(null)
  const pendingSeq = useRef(0)

  // 搜索选中后：等新页面渲染完成，再滚动到卡片并加高亮 class。
  // 桌面用 `StartBringIntoView` + 焦点；网页端卡片不可聚焦，改用"滚动 + 短暂高亮"表达同一件事。
  useEffect(() => {
    if (pending === null) {
      return
    }
    const element = document.getElementById(pending.target.anchor)
    if (element === null) {
      return
    }

    const prefersReducedMotion =
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    element.scrollIntoView({
      block: 'center',
      behavior: prefersReducedMotion ? 'auto' : 'smooth',
    })
    element.classList.add(SEARCH_HIGHLIGHT_CLASS)

    const timer = window.setTimeout(() => {
      element.classList.remove(SEARCH_HIGHLIGHT_CLASS)
    }, SEARCH_HIGHLIGHT_DURATION_MS)

    return () => {
      window.clearTimeout(timer)
      element.classList.remove(SEARCH_HIGHLIGHT_CLASS)
    }
  }, [pending])

  if (!props.open) {
    return null
  }

  const page = findSettingsPage(pageId)

  /** 搜索选中：切页 + 登记待定位目标（同页时也要重新定位，所以用自增序号）。 */
  const handleSearchSelect = (target: SettingsSearchTarget): void => {
    setPageId(target.pageId)
    pendingSeq.current += 1
    setPending({ target, seq: pendingSeq.current })
  }

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
          {/*
            `DialogTitle` 仍要渲染（Fluent 用它生成 `aria-labelledby` 的标题 id），
            但关闭按钮不再走它的 `action` 槽位：搜索框要占标题行右侧、关闭按钮左边，
            所以整行用 flex 容器自己排。
          */}
          <div className="wb-settings-titlebar">
            <DialogTitle className="wb-settings-title">{t('Common_Settings')}</DialogTitle>
            <SettingsSearchBox onSelect={handleSearchSelect} />
            <Button
              appearance="subtle"
              icon={<Dismiss24Regular />}
              aria-label="关闭设置"
              onClick={props.onClose}
            />
          </div>

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
              <div className="wb-settings-page-scroll">{renderPage(pageId, setPageId)}</div>
            </div>
          </div>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
