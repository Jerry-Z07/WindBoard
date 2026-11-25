/**
 * 设置 → Dock（桌面 Features/Dock/UI/DockSettingsPage.xaml）。结构对齐桌面：拖动提示（:34-37，Settings_Dock_DragHint）；预览区（:40，Common_Preview）为四组可重排列表（左侧 Dock、工具、撤销重做、页面），分组与默认顺序取自 Features/Dock/Models/DockItemIds.cs（左组用 settings/model.ts 的 `WEB_LEFT_DOCK_ITEMS`，其余与 DEFAULT_*_DOCK_ITEMS 一一对应）；选项区（:231，Common_Options）含显示撤销重做与恢复默认布局。网页端不显示「最小化」（浏览器没有窗口最小化能力），也不提供「显示快捷入口 Dock」开关（v1 不实现快捷 Dock，字段仅随设置文件保留）。
 * 网页端的实现约束：拖动用 Pointer Events 自己实现（见 DockOrderGroup），鼠标、触摸、笔都能拖，未引入第三方拖拽库；拖动结束只写发生变化的那一组（桌面一次性写回四组，DockSettingsPage.xaml.cs:395-412），避免无意义的重渲染与存储写入；预览为可访问性补了组标题（桌面靠上中下三行位置区分，:47-229）；拖动反馈用拖动项半透明加落点高亮（桌面有系统拖动浮层）。
 */

import { Button, Switch } from '@fluentui/react-components'
import {
  AddRegular,
  ArrowDownRegular,
  ArrowImportRegular,
  ArrowRedoRegular,
  ArrowUndoRegular,
  ArrowUpRegular,
  ChevronLeftRegular,
  ChevronRightRegular,
  CursorRegular,
  DocumentRegular,
  EditRegular,
  EraserRegular,
  MoreHorizontalRegular,
  ReOrderDotsVerticalRegular,
} from '@fluentui/react-icons'
import { useRef, useState, type PointerEvent as ReactPointerEvent, type ReactNode } from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'
import { isSameOrder, moveItem, moveItemDown, moveItemUp } from '../dock-order.ts'
import {
  DEFAULT_PAGE_DOCK_ITEMS,
  DEFAULT_SETTINGS,
  DEFAULT_TOOL_DOCK_ITEMS,
  DEFAULT_UNDO_REDO_DOCK_ITEMS,
  WEB_LEFT_DOCK_ITEMS,
  type DockItemId,
} from '../model.ts'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
import { SettingsCard, SettingsSection } from './SettingsCard.tsx'

/** 四组顺序在设置模型（`DockSettings`）里的字段名。 */
type DockOrderKey = 'leftOrder' | 'toolsOrder' | 'undoRedoOrder' | 'pagesOrder'

/** 一组 Dock 项的定义。 */
interface DockGroupDefinition {
  /** 对应 `DockSettings` 的字段。 */
  readonly orderKey: DockOrderKey
  /** 组标题的文案 key（桌面预览无标题，网页端为可访问性补充）。 */
  readonly titleKey: MessageKey
  /** 该组的全部合法项，同时是默认顺序。 */
  readonly items: readonly DockItemId[]
}

/** 四组的分组顺序与桌面 XAML 的摆放一致：左 → 中（工具 + 撤销重做）→ 右。左组用网页端实际渲染的项（无「最小化」）。 */
const DOCK_GROUPS: readonly DockGroupDefinition[] = [
  { orderKey: 'leftOrder', titleKey: 'Web_Settings_DockGroupLeft', items: WEB_LEFT_DOCK_ITEMS },
  { orderKey: 'toolsOrder', titleKey: 'Web_Settings_DockGroupTools', items: DEFAULT_TOOL_DOCK_ITEMS },
  {
    orderKey: 'undoRedoOrder',
    titleKey: 'Web_Settings_DockGroupUndoRedo',
    items: DEFAULT_UNDO_REDO_DOCK_ITEMS,
  },
  { orderKey: 'pagesOrder', titleKey: 'Web_Settings_DockGroupPages', items: DEFAULT_PAGE_DOCK_ITEMS },
]

/**
 * 网页端不渲染的 Dock 项：浏览器没有窗口最小化能力（桌面 `MainWindow.xaml:133-145`）。
 * 设置文件里仍保留该 id（默认顺序必须与桌面语料一致），预览与主 Dock 都不出现。
 */
type HiddenDockItemId = 'minimize'

/** 网页端实际渲染的 Dock 项 id。 */
type WebDockItemId = Exclude<DockItemId, HiddenDockItemId>

/** 类型守卫：判断设置里的 Dock id 是否会在网页端渲染。 */
function isWebDockItemId(id: DockItemId): id is WebDockItemId {
  return id !== 'minimize'
}

/**
 * 把预览里重排后的可见项合并回完整顺序：不渲染的项（最小化）保持原下标不动，可见项按新顺序依次填入。
 * 这样写回的 `leftOrder` 不会丢掉桌面端需要的「最小化」（导出设置文件后桌面仍能正常还原布局）。
 */
function mergeVisibleOrder(
  full: readonly DockItemId[],
  visible: readonly WebDockItemId[],
): readonly DockItemId[] {
  let index = 0
  return full.map((id) => (isWebDockItemId(id) ? (visible[index++] ?? id) : id))
}

/** 单项的显示信息。 */
interface DockItemMeta {
  /** 本地化名称的 key（取桌面 zh-CN L10n）。 */
  readonly labelKey: MessageKey
  readonly icon: ReactNode
}

/**
 * 每项的图标：与主 Dock（`ui/shell/DockBar.tsx`）保持一致，"预览"才真的是预览。
 *
 * 桌面字形出处：`Features/Dock/UI/DockSettingsPage.xaml.cs:424-441`（`CreateItem` 的 switch）。
 */
const DOCK_ITEM_META: Record<WebDockItemId, DockItemMeta> = {
  // 桌面 Symbol.More（更多）
  more: { labelKey: 'Common_More', icon: <MoreHorizontalRegular fontSize={20} /> },
  // 桌面 Symbol.Download（导入）
  import: { labelKey: 'Common_Import', icon: <ArrowImportRegular fontSize={20} /> },
  // 桌面 Symbol.TouchPointer；Fluent 无同名图标，主 Dock 用 CursorRegular（见 DockBar.tsx:275-278）
  toolSelect: { labelKey: 'Tool_Select', icon: <CursorRegular fontSize={20} /> },
  // 桌面 Symbol.Edit（书写）
  toolPen: { labelKey: 'Tool_Pen', icon: <EditRegular fontSize={20} /> },
  // 桌面 FontIcon "\uE75C"（擦除）
  toolEraser: { labelKey: 'Tool_Eraser', icon: <EraserRegular fontSize={20} /> },
  // 桌面 FontIcon "\uEC87"（Draw 近似，Segoe Fluent Icons 无 Shapes）
  toolShape: { labelKey: 'Tool_Shape', icon: <ShapeToolIcon /> },
  // 桌面 Symbol.Undo（撤销）
  undo: { labelKey: 'Common_Undo', icon: <ArrowUndoRegular fontSize={20} /> },
  // 桌面 Symbol.Redo（重做）
  redo: { labelKey: 'Common_Redo', icon: <ArrowRedoRegular fontSize={20} /> },
  // 桌面 Symbol.Back（上一页）
  pagePrev: { labelKey: 'Common_PreviousPage', icon: <ChevronLeftRegular fontSize={20} /> },
  // 桌面 FontIcon "\uE8A7"（页码）；主 Dock 该位置显示页码文本，预览用文档图标近似
  pageIndicator: { labelKey: 'Common_PageNumber', icon: <DocumentRegular fontSize={20} /> },
  // 桌面 Symbol.Forward（下一页）
  pageNext: { labelKey: 'Common_NextPage', icon: <ChevronRightRegular fontSize={20} /> },
  // 桌面 Symbol.Add（新增）
  pageAdd: { labelKey: 'Common_Add', icon: <AddRegular fontSize={20} /> },
}

/**
 * 形状工具的自绘图标（MainWindow.xaml:502-518 的 ShapeToolIconPath），与 ui/shell/DockBar.tsx 的 ShapeToolIcon 是同一段 Path 数据；桌面刻意不用字体图标，因为 Segoe Fluent Icons 里没有「形状」语义的图标。这里没有抽公共组件，改动时需两处同步。
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

/** 拖动状态：哪一组的哪一项正被拖到哪一项上。 */
interface DragState {
  readonly orderKey: DockOrderKey
  readonly id: WebDockItemId
  readonly overId: WebDockItemId
}

/** 单组可重排列表的属性。 */
interface DockOrderGroupProps {
  readonly group: DockGroupDefinition
  readonly order: readonly WebDockItemId[]
  readonly drag: DragState | null
  readonly onDragStart: (orderKey: DockOrderKey, id: WebDockItemId) => void
  readonly onDragOver: (orderKey: DockOrderKey, overId: WebDockItemId) => void
  readonly onDragEnd: (
    orderKey: DockOrderKey,
    id: WebDockItemId,
    overId: WebDockItemId | null,
  ) => void
  readonly onMoveUp: (orderKey: DockOrderKey, index: number) => void
  readonly onMoveDown: (orderKey: DockOrderKey, index: number) => void
}

/**
 * 一组可重排的 Dock 项。交互：指针（鼠标、触摸、笔）按住任意一项后拖动，落点项高亮，松手时写入新顺序，点在「上移」「下移」按钮上不触发拖动（按钮自己处理点击）；每项可聚焦（tabIndex=0），键盘用户通过项内的「上移」「下移」按钮调整顺序，拖动中的项带 aria-grabbed，屏幕阅读器可感知拖动状态。
 */
function DockOrderGroup(props: DockOrderGroupProps): ReactNode {
  const { group, order, drag } = props
  const listRef = useRef<HTMLUListElement>(null)
  const activeDrag = drag !== null && drag.orderKey === group.orderKey ? drag : null
  const { t } = useI18n()

  /** 指针当前落在哪一项上（用 DOM 矩形判断，兼容换行布局）。 */
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
    <div className="wb-dock-order__group">
      <h4 className="wb-dock-order__group-title">{t(group.titleKey)}</h4>
      <ul
        className="wb-dock-order__list"
        aria-label={t('Web_Settings_DockGroupOrderAria_Fmt', t(group.titleKey))}
        ref={listRef}
      >
        {order.map((id, index) => {
          const meta = DOCK_ITEM_META[id]
          const isDragging = activeDrag !== null && activeDrag.id === id
          const isOver = activeDrag !== null && activeDrag.id !== id && activeDrag.overId === id
          const className = [
            'wb-dock-order__item',
            isDragging ? 'wb-dock-order__item--dragging' : '',
            isOver ? 'wb-dock-order__item--over' : '',
          ]
            .filter((part) => part !== '')
            .join(' ')

          const handlePointerDown = (event: ReactPointerEvent<HTMLLIElement>): void => {
            // 点在项内按钮上时交给按钮自己处理，避免"上移/下移"被拖动抢走
            if ((event.target as HTMLElement).closest('button') !== null) {
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
              tabIndex={0}
              aria-grabbed={isDragging}
              aria-label={t(
                'Web_Settings_DockItemAria_Fmt',
                t(meta.labelKey),
                index + 1,
                order.length,
              )}
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
              <span className="wb-dock-order__grip" aria-hidden>
                <ReOrderDotsVerticalRegular fontSize={12} />
              </span>
              <span className="wb-dock-order__icon" aria-hidden>
                {meta.icon}
              </span>
              <span className="wb-dock-order__label">{t(meta.labelKey)}</span>
              <span className="wb-dock-order__moves">
                <Button
                  size="small"
                  appearance="subtle"
                  className="wb-dock-order__move"
                  icon={<ArrowUpRegular />}
                  aria-label={t('Web_Settings_MoveUp_Fmt', t(meta.labelKey))}
                  disabled={index === 0}
                  onClick={() => {
                    props.onMoveUp(group.orderKey, index)
                  }}
                />
                <Button
                  size="small"
                  appearance="subtle"
                  className="wb-dock-order__move"
                  icon={<ArrowDownRegular />}
                  aria-label={t('Web_Settings_MoveDown_Fmt', t(meta.labelKey))}
                  disabled={index === order.length - 1}
                  onClick={() => {
                    props.onMoveDown(group.orderKey, index)
                  }}
                />
              </span>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

/** Dock 设置页。 */
export function DockPage(): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()
  const [drag, setDrag] = useState<DragState | null>(null)
  const { t } = useI18n()

  /** 某一组在预览里展示的顺序：去掉网页端不渲染的项（最小化），其余保持设置里的顺序。 */
  const visibleOrderOf = (orderKey: DockOrderKey): readonly WebDockItemId[] =>
    settings.dock[orderKey].filter(isWebDockItemId)

  /** 写入某一组的顺序；内容没变就跳过，避免无意义的持久化与重渲染。 */
  const commitOrder = (orderKey: DockOrderKey, nextVisible: readonly WebDockItemId[]): void => {
    const next = mergeVisibleOrder(settings.dock[orderKey], nextVisible)
    if (isSameOrder(settings.dock[orderKey], next)) {
      return
    }
    switch (orderKey) {
      case 'leftOrder':
        settingsService.patch({ dock: { leftOrder: next } })
        break
      case 'toolsOrder':
        settingsService.patch({ dock: { toolsOrder: next } })
        break
      case 'undoRedoOrder':
        settingsService.patch({ dock: { undoRedoOrder: next } })
        break
      case 'pagesOrder':
        settingsService.patch({ dock: { pagesOrder: next } })
        break
    }
  }

  const handleDragStart = (orderKey: DockOrderKey, id: WebDockItemId): void => {
    setDrag({ orderKey, id, overId: id })
  }

  const handleDragOver = (orderKey: DockOrderKey, overId: WebDockItemId): void => {
    setDrag((current) => {
      if (current === null || current.orderKey !== orderKey || current.overId === overId) {
        return current
      }
      return { ...current, overId }
    })
  }

  /** 松手：按"拖动项 → 落点项"算出新顺序并落盘；`overId` 为 null 表示取消。 */
  const handleDragEnd = (
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

  const handleMoveUp = (orderKey: DockOrderKey, index: number): void => {
    commitOrder(orderKey, moveItemUp(visibleOrderOf(orderKey), index))
  }

  const handleMoveDown = (orderKey: DockOrderKey, index: number): void => {
    commitOrder(orderKey, moveItemDown(visibleOrderOf(orderKey), index))
  }

  return (
    <div className="wb-settings-page">
      {/* 桌面 `DockSettingsPage.xaml:34-37` 的拖动提示 */}
      <p className="wb-settings-hint">
        {t('Settings_Dock_DragHint')}
      </p>

      <SettingsSection title={t('Common_Preview')}>
        {DOCK_GROUPS.map((group) => (
          <DockOrderGroup
            key={group.orderKey}
            group={group}
            order={visibleOrderOf(group.orderKey)}
            drag={drag}
            onDragStart={handleDragStart}
            onDragOver={handleDragOver}
            onDragEnd={handleDragEnd}
            onMoveUp={handleMoveUp}
            onMoveDown={handleMoveDown}
          />
        ))}
      </SettingsSection>

      <SettingsSection title={t('Common_Options')}>
        <SettingsCard
          title={t('Settings_Dock_ShowUndoRedo_Header')}
          description={t('Web_Settings_DockUndoRedo_Description')}
          anchor={SETTINGS_ANCHORS.dockUndoRedoVisible}
        >
          <Switch
            checked={settings.dock.isUndoRedoVisible}
            aria-label={t('Settings_Dock_ShowUndoRedo_Header')}
            onChange={(_, data) => {
              settingsService.patch({ dock: { isUndoRedoVisible: data.checked } })
            }}
          />
        </SettingsCard>

        {/* 桌面 `DockSettingsPage.xaml:418-424`：恢复默认布局（`s.Dock = new DockSettings()`） */}
        <SettingsCard
          title={t('Settings_Dock_ResetLayout_Button')}
          description={t('Web_Settings_DockReset_Description')}
        >
          <Button
            onClick={() => {
              setDrag(null)
              settingsService.patch({ dock: DEFAULT_SETTINGS.dock })
            }}
          >
            {t('Common_ResetToDefault')}
          </Button>
        </SettingsCard>
      </SettingsSection>
    </div>
  )
}
