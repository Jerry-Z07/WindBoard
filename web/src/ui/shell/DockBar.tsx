/**
 * 底部 Dock：三块浮层（左入口组 / 中部工具组 / 右侧页面组）。
 *
 * 桌面出处：
 * - 三块浮层分别对齐左/中/右 + `RequestedTheme="Light"` + 交互态资源覆盖：`WindBoard/MainWindow.xaml:54-70`
 * - 左 Dock（更多/最小化/导入）：`:82-160`
 * - 主 Dock（工具 + 分隔线 + 撤销重做）：`:441-558`
 * - 页面 Dock（上一页/指示/下一页/新增）：`:685-737`
 * - 按钮样式 `DockButtonStyle`：`WindBoard/App.xaml:20-28`
 *
 * 两个关键还原点：
 * 1. **强制浅色**：整块 Dock 包一层 `FluentProvider theme={webLightTheme}`，
 *    不受 App 级深色主题影响（桌面靠 `RequestedTheme="Light"` 实现）；
 * 2. **二次点击才弹面板**：开合由 `planToolButtonClick`（`ui/flyouts/flyout-machine.ts`）
 *    计算后驱动受控 `Popover`，不使用 `PopoverTrigger` 的默认开合。
 */

import type { ReactElement, ReactNode } from 'react'
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
  SubtractRegular,
} from '@fluentui/react-icons'
import {
  useBoardController,
  useBoardSnapshot,
  useFlyoutMachine,
  useOpenFlyout,
  usePageIndicatorText,
  useReminderService,
  useSettings,
  useShellActions,
  useShowPagePager,
} from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'
import type { ToolKind } from '../../interaction/pointer-router.ts'
import type { DockItemId } from '../../settings/model.ts'
import { EraserFlyout } from '../flyouts/EraserFlyout.tsx'
import { PagesFlyout } from '../flyouts/PagesFlyout.tsx'
import { PenFlyout } from '../flyouts/PenFlyout.tsx'
import { ShapeFlyout } from '../flyouts/ShapeFlyout.tsx'
import { planToolButtonClick } from '../flyouts/flyout-machine.ts'
import { DockButton } from './DockButton.tsx'
import {
  applyDockOrder,
  buildMoreMenuEntries,
  type DockPlacement,
  type MoreMenuActionId,
} from './dock-layout.ts'

/** 属性。 */
export interface DockBarProps {
  /** Dock 的位置：左侧入口组 / 中部工具组 / 右侧页面组。 */
  readonly placement: DockPlacement
}

/** 左 Dock 的项与默认顺序（`Features/Dock/Models/DockItemIds.cs:11-61`）。 */
const LEFT_DOCK_ITEMS: readonly DockItemId[] = ['more', 'minimize', 'import']

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
 * 工具项文案 key（`Strings/zh-CN/Tool.resw`：选择 / 书写 / 擦除 / 形状）。
 *
 * 这里存 key 而不是中文：调用点在渲染时用 `t(...)` 取词，
 * 语言切换（设置 → 常规 → 语言）后无需重新构造映射表。
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
 * 弹出层容器的内联样式：桌面用 `CompactFlyoutPresenterStyle` 把 Fluent 自带的
 * 边框/阴影/内边距全部清掉，只留内层面板自己的圆角 14 与 1px 描边
 * （`WindBoard/App.xaml:41-53` + `MainWindow.xaml:230-234`）。
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

/** Dock（按 placement 分派到三块浮层）。 */
export function DockBar(props: DockBarProps): ReactNode {
  const content =
    props.placement === 'left' ? (
      <LeftDock />
    ) : props.placement === 'center' ? (
      <CenterDock />
    ) : (
      <RightDock />
    )

  return (
    <FluentProvider theme={webLightTheme} className="wb-dock-theme" style={THEME_STYLE}>
      {content}
    </FluentProvider>
  )
}

// ===== 左 Dock（`MainWindow.xaml:82-160`） =====

/** 左 Dock：更多（菜单）/ 最小化 / 导入。 */
function LeftDock(): ReactNode {
  const { t } = useI18n()
  const actions = useShellActions()
  const reminders = useReminderService()
  const settings = useSettings()
  const ordered = applyDockOrder(LEFT_DOCK_ITEMS, (id) => id, settings.dock.leftOrder)

  const renderItem = (id: DockItemId): ReactNode => {
    switch (id) {
      case 'more':
        return <MoreButton key={id} />
      case 'minimize':
        return (
          <DockButton
            key={id}
            icon={<SubtractRegular fontSize={20} />}
            label={t('Common_Minimize')}
            onClick={() => {
              // 浏览器没有"最小化窗口"能力（桌面 `MainWindow.xaml:133-145` 的 `BackToWindow`）。
              // 保留按钮位置以维持与桌面一致的 Dock 结构，点击时用提醒解释为什么不可用。
              reminders.push({ message: '浏览器不支持最小化窗口', severity: 'informational' })
            }}
          />
        )
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

  /**
   * 菜单项文案：条目里的 `label` 是 `dock-layout.ts` 的静态中文，
   * 这里按 id 改用文案表取词（`Common_Settings` / `Common_Export` / `Common_FullScreen` /
   * `Common_ExitFullScreen`），语言切换后菜单才会跟着变。
   */
  const menuLabel = (id: MoreMenuActionId): string => {
    switch (id) {
      case 'settings':
        return t('Common_Settings')
      case 'export':
        return t('Common_Export')
      default:
        return actions.isFullscreen ? t('Common_ExitFullScreen') : t('Common_FullScreen')
    }
  }

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
                  {menuLabel(entry.id)}
                </MenuItem>
              ),
            )}
          </MenuList>
        </FluentProvider>
      </MenuPopover>
    </Menu>
  )
}

// ===== 主 Dock（`MainWindow.xaml:441-558`） =====

/** 主 Dock：选择 / 笔 / 橡皮 / 形状 + 分隔线 + 撤销 / 重做。 */
function CenterDock(): ReactNode {
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
   * 受控 Popover 的开合回调：只处理"关闭"。
   *
   * 打开由 `clickTool` 里的状态机决定，因此这里忽略 `open: true`——
   * 这样 `PopoverTrigger` 自带的"点击切换"不会与桌面的"二次点击才弹"打架。
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

  return (
    <div className="wb-dock wb-dock--center">
      {ordered.map(renderTool)}
      {/* 分隔线 1×30、40% 黑（`MainWindow.xaml:522-527`）；撤销重做整组隐藏时一并隐藏 */}
      {showUndoRedo ? <div className="wb-dock__separator" aria-hidden /> : null}
      {showUndoRedo ? (
        <div className="wb-dock__undo-redo">
          {applyDockOrder(['undo', 'redo'] as const, (id) => id, undoRedoOrder).map(renderUndoRedo)}
        </div>
      ) : null}
    </div>
  )
}

/**
 * 形状工具的自绘图标（`MainWindow.xaml:502-518` 的 `ShapeToolIconPath`）。
 *
 * 桌面刻意不用字体图标（Segoe Fluent Icons 里没有"形状"语义的图标），
 * 这里逐字复用同一条 Path 数据：20×20、描边 1.7、圆头圆角。
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

// ===== 页面 Dock（`MainWindow.xaml:685-737`） =====

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
                <PagesFlyout onClose={closeFlyout} />
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
            title="新增页"
            onClick={() => {
              controller.addPage()
            }}
          />
        )
      default:
        return null
    }
  }

  return <div className="wb-dock wb-dock--pages">{ordered.map(renderItem)}</div>
}
