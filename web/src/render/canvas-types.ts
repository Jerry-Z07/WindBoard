/**
 * 渲染层使用的结构化画布与 2D 上下文接口。不直接引用 DOM 类型的原因：渲染层必须能在 Node 环境的单测里跑（web/vite.config.ts 的 environment: 'node'），而 web/tsconfig.test.json 的 lib 不含 DOM，直接写 HTMLCanvasElement 与 CanvasRenderingContext2D 会让测试工程找不到类型名而编译失败。
 * 浏览器实现（HTMLCanvasElement 与 CanvasRenderingContext2D）在结构上满足这些接口，因此 createBoardRenderHandle(canvas, …) 可以直接传入真实画布；getContext 返回 unknown，真正的收窄放在渲染句柄的边界处（渲染层唯一接触 DOM 的地方）。
 */

/** 2D 上下文（`CanvasRenderingContext2D` 的最小结构视图，只列渲染层实际用到的方法）。 */
export interface RenderContext2D {
  /** 填充样式。浏览器侧类型为 `string | CanvasGradient | CanvasPattern`，渲染层只写字符串。 */
  fillStyle: string | object
  /** 描边样式（同上）。 */
  strokeStyle: string | object
  lineWidth: number
  lineCap: 'butt' | 'round' | 'square'
  lineJoin: 'bevel' | 'round' | 'miter'
  miterLimit: number
  font: string
  textAlign: 'center' | 'end' | 'left' | 'right' | 'start'
  textBaseline: 'alphabetic' | 'bottom' | 'hanging' | 'ideographic' | 'middle' | 'top'

  /** 直接设置变换矩阵（不做叠加），等价 Canvas2D 的 `setTransform(a, b, c, d, e, f)`。 */
  setTransform(a: number, b: number, c: number, d: number, e: number, f: number): void
  save(): void
  restore(): void

  beginPath(): void
  closePath(): void
  moveTo(x: number, y: number): void
  lineTo(x: number, y: number): void
  rect(x: number, y: number, width: number, height: number): void
  arc(
    x: number,
    y: number,
    radius: number,
    startAngle: number,
    endAngle: number,
    counterclockwise?: boolean,
  ): void
  ellipse(
    x: number,
    y: number,
    radiusX: number,
    radiusY: number,
    rotation: number,
    startAngle: number,
    endAngle: number,
    counterclockwise?: boolean,
  ): void
  /** 圆角矩形（Canvas2D 标准方法；旧环境缺失时渲染层退化为直角矩形）。 */
  roundRect?(
    x: number,
    y: number,
    width: number,
    height: number,
    radii?: number | number[],
  ): void

  fill(): void
  stroke(): void
  clip(): void
  fillRect(x: number, y: number, width: number, height: number): void
  /**
   * 把另一块画布或位图按 1:1 贴到目标位置（不缩放）。静态层缓存（static-cache.ts）靠它把离屏画好的整帧贴到主画布上；只声明三参数重载，因为渲染层只按设备像素 1:1 贴图。
   */
  drawImage(image: unknown, dx: number, dy: number): void
  fillText(text: string, x: number, y: number, maxWidth?: number): void
}

/**
 * 画布的 CSS 布局矩形（getBoundingClientRect() 的数值子集）。与 clientWidth/clientHeight 的区别是带小数：clientWidth 会四舍五入成整数，而设备像素对齐必须知道真实的小数尺寸与原点（见 render-handle.ts 的 computeDeviceGridAlignment）。
 */
export interface CanvasLayoutRect {
  readonly left: number
  readonly top: number
  readonly width: number
  readonly height: number
}

/**
 * 画布元素上需要写入的内联样式（`CSSStyleDeclaration` 的数值子集）。
 *
 * 渲染层只写这三项：把 CSS 尺寸钉到整设备像素，并用位移把左上角对齐到整设备像素。
 */
export interface CanvasInlineStyle {
  width: string
  height: string
  transform: string
}

/** 画布元素（`HTMLCanvasElement` 的最小结构视图）。 */
export interface RenderCanvas {
  /** CSS 像素宽（DIP，整数，四舍五入后）。 */
  readonly clientWidth: number
  /** CSS 像素高（DIP，整数，四舍五入后）。 */
  readonly clientHeight: number
  /** 后备存储像素宽；写它等于重置画布状态。 */
  width: number
  /** 后备存储像素高。 */
  height: number
  /** 取 2D 上下文；渲染层在边界处收窄为 `RenderContext2D`。 */
  getContext(contextId: '2d'): unknown
  /**
   * 带小数的布局矩形（可选；缺省时退回 `clientWidth/clientHeight`）。
   *
   * 离屏画布（`static-cache.ts` 造的）不需要它：离屏层与主画布共用同一套设备像素。
   */
  readonly getBoundingClientRect?: () => CanvasLayoutRect
  /**
   * 内联样式（可选；缺省时跳过设备像素对齐，行为退化为「只算后备存储尺寸」）。
   */
  readonly style?: CanvasInlineStyle
}
