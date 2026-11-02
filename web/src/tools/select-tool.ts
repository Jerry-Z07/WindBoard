import {
  CompositeCommand,
  UpdateElementTransformCommand,
  UpdateShapeGeometryCommand,
  UpdateStrokePointsCommand,
  type ShapeGeometry,
} from '../board/commands.ts'
import { getElementBoundsWorld, hitTestTopMostElement } from '../board/editing/element-pick.ts'
import { getInkItemsScreenBounds } from '../board/editing/ink-bounds.ts'
import { hitTestTopMostInkItem } from '../board/editing/pick-test.ts'
import { hitTestInkItemsInWorldRect } from '../board/editing/rect-select.ts'
import type { BoardElement } from '../board/model/element.ts'
import type { Shape } from '../board/model/shape.ts'
import type { Stroke, StrokePoint, Vec2 } from '../board/model/stroke.ts'
import { STROKE_KIND } from '../board/model/stroke.ts'
import type { InkItem } from '../board/model/workspace.ts'
import type { BoardCommand, BoardSession } from '../board/session.ts'
import type { AffineMatrix2D, BoardViewport } from '../board/viewport.ts'
import { getShapeBounds, getStrokeBounds } from '../ink/geometry.ts'
import type { InkTool, ToolGestureContext } from './tool-settings.ts'

/**
 * 选择工具状态机（P2），逐条移植桌面 `WindBoard/Interaction/Tools/SelectTool.cs`（910 行）。
 *
 * 分工（桌面注释 `SelectTool.cs:16-25`）：
 * - `begin` / `move` / `end` / `cancel` 只承载**框选（marquee）手势**（Begin=起点、Move=拖动、
 *   End=提交选择、Cancel=丢弃）；
 * - 拖动选中项、滚轮/双指变换由控制器调用本文件的专用方法
 *   （`beginSelectionMove` / `moveSelectionByScreenDelta` / `applyMatrixTransformToSelectedStrokes`
 *   / `commitSelection` / `cancelSelection`），**不在**统一生命周期内；
 * - 指针捕获、pointerId 跟踪由控制器负责，本文件不碰 DOM。
 *
 * 选中集泛化：笔迹与两点式形状都可选中（`InkItem = Stroke | Shape`）。
 * 矩阵变换（滚轮/双指缩放旋转）**只作用于笔迹**，形状只支持平移——轴对齐两点式几何无法无损
 * 承载旋转/非均匀缩放（`SelectTool.cs:22-24`、design D 明示边界）。
 *
 * 与桌面的两处实现差异（都不改变对外语义）：
 * 1. 桌面 `Stroke` 缓存 `BoundsMin/BoundsMax` 并在平移/变换后重算；web 侧笔迹是纯数据、不缓存
 *    包围盒（`ink/geometry.ts` 每次由点集重建），因此这里没有 `RecalculateBoundsFromPoints` 对应动作；
 * 2. 桌面选中项变更只发 `SelectionChanged`；本文件额外在「修改态 modified 翻转」时也发一次
 *    `onSelectionChanged`（lead 要求控制器据此刷新 UI），桌面此时仅靠控制器重绘。
 */

/** 框选矩形宽高都不超过该阈值（DIP）时按「点击」处理（桌面 `MarqueeClickThresholdDip`，`SelectTool.cs:29`）。 */
export const MARQUEE_CLICK_THRESHOLD_DIP = 6.0

/** 点选命中容差（DIP，世界坐标要除以 zoom；桌面 `SelectHitToleranceDip`，`SelectTool.cs:32`）。 */
export const SELECT_HIT_TOLERANCE_DIP = 8.0

/** 反向换算的除零保护（桌面 `Math.Max(0.0001f, Viewport.Zoom)`）。 */
const MIN_ZOOM = 0.0001

/**
 * 「拖动选中项」判定 modified 的屏幕增量阈值（平方）。
 *
 * 桌面 `SelectTool.cs:499`：`if (deltaScreenDip.LengthSquared() > 0.0001f)`。
 * 注意这是**平方**比较，即屏幕位移超过 0.01 DIP 才标记修改。
 */
const MODIFIED_DELTA_SCREEN_SQUARED = 0.0001

/** 世界平移的最小有效增量（平方）：桌面 `Stroke.Translate` / `BoardShape.Translate` 的守卫 `<= 0.0000001f`。 */
const TRANSLATE_EPSILON_SQUARED = 0.0000001

/** 快照比对阈值（平方）：桌面 `CommitSelection` 的 `0.000001f`（元素位移/尺寸、形状两点几何）。 */
const SNAPSHOT_EPSILON_SQUARED = 0.000001

/** 框选进行中的屏幕矩形（DIP，min ≤ max）。 */
export interface SelectionMarqueeRect {
  readonly min: Vec2
  readonly max: Vec2
}

/** 选择态快照（`onSelectionChanged` 的载荷，控制器据此重绘 + 刷新 UI）。 */
export interface SelectionState {
  /** 选中的条目（笔迹/形状），按文档顺序（= z 序）。 */
  readonly items: readonly InkItem[]
  /** 选中的元素（与 `items` 互斥：选中元素时 `items` 为空）。 */
  readonly element: BoardElement | null
  /** 自上次提交/取消以来选中对象是否发生过变换。 */
  readonly modified: boolean
  /** 框选进行中的屏幕矩形（DIP）；`null` 表示没有框选。 */
  readonly marquee: SelectionMarqueeRect | null
  /**
   * 选中集里是否含笔迹（桌面 `HasTransformableStrokes`，`SelectTool.cs:90-104`）。
   *
   * 矩阵变换只作用于笔迹；纯形状选择时控制器据此把缩放/旋转手势透传给视口，
   * 避免手势被吞掉后视口失效。
   */
  readonly hasTransformableStrokes: boolean
}

/** 构造选项（控制器持有会话与视口；`onSelectionChanged` 用于重绘 + UI 刷新）。 */
export interface SelectToolOptions {
  /** 当前页会话（命令入栈、文档读写）。 */
  readonly session: BoardSession
  /** 视口（屏幕/世界换算、缩放）；手势内优先用 `ctx.toWorld` / `ctx.zoom`，两者必须指向同一视口。 */
  readonly viewport: BoardViewport
  /** 选中集/修改态变化通知（桌面 `SelectionChanged`）。 */
  readonly onSelectionChanged?: (state: SelectionState) => void
}

/** 选择变换快照（桌面 `SelectionItemSnapshot` 家族，`SelectTool.cs:870-908`）。 */
interface StrokeSelectionSnapshot {
  readonly kind: 'stroke'
  readonly item: InkItem
  readonly stroke: Stroke
  readonly beforePoints: readonly StrokePoint[]
}

interface ShapeSelectionSnapshot {
  readonly kind: 'shape'
  readonly item: InkItem
  readonly shape: Shape
  readonly beforeGeometry: ShapeGeometry
}

type SelectionItemSnapshot = StrokeSelectionSnapshot | ShapeSelectionSnapshot

/**
 * 选择工具。
 *
 * 手势语义（`SelectTool.cs:127-192`）：
 * - `begin` 记录框选起点；`move` 更新当前点；`end` 依据「宽高是否都 ≤ 6 DIP」分成
 *   点击路径（点选条目/元素）与框选路径（屏幕矩形 → 世界 AABB → 条目框选）；
 * - 元素**只能单击选中**，不参与框选（`SelectTool.cs:438-441`）；
 * - `cancel` 只丢弃框选，不影响已有选中集。
 */
export class SelectTool implements InkTool {
  private readonly _session: BoardSession
  private readonly _viewport: BoardViewport
  private readonly _onSelectionChanged: ((state: SelectionState) => void) | undefined

  /** 选中集：条目（按文档顺序）或单个元素，二者互斥。 */
  private readonly _selectedItems: InkItem[] = []
  private _selectedElement: BoardElement | null = null

  /** 框选几何（屏幕 DIP）。 */
  private _isMarqueeActive = false
  private _marqueeStartScreen: Vec2 = { x: 0, y: 0 }
  private _marqueeCurrentScreen: Vec2 = { x: 0, y: 0 }

  /** 选择变换快照（拖动/滚轮/双指共用；一次连续交互只建一次）。 */
  private _selectionSnapshots: SelectionItemSnapshot[] | null = null
  private _selectionTransformElement: BoardElement | null = null
  private _selectionElementBeforePositionWorld: Vec2 | null = null
  private _selectionElementBeforeSizeWorld: Vec2 | null = null
  private _selectionModified = false

  /** 最后一次「点击选中元素」的元素与屏幕坐标（双击打开依赖它）。 */
  private _lastMarqueeClickedElement: BoardElement | null = null
  private _lastMarqueeClickScreenDip: Vec2 = { x: 0, y: 0 }

  /** `getState()` 的缓存（仅在状态变化时重建，便于 UI 侧做引用比较）。 */
  private _stateCache: SelectionState | null = null

  constructor(options: SelectToolOptions) {
    this._session = options.session
    this._viewport = options.viewport
    this._onSelectionChanged = options.onSelectionChanged
  }

  // ==== 手势生命周期（= 框选）====

  /**
   * 开始框选手势（`SelectTool.cs:127-132`）。
   *
   * 返回 `true`：选择工具总是接管被路由到它的事件（是否选中由 `end` 决定）。
   */
  begin(screenDip: Vec2, _ctx: ToolGestureContext): boolean {
    this._isMarqueeActive = true
    this._marqueeStartScreen = copyVec2(screenDip)
    this._marqueeCurrentScreen = copyVec2(screenDip)
    this._invalidateState()
    return true
  }

  /** 推进框选（`SelectTool.cs:134-137`）；返回是否有视觉变化（需要重绘框选矩形）。 */
  move(screenDip: Vec2, _ctx: ToolGestureContext): boolean {
    if (!this._isMarqueeActive) {
      return false
    }

    if (
      this._marqueeCurrentScreen.x === screenDip.x &&
      this._marqueeCurrentScreen.y === screenDip.y
    ) {
      return false
    }

    this._marqueeCurrentScreen = copyVec2(screenDip)
    this._invalidateState()
    return true
  }

  /**
   * 结束框选并提交选择（`SelectTool.cs:139-185`）。
   *
   * 注意：桌面**忽略** `End` 传入的指针位置，只用 begin/move 记下的起止点。
   */
  end(_screenDip: Vec2, ctx: ToolGestureContext): void {
    if (!this._isMarqueeActive) {
      return
    }

    this._isMarqueeActive = false
    this._lastMarqueeClickedElement = null
    this._lastMarqueeClickScreenDip = { x: 0, y: 0 }

    const rect = createRectFromTwoPoints(this._marqueeStartScreen, this._marqueeCurrentScreen)
    const isClick =
      rect.max.x - rect.min.x <= MARQUEE_CLICK_THRESHOLD_DIP &&
      rect.max.y - rect.min.y <= MARQUEE_CLICK_THRESHOLD_DIP

    if (isClick) {
      // 点击路径（`SelectTool.cs:154-168`）：先条目、再元素；都没有则清空选中。
      const hit = this._hitTestSelectableAtScreenPoint(this._marqueeStartScreen, ctx)
      if (hit.item !== null) {
        this._setSelectedItems([hit.item])
      } else {
        this._setSelectedElement(hit.element)
        // 双击打开依赖这两个字段（`SelectTool.cs:58-62,165-166`）：
        // 只有「点击路径」且命中元素时才记录，点选条目/框选都保持 null。
        this._lastMarqueeClickedElement = hit.element
        this._lastMarqueeClickScreenDip = copyVec2(this._marqueeStartScreen)
      }
    } else {
      // 框选路径（`SelectTool.cs:169-184`）：屏幕矩形两角 → 世界坐标，再各自取 min/max。
      const worldTopLeft = ctx.toWorld({ x: rect.min.x, y: rect.min.y })
      const worldBottomRight = ctx.toWorld({ x: rect.max.x, y: rect.max.y })

      const minWorld: Vec2 = {
        x: Math.min(worldTopLeft.x, worldBottomRight.x),
        y: Math.min(worldTopLeft.y, worldBottomRight.y),
      }
      const maxWorld: Vec2 = {
        x: Math.max(worldTopLeft.x, worldBottomRight.x),
        y: Math.max(worldTopLeft.y, worldBottomRight.y),
      }

      // 只框选条目（笔迹/形状），元素不参与框选（`SelectTool.cs:438-441`）。
      const selectedItems = hitTestInkItemsInWorldRect(
        this._session.document.items,
        minWorld,
        maxWorld,
      )
      this._setSelectedItems(selectedItems.length > 0 ? selectedItems : null)
    }

    // 框选结束：marquee 从状态里消失（即使选中集没变也要让缓存失效）。
    this._invalidateState()
  }

  /** 取消框选（`SelectTool.cs:187-192`）：不改变已有选中集，也不写撤销栈。 */
  cancel(): void {
    this._isMarqueeActive = false
    this._marqueeStartScreen = { x: 0, y: 0 }
    this._marqueeCurrentScreen = { x: 0, y: 0 }
    this._invalidateState()
  }

  // ==== 选择态 ====

  /** 当前选择态（含框选矩形；同一状态内返回同一对象引用）。 */
  getState(): SelectionState {
    const cached = this._stateCache
    if (cached !== null) {
      return cached
    }

    const state: SelectionState = {
      items: [...this._selectedItems],
      element: this._selectedElement,
      modified: this._selectionModified,
      marquee: this._isMarqueeActive
        ? createRectFromTwoPoints(this._marqueeStartScreen, this._marqueeCurrentScreen)
        : null,
      hasTransformableStrokes: this._hasTransformableStrokes(),
    }
    this._stateCache = state
    return state
  }

  /** 当前选中的条目（按文档顺序）。 */
  get selectedItems(): readonly InkItem[] {
    return this._selectedItems
  }

  /** 当前选中的元素。 */
  get selectedElement(): BoardElement | null {
    return this._selectedElement
  }

  /** 自上次提交/取消以来是否发生过变换。 */
  get selectionModified(): boolean {
    return this._selectionModified
  }

  /** 选中集里是否含笔迹（桌面 `HasTransformableStrokes`）。 */
  get hasTransformableStrokes(): boolean {
    return this._hasTransformableStrokes()
  }

  /** 清空选中（条目与元素）。 */
  clearSelection(): void {
    this._setSelectedItems(null)
    this._setSelectedElement(null)
  }

  /** 直接设置选中的条目集合（`null` / 空数组 = 清空）。 */
  setSelectionItems(items: readonly InkItem[] | null): void {
    this._setSelectedItems(items)
  }

  /** 直接设置选中的元素（传 `null` = 清空）。 */
  setSelectionElement(element: BoardElement | null): void {
    this._setSelectedElement(element)
  }

  /**
   * 校验选中集是否仍存在于文档（撤销/重做后清理「幽灵选择」，`SelectTool.cs:236-273`）。
   *
   * 条目集合会按文档当前顺序重新归一化；元素被移除则清空选中。
   */
  validateSelection(): void {
    if (this._selectedItems.length > 0) {
      const set = new Set<InkItem>(this._selectedItems)
      const normalized: InkItem[] = []
      for (const item of this._session.document.items) {
        if (set.has(item)) {
          normalized.push(item)
        }
      }

      if (isSameInkItemList(this._selectedItems, normalized)) {
        return
      }

      this._selectedItems.length = 0
      this._selectedItems.push(...normalized)
      this._raiseSelectionChanged()
      return
    }

    const element = this._selectedElement
    if (element === null) {
      return
    }

    if (this._session.document.elements.includes(element)) {
      return
    }

    this._selectedElement = null
    this._raiseSelectionChanged()
  }

  // ==== 选中范围判定 ====

  /**
   * 屏幕点（含 8 DIP 容差）是否落在选中对象包围盒内（`SelectTool.cs:348-361`）。
   *
   * 控制器用它决定「在选区上按下并拖动」= `move-selection` 还是 `select-marquee`
   * （web 侧对应 `interaction/pointer-router.ts` 的 `insideSelectionBounds`）。
   */
  isScreenPointInsideSelectedBounds(screenDip: Vec2): boolean {
    if (this._selectedItems.length > 0) {
      const bounds = getInkItemsScreenBounds(this._selectedItems, this._viewport)
      if (bounds === null) {
        return false
      }

      return (
        screenDip.x >= bounds.min.x - SELECT_HIT_TOLERANCE_DIP &&
        screenDip.x <= bounds.max.x + SELECT_HIT_TOLERANCE_DIP &&
        screenDip.y >= bounds.min.y - SELECT_HIT_TOLERANCE_DIP &&
        screenDip.y <= bounds.max.y + SELECT_HIT_TOLERANCE_DIP
      )
    }

    const element = this._selectedElement
    if (element === null) {
      return false
    }

    const boundsWorld = getElementBoundsWorld(element)
    if (boundsWorld.right - boundsWorld.left <= 0.0001 || boundsWorld.bottom - boundsWorld.top <= 0.0001) {
      return false
    }

    const minScreen = this._viewport.worldToScreen({ x: boundsWorld.left, y: boundsWorld.top })
    const maxScreen = this._viewport.worldToScreen({ x: boundsWorld.right, y: boundsWorld.bottom })

    const left = Math.min(minScreen.x, maxScreen.x) - SELECT_HIT_TOLERANCE_DIP
    const top = Math.min(minScreen.y, maxScreen.y) - SELECT_HIT_TOLERANCE_DIP
    const right = Math.max(minScreen.x, maxScreen.x) + SELECT_HIT_TOLERANCE_DIP
    const bottom = Math.max(minScreen.y, maxScreen.y) + SELECT_HIT_TOLERANCE_DIP

    return screenDip.x >= left && screenDip.x <= right && screenDip.y >= top && screenDip.y <= bottom
  }

  // ==== 选择变换（拖动选中项 / 滚轮 / 双指手势）====

  /**
   * 开始「拖动选中项」手势（`SelectTool.cs:451-461`）：对选中集或选中元素建立变换快照。
   */
  beginSelectionMove(): void {
    if (this._selectedItems.length > 0) {
      this._beginSelectionTransformSnapshotForItems(this._selectedItems)
      return
    }

    if (this._selectedElement !== null) {
      this._beginElementTransformSnapshot(this._selectedElement)
    }
  }

  /**
   * 对选中条目集合建立变换快照（滚轮/双指路径；已对同一集合建过则复用，`SelectTool.cs:466-469`）。
   */
  beginSelectionTransformSnapshotForSelectedItems(): void {
    this._beginSelectionTransformSnapshotForItems(this._selectedItems)
  }

  /**
   * 确保对指定元素建立了变换快照（滚轮缩放路径：快照不存在或目标变化时重建，`SelectTool.cs:474-480`）。
   */
  ensureElementTransformSnapshot(element: BoardElement): void {
    if (
      this._selectionElementBeforePositionWorld === null ||
      this._selectionTransformElement !== element
    ) {
      this._beginElementTransformSnapshot(element)
    }
  }

  /**
   * 按屏幕增量平移选中对象（拖动选中项路径；`SelectTool.cs:483-503`）。
   *
   * 形状与笔迹都支持平移；元素只改 `positionWorld`。
   * 与桌面一致：屏幕增量平方 ≤ 0.0001 时**仍然移动**，但不标记 modified（桌面 quirk）。
   */
  moveSelectionByScreenDelta(deltaScreenDip: Vec2): void {
    const zoom = Math.max(MIN_ZOOM, this._viewport.zoom)
    const deltaWorld: Vec2 = { x: deltaScreenDip.x / zoom, y: deltaScreenDip.y / zoom }

    if (this._selectionSnapshots !== null && this._selectedItems.length > 0) {
      for (const item of this._selectedItems) {
        translateInkItem(item, deltaWorld)
      }
    } else if (this._selectionTransformElement !== null) {
      const element = this._selectionTransformElement
      element.positionWorld = {
        x: element.positionWorld.x + deltaWorld.x,
        y: element.positionWorld.y + deltaWorld.y,
      }
    }

    if (
      deltaScreenDip.x * deltaScreenDip.x + deltaScreenDip.y * deltaScreenDip.y >
      MODIFIED_DELTA_SCREEN_SQUARED
    ) {
      this._setModified(true)
    }
  }

  /**
   * 按世界坐标增量平移整个选中集（双指纯平移路径；形状与笔迹都生效，`SelectTool.cs:506-519`）。
   */
  translateSelectedItems(deltaWorld: Vec2): void {
    if (deltaWorld.x * deltaWorld.x + deltaWorld.y * deltaWorld.y <= TRANSLATE_EPSILON_SQUARED) {
      return
    }

    for (const item of this._selectedItems) {
      translateInkItem(item, deltaWorld)
    }

    this._setModified(true)
  }

  /**
   * 对选中集应用 2D 矩阵变换（滚轮缩放/旋转、双指捏合，`SelectTool.cs:528-539`）。
   *
   * **只作用于笔迹**：形状跳过（轴对齐两点式几何无法无损承载旋转/非均匀缩放，design D）。
   * 与桌面一致：即使选中集里没有笔迹（或为空）也会标记 modified（桌面 quirk，见报告）。
   */
  applyMatrixTransformToSelectedStrokes(transform: AffineMatrix2D): void {
    for (const item of this._selectedItems) {
      if (item.kind === STROKE_KIND) {
        transformStroke(item, transform)
      }
    }

    this._setModified(true)
  }

  /** 标记选中对象已发生变换（控制器直接改元素后调用，`SelectTool.cs:542-545`）。 */
  markSelectionModified(): void {
    this._setModified(true)
  }

  /**
   * 选中条目集合的整体中心（世界坐标，滚轮旋转/缩放的锚点，`SelectTool.cs:550-600`）。
   *
   * 单条目沿用该条目中心（笔迹包围盒中心 → 无包围盒时点集平均；形状为包围盒中心）；
   * 多条目取整体包围盒中心。没有选中条目时返回 `null`（桌面此处返回 `Vector2.Zero`，
   * 返回 null 让控制器能区分「没有选中集」与「中心恰在原点」）。
   */
  getSelectedItemsCenterWorld(): Vec2 | null {
    if (this._selectedItems.length === 0) {
      return null
    }

    if (this._selectedItems.length === 1) {
      return getInkItemCenterWorld(this._selectedItems[0])
    }

    let minX = Number.POSITIVE_INFINITY
    let minY = Number.POSITIVE_INFINITY
    let maxX = Number.NEGATIVE_INFINITY
    let maxY = Number.NEGATIVE_INFINITY
    let hasAny = false

    for (const item of this._selectedItems) {
      const bounds = getInkItemBoundsWorldForCenter(item)
      if (bounds === null) {
        continue
      }

      minX = Math.min(minX, bounds.min.x)
      minY = Math.min(minY, bounds.min.y)
      maxX = Math.max(maxX, bounds.max.x)
      maxY = Math.max(maxY, bounds.max.y)
      hasAny = true
    }

    return hasAny ? { x: (minX + maxX) / 2, y: (minY + maxY) / 2 } : null
  }

  /**
   * 提交选择变换：把快照差异写入撤销记录（一次连续交互 = 一次 Ctrl+Z，`SelectTool.cs:609-687`）。
   *
   * - 笔迹：点列 diff → `UpdateStrokePointsCommand`；
   * - 形状：两点几何 diff → `UpdateShapeGeometryCommand`；
   * - 元素：位置/尺寸 diff → `UpdateElementTransformCommand`；
   * - 多条命令经 `CompositeCommand` 合并为一次撤销（混合选择只产生一条撤销记录）。
   * 没有任何实际变化时**不入栈**（`IsSameSelectionSnapshot` / 点列与几何比对）。
   */
  commitSelection(): void {
    const snapshots = this._selectionSnapshots
    const element = this._selectionTransformElement
    const elementBeforePosition = this._selectionElementBeforePositionWorld
    const elementBeforeSize = this._selectionElementBeforeSizeWorld

    this._selectionSnapshots = null
    this._selectionTransformElement = null
    this._selectionElementBeforePositionWorld = null
    this._selectionElementBeforeSizeWorld = null

    const commands: BoardCommand[] = []

    if (this._selectionModified && snapshots !== null && snapshots.length > 0) {
      for (const snapshot of snapshots) {
        if (snapshot.kind === 'stroke') {
          const afterPoints = snapshot.stroke.points
          if (isSameStrokePointList(snapshot.beforePoints, afterPoints)) {
            continue
          }

          commands.push(new UpdateStrokePointsCommand(snapshot.stroke, snapshot.beforePoints, afterPoints))
          continue
        }

        const afterGeometry: ShapeGeometry = {
          start: snapshot.shape.start,
          end: snapshot.shape.end,
        }
        if (isSameShapeGeometry(snapshot.beforeGeometry, afterGeometry)) {
          continue
        }

        commands.push(new UpdateShapeGeometryCommand(snapshot.shape, snapshot.beforeGeometry, afterGeometry))
      }
    }

    if (
      element !== null &&
      elementBeforePosition !== null &&
      elementBeforeSize !== null &&
      this._selectionModified
    ) {
      const afterPosition = element.positionWorld
      const afterSize = element.sizeWorld

      const moved = distanceSquared(elementBeforePosition, afterPosition) > SNAPSHOT_EPSILON_SQUARED
      const resized = distanceSquared(elementBeforeSize, afterSize) > SNAPSHOT_EPSILON_SQUARED
      if (moved || resized) {
        commands.push(
          new UpdateElementTransformCommand(
            element,
            elementBeforePosition,
            afterPosition,
            elementBeforeSize,
            afterSize,
          ),
        )
      }
    }

    if (commands.length > 0) {
      this._session.execute(commands.length === 1 ? commands[0] : new CompositeCommand(commands))
    }

    this._setModified(false)
  }

  /** 取消选择变换：恢复快照，不写撤销栈（`SelectTool.cs:695-729`）。 */
  cancelSelection(): void {
    if (this._selectionSnapshots !== null) {
      for (const snapshot of this._selectionSnapshots) {
        if (snapshot.kind === 'stroke') {
          applyStrokePoints(snapshot.stroke, snapshot.beforePoints)
          continue
        }

        setShapeGeometry(snapshot.shape, snapshot.beforeGeometry)
      }
    }

    const element = this._selectionTransformElement
    if (element !== null && this._selectionElementBeforePositionWorld !== null) {
      element.positionWorld = copyVec2(this._selectionElementBeforePositionWorld)
    }

    if (element !== null && this._selectionElementBeforeSizeWorld !== null) {
      element.sizeWorld = copyVec2(this._selectionElementBeforeSizeWorld)
    }

    this._selectionSnapshots = null
    this._selectionTransformElement = null
    this._selectionElementBeforePositionWorld = null
    this._selectionElementBeforeSizeWorld = null
    this._setModified(false)
  }

  /** 是否存在待提交的变换（滚轮交互结束时的提交判定，`SelectTool.cs:109-123`）。 */
  hasPendingChanges(): boolean {
    if (!this._selectionModified) {
      return false
    }

    return (
      (this._selectionSnapshots !== null && this._selectionSnapshots.length > 0) ||
      (this._selectionTransformElement !== null &&
        this._selectionElementBeforePositionWorld !== null &&
        this._selectionElementBeforeSizeWorld !== null)
    )
  }

  /**
   * 最后一次「点击选中元素」的元素与屏幕坐标（双击打开依赖，`SelectTool.cs:58-62`）。
   *
   * 只在点击路径命中元素时非 null；点选条目、框选、点击空白都会让它保持 null。
   */
  getLastMarqueeClick(): { element: BoardElement; screenDip: Vec2 } | null {
    const element = this._lastMarqueeClickedElement
    if (element === null) {
      return null
    }

    return { element, screenDip: copyVec2(this._lastMarqueeClickScreenDip) }
  }

  // ==== 预览（选择工具没有未提交条目）====

  getPreviewStroke(): Stroke | null {
    return null
  }

  getPreviewShape(): Shape | null {
    return null
  }

  // ==== 内部实现 ====

  private _hasTransformableStrokes(): boolean {
    for (const item of this._selectedItems) {
      if (item.kind === STROKE_KIND) {
        return true
      }
    }

    return false
  }

  /**
   * 点选命中（视觉优先级：上层元素 → 笔迹条目 → 下层元素，`SelectTool.cs:410-434`）。
   *
   * 与桌面的唯一差异：桌面分两次调 `HitTestTopMostElement`（分别传 above/below 列表），
   * web 侧元素存单数组 + `layer` 字段，因此先取 `elementsAboveInk()` 再取 `elementsBelowInk()`。
   */
  private _hitTestSelectableAtScreenPoint(
    screenDip: Vec2,
    ctx: ToolGestureContext,
  ): { item: InkItem | null; element: BoardElement | null } {
    const document = this._session.document
    const pointWorld = ctx.toWorld(screenDip)
    const toleranceWorld = SELECT_HIT_TOLERANCE_DIP / Math.max(MIN_ZOOM, ctx.zoom)

    const aboveElement = hitTestTopMostElement(
      document.elementsAboveInk(),
      pointWorld,
      toleranceWorld,
    )
    if (aboveElement !== null) {
      return { item: null, element: aboveElement }
    }

    const item = hitTestTopMostInkItem(document.items, pointWorld, toleranceWorld)
    if (item !== null) {
      return { item, element: null }
    }

    const belowElement = hitTestTopMostElement(
      document.elementsBelowInk(),
      pointWorld,
      toleranceWorld,
    )
    return { item: null, element: belowElement }
  }

  /**
   * 设置选中条目集合（`SelectTool.cs:275-324`）。
   *
   * 归一化规则（与桌面一致）：
   * - 去重后按**文档顺序**排列（框选保持相对层级顺序）；
   * - 过滤掉不在文档里的对象（避免撤销/重做后出现「幽灵选择」）；
   * - 空集合等价于清空选中（条目 + 元素）。
   */
  private _setSelectedItems(items: readonly InkItem[] | null): void {
    if (items === null || items.length === 0) {
      if (this._selectedItems.length === 0 && this._selectedElement === null) {
        return
      }

      this._selectedItems.length = 0
      this._selectedElement = null
      this._raiseSelectionChanged()
      return
    }

    const set = new Set<InkItem>()
    for (const item of items) {
      set.add(item)
    }

    const ordered: InkItem[] = []
    for (const item of this._session.document.items) {
      if (set.has(item)) {
        ordered.push(item)
      }
    }

    const unchanged = this._selectedElement === null && isSameInkItemList(this._selectedItems, ordered)
    if (unchanged) {
      return
    }

    this._selectedItems.length = 0
    this._selectedItems.push(...ordered)
    this._selectedElement = null
    this._raiseSelectionChanged()
  }

  /** 设置选中元素（与条目互斥，`SelectTool.cs:326-336`）。 */
  private _setSelectedElement(element: BoardElement | null): void {
    if (this._selectedElement === element && this._selectedItems.length === 0) {
      return
    }

    this._selectedElement = element
    this._selectedItems.length = 0
    this._raiseSelectionChanged()
  }

  /** 为选中条目集合建立变换快照（`SelectTool.cs:731-763`）。 */
  private _beginSelectionTransformSnapshotForItems(items: readonly InkItem[]): void {
    // 一次连续交互（拖拽/触摸/滚轮）只创建一次 Before 快照；同一集合重复调用直接复用。
    if (this._isSameSelectionSnapshot(items)) {
      return
    }

    const snapshots: SelectionItemSnapshot[] = []
    for (const item of items) {
      if (item.kind === STROKE_KIND) {
        snapshots.push({
          kind: 'stroke',
          item,
          stroke: item,
          beforePoints: item.points.map(copyStrokePoint),
        })
        continue
      }

      snapshots.push({
        kind: 'shape',
        item,
        shape: item,
        beforeGeometry: { start: copyVec2(item.start), end: copyVec2(item.end) },
      })
    }

    this._selectionSnapshots = snapshots.length > 0 ? snapshots : null
    this._selectionTransformElement = null
    this._selectionElementBeforePositionWorld = null
    this._selectionElementBeforeSizeWorld = null
    this._setModified(false)
  }

  /** 为单个元素建立变换快照（`SelectTool.cs:765-773`）。 */
  private _beginElementTransformSnapshot(element: BoardElement): void {
    this._selectionSnapshots = null
    this._selectionTransformElement = element
    this._selectionElementBeforePositionWorld = copyVec2(element.positionWorld)
    this._selectionElementBeforeSizeWorld = copyVec2(element.sizeWorld)
    this._setModified(false)
  }

  /** 快照是否已对同一集合建立（逐项引用比较，`SelectTool.cs:775-796`）。 */
  private _isSameSelectionSnapshot(items: readonly InkItem[]): boolean {
    const snapshots = this._selectionSnapshots
    if (snapshots === null || snapshots.length === 0) {
      return false
    }

    if (items.length !== snapshots.length) {
      return false
    }

    for (let index = 0; index < snapshots.length; index += 1) {
      if (snapshots[index].item !== items[index]) {
        return false
      }
    }

    return true
  }

  /** 更新 modified 并通知（桌面直接赋值；这里额外在翻转时发一次 SelectionChanged）。 */
  private _setModified(value: boolean): void {
    if (this._selectionModified === value) {
      return
    }

    this._selectionModified = value
    this._raiseSelectionChanged()
  }

  /**
   * 通知"选中对象的内容被就地修改了"（不改选中集本身，也不动 `modified` 标志）。
   *
   * 用途：外部经命令栈改了选中形状的几何 / 元素的位置尺寸后，`getState()` 的缓存仍是旧的，
   * `BoardController.refreshSnapshot` 会因引用未变而判定"没变化"，于是 React 侧的覆盖层
   * （形状属性浮层的数值、选中项 Dock 的三态）不会刷新——只有 canvas 因为按需重绘而正确。
   * 桌面没有这个问题：XAML 浮层在 `CommitShapePropertiesFromBoxes` 里会主动回读。
   *
   * 语义等价于桌面 `SelectionChanged` 的"内容变更"分支。
   */
  notifySelectionContentChanged(): void {
    this._raiseSelectionChanged()
  }

  private _raiseSelectionChanged(): void {
    this._invalidateState()
    this._onSelectionChanged?.(this.getState())
  }

  private _invalidateState(): void {
    this._stateCache = null
  }
}

// ==== 纯函数辅助（无状态，便于单测覆盖）====

function copyVec2(value: Vec2): Vec2 {
  return { x: value.x, y: value.y }
}

function copyStrokePoint(point: StrokePoint): StrokePoint {
  return { x: point.x, y: point.y, pressure: point.pressure }
}

/** 由两点构造规范化矩形（`SelectTool.cs:800-808` 的 `Rect.FromLTRB`）。 */
function createRectFromTwoPoints(a: Vec2, b: Vec2): SelectionMarqueeRect {
  return {
    min: { x: Math.min(a.x, b.x), y: Math.min(a.y, b.y) },
    max: { x: Math.max(a.x, b.x), y: Math.max(a.y, b.y) },
  }
}

/** 逐项引用比较（桌面 `InkItemListComparer.IsSameList`，`InkItemListComparer.cs:15-31`）。 */
function isSameInkItemList(a: readonly InkItem[], b: readonly InkItem[]): boolean {
  if (a.length !== b.length) {
    return false
  }

  for (let index = 0; index < a.length; index += 1) {
    if (a[index] !== b[index]) {
      return false
    }
  }

  return true
}

/**
 * 点列是否完全一致（桌面 `IsSameStrokePointList`，`SelectTool.cs:845-861`）。
 *
 * 桌面 `StrokePoint` 是 record struct，`!=` 逐字段比较（Position + Pressure），这里逐字段比。
 */
function isSameStrokePointList(a: readonly StrokePoint[], b: readonly StrokePoint[]): boolean {
  if (a.length !== b.length) {
    return false
  }

  for (let index = 0; index < a.length; index += 1) {
    const left = a[index]
    const right = b[index]
    if (left.x !== right.x || left.y !== right.y || left.pressure !== right.pressure) {
      return false
    }
  }

  return true
}

/** 形状两点几何是否一致（`SelectTool.cs:863-867`，`<= 0.000001f`）。 */
function isSameShapeGeometry(a: ShapeGeometry, b: ShapeGeometry): boolean {
  return (
    distanceSquared(a.start, b.start) <= SNAPSHOT_EPSILON_SQUARED &&
    distanceSquared(a.end, b.end) <= SNAPSHOT_EPSILON_SQUARED
  )
}

function distanceSquared(a: Vec2, b: Vec2): number {
  const dx = a.x - b.x
  const dy = a.y - b.y
  return dx * dx + dy * dy
}

/** 原地写入笔迹点列（对应桌面 `Stroke.Points.Clear(); AddRange(...)`，`SelectTool.cs:838-843`）。 */
function applyStrokePoints(stroke: Stroke, points: readonly StrokePoint[]): void {
  stroke.points.length = 0
  for (const point of points) {
    stroke.points.push(copyStrokePoint(point))
  }
}

/** 原地写入形状两点几何（对应桌面 `BoardShape.SetGeometry`，`BoardShape.cs:98-103`）。 */
function setShapeGeometry(shape: Shape, geometry: ShapeGeometry): void {
  shape.start = copyVec2(geometry.start)
  shape.end = copyVec2(geometry.end)
}

/**
 * 平移单个条目（桌面 `Stroke.Translate` / `BoardShape.Translate`）。
 *
 * 世界增量平方 ≤ 0.0000001 时直接返回（桌面两个 `Translate` 都有该守卫）。
 */
function translateInkItem(item: InkItem, deltaWorld: Vec2): void {
  if (deltaWorld.x * deltaWorld.x + deltaWorld.y * deltaWorld.y <= TRANSLATE_EPSILON_SQUARED) {
    return
  }

  if (item.kind === STROKE_KIND) {
    for (let index = 0; index < item.points.length; index += 1) {
      const point = item.points[index]
      item.points[index] = {
        x: point.x + deltaWorld.x,
        y: point.y + deltaWorld.y,
        pressure: point.pressure,
      }
    }
    return
  }

  setShapeGeometry(item, {
    start: { x: item.start.x + deltaWorld.x, y: item.start.y + deltaWorld.y },
    end: { x: item.end.x + deltaWorld.x, y: item.end.y + deltaWorld.y },
  })
}

/**
 * 对笔迹点列应用仿射矩阵（桌面 `Stroke.Transform` → `Vector2.Transform`，`BoardDocument.cs:127-141`）。
 *
 * 矩阵字段顺序与 Canvas2D `setTransform(a,b,c,d,e,f)` 一致：`x' = a·x + c·y + e`、`y' = b·x + d·y + f`。
 * 压感不参与变换（与桌面一致）。
 */
function transformStroke(stroke: Stroke, transform: AffineMatrix2D): void {
  if (stroke.points.length === 0) {
    return
  }

  for (let index = 0; index < stroke.points.length; index += 1) {
    const point = stroke.points[index]
    stroke.points[index] = {
      x: transform.a * point.x + transform.c * point.y + transform.e,
      y: transform.b * point.x + transform.d * point.y + transform.f,
      pressure: point.pressure,
    }
  }
}

/** 条目中心（桌面 `GetInkItemCenterWorld`，`SelectTool.cs:810-836`）。 */
function getInkItemCenterWorld(item: InkItem): Vec2 {
  if (item.kind === STROKE_KIND) {
    const bounds = getStrokeBounds(item)
    if (bounds !== null) {
      return { x: (bounds.min.x + bounds.max.x) / 2, y: (bounds.min.y + bounds.max.y) / 2 }
    }

    // 桌面：无包围盒（空点集）返回原点，否则退化为点集平均。
    if (item.points.length === 0) {
      return { x: 0, y: 0 }
    }

    let sumX = 0
    let sumY = 0
    for (const point of item.points) {
      sumX += point.x
      sumY += point.y
    }

    return { x: sumX / item.points.length, y: sumY / item.points.length }
  }

  const bounds = getShapeBounds(item)
  return { x: (bounds.min.x + bounds.max.x) / 2, y: (bounds.min.y + bounds.max.y) / 2 }
}

/**
 * 多条目中心计算用的条目包围盒（`SelectTool.cs:563-597`）。
 *
 * 桌面：笔迹无包围盒则重算、仍无则跳过；形状宽高同时 ≤ 0.0001 视为无效。
 * web 侧 `getShapeBounds` 至少外扩 0.25，形状守卫不可达，保留以对齐语义。
 */
function getInkItemBoundsWorldForCenter(item: InkItem): { min: Vec2; max: Vec2 } | null {
  if (item.kind === STROKE_KIND) {
    return getStrokeBounds(item)
  }

  const bounds = getShapeBounds(item)
  if (bounds.max.x - bounds.min.x <= 0.0001 && bounds.max.y - bounds.min.y <= 0.0001) {
    return null
  }

  return bounds
}
