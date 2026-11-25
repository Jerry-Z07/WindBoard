/**
 * 底部 Dock：三块浮层（左入口组、中部工具组、右侧页面组）。桌面出处：三块浮层对齐左中右并强制 RequestedTheme="Light"（MainWindow.xaml:54-70）、左 Dock（:82-160）、主 Dock 含工具与分隔线与撤销重做（:441-558）、页面 Dock（:685-737），按钮样式 DockButtonStyle 见 App.xaml:20-28。
 * 两个还原点：整块 Dock 包一层 FluentProvider theme={webLightTheme}，不受 App 级深色主题影响（桌面靠 RequestedTheme="Light"）；开合由 planToolButtonClick（ui/flyouts/flyout-machine.ts）算好后驱动受控 Popover，不用 PopoverTrigger 的默认开合。
 */

import { useState, type ReactElement, type ReactNode } from 'react'
import {
  FluentProvider,
  Menu,
  MenuDivider,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  webLightTheme,
} from '@fluentui/react-components'
import {
  AddRegular,
  ArrowExportRegular,
  ArrowImportRegular,
  ArrowRedoRegular,
  ArrowUndoRegular,
  ChevronLeftRegular,
  ChevronRightRegular,
  CursorRegular,
  EditRegular,
  EraserRegular,
  FullScreenMaximizeRegular,
  MoreHorizontalRegular,
  SettingsRegular,
} from '@fluentui/react-icons'
import {
  useBoardController,
  useBoardSnapshot,
  useFlyoutMachine,
  useOpenFlyout,
  usePageIndicatorText,
  useSettings,
  useShellActions,
  useShowPagePager,
} from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'
import type { ToolKind } from '../../interaction/pointer-router.ts'
import { WEB_LEFT_DOCK_ITEMS, type DockItemId } from '../../settings/model.ts'
import { EraserFlyout } from '../flyouts/EraserFlyout.tsx'
import { PagesFlyout } from '../flyouts/PagesFlyout.tsx'
import { PenFlyout } from '../flyouts/PenFlyout.tsx'
import { usePageDeletion } from '../flyouts/use-page-deletion.tsx'
import { ShapeFlyout } from '../flyouts/ShapeFlyout.tsx'
import { planToolButtonClick } from '../flyouts/flyout-machine.ts'
import { DockButton } from './DockButton.tsx'
import {
  applyDockOrder,
  buildMoreMenuEntries,
  type DockPlacement,
} from './dock-layout.ts'

/** 属性。 */
export interface DockBarProps {
  /** Dock 的位置：左侧入口组 / 中部工具组 / 右侧页面组。 */
  readonly placement: DockPlacement
}

/** 左 Dock 的项与默认顺序（桌面 `Features/Dock/Models/DockItemIds.cs:34-38` 去掉「最小化」，浏览器没有窗口最小化能力）。 */
const LEFT_DOCK_ITEMS: readonly DockItemId[] = WEB_LEFT_DOCK_ITEMS

/** 主 Dock 的工具项与默认顺序（同上）。 */
const TOOL_DOCK_ITEMS: readonly DockItemId[] = ['toolSelect', 'toolPen', 'toolEraser', 'toolShape']

/** 页面 Dock 的项与默认顺序（同上）。 */
const PAGE_DOCK_ITEMS: readonly DockItemId[] = ['pagePrev', 'pageIndicator', 'pageNext', 'pageAdd']

/** 工具项 → 工具语义。 */
const DOCK_TOOL_KIND: Readonly<Partial<Record<DockItemId, ToolKind>>> = {
  toolSelect: 'select',
  toolPen: 'pen',
  toolEraser: 'eraser',
  toolShape: 'shape',
}

/**
 * 工具项文案 key（Strings/zh-CN/Tool.resw：选择、书写、擦除、形状）。这里存 key 而不存中文，调用点渲染时用 t(...) 取词，语言切换（设置 → 常规 → 语言）后无需重新构造映射表。
 */
const TOOL_LABEL_KEYS: Readonly<Record<ToolKind, MessageKey>> = {
  select: 'Tool_Select',
  pen: 'Tool_Pen',
  eraser: 'Tool_Eraser',
  shape: 'Tool_Shape',
}

/**
 * Dock 主题容器的内联样式：抵消 `FluentProvider` 默认的浅色背景与块级占位，
 * 让 Dock 只保留自己的 0.85 半透明底板（`MainWindow.xaml:76-80`）。
 */
const THEME_STYLE = { background: 'transparent', color: 'inherit', display: 'block' } as const

/**
 * 弹出层容器的内联样式：桌面用 CompactFlyoutPresenterStyle 把 Fluent 自带的边框、阴影、内边距全部清掉，只留内层面板自己的圆角 14 与 1px 描边（WindBoard/App.xaml:41-53 + MainWindow.xaml:230-234）。
 */
const SURFACE_STYLE = {
  padding: 0,
  background: 'transparent',
  border: 'none',
  borderRadius: 0,
  boxShadow: 'none',
  minWidth: 0,
} as const

/** 弹出层里的主题容器：同样抵消默认背景。 */
const SURFACE_THEME_STYLE = { background: 'transparent' } as const

/** Dock（按 placement 分派到三块浮层；窄屏走合并后的紧凑栏）。 */
export function DockBar(props: DockBarProps): ReactNode {
  const content =
    props.placement === 'left' ? (
      <LeftDock />
    ) : props.placement === 'center' ? (
      <CenterDock />
    ) : props.placement === 'narrow' ? (
      <NarrowDock />
    ) : (
      <RightDock />
    )

  return (
    <FluentProvider theme={webLightTheme} className="wb-dock-theme" style={THEME_STYLE}>
      {content}
    </FluentProvider>
  )
}

// 左 Dock（`MainWindow.xaml:82-160`）

/** 左 Dock：更多（菜单）/ 导入。 */
function LeftDock(): ReactNode {
  const { t } = useI18n()
  const actions = useShellActions()
  const settings = useSettings()
  const ordered = applyDockOrder(LEFT_DOCK_ITEMS, (id) => id, settings.dock.leftOrder)

  const renderItem = (id: DockItemId): ReactNode => {
    switch (id) {
      case 'more':
        return <MoreButton key={id} />
      case 'import':
        return (
          <DockButton
            key={id}
            icon={<ArrowImportRegular fontSize={20} />}
            label={t('Common_Import')}
            onClick={actions.openImport}
          />
        )
      default:
        return null
    }
  }

  return <div className="wb-dock wb-dock--left">{ordered.map(renderItem)}</div>
}

/** 更多菜单（`MainWindow.xaml:92-124`：设置 / 分隔 / 导出 / 全屏切换）。 */
function MoreButton(): ReactNode {
  const { t } = useI18n()
  const actions = useShellActions()
  const entries = buildMoreMenuEntries(actions.isFullscreen)

  const iconOf = (id: 'settings' | 'export' | 'fullscreen'): ReactElement => {
    switch (id) {
      case 'settings':
        return <SettingsRegular />
      case 'export':
        return <ArrowExportRegular />
      default:
        return <FullScreenMaximizeRegular />
    }
  }

  const runOf = (id: 'settings' | 'export' | 'fullscreen'): (() => void) => {
    switch (id) {
      case 'settings':
        return actions.openSettings
      case 'export':
        return actions.openExport
      default:
        return actions.toggleFullscreen
    }
  }

  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <DockButton icon={<MoreHorizontalRegular fontSize={20} />} label={t('Common_More')} />
      </MenuTrigger>
      <MenuPopover>
        {/* 菜单挂在 body 上，会脱离 Dock 的浅色主题，这里补一层，避免深色系统下变成深色菜单 */}
        <FluentProvider theme={webLightTheme} style={SURFACE_THEME_STYLE}>
          <MenuList>
            {entries.map((entry) =>
              entry.kind === 'divider' ? (
                <MenuDivider key="divider" />
              ) : (
                <MenuItem key={entry.id} icon={iconOf(entry.id)} onClick={runOf(entry.id)}>
                  {t(entry.labelKey)}
                </MenuItem>
              ),
            )}
          </MenuList>
        </FluentProvider>
      </MenuPopover>
    </Menu>
  )
}

// 主 Dock（`MainWindow.xaml:441-558`）

/**
 * 工具组：选择、笔、橡皮、形状。独立成组件是因为窄屏紧凑栏（NarrowDock）要复用同一组按钮与弹出面板，复制一份会漏掉「二次点击才弹面板」这类行为。
 */
function ToolButtons(): ReactNode {
  const { t } = useI18n()
  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const settings = useSettings()
  const flyouts = useFlyoutMachine()
  const openFlyout = useOpenFlyout()
  const ordered = applyDockOrder(TOOL_DOCK_ITEMS, (id) => id, settings.dock.toolsOrder)

  /** 工具按钮点击：先按桌面规则决定面板状态，再切工具（`MainWindow.xaml.cs:252-268` 等）。 */
  const clickTool = (tool: ToolKind): void => {
    const plan = planToolButtonClick(snapshot.tool, tool, openFlyout)
    if (plan.selectTool) {
      controller.setTool(tool)
    }
    flyouts.set(plan.flyout)
  }

  /** 关闭面板（点击外部 / Esc 时由 Popover 回调）。 */
  const closeFlyout = (): void => {
    flyouts.close()
  }

  /**
   * 受控 Popover 的开合回调：只处理「关闭」。打开由 clickTool 里的状态机决定，因此这里忽略 open: true，这样 PopoverTrigger 自带的点击切换不会与桌面的「二次点击才弹」打架。
   */
  const onOpenChange = (_event: unknown, data: { open: boolean }): void => {
    if (!data.open) {
      closeFlyout()
    }
  }

  const toolIcon = (tool: ToolKind): ReactNode => {
    switch (tool) {
      case 'select':
        // 桌面用 Segoe Fluent Icons 的 `TouchPointer`（`MainWindow.xaml` 选择工具按钮）；
        // Fluent UI 图标库没有同名图标，取语义最接近的鼠标指针 `CursorRegular` 并在此说明。
        return <CursorRegular fontSize={20} />
      case 'pen':
        return <EditRegular fontSize={20} />
      case 'eraser':
        return <EraserRegular fontSize={20} />
      default:
        return <ShapeToolIcon />
    }
  }

  const renderTool = (id: DockItemId): ReactNode => {
    const tool = DOCK_TOOL_KIND[id]
    if (tool === undefined) {
      return null
    }

    const button = (
      <DockButton
        icon={toolIcon(tool)}
        label={t(TOOL_LABEL_KEYS[tool])}
        pressed={snapshot.tool === tool}
        title={t(TOOL_LABEL_KEYS[tool])}
        onClick={() => {
          clickTool(tool)
        }}
      />
    )

    // 选择工具没有弹出面板（`TOOL_FLYOUT` 里没有 select），直接作为一个 Dock 槽位
    if (tool === 'select') {
      return (
        <span key={id} className="wb-dock__slot">
          {button}
        </span>
      )
    }

    return (
      <Popover
        key={id}
        open={openFlyout === tool}
        onOpenChange={onOpenChange}
        positioning={{ position: 'above', align: 'center' }}
      >
        <PopoverTrigger disableButtonEnhancement>{button}</PopoverTrigger>
        <PopoverSurface style={SURFACE_STYLE}>
          <FluentProvider theme={webLightTheme} style={SURFACE_THEME_STYLE}>
            {tool === 'pen' ? (
              <PenFlyout onClose={closeFlyout} />
            ) : tool === 'eraser' ? (
              <EraserFlyout onClose={closeFlyout} />
            ) : (
              <ShapeFlyout onClose={closeFlyout} />
            )}
          </FluentProvider>
        </PopoverSurface>
      </Popover>
    )
  }

  return <>{ordered.map(renderTool)}</>
}

/**
 * 撤销与重做组。桌面把这一组和工具组放在同一块浮层里、用一条 1×30 的分隔线隔开（MainWindow.xaml:522-527）；是否显示由设置项 dock.isUndoRedoVisible 决定，隐藏时连分隔线一起去掉，所以分隔线由调用方按同一开关渲染。
 */
function UndoRedoButtons(): ReactNode {
  const { t } = useI18n()
  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const settings = useSettings()
  const showUndoRedo = settings.dock.isUndoRedoVisible
  const undoRedoOrder = settings.dock.undoRedoOrder

  const renderUndoRedo = (id: DockItemId): ReactNode => {
    switch (id) {
      case 'undo':
        return (
          <DockButton
            key={id}
            icon={<ArrowUndoRegular fontSize={20} />}
            label={t('Common_Undo')}
            // 窄屏隐藏文字后，无障碍名会回落到 title；桌面也顺手有了悬停提示
            title={t('Common_Undo')}
            disabled={!snapshot.canUndo}
            onClick={() => {
              controller.undo()
            }}
          />
        )
      case 'redo':
        return (
          <DockButton
            key={id}
            icon={<ArrowRedoRegular fontSize={20} />}
            label={t('Common_Redo')}
            title={t('Common_Redo')}
            disabled={!snapshot.canRedo}
            onClick={() => {
              controller.redo()
            }}
          />
        )
      default:
        return null
    }
  }

  return !showUndoRedo ? null : (
    <div className="wb-dock__undo-redo">
      {applyDockOrder(['undo', 'redo'] as const, (id) => id, undoRedoOrder).map(renderUndoRedo)}
    </div>
  )
}

/** 主 Dock：工具组 + 分隔线 + 撤销 / 重做（`MainWindow.xaml:441-558`）。 */
function CenterDock(): ReactNode {
  const settings = useSettings()

  return (
    <div className="wb-dock wb-dock--center">
      <ToolButtons />
      {/* 分隔线 1×30、40% 黑（`MainWindow.xaml:522-527`）；撤销重做整组隐藏时一并隐藏 */}
      {settings.dock.isUndoRedoVisible ? <div className="wb-dock__separator" aria-hidden /> : null}
      <UndoRedoButtons />
    </div>
  )
}

/**
 * 形状工具的自绘图标（MainWindow.xaml:502-518 的 ShapeToolIconPath）。Segoe Fluent Icons 里没有「形状」语义的图标，桌面因此用自绘 Path，这里逐字复用同一条数据：20×20、描边 1.7、圆头圆角。
 */
function ShapeToolIcon(): ReactNode {
  return (
    <svg width={20} height={20} viewBox="0 0 20 20" aria-hidden focusable="false">
      <path
        d="M10.5,1.5 H18.5 V9.5 H10.5 Z M1.5,14.5 A4,4 0 1 1 9.5,14.5 A4,4 0 1 1 1.5,14.5 Z"
        fill="none"
        stroke="currentColor"
        strokeWidth={1.7}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

// 页面 Dock（`MainWindow.xaml:685-737`）

/** 页面 Dock：上一页 / 指示器 / 下一页 / 新增（单页时整块隐藏）。 */
function RightDock(): ReactNode {
  const { t } = useI18n()
  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const settings = useSettings()
  const flyouts = useFlyoutMachine()
  const openFlyout = useOpenFlyout()
  const indicatorText = usePageIndicatorText()
  const showPager = useShowPagePager()
  const deletion = usePageDeletion()
  const ordered = applyDockOrder(PAGE_DOCK_ITEMS, (id) => id, settings.dock.pagesOrder)

  const closeFlyout = (): void => {
    flyouts.close()
  }

  const onOpenChange = (_event: unknown, data: { open: boolean }): void => {
    if (!data.open) {
      closeFlyout()
    }
  }

  const indicatorButton = (
    <DockButton
      label={indicatorText}
      className="wb-dock__page-indicator"
      title={t('MainWindow_PageManager_Title')}
      onClick={() => {
        flyouts.toggle('pages')
      }}
    />
  )

  const renderItem = (id: DockItemId): ReactNode => {
    // 单页时只折叠翻页三件套，「+」始终可见（否则没有入口新增第二页）
    if (!showPager && (id === 'pagePrev' || id === 'pageIndicator' || id === 'pageNext')) {
      return null
    }
    switch (id) {
      case 'pagePrev':
        return (
          <DockButton
            key={id}
            icon={<ChevronLeftRegular fontSize={20} />}
            title={t('Common_PreviousPage')}
            disabled={snapshot.currentPageIndex <= 0}
            onClick={() => {
              controller.setCurrentPageIndex(snapshot.currentPageIndex - 1)
            }}
          />
        )
      case 'pageIndicator':
        // 面板以指示器为锚点向上弹出（桌面以整个右侧 Dock 为锚点、`Placement="TopEdgeAlignedRight"`，
        // 见 `MainWindow.xaml:586-588`；这里用右对齐近似同一条视觉边）
        return (
          <Popover
            key={id}
            open={openFlyout === 'pages'}
            onOpenChange={onOpenChange}
            positioning={{ position: 'above', align: 'end' }}
          >
            <PopoverTrigger disableButtonEnhancement>{indicatorButton}</PopoverTrigger>
            <PopoverSurface style={SURFACE_STYLE}>
              <FluentProvider theme={webLightTheme} style={SURFACE_THEME_STYLE}>
                <PagesFlyout onClose={closeFlyout} onRequestDeletePage={deletion.requestDeletePage} />
              </FluentProvider>
            </PopoverSurface>
          </Popover>
        )
      case 'pageNext':
        return (
          <DockButton
            key={id}
            icon={<ChevronRightRegular fontSize={20} />}
            title={t('Common_NextPage')}
            disabled={snapshot.currentPageIndex >= snapshot.pageCount - 1}
            onClick={() => {
              controller.setCurrentPageIndex(snapshot.currentPageIndex + 1)
            }}
          />
        )
      case 'pageAdd':
        return (
          <DockButton
            key={id}
            icon={<AddRegular fontSize={20} />}
            title={t('Web_AddPage')}
            onClick={() => {
              controller.addPage()
            }}
          />
        )
      default:
        return null
    }
  }

  return (
    <>
      <div className="wb-dock wb-dock--pages">{ordered.map(renderItem)}</div>
      {deletion.confirmDialog}
    </>
  )
}

// 窄屏紧凑栏（网页端新增，桌面无对应实现）

/**
 * 窄屏（视口 <600，见 use-narrow-layout.ts）的 Dock：三段合并成一段。桌面窗口最窄也有几百 DIP 余量，手机竖屏只有 360~430，三段实测宽 198 / 395 / 62，横排必然互相覆盖（playwright-cli 实测：中段按钮被中段容器自身盖住，点击直接命中错误元素）。
 * 保留 4 个工具加撤销重做（书写闭环的高频操作），导入、设置、导出、全屏、页面收进【更多】。7 个按钮是上限：44×44 触摸目标加间距 4 加内边距 4 约 340，加两侧 8 的外边距刚好放进 360 宽，8 个要 388 放不下。
 */
function NarrowDock(): ReactNode {
  const settings = useSettings()

  return (
    <div className="wb-dock wb-dock--narrow">
      <ToolButtons />
      {settings.dock.isUndoRedoVisible ? <div className="wb-dock__separator" aria-hidden /> : null}
      <UndoRedoButtons />
      <NarrowMoreButton />
    </div>
  )
}

/** 紧凑栏里的小按钮：图标 20 + 文字 11，触摸目标 48×48（`styles/dock.css` 的 `.wb-sheet-button`）。 */
function SheetButton(props: {
  readonly icon: ReactNode
  readonly label: string
  readonly disabled?: boolean
  readonly onClick: () => void
}): ReactNode {
  return (
    <button
      type="button"
      className="wb-sheet-button"
      disabled={props.disabled}
      onClick={props.onClick}
    >
      <span className="wb-sheet-button__icon" aria-hidden>
        {props.icon}
      </span>
      <span className="wb-sheet-button__label">{props.label}</span>
    </button>
  )
}

/**
 * 窄屏的「更多」浮层：页面行、动作网格与可展开的页面管理面板。面板里既有图标按钮网格、又要内嵌 PagesFlyout，Fluent 的 MenuList 只能放菜单项，装不下这种结构，因此用 Popover；开合受控，点动作后统一关闭，避免浮层残留挡住画布。
 */
function NarrowMoreButton(): ReactNode {
  const { t } = useI18n()
  const actions = useShellActions()
  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const indicatorText = usePageIndicatorText()
  const [open, setOpen] = useState(false)
  const [pagesOpen, setPagesOpen] = useState(false)
  const deletion = usePageDeletion()

  const close = (): void => {
    setOpen(false)
    setPagesOpen(false)
  }

  /** 执行动作并关闭浮层。 */
  const run = (action: () => void) => (): void => {
    close()
    action()
  }

  return (
    <>
      <Popover
        open={open}
        onOpenChange={(_event, data) => {
          // 浮层是受控的：触发器自己只会报告开合意图，必须两边都接住，
          // 否则（只处理关闭）点按钮永远打不开
          if (data.open) {
            setOpen(true)
          } else {
            close()
          }
        }}
        positioning={{ position: 'above', align: 'end' }}
      >
        <PopoverTrigger disableButtonEnhancement>
          <DockButton icon={<MoreHorizontalRegular fontSize={20} />} title={t('Common_More')} />
        </PopoverTrigger>
        <PopoverSurface style={SURFACE_STYLE}>
          <FluentProvider theme={webLightTheme} style={SURFACE_THEME_STYLE}>
            <div className="wb-sheet">
              <div className="wb-sheet__pages">
                <SheetButton
                  icon={<ChevronLeftRegular fontSize={20} />}
                  label={t('Common_PreviousPage')}
                  disabled={snapshot.currentPageIndex <= 0}
                  onClick={run(() => {
                    controller.setCurrentPageIndex(snapshot.currentPageIndex - 1)
                  })}
                />
                {/* 页码不可点：点它没有更自然的动作，页面切换用左右两个按钮 + 下面的"页面管理" */}
                <div className="wb-sheet__indicator" aria-label={t('MainWindow_PageManager_Title')}>
                  {indicatorText}
                </div>
                <SheetButton
                  icon={<ChevronRightRegular fontSize={20} />}
                  label={t('Common_NextPage')}
                  disabled={snapshot.currentPageIndex >= snapshot.pageCount - 1}
                  onClick={run(() => {
                    controller.setCurrentPageIndex(snapshot.currentPageIndex + 1)
                  })}
                />
                <SheetButton
                  icon={<AddRegular fontSize={20} />}
                  label={t('Web_AddPage')}
                  onClick={run(() => {
                    controller.addPage()
                  })}
                />
              </div>

              <div className="wb-sheet__actions">
                <SheetButton
                  icon={<ArrowImportRegular fontSize={20} />}
                  label={t('Common_Import')}
                  onClick={run(actions.openImport)}
                />
                <SheetButton
                  icon={<ArrowExportRegular fontSize={20} />}
                  label={t('Common_Export')}
                  onClick={run(actions.openExport)}
                />
                <SheetButton
                  icon={<SettingsRegular fontSize={20} />}
                  label={t('Common_Settings')}
                  onClick={run(actions.openSettings)}
                />
                <SheetButton
                  icon={<FullScreenMaximizeRegular fontSize={20} />}
                  label={t(actions.isFullscreen ? 'Common_ExitFullScreen' : 'Common_FullScreen')}
                  onClick={run(actions.toggleFullscreen)}
                />
                <SheetButton
                  icon={<EditRegular fontSize={20} />}
                  label={t('MainWindow_PageManager_Title')}
                  onClick={() => {
                    setPagesOpen(!pagesOpen)
                  }}
                />
              </div>

              {pagesOpen ? (
                <div className="wb-sheet__pages-panel">
                  <PagesFlyout onClose={close} onRequestDeletePage={deletion.requestDeletePage} />
                </div>
              ) : null}
            </div>
          </FluentProvider>
        </PopoverSurface>
      </Popover>
      {deletion.confirmDialog}
    </>
  )
}
