import type { Vec2 } from './model/stroke.ts'

/**
 * 画板视口（对应桌面 `WindBoard.Board.Viewport.BoardViewport`）。
 *
 * 坐标约定（与桌面 `BoardViewport.cs` 逐条对齐）：
 * - `cameraWorld` 是**视口中心**对应的世界坐标，不是左上角；
 * - 屏幕坐标单位为 DIP，原点在视口左上角，`sizeDip` 由调用方注入（域层不读 DOM）；
 * - 世界 → 屏幕：`screen = (world − cameraWorld) · zoom + sizeDip / 2`；
 * - 屏幕 → 世界：`world = (screen − sizeDip / 2) / max(0.0001, zoom) + cameraWorld`；
 * - 平移：`cameraWorld −= deltaScreen / max(0.0001, zoom)`；
 * - 锚点缩放：锚点下的世界点保持不动，`zoom' = clamp(zoom · factor, 0.05, 32)`，
 *   `|zoom' − zoom| < 1e-6` 视为死区（桌面同款）。
 *
 * 状态变更通知（`subscribe`）：
 * - 只有状态**实际变化**时才通知（零平移、死区缩放、尺寸不变都不通知）；
 * - `cameraWorld` / `sizeDip` 返回不可变快照，仅在变化时更换对象引用，
 *   便于 UI 侧用 `useSyncExternalStore` 安全订阅；
 * - `updateViewportSize` 也会通知：可见区域变化后渲染层需要重绘。
 */

/** 交互缩放下限（桌面 `BoardViewport.MinZoom`）。 */
export const MIN_ZOOM = 0.05

/** 交互缩放上限（桌面 `BoardViewport.MaxZoom`）。 */
export const MAX_ZOOM = 32

/** 反向换算的除零保护（桌面 `Math.Max(0.0001f, Zoom)`）。 */
export const ZOOM_EPSILON = 0.0001

/** 锚点缩放死区：缩放变化小于该值时视为无效操作（桌面 `0.000001f`）。 */
export const ZOOM_DEAD_ZONE = 0.000001

/** 初始视口尺寸（桌面 `ViewportSizeDip = (1, 1)`，宽高各自不小于 1）。 */
const INITIAL_SIZE_DIP: Readonly<Vec2> = Object.freeze({ x: 1, y: 1 })

/** 初始相机（桌面 `CameraWorld = Vector2.Zero`）。 */
const INITIAL_CAMERA_WORLD: Readonly<Vec2> = Object.freeze({ x: 0, y: 0 })

/** 世界坐标包围盒（AABB）。 */
export interface WorldBounds {
  min: Vec2
  max: Vec2
}

/** 二维仿射矩阵，字段顺序对应 Canvas2D `setTransform(a, b, c, d, e, f)`（世界 → 屏幕）。 */
export interface AffineMatrix2D {
  a: number
  b: number
  c: number
  d: number
  e: number
  f: number
}

/** 把缩放收敛到交互区间（桌面 `SetView` / `ZoomAboutScreenPoint` 的 clamp）。 */
export function clampZoom(zoom: number): number {
  return Math.min(Math.max(zoom, MIN_ZOOM), MAX_ZOOM)
}

/** 画板视口。 */
export class BoardViewport {
  private _cameraWorld: Readonly<Vec2> = INITIAL_CAMERA_WORLD
  private _zoom = 1
  private _sizeDip: Readonly<Vec2> = INITIAL_SIZE_DIP
  private readonly _listeners = new Set<() => void>()

  /** 视口中心对应的世界坐标（不可变快照）。 */
  get cameraWorld(): Readonly<Vec2> {
    return this._cameraWorld
  }

  /** 缩放（0.05 ~ 32）。 */
  get zoom(): number {
    return this._zoom
  }

  /** 视口尺寸（DIP，宽高各自不小于 1；不可变快照）。 */
  get sizeDip(): Readonly<Vec2> {
    return this._sizeDip
  }

  /** 视口中心（屏幕坐标 DIP）。每次调用返回新对象，不参与状态订阅。 */
  get viewportCenterDip(): Vec2 {
    return { x: this._sizeDip.x / 2, y: this._sizeDip.y / 2 }
  }

  /** 世界 → 屏幕。 */
  worldToScreen(world: Vec2): Vec2 {
    const center = this.viewportCenterDip
    return {
      x: (world.x - this._cameraWorld.x) * this._zoom + center.x,
      y: (world.y - this._cameraWorld.y) * this._zoom + center.y,
    }
  }

  /** 屏幕 → 世界。 */
  screenToWorld(screen: Vec2): Vec2 {
    const center = this.viewportCenterDip
    const zoom = Math.max(ZOOM_EPSILON, this._zoom)
    return {
      x: (screen.x - center.x) / zoom + this._cameraWorld.x,
      y: (screen.y - center.y) / zoom + this._cameraWorld.y,
    }
  }

  /**
   * 世界 → 屏幕的仿射矩阵（供 Canvas2D `setTransform` 使用）。
   *
   * 与 `worldToScreen` 完全等价：`x' = a·x + c·y + e`，`y' = b·x + d·y + f`。
   */
  getWorldToScreenMatrix(): AffineMatrix2D {
    const center = this.viewportCenterDip
    return {
      a: this._zoom,
      b: 0,
      c: 0,
      d: this._zoom,
      e: center.x - this._cameraWorld.x * this._zoom,
      f: center.y - this._cameraWorld.y * this._zoom,
    }
  }

  /** 当前可见区域的世界坐标包围盒（桌面 `GetVisibleWorldBounds`）。 */
  getVisibleWorldBounds(): WorldBounds {
    const first = this.screenToWorld({ x: 0, y: 0 })
    const second = this.screenToWorld({ x: this._sizeDip.x, y: this._sizeDip.y })
    return {
      min: { x: Math.min(first.x, second.x), y: Math.min(first.y, second.y) },
      max: { x: Math.max(first.x, second.x), y: Math.max(first.y, second.y) },
    }
  }

  /**
   * 直接设置相机与缩放（桌面 `SetView`：缩放到交互区间后钳制）。
   *
   * 状态未变化时不通知订阅者。
   */
  setView(cameraWorld: Vec2, zoom: number): void {
    const nextZoom = clampZoom(zoom)
    const nextCamera = Object.freeze({ x: cameraWorld.x, y: cameraWorld.y })
    if (
      nextZoom === this._zoom &&
      nextCamera.x === this._cameraWorld.x &&
      nextCamera.y === this._cameraWorld.y
    ) {
      return
    }

    this._zoom = nextZoom
    this._cameraWorld = nextCamera
    this._emit()
  }

  /** 更新视口尺寸（桌面 `UpdateViewportSize`：宽高各自取 `max(1, value)`）。 */
  updateViewportSize(sizeDip: Vec2): void {
    const width = Math.max(1, sizeDip.x)
    const height = Math.max(1, sizeDip.y)
    if (width === this._sizeDip.x && height === this._sizeDip.y) {
      return
    }

    this._sizeDip = Object.freeze({ x: width, y: height })
    this._emit()
  }

  /** 按屏幕位移平移（桌面 `PanByScreenDelta`）。 */
  panByScreenDelta(deltaScreenDip: Vec2): void {
    const zoom = Math.max(ZOOM_EPSILON, this._zoom)
    const nextX = this._cameraWorld.x - deltaScreenDip.x / zoom
    const nextY = this._cameraWorld.y - deltaScreenDip.y / zoom
    if (nextX === this._cameraWorld.x && nextY === this._cameraWorld.y) {
      return
    }

    this._cameraWorld = Object.freeze({ x: nextX, y: nextY })
    this._emit()
  }

  /**
   * 以屏幕锚点缩放（桌面 `ZoomAboutScreenPoint`）：锚点下的世界点保持不动。
   *
   * `factor <= 0` 或非有限值（含 NaN）直接忽略；缩放变化小于死区时忽略。
   */
  zoomAboutScreenPoint(anchorScreenDip: Vec2, factor: number): void {
    if (!(factor > 0) || !Number.isFinite(factor)) {
      return
    }

    const oldZoom = this._zoom
    const newZoom = clampZoom(oldZoom * factor)
    if (Math.abs(newZoom - oldZoom) < ZOOM_DEAD_ZONE) {
      return
    }

    const center = this.viewportCenterDip
    const oldSafeZoom = Math.max(ZOOM_EPSILON, oldZoom)
    const worldBeforeX = (anchorScreenDip.x - center.x) / oldSafeZoom + this._cameraWorld.x
    const worldBeforeY = (anchorScreenDip.y - center.y) / oldSafeZoom + this._cameraWorld.y

    const newSafeZoom = Math.max(ZOOM_EPSILON, newZoom)
    this._zoom = newZoom
    this._cameraWorld = Object.freeze({
      x: worldBeforeX - (anchorScreenDip.x - center.x) / newSafeZoom,
      y: worldBeforeY - (anchorScreenDip.y - center.y) / newSafeZoom,
    })
    this._emit()
  }

  /** 订阅视口状态变化；返回取消订阅函数。 */
  subscribe(listener: () => void): () => void {
    this._listeners.add(listener)
    return () => {
      this._listeners.delete(listener)
    }
  }

  private _emit(): void {
    // 复制一份再遍历：允许监听器在回调里退订而不影响本轮通知。
    for (const listener of [...this._listeners]) {
      listener()
    }
  }
}
