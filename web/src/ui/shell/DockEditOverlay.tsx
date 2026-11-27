/**
 * Dock 就地编辑层（网页端对应桌面 `Features/Dock/UI/DockEditOverlay.xaml`）。
 *
 * 进入方式：设置 → Dock → 「编辑 Dock」；编辑期间不渲染正常 Dock，本层铺满内容区拦截画布输入，
 * 并在底部按真实 Dock 的左 / 中 / 右三块位置呈现编辑项。每次变更立即写回 `settings.dock`
 * （四组 order、hiddenItems），退出后正常 Dock 按最新设置重绘。
 *
 * 拖动用 Pointer Events 自己实现（鼠标、触摸、笔都能拖），不引入第三方拖拽库：
 * 指针按下时对项调用 `setPointerCapture`，移动时用各项 DOM 矩形判断落点，松手写回顺序。
 * 窄屏（≤700px，与 `use-narrow-layout.ts` 同断点）改为可滚动的纵向分组，保证 360px 可用；
 * 宽屏沿用真实 Dock 的三块布局。
 */

import {
  Button,
  FluentProvider,
} from '@fluentui/react-components'
import { winuiLightThemeForced } from '../../app/theme.ts'
import {
  AddRegular,
  ArrowImportRegular,
  ArrowRedoRegular,
  ArrowResetRegular,
  ArrowUndoRegular,
  CheckmarkRegular,
  ChevronLeftRegular,
  ChevronRightRegular,
  CursorRegular,
  DocumentRegular,
  EditRegular,
  EraserRegular,
  LockClosedRegular,
  MoreHorizontalRegular,
  SubtractRegular,
} from '@fluentui/react-icons'
import {
  useEffect,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
} from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'
import { isSameOrder, mergeDockOrder, moveItem, toggleDockHiddenItem } from '../../settings/dock-order.ts'
import { DEFAULT_SETTINGS, type DockItemId, type DockSettings } from '../../settings/model.ts'
import { ConfirmDialog } from '../dialogs/ConfirmDialog.tsx'
import { useNarrowLayout } from './use-narrow-layout.ts'

/** 四组顺序在设置模型（`DockSettings`）里的字段名。 */
type DockOrderKey = 'leftOrder' | 'toolsOrder' | 'undoRedoOrder' | 'pagesOrder'

/** 网页端不渲染的 Dock 项：浏览器没有窗口最小化能力（桌面 `MainWindow.xaml:133-145`）。 */
type WebDockItemId = Exclude<DockItemId, 'minimize'>

/** 类型守卫：判断设置里的 Dock id 是否会在网页端渲染。 */
function isWebDockItemId(id: DockItemId): id is WebDockItemId {
  return id !== 'minimize'
}

/** 一组编辑项的定义。 */
interface DockGroupDefinition {
  readonly orderKey: DockOrderKey
  /** 组标题（窄屏纵向分组时显示，同时作为列表的无障碍名）。 */
  readonly titleKey: MessageKey
}

/** 四组顺序与桌面 XAML 的摆放一致：左 → 中（工具 + 撤销重做）→ 右。 */
const LEFT_GROUP: DockGroupDefinition = {
  orderKey: 'leftOrder',
  titleKey: 'Web_Settings_DockGroupLeft',
}

const TOOLS_GROUP: DockGroupDefinition = {
  orderKey: 'toolsOrder',
  titleKey: 'Web_Settings_DockGroupTools',
}

const UNDO_REDO_GROUP: DockGroupDefinition = {
  orderKey: 'undoRedoOrder',
  titleKey: 'Web_Settings_DockGroupUndoRedo',
}

const PAGES_GROUP: DockGroupDefinition = {
  orderKey: 'pagesOrder',
  titleKey: 'Web_Settings_DockGroupPages',
}

/** 四组顺序（窄屏纵向排列用）。 */
const DOCK_GROUPS: readonly DockGroupDefinition[] = [
  LEFT_GROUP,
  TOOLS_GROUP,
  UNDO_REDO_GROUP,
  PAGES_GROUP,
]

/** FluentProvider 只做主题覆盖，不参与布局（absolute 子元素仍以 `.wb-dock-edit` 为包含块）。 */
const THEME_STYLE = { background: 'transparent', color: 'inherit', display: 'block' } as const

/** 单项的显示信息。 */
interface DockItemMeta {
  readonly labelKey: MessageKey
  readonly icon: ReactNode
}

/**
 * 每项的图标与文案，与主 Dock（`ui/shell/DockBar.tsx`）保持一致。
 * 桌面字形出处：`Features/Dock/UI/DockEditOverlay.xaml.cs` 的 `CreateItem` switch。
 */
const DOCK_ITEM_META: Record<WebDockItemId, DockItemMeta> = {
  more: { labelKey: 'Common_More', icon: <MoreHorizontalRegular fontSize={20} /> },
  import: { labelKey: 'Common_Import', icon: <ArrowImportRegular fontSize={20} /> },
  toolSelect: { labelKey: 'Tool_Select', icon: <CursorRegular fontSize={20} /> },
  toolPen: { labelKey: 'Tool_Pen', icon: <EditRegular fontSize={20} /> },
  toolEraser: { labelKey: 'Tool_Eraser', icon: <EraserRegular fontSize={20} /> },
  toolShape: { labelKey: 'Tool_Shape', icon: <ShapeToolIcon /> },
  undo: { labelKey: 'Common_Undo', icon: <ArrowUndoRegular fontSize={20} /> },
  redo: { labelKey: 'Common_Redo', icon: <ArrowRedoRegular fontSize={20} /> },
  pagePrev: { labelKey: 'Common_PreviousPage', icon: <ChevronLeftRegular fontSize={20} /> },
  pageIndicator: { labelKey: 'Common_PageNumber', icon: <DocumentRegular fontSize={20} /> },
  pageNext: { labelKey: 'Common_NextPage', icon: <ChevronRightRegular fontSize={20} /> },
  pageAdd: { labelKey: 'Common_Add', icon: <AddRegular fontSize={20} /> },
}

/** 形状工具的自绘图标（`MainWindow.xaml:502-518` 的 ShapeToolIconPath），与 `DockBar.tsx` 同一段 Path。 */
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

/** 拖动状态：哪一组的哪一项正被拖到哪一项上。 */
interface DragState {
  readonly orderKey: DockOrderKey
  readonly id: WebDockItemId
  readonly overId: WebDockItemId
}

/** 写入某一组顺序时构造的补丁（逐字段写，保持 `Partial<DockSettings>` 的类型收窄）。 */
function orderPatch(orderKey: DockOrderKey, order: readonly DockItemId[]): Partial<DockSettings> {
  switch (orderKey) {
    case 'leftOrder':
      return { leftOrder: order }
    case 'toolsOrder':
      return { toolsOrder: order }
    case 'undoRedoOrder':
      return { undoRedoOrder: order }
    case 'pagesOrder':
      return { pagesOrder: order }
  }
}

/** 属性。 */
export interface DockEditOverlayProps {
  /** 退出编辑模式（「完成」按钮与 Esc 共用）。 */
  readonly onDone: () => void
}

/** Dock 编辑层。 */
export function DockEditOverlay(props: DockEditOverlayProps): ReactNode {
  const { onDone } = props
  const { t } = useI18n()
  const settings = useSettings()
  const settingsService = useSettingsService()
  const narrow = useNarrowLayout()
  const [drag, setDrag] = useState<DragState | null>(null)
  const [resetConfirmOpen, setResetConfirmOpen] = useState(false)

  // Esc 退出（桌面 OnOverlayKeyDown）：监听 window 而不是依赖某个元素获得焦点。
  // 重置确认框打开时 Esc 交给对话框自己处理，不退出整个编辑层。
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape' && !resetConfirmOpen) {
        event.preventDefault()
        onDone()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [onDone, resetConfirmOpen])

  const visibleOrderOf = (orderKey: DockOrderKey): readonly WebDockItemId[] =>
    settings.dock[orderKey].filter(isWebDockItemId)

  const hiddenIds = new Set(settings.dock.hiddenItems.filter(isWebDockItemId))

  /** 写入某一组的顺序；内容没变就跳过，避免无意义的持久化与重渲染。 */
  const commitOrder = (orderKey: DockOrderKey, nextVisible: readonly WebDockItemId[]): void => {
    const next = mergeDockOrder(settings.dock[orderKey], nextVisible, isWebDockItemId)
    if (isSameOrder(settings.dock[orderKey], next)) {
      return
    }
    settingsService.patch({ dock: orderPatch(orderKey, next) })
  }

  /** 隐藏/恢复：`more` 是设置入口，双端都不允许隐藏（toggleDockHiddenItem 内部也会挡）。 */
  const toggleHidden = (id: WebDockItemId): void => {
    settingsService.patch({
      dock: { hiddenItems: toggleDockHiddenItem(settings.dock.hiddenItems, id) },
    })
  }

  const startDrag = (orderKey: DockOrderKey, id: WebDockItemId): void => {
    setDrag({ orderKey, id, overId: id })
  }

  const dragOver = (orderKey: DockOrderKey, overId: WebDockItemId): void => {
    setDrag((current) => {
      if (current === null || current.orderKey !== orderKey || current.overId === overId) {
        return current
      }
      return { ...current, overId }
    })
  }

  /** 松手：按「拖动项 → 落点项」算出新顺序并落盘；`overId` 为 null 表示取消。 */
  const endDrag = (
    orderKey: DockOrderKey,
    id: WebDockItemId,
    overId: WebDockItemId | null,
  ): void => {
    setDrag(null)
    if (overId === null || overId === id) {
      return
    }
    const order = visibleOrderOf(orderKey)
    const fromIndex = order.indexOf(id)
    const toIndex = order.indexOf(overId)
    if (fromIndex < 0 || toIndex < 0) {
      return
    }
    commitOrder(orderKey, moveItem(order, fromIndex, toIndex))
  }

  const resetLayout = (): void => {
    setDrag(null)
    settingsService.patch({ dock: DEFAULT_SETTINGS.dock })
  }

  const listProps = (group: DockGroupDefinition): DockItemListProps => ({
    group,
    order: visibleOrderOf(group.orderKey),
    hidden: hiddenIds,
    drag,
    onDragStart: startDrag,
    onDragOver: dragOver,
    onDragEnd: endDrag,
    onToggleHidden: toggleHidden,
  })

  return (
    <div
      className="wb-dock-edit"
      role="dialog"
      aria-modal="true"
      aria-label={t('Settings_Dock_EditInPlace_Header')}
    >
      <FluentProvider theme={winuiLightThemeForced} className="wb-winui-light" style={THEME_STYLE}>
        <div className="wb-dock-edit__bottom">
          <div className="wb-dock-edit__toolbar" role="toolbar" aria-label={t('Settings_Dock_EditInPlace_Header')}>
            <p className="wb-dock-edit__hint">{t('Dock_Edit_Hint')}</p>
            <div className="wb-dock-edit__actions">
              <Button
                icon={<ArrowResetRegular />}
                onClick={() => {
                  setResetConfirmOpen(true)
                }}
              >
                {t('Common_ResetToDefault')}
              </Button>
              <Button appearance="primary" icon={<CheckmarkRegular />} onClick={onDone}>
                {t('Dock_Edit_Done')}
              </Button>
            </div>
          </div>

          {narrow ? (
            <div className="wb-dock-edit__row wb-dock-edit__row--narrow">
              {DOCK_GROUPS.map((group) => (
                <div key={group.orderKey} className="wb-dock-edit__group">
                  <h4 className="wb-dock-edit__group-title">{t(group.titleKey)}</h4>
                  <div className="wb-dock">
                    <DockItemList {...listProps(group)} />
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="wb-dock-edit__row">
              <div className="wb-dock wb-dock-edit__plate wb-dock-edit__plate--left">
                <DockItemList {...listProps(LEFT_GROUP)} />
              </div>
              <div className="wb-dock wb-dock-edit__plate wb-dock-edit__plate--center">
                <DockItemList {...listProps(TOOLS_GROUP)} />
                <div className="wb-dock__separator" aria-hidden />
                <DockItemList {...listProps(UNDO_REDO_GROUP)} />
              </div>
              <div className="wb-dock wb-dock-edit__plate wb-dock-edit__plate--right">
                <DockItemList {...listProps(PAGES_GROUP)} />
              </div>
            </div>
          )}
        </div>

        <ConfirmDialog
          open={resetConfirmOpen}
          title={t('Settings_Dock_ResetLayout_Button')}
          message={t('Settings_Dock_ResetConfirm_Content')}
          confirmText={t('Common_ResetToDefault')}
          cancelText={t('Common_Cancel')}
          onConfirm={() => {
            setResetConfirmOpen(false)
            resetLayout()
          }}
          onCancel={() => {
            setResetConfirmOpen(false)
          }}
        />
      </FluentProvider>
    </div>
  )
}

/** 单组可重排列表的属性。 */
interface DockItemListProps {
  readonly group: DockGroupDefinition
  readonly order: readonly WebDockItemId[]
  readonly hidden: ReadonlySet<WebDockItemId>
  readonly drag: DragState | null
  readonly onDragStart: (orderKey: DockOrderKey, id: WebDockItemId) => void
  readonly onDragOver: (orderKey: DockOrderKey, overId: WebDockItemId) => void
  readonly onDragEnd: (
    orderKey: DockOrderKey,
    id: WebDockItemId,
    overId: WebDockItemId | null,
  ) => void
  readonly onToggleHidden: (id: WebDockItemId) => void
}

/**
 * 一组 Dock 编辑项。指针（鼠标、触摸、笔）按住项后拖动，落点项高亮，松手写回新顺序；
 * 点右上角减号/加号不触发拖动（按钮自己处理点击）。隐藏项保留原位，用虚线加降低透明度表示。
 */
function DockItemList(props: DockItemListProps): ReactNode {
  const { group, order, drag } = props
  const { t } = useI18n()
  const listRef = useRef<HTMLUListElement>(null)
  const activeDrag = drag !== null && drag.orderKey === group.orderKey ? drag : null

  /** 指针当前落在哪一项上（用 DOM 矩形判断，兼容换行与滚动）。 */
  const resolveOverId = (clientX: number, clientY: number): WebDockItemId | null => {
    const list = listRef.current
    if (list === null) {
      return null
    }
    for (const element of list.querySelectorAll<HTMLElement>('[data-dock-item-id]')) {
      const rect = element.getBoundingClientRect()
      const inside =
        clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom
      if (!inside) {
        continue
      }
      const id = element.dataset.dockItemId
      if (id !== undefined && (order as readonly string[]).includes(id)) {
        return id as WebDockItemId
      }
    }
    return null
  }

  return (
    <ul
      className="wb-dock-edit__list"
      data-dock-group={group.orderKey}
      aria-label={t('Web_Settings_DockGroupOrderAria_Fmt', t(group.titleKey))}
      ref={listRef}
    >
      {order.map((id, index) => {
        const meta = DOCK_ITEM_META[id]
        const isDragging = activeDrag !== null && activeDrag.id === id
        const isOver = activeDrag !== null && activeDrag.id !== id && activeDrag.overId === id
        const isHidden = props.hidden.has(id)
        const className = [
          'wb-dock-edit__item',
          isDragging ? 'wb-dock-edit__item--dragging' : '',
          isOver ? 'wb-dock-edit__item--over' : '',
          isHidden ? 'wb-dock-edit__item--hidden' : '',
        ]
          .filter((part) => part !== '')
          .join(' ')

        const handlePointerDown = (event: ReactPointerEvent<HTMLLIElement>): void => {
          // 点在项内按钮上时交给按钮自己处理，避免隐藏/恢复被拖动抢走
          if ((event.target as HTMLElement).closest('button') !== null) {
            return
          }
          // 鼠标只认左键；触摸与笔没有「按键」概念
          if (event.pointerType === 'mouse' && event.button !== 0) {
            return
          }
          event.currentTarget.setPointerCapture(event.pointerId)
          props.onDragStart(group.orderKey, id)
        }

        return (
          <li
            key={id}
            className={className}
            data-dock-item-id={id}
            aria-label={t('Web_Settings_DockItemAria_Fmt', t(meta.labelKey), index + 1, order.length)}
            onPointerDown={handlePointerDown}
            onPointerMove={(event) => {
              if (activeDrag === null || activeDrag.id !== id) {
                return
              }
              const overId = resolveOverId(event.clientX, event.clientY)
              if (overId !== null) {
                props.onDragOver(group.orderKey, overId)
              }
            }}
            onPointerUp={(event) => {
              if (activeDrag === null || activeDrag.id !== id) {
                return
              }
              const overId = resolveOverId(event.clientX, event.clientY) ?? activeDrag.overId
              props.onDragEnd(group.orderKey, id, overId)
            }}
            onPointerCancel={() => {
              if (activeDrag === null || activeDrag.id !== id) {
                return
              }
              props.onDragEnd(group.orderKey, id, null)
            }}
          >
            <span className="wb-dock-edit__icon" aria-hidden>
              {meta.icon}
            </span>
            <span className="wb-dock-edit__label">{t(meta.labelKey)}</span>

            {id === 'more' ? (
              // 「更多」是设置入口，桌面与网页端都锁死，不提供隐藏按钮
              <span
                className="wb-dock-edit__lock"
                role="img"
                title={t('Dock_Edit_CannotHideMore_Tooltip')}
                aria-label={t('Dock_Edit_CannotHideMore_Tooltip')}
              >
                <LockClosedRegular fontSize={11} />
              </span>
            ) : (
              <button
                type="button"
                className="wb-dock-edit__badge"
                aria-label={t(isHidden ? 'Dock_Edit_Restore_Tooltip' : 'Dock_Edit_Hide_Tooltip')}
                title={t(isHidden ? 'Dock_Edit_Restore_Tooltip' : 'Dock_Edit_Hide_Tooltip')}
                onClick={() => {
                  props.onToggleHidden(id)
                }}
              >
                {isHidden ? <AddRegular fontSize={12} /> : <SubtractRegular fontSize={12} />}
              </button>
            )}
          </li>
        )
      })}
    </ul>
  )
}
