/**
 * 画布宿主：唯一一个把 React 与命令式画布控制器接起来的地方。职责刻意做窄：创建与销毁 BoardController（内部含渲染句柄与 DOM 输入适配器）、把设置里的画布背景色同步给控制器、让画布元素铺满容器（touch-action: none 由样式表保证）、渲染 P2.3 覆盖层（选中后在选择框下方显示形状属性浮层与选中项 Dock）；选择框、8 个手柄与框选矩形画在 canvas 上（render/selection.ts，由控制器驱动），这里只负责桌面上那两块 XAML 浮层（BoardCanvasControl.xaml:74-168）。
 * 工具栏、弹出面板与设置窗口不归它管，由 shell 组件按控制器快照渲染。
 */

import { useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore, type ReactNode } from 'react'
import {
  createBoardController,
  type BoardController,
  type BoardControllerSnapshot,
} from '../../canvas/board-controller.ts'
import { attachBoardInput } from '../../canvas/dom-input.ts'
import { useReminderService, useSettings } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import { setLaunchFileHandler } from '../../platform/file-handlers.ts'
import { applyImportedWorkspace } from '../../features/import/wbix-import.ts'
import { hexToRgba, resolveInitialPenColorHex } from '../../settings/model.ts'
import { createDefaultPenSettings } from '../../tools/tool-settings.ts'
import {
  centimetersToWorld,
  computeLineEndFromLengthAngle,
  computeRectGeometryFromSize,
  MIN_SHAPE_PROPERTY_LENGTH,
} from '../../board/editing/shape-property.ts'
import type { Shape } from '../../board/model/shape.ts'
import { createAutosaveService, type AutosaveService } from '../../persistence/autosave.ts'
import {
  createBrowserAutosaveStore,
  type AutosaveMeta,
  type AutosaveStore,
} from '../../persistence/autosave-store.ts'
import { writeWbix } from '../../persistence/wbix/write.ts'
import { parseWbixForImport } from '../../features/import/wbix-import.ts'
import { workspaceToSnapshot } from '../../board/snapshot.ts'
import { RecoveryDialog } from '../dialogs/RecoveryDialog.tsx'
import { SelectionActionBar } from '../overlay/SelectionActionBar.tsx'
import { ShapePropertiesPanel } from '../overlay/ShapePropertiesPanel.tsx'
import {
  deriveShapePropertyFields,
  type ShapePropertyFieldId,
} from '../overlay/shape-property-fields.ts'

/** 属性。 */
export interface BoardSurfaceProps {
  /**
   * 控制器创建与销毁时回调。工具栏依赖控制器存在，而控制器又必须等画布元素挂载后才能创建，shell 用它决定什么时候可以渲染工具栏。
   */
  readonly onControllerChange?: (controller: BoardController | null) => void
  /** 画布之上、同一容器内的覆盖层（保留给 shell 用）。 */
  readonly children?: ReactNode
}

/** 桌面浮层与选择框的垂直间距（`BoardCanvasControl.Rendering.cs:225`/`:232` 的 `+ 8.0`）。 */
const OVERLAY_GAP_DIP = 8

/** 订阅控制器快照；控制器尚未创建时返回 null。 */
function useControllerSnapshot(controller: BoardController | null): BoardControllerSnapshot | null {
  const subscribe = (onChange: () => void): (() => void) =>
    controller === null ? () => undefined : controller.subscribe(onChange)
  const getSnapshot = (): BoardControllerSnapshot | null =>
    controller === null ? null : controller.getSnapshot()
  return useSyncExternalStore(subscribe, getSnapshot)
}

/** 覆盖层的实测尺寸（用于叠放与边界钳制；桌面同样先 `Measure` 再 `Clamp`）。 */
interface OverlayMetrics {
  readonly panelWidth: number
  readonly panelHeight: number
  readonly barWidth: number
}

const ZERO_METRICS: OverlayMetrics = { panelWidth: 0, panelHeight: 0, barWidth: 0 }

/** 取单个选中形状（桌面 `TryGetSingleSelectedShape` 的等价物：仅单选且是形状时返回）。 */
function singleSelectedShape(snapshot: BoardControllerSnapshot | null): Shape | null {
  if (snapshot === null || snapshot.selection.element !== null) {
    return null
  }
  const items = snapshot.selection.items
  if (items.length !== 1) {
    return null
  }
  const only = items[0]
  return only.kind === 'stroke' ? null : only
}

/** 画布宿主。 */
export function BoardSurface(props: BoardSurfaceProps): ReactNode {
  const settings = useSettings()
  const reminders = useReminderService()
  const { t } = useI18n()
  /**
   * 控制器的取词函数：控制器在挂载时创建一次，但语言可以随时切换，
   * 所以传一层读 ref 的包装（每次渲染更新 ref），而不是把 `t` 的当前值钉死进去。
   */
  const translateRef = useRef(t)
  translateRef.current = t
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const overlayHostRef = useRef<HTMLDivElement | null>(null)
  const [controller, setController] = useState<BoardController | null>(null)
  const snapshot = useControllerSnapshot(controller)
  const [metrics, setMetrics] = useState<OverlayMetrics>(ZERO_METRICS)
  /** 自动保存存储与服务（浏览器不支持 OPFS 时降级为内存，`durable` 为 false 就不提供恢复）。 */
  const autosaveStoreRef = useRef<AutosaveStore | null>(null)
  const autosaveServiceRef = useRef<AutosaveService | null>(null)
  const flushRef = useRef<(() => void) | null>(null)
  const [pendingAutosave, setPendingAutosave] = useState<AutosaveMeta | null>(null)

  // 语言切换后画布上的元素卡文案要重画：`translate` 读的是 ref，值已经更新，
  // 但没有任何交互会触发新帧，这里显式请求一次（`t` 的身份随语言变化）。
  useEffect(() => {
    controller?.requestRender()
  }, [controller, t])

  const background = settings.appearance.canvasBackgroundHex
  const paletteHexes = settings.writing.pen.paletteHexes

  useEffect(() => {
    const canvas = canvasRef.current
    if (canvas === null) {
      return
    }

    // 启动时的画笔颜色取色板首个有效色（桌面 `ApplyInitialPenColor`，默认 #FFFFFF）；色板全为空块时保留控制器默认值。粗细固定用默认 3.0（桌面不改写 `PenBaseSize`，面板里"哪一档被选中"是打开时按 `|当前值 - 档位| < 0.001` 反推的）。
    const initialColorHex = resolveInitialPenColorHex(paletteHexes)
    const penSettings = createDefaultPenSettings()
    if (initialColorHex !== null) {
      penSettings.color = hexToRgba(initialColorHex)
    }

    const created = createBoardController({
      canvas,
      background,
      penSettings,
      translate: (key, ...args) => translateRef.current(key, ...args),
    })
    // 输入绑定必须和控制器同生共死：`createBoardController` 只建模型与渲染，不碰 DOM 事件。
    const input = attachBoardInput(created, { canvas })
    setController(created)
    props.onControllerChange?.(created)

    return () => {
      props.onControllerChange?.(null)
      input.detach()
      created.dispose()
      setController(null)
    }
    // 只在挂载时创建一次：背景色变化走下面的 effect，避免重建控制器丢掉撤销栈。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 背景色跟随设置。
  useEffect(() => {
    controller?.setBackground(background)
  }, [controller, background])

  /**
   * 开发期调试句柄：把控制器挂到 window 上，供 DevTools 与性能脚本操作。只在 import.meta.env.DEV 下存在，生产构建里整段被摇掉；1 万笔迹压测需要批量灌入笔迹并直接读帧时间，靠模拟指针事件造数据既慢又失真。
   */
  useEffect(() => {
    if (!import.meta.env.DEV || controller === null) {
      return
    }
    const scope = globalThis as { __windboardDev?: { controller: BoardController } }
    scope.__windboardDev = { controller }
    return () => {
      delete scope.__windboardDev
    }
  }, [controller])

  /**
   * 系统「用 WindBoard 打开 .wbix」：把 file_handlers 递进来的文件插到最后一页之后。语义与导入对话框一致（桌面 ImportFlow.cs:99-101 只有 AppendAfterLastPage 一条活路径），因此直接复用 applyImportedWorkspace，不另写覆盖或合并逻辑。
   */
  useEffect(() => {
    if (controller === null) {
      return
    }
    setLaunchFileHandler((files) => {
      for (const file of files) {
        try {
          const imported = parseWbixForImport(file.bytes)
          applyImportedWorkspace(controller.workspace, imported)
          reminders.push({
            title: t('Web_OpenedFile_Title'),
            message: t('Web_OpenedFile_Message_Fmt', file.name),
            severity: 'informational',
          })
        } catch (error) {
          console.warn('打开系统传入的 WBIX 失败', error)
          reminders.push({
            title: t('Common_OpenFailed_Title'),
            message: t('Web_OpenFileFailed_Message_Fmt', file.name),
            severity: 'error',
          })
        }
      }
    })
    return () => {
      setLaunchFileHandler(null)
    }
  }, [controller, reminders, t])

  /**
   * 自动保存：控制器就绪后启动，文档与页变化时按 2s 节流落盘（网页端增强，桌面无此功能）。启动时先读元数据，有存档就弹 RecoveryDialog，恢复走「解析存档 → 替换工作区」，等价于导入自己刚存的 WBIX。
   */
  useEffect(() => {
    if (controller === null) {
      return
    }
    let disposed = false
    let unsubscribeSession: (() => void) | null = null
    let unsubscribeWorkspace: (() => void) | null = null

    void (async () => {
      const store = await createBrowserAutosaveStore()
      if (disposed) {
        return
      }
      autosaveStoreRef.current = store

      // 只有真正持久化时才提供"恢复"（内存存储重启即失，没有恢复价值）
      const meta = store.durable ? await store.readMeta() : null
      if (!disposed && meta !== null) {
        setPendingAutosave(meta)
      }

      const service = createAutosaveService({
        store,
        getBytes: () =>
          writeWbix(workspaceToSnapshot(controller.workspace, controller.viewport)),
        getMeta: () => {
          let itemCount = 0
          for (const page of controller.workspace.pages) {
            itemCount += page.session.document.items.length
          }
          return { pageCount: controller.workspace.pages.length, itemCount }
        },
        onError: () => {
          // 写盘失败只记录一次，不打断书写（下一步操作还会再试）
          console.warn('[autosave] 自动保存失败')
        },
      })
      autosaveServiceRef.current = service

      const schedule = (): void => {
        service.schedule()
      }
      unsubscribeSession = controller.getSession().subscribe(schedule)
      unsubscribeWorkspace = controller.workspace.subscribe(schedule)

      // 页面隐藏/卸载前尽力落盘（`pagehide` 比 `beforeunload` 更可靠）
      const flush = (): void => {
        void service.flush()
      }
      window.addEventListener('pagehide', flush)
      flushRef.current = flush
    })()

    return () => {
      disposed = true
      unsubscribeSession?.()
      unsubscribeWorkspace?.()
      if (flushRef.current !== null) {
        window.removeEventListener('pagehide', flushRef.current)
        flushRef.current = null
      }
      autosaveServiceRef.current?.dispose()
      autosaveServiceRef.current = null
    }
  }, [controller])

  /** 恢复存档：解析字节 → 替换工作区；成功后清掉存档。 */
  const handleRestoreAutosave = (): void => {
    void (async () => {
      const store = autosaveStoreRef.current
      setPendingAutosave(null)
      if (store === null) {
        return
      }
      try {
        const bytes = await store.readBytes()
        if (bytes === null) {
          await store.clear()
          return
        }
        const imported = parseWbixForImport(bytes)
        controller?.workspace.replaceWithSnapshots(imported.snapshots, imported.currentIndex)
        await store.clear()
      } catch {
        // 存档读不出来只能丢弃，但必须让用户知道内容没了，否则下次打开是空白画板且没有任何解释
        reminders.push({
          message: t('Web_AutosaveCorrupt_Message'),
          severity: 'warning',
        })
        await store.clear()
      }
    })()
  }

  /** 丢弃存档。 */
  const handleDiscardAutosave = (): void => {
    setPendingAutosave(null)
    void autosaveStoreRef.current?.clear()
  }

  // 选中集变化 → 重算覆盖层几何（选择框是 canvas 画的，这里只更新 DOM 浮层）。
  const selection = snapshot?.selection ?? null
  const hasSelection = selection !== null && (selection.items.length > 0 || selection.element !== null)

  // 浮层是先渲染、再量尺寸、再定位（桌面也是 `Measure` 后 `Clamp`）。用 layout effect 保证不闪烁。
  // 这里刻意不写依赖数组：每次渲染后都要重量（浮层内容随选中对象变化），而 `setMetrics` 只在尺寸真的变了才换对象，不会形成更新循环（下一帧 `current === next` 直接返回原值）。
  useLayoutEffect(() => {
    const host = overlayHostRef.current
    if (host === null) {
      return
    }
    const panel = host.querySelector('.wb-shape-props')
    const bar = host.querySelector('.wb-selection-actions')
    const next: OverlayMetrics = {
      panelWidth: panel === null ? 0 : panel.getBoundingClientRect().width,
      panelHeight: panel === null ? 0 : panel.getBoundingClientRect().height,
      barWidth: bar === null ? 0 : bar.getBoundingClientRect().width,
    }
    setMetrics((current) =>
      current.panelWidth === next.panelWidth &&
      current.panelHeight === next.panelHeight &&
      current.barWidth === next.barWidth
        ? current
        : next,
    )
  })

  const shape = singleSelectedShape(snapshot)
  const bounds = hasSelection ? (controller?.getSelectionScreenBounds() ?? null) : null
  const viewportSize = controller?.viewport.sizeDip ?? { x: 0, y: 0 }
  const shapeFields = shape === null ? null : deriveShapePropertyFields(shape)

  /**
   * 浮层锚点：选择框下沿中心（桌面 bounds.Bottom + 8），并按已量到的浮层宽度把 X 钳进画布。形状属性浮层直接锚在选择框下沿；选中项 Dock 在有属性浮层时排在浮层下方（桌面 panelBottom + 8），否则也在选择框下沿。两个组件内部都自带 translate(-50%, 8px)，所以这里传下沿中心即可。
   */
  const centerX = bounds === null ? 0 : (bounds.min.x + bounds.max.x) / 2
  const selectionBarAnchor =
    bounds === null
      ? null
      : {
          x: clampAnchorCenterX(centerX, metrics.barWidth, viewportSize.x),
          y: bounds.max.y + (shape === null ? 0 : metrics.panelHeight + OVERLAY_GAP_DIP),
        }

  const shapePanelAnchor =
    bounds === null || shape === null
      ? null
      : { x: clampAnchorCenterX(centerX, metrics.panelWidth, viewportSize.x), y: bounds.max.y }

  /** 形状属性浮层提交：厘米/角度 → 世界几何 → 命令栈（桌面 `CommitShapePropertiesFromBoxes`）。 */
  const onShapePropertyChange = (id: ShapePropertyFieldId, value: number): void => {
    if (controller === null || shape === null) {
      return
    }
    const field = shapeFields?.find((candidate) => candidate.id === id)
    if (field === undefined) {
      return
    }
    // 两个字段都是"提交时一次性换算"，所以要把另一个字段的当前值也读出来一起算。
    const primary = id === 'primary' ? value : (shapeFields?.[0]?.value ?? 0)
    const secondary = id === 'secondary' ? value : (shapeFields?.[1]?.value ?? 0)

    let next: { start: { x: number; y: number }; end: { x: number; y: number } }
    if (shape.kind === 'line' || shape.kind === 'arrow') {
      // 桌面：以 Start 为锚点，长度下限钳制后再换回世界坐标（顺序不可颠倒）。
      const lengthWorld = Math.max(MIN_SHAPE_PROPERTY_LENGTH, centimetersToWorld(primary))
      next = {
        start: { ...shape.start },
        end: computeLineEndFromLengthAngle(shape.start, lengthWorld, secondary),
      }
    } else {
      const widthWorld = Math.max(MIN_SHAPE_PROPERTY_LENGTH, centimetersToWorld(primary))
      const heightWorld = Math.max(MIN_SHAPE_PROPERTY_LENGTH, centimetersToWorld(secondary))
      next = computeRectGeometryFromSize(shape.start, shape.end, widthWorld, heightWorld)
    }
    controller.applySelectedShapeGeometry(next)
  }

  return (
    // 背景色与画布内的填充色保持一致：画布尺寸被钉成整设备像素，最多差半个像素，
    // 少掉的那条缝露出的就是这里的内联背景色，不会出现异色细线。
    <div className="wb-surface" style={{ background }}>
      <canvas ref={canvasRef} className="wb-surface__canvas" />
      {/* P2.3 覆盖层：桌面这两块是 XAML 浮层（`BoardCanvasControl.xaml:74-168`），
          选择框与手柄则由 canvas 自己绘制。 */}
      <div ref={overlayHostRef} className="wb-overlay-host">
        <ShapePropertiesPanel
          fields={shapeFields}
          anchorScreenDip={shapePanelAnchor}
          onChange={onShapePropertyChange}
        />
        <SelectionActionBar
          anchorScreenDip={selectionBarAnchor}
          onBringToFront={
            controller === null
              ? undefined
              : () => {
                  controller.bringSelectionToFront()
                }
          }
          onDuplicate={
            controller === null
              ? undefined
              : () => {
                  controller.duplicateSelection()
                }
          }
          onDelete={
            controller === null
              ? undefined
              : () => {
                  controller.deleteSelection()
                }
          }
        />
      </div>
      <RecoveryDialog
        meta={pendingAutosave}
        onRestore={handleRestoreAutosave}
        onDiscard={handleDiscardAutosave}
      />
      {/* 覆盖层的上下文由 `AppShell` 统一提供：Dock 是 `BoardSurface` 的兄弟节点，
          若只在这里包一层，Dock 就拿不到控制器。 */}
      {props.children}
    </div>
  )
}

/**
 * 把「居中锚点」的 X 钳进画布范围。桌面是把浮层的结果矩形钳到 [0, 画布宽 - 浮层宽]（Rendering.cs:308-317），网页端浮层靠 translate(-50%, 8px) 居中，因此按半宽收边，等价于同一个结果矩形钳制；宽度首帧尚未量到时传 0，视为未约束，下一帧量到后自然收敛。
 */
function clampAnchorCenterX(x: number, width: number, viewportWidth: number): number {
  if (width <= 0 || viewportWidth <= width) {
    return x
  }
  const half = width / 2
  return Math.min(Math.max(x, half), viewportWidth - half)
}
