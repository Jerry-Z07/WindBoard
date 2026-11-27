import type { BoardElement, Layer } from './model/element.ts'
import type { Shape } from './model/shape.ts'
import type { InkItem } from './model/workspace.ts'
import type { Stroke, StrokePoint, Vec2 } from './model/stroke.ts'
import type { BoardDocument } from './document.ts'
import type { BoardCommand } from './session.ts'

/**
 * 可撤销命令（对应桌面 WindBoard.Board.Commands.IBoardCommand）。与桌面签名一致：do 与 undo 接收目标文档，命令本身不持有文档引用，同一命令对象可在不同页的会话之间复用。
 * 命令粒度与桌面一致（design §6）：画一笔或画一个形状为一条 AddInkItemCommand，一次橡皮手势（可能擦除多段）为一条 ReplaceInkItemsCommand，一次选择变换（P2）为一条 CompositeCommand，清屏为一条 ClearCommand。
 */

/** 把文档的条目数组整体替换为给定内容（对应桌面 `ReplaceStrokesCommand.Replace`）。 */
function replaceItems(document: BoardDocument, items: readonly InkItem[]): void {
  document.items.length = 0
  document.items.push(...items)
}

/** 添加一个墨迹条目（对应桌面 `AddInkItemCommand`）。 */
export class AddInkItemCommand implements BoardCommand {
  private readonly _item: InkItem
  private _index: number | null = null

  constructor(item: InkItem) {
    this._item = item
  }

  do(document: BoardDocument): void {
    // 首次执行记录插入位置；撤销后重做仍插回原位置（桌面 `_index ??=`）。
    this._index ??= document.items.length
    document.items.splice(this._index, 0, this._item)
  }

  undo(document: BoardDocument): void {
    const index = this._index
    if (index === null) {
      return
    }

    // 位置未被其它命令打乱时按引用精确删除；否则退化为按引用查找（桌面同款）。
    if (index >= 0 && index < document.items.length && document.items[index] === this._item) {
      document.items.splice(index, 1)
      return
    }

    const found = document.items.indexOf(this._item)
    if (found >= 0) {
      document.items.splice(found, 1)
    }
  }
}

/** 用一组条目整体替换文档条目（对应桌面 `ReplaceStrokesCommand`）。 */
export class ReplaceInkItemsCommand implements BoardCommand {
  private readonly _before: readonly InkItem[]
  private readonly _after: readonly InkItem[]

  constructor(before: readonly InkItem[], after: readonly InkItem[]) {
    // 构造时拷贝，避免调用方后续修改数组影响命令历史。
    this._before = [...before]
    this._after = [...after]
  }

  do(document: BoardDocument): void {
    replaceItems(document, this._after)
  }

  undo(document: BoardDocument): void {
    replaceItems(document, this._before)
  }
}

/**
 * 删除一组墨迹条目（桌面 RemoveInkItemCommand 的多条目泛化）。首次执行时记录每个条目在文档中的下标，撤销时按下标升序插回，重做时按记录下标降序删除，保证多条目删除后顺序仍与撤销前一致。
 */
export class RemoveInkItemsCommand implements BoardCommand {
  private readonly _items: readonly InkItem[]
  private _indices: number[] | null = null

  constructor(items: readonly InkItem[]) {
    this._items = [...items]
  }

  do(document: BoardDocument): void {
    if (this._indices === null) {
      this._indices = this._items.map((item) => document.items.indexOf(item))
    }

    const indices = this._indices
    // 降序删除：先删后面的条目，前面记录的下标才不会失效。
    const order = indices.map((_index, itemIndex) => itemIndex).sort((a, b) => indices[b] - indices[a])
    for (const itemIndex of order) {
      const item = this._items[itemIndex]
      const index = indices[itemIndex]
      if (index >= 0 && index < document.items.length && document.items[index] === item) {
        document.items.splice(index, 1)
        continue
      }

      const found = document.items.indexOf(item)
      if (found >= 0) {
        document.items.splice(found, 1)
      }
    }
  }

  undo(document: BoardDocument): void {
    const indices = this._indices
    if (indices === null) {
      return
    }

    const order = indices.map((_index, itemIndex) => itemIndex).sort((a, b) => indices[a] - indices[b])
    for (const itemIndex of order) {
      const item = this._items[itemIndex]
      const index = indices[itemIndex]
      if (index < 0 || document.items.includes(item)) {
        continue
      }

      document.items.splice(Math.min(index, document.items.length), 0, item)
    }
  }
}

/** 组合命令（对应桌面 `CompositeCommand`）：正序执行、倒序撤销，整体原子。 */
export class CompositeCommand implements BoardCommand {
  private readonly _commands: readonly BoardCommand[]

  constructor(commands: readonly (BoardCommand | null | undefined)[]) {
    this._commands = commands.filter(
      (command): command is BoardCommand => command !== null && command !== undefined,
    )
  }

  /** 组合内的子命令（只读）。 */
  get commands(): readonly BoardCommand[] {
    return this._commands
  }

  do(document: BoardDocument): void {
    for (const command of this._commands) {
      command.do(document)
    }
  }

  undo(document: BoardDocument): void {
    for (let index = this._commands.length - 1; index >= 0; index -= 1) {
      this._commands[index].undo(document)
    }
  }
}

/** 清空文档全部墨迹条目（对应桌面 `ClearCommand`，构造时快照原内容）。 */
export class ClearCommand implements BoardCommand {
  private readonly _snapshot: readonly InkItem[]

  constructor(snapshot: readonly InkItem[]) {
    this._snapshot = [...snapshot]
  }

  do(document: BoardDocument): void {
    document.items.length = 0
  }

  undo(document: BoardDocument): void {
    document.items.length = 0
    document.items.push(...this._snapshot)
  }
}

/**
 * 元素命令（P2 选择与元素，对应桌面 Board/Commands/ 下的元素命令）。web 侧元素存 document.elements 单数组加 layer 字段（见 document.ts），桌面侧分 ElementsBelowInk 与 ElementsAboveInk 两个列表；下面这些命令把桌面的「层内下标」映射到单数组的绝对位置：层内顺序为同层元素在数组中的相对顺序，层序为先 belowInk 后 aboveInk。运行时数组顺序即 z 序，order 只是 WBIX 持久化字段，这些命令不维护它。
 */

/** 某层的元素个数（对应桌面某个列表的 `Count`）。 */
function layerElementCount(document: BoardDocument, layer: Layer): number {
  let count = 0
  for (const element of document.elements) {
    if (element.layer === layer) {
      count += 1
    }
  }
  return count
}

/** 某层的元素（保持数组顺序，即层内 z 序，对应桌面列表本身）。 */
function layerElements(document: BoardDocument, layer: Layer): BoardElement[] {
  return document.elements.filter((element) => element.layer === layer)
}

/** 从文档中按引用移除元素（不存在则忽略）。 */
function removeElementFromDocument(document: BoardDocument, element: BoardElement): void {
  const index = document.elements.indexOf(element)
  if (index >= 0) {
    document.elements.splice(index, 1)
  }
}

/**
 * 把元素插回指定层的层内下标处（index 越界时收敛到该层末尾）。该层为空时 belowInk 插到文档最前、aboveInk 插到文档最后，以保持「先 belowInk 组、后 aboveInk 组」的规范顺序（读盘与渲染都依赖它）。
 */
function insertElementIntoLayer(
  document: BoardDocument,
  element: BoardElement,
  layer: Layer,
  index: number,
): void {
  const clamped = Math.max(0, Math.min(index, layerElementCount(document, layer)))

  let seen = 0
  for (let absolute = 0; absolute < document.elements.length; absolute += 1) {
    if (document.elements[absolute].layer !== layer) {
      continue
    }

    if (seen === clamped) {
      document.elements.splice(absolute, 0, element)
      return
    }

    seen += 1
  }

  // 层内没有第 clamped 个元素：插到该层最后一个元素之后。
  let lastIndex = -1
  for (let absolute = document.elements.length - 1; absolute >= 0; absolute -= 1) {
    if (document.elements[absolute].layer === layer) {
      lastIndex = absolute
      break
    }
  }

  if (lastIndex >= 0) {
    document.elements.splice(lastIndex + 1, 0, element)
    return
  }

  if (layer === 'belowInk') {
    document.elements.unshift(element)
    return
  }

  document.elements.push(element)
}

/**
 * 把某层全部元素的 order 置为 -1，让 WBIX 写侧按层内下标重算。桌面 BringElementToFrontCommand 只移动列表位置、不更新 Order，而桌面 WbixWorkspaceSerializer.Save.cs:225 与 web persistence/wbix/page-payload.ts:405 都按 order < 0 ? 层内下标 : order 写出，桌面版「置顶 → 保存 → 重开」会保存过期 order（已知缺陷）；网页端数组顺序即权威 z 序，重排后把受影响层的 order 全部置 -1，保存结果始终等于真实层内顺序。
 */
function invalidateLayerOrder(document: BoardDocument, layer: Layer): void {
  for (const element of document.elements) {
    if (element.layer === layer) {
      element.order = -1
    }
  }
}

/** 添加元素（对应桌面 `AddElementCommand`，`AddElementCommand.cs`）。 */
export class AddElementCommand implements BoardCommand {
  private readonly _element: BoardElement
  private readonly _aboveInk: boolean
  private _index: number | null = null

  /**
   * @param element 待添加元素；`element.layer` 会被同步为实际插入层
   * @param aboveInk 是否插入到笔迹上层；缺省沿用 `element.layer`
   */
  constructor(element: BoardElement, aboveInk: boolean = element.layer === 'aboveInk') {
    this._element = element
    this._aboveInk = aboveInk
  }

  do(document: BoardDocument): void {
    const layer: Layer = this._aboveInk ? 'aboveInk' : 'belowInk'
    this._element.layer = layer
    // 首次执行记录层内插入位置；撤销后重做仍插回原位置（桌面 `_index ??=`，AddElementCommand.cs:16-21）。
    this._index ??= layerElementCount(document, layer)
    insertElementIntoLayer(document, this._element, layer, this._index)
  }

  undo(document: BoardDocument): void {
    // 桌面先按记录下标校验引用、否则按引用删除（AddElementCommand.cs:23-34）；元素对象唯一，两者等价。
    removeElementFromDocument(document, this._element)
  }
}

/** 把元素移到笔迹上层最上面（对应桌面 `BringElementToFrontCommand`）。 */
export class BringElementToFrontCommand implements BoardCommand {
  private readonly _element: BoardElement
  private _fromAboveInk: boolean | null = null
  private _fromIndex: number | null = null

  constructor(element: BoardElement) {
    this._element = element
  }

  do(document: BoardDocument): void {
    if (this._fromIndex === null || this._fromAboveInk === null) {
      // 首次执行：定位来源层与层内下标（BringElementToFrontCommand.cs:17-49）。
      const aboveGroup = layerElements(document, 'aboveInk')
      const aboveIndex = aboveGroup.indexOf(this._element)
      if (aboveIndex >= 0) {
        // 已经是上层最上面：不产生效果，也不记录撤销信息（BringElementToFrontCommand.cs:26-29）。
        if (aboveIndex === aboveGroup.length - 1) {
          return
        }

        this._fromAboveInk = true
        this._fromIndex = aboveIndex
        removeElementFromDocument(document, this._element)
        this._element.layer = 'aboveInk'
        insertElementIntoLayer(
          document,
          this._element,
          'aboveInk',
          layerElementCount(document, 'aboveInk'),
        )
        // 同层重排后 aboveInk 层内下标全部变化，order 置 -1 由写侧重算。
        invalidateLayerOrder(document, 'aboveInk')
        return
      }

      const belowIndex = layerElements(document, 'belowInk').indexOf(this._element)
      if (belowIndex < 0) {
        return
      }

      this._fromAboveInk = false
      this._fromIndex = belowIndex
      removeElementFromDocument(document, this._element)
      this._element.layer = 'aboveInk'
      insertElementIntoLayer(
        document,
        this._element,
        'aboveInk',
        layerElementCount(document, 'aboveInk'),
      )
      // 跨层移动：来源层与目标层的层内下标都变化，两层的 order 都置 -1。
      invalidateLayerOrder(document, 'belowInk')
      invalidateLayerOrder(document, 'aboveInk')
      return
    }

    // 重做：不依赖当前所在层，移除后追加到上层末尾（BringElementToFrontCommand.cs:51-55）。
    removeElementFromDocument(document, this._element)
    this._element.layer = 'aboveInk'
    insertElementIntoLayer(
      document,
      this._element,
      'aboveInk',
      layerElementCount(document, 'aboveInk'),
    )
    invalidateLayerOrder(document, 'aboveInk')
    if (this._fromAboveInk === false) {
      invalidateLayerOrder(document, 'belowInk')
    }
  }

  undo(document: BoardDocument): void {
    const fromIndex = this._fromIndex
    const fromAboveInk = this._fromAboveInk
    if (fromIndex === null || fromAboveInk === null) {
      return
    }

    removeElementFromDocument(document, this._element)
    const layer: Layer = fromAboveInk ? 'aboveInk' : 'belowInk'
    this._element.layer = layer
    insertElementIntoLayer(document, this._element, layer, fromIndex)
    // 撤销同样改变目标层与恢复层的层内下标。
    invalidateLayerOrder(document, 'aboveInk')
    invalidateLayerOrder(document, layer)
  }
}

/** 删除元素（对应桌面 `RemoveElementCommand`）。 */
export class RemoveElementCommand implements BoardCommand {
  private readonly _element: BoardElement
  private _fromAboveInk: boolean | null = null
  private _index: number | null = null

  constructor(element: BoardElement) {
    this._element = element
  }

  do(document: BoardDocument): void {
    if (this._index === null || this._fromAboveInk === null) {
      // 首次执行：按"先上层、后下层"顺序定位并记录来源（RemoveElementCommand.cs:16-36）。
      if (this._tryRemoveFromLayer(document, 'aboveInk')) {
        return
      }

      this._tryRemoveFromLayer(document, 'belowInk')
      return
    }

    // 重做：按记录层与层内下标删除，否则按引用兜底（RemoveElementCommand.cs:38-49）。
    const layer: Layer = this._fromAboveInk ? 'aboveInk' : 'belowInk'
    const group = layerElements(document, layer)
    const recorded = this._index
    if (recorded >= 0 && recorded < group.length && group[recorded] === this._element) {
      document.elements.splice(document.elements.indexOf(this._element), 1)
      return
    }

    removeElementFromDocument(document, this._element)
  }

  undo(document: BoardDocument): void {
    const index = this._index
    const fromAboveInk = this._fromAboveInk
    if (index === null || fromAboveInk === null) {
      return
    }

    // 桌面：元素已在任一列表则跳过（RemoveElementCommand.cs:58-61）。
    if (document.elements.includes(this._element)) {
      return
    }

    const layer: Layer = fromAboveInk ? 'aboveInk' : 'belowInk'
    this._element.layer = layer
    insertElementIntoLayer(document, this._element, layer, index)
  }

  /** 尝试从指定层移除并记录层内下标；不在该层返回 false。 */
  private _tryRemoveFromLayer(document: BoardDocument, layer: Layer): boolean {
    const index = layerElements(document, layer).indexOf(this._element)
    if (index < 0) {
      return false
    }

    this._fromAboveInk = layer === 'aboveInk'
    this._index = index
    removeElementFromDocument(document, this._element)
    return true
  }
}

/** 更新元素位置/尺寸（对应桌面 `UpdateElementTransformCommand`）。 */
export class UpdateElementTransformCommand implements BoardCommand {
  private readonly _element: BoardElement
  private readonly _beforePosition: Vec2
  private readonly _afterPosition: Vec2
  private readonly _beforeSize: Vec2 | null
  private readonly _afterSize: Vec2 | null

  /**
   * @param beforeSizeWorld 不传则撤销时不改尺寸（对应桌面 `Vector2?` 可空参数）
   * @param afterSizeWorld 不传则执行时不改尺寸
   */
  constructor(
    element: BoardElement,
    beforePositionWorld: Vec2,
    afterPositionWorld: Vec2,
    beforeSizeWorld?: Vec2,
    afterSizeWorld?: Vec2,
  ) {
    this._element = element
    // 桌面 Vector2 是值类型，赋值即拷贝；web 侧显式拷贝，避免调用方后续修改向量影响命令历史。
    this._beforePosition = { x: beforePositionWorld.x, y: beforePositionWorld.y }
    this._afterPosition = { x: afterPositionWorld.x, y: afterPositionWorld.y }
    this._beforeSize =
      beforeSizeWorld === undefined ? null : { x: beforeSizeWorld.x, y: beforeSizeWorld.y }
    this._afterSize =
      afterSizeWorld === undefined ? null : { x: afterSizeWorld.x, y: afterSizeWorld.y }
  }

  do(_document: BoardDocument): void {
    // 桌面只改元素自身字段，不使用 document（UpdateElementTransformCommand.cs:25-33）。
    this._element.positionWorld = { x: this._afterPosition.x, y: this._afterPosition.y }
    if (this._afterSize !== null) {
      this._element.sizeWorld = { x: this._afterSize.x, y: this._afterSize.y }
    }
  }

  undo(_document: BoardDocument): void {
    this._element.positionWorld = { x: this._beforePosition.x, y: this._beforePosition.y }
    if (this._beforeSize !== null) {
      this._element.sizeWorld = { x: this._beforeSize.x, y: this._beforeSize.y }
    }
  }
}

/**
 * 把一批笔迹条目与元素一次性加入文档（桌面选择 Dock 的「复制」同款）。桌面在 OnSelectionDuplicateClicked 里逐条构造 AddInkItemCommand 与 AddElementCommand，再按「1 条直接执行、多条包 CompositeCommand」合并（BoardCanvasControl.Rendering.cs:554-560）；返回 null 表示没有要添加的内容，调用方据此跳过 execute。
 */
export function createAddItemsCommand(
  inkItems: readonly InkItem[] = [],
  elements: readonly BoardElement[] = [],
): BoardCommand | null {
  const commands: BoardCommand[] = []

  for (const item of inkItems) {
    commands.push(new AddInkItemCommand(item))
  }

  for (const element of elements) {
    // AddElementCommand 缺省按 element.layer 决定插入层，与桌面 TryCloneElement 的 aboveInk 判定等价。
    commands.push(new AddElementCommand(element))
  }

  if (commands.length === 0) {
    return null
  }

  return commands.length === 1 ? commands[0] : new CompositeCommand(commands)
}

/** 形状两点几何快照（对应桌面 `UpdateShapeGeometryCommand` 的 `(Vector2 Start, Vector2 End)` 元组）。 */
export interface ShapeGeometry {
  readonly start: Vec2
  readonly end: Vec2
}

/** 深拷贝单个采样点（桌面 `StrokePoint` 是 record struct，赋值即值拷贝）。 */
function copyStrokePoint(point: StrokePoint): StrokePoint {
  return { x: point.x, y: point.y, pressure: point.pressure }
}

/** 深拷贝二维向量（桌面 `Vector2` 是 struct，赋值即值拷贝）。 */
function copyVec2(point: Vec2): Vec2 {
  return { x: point.x, y: point.y }
}

/** 深拷贝形状两点几何（桌面元组是 struct 值拷贝）。 */
function copyShapeGeometry(geometry: ShapeGeometry): ShapeGeometry {
  return { start: copyVec2(geometry.start), end: copyVec2(geometry.end) }
}

/** 原地写入点列（对应桌面 `UpdateStrokePointsCommand.Apply`，`UpdateStrokePointsCommand.cs:37-42`）。 */
function applyStrokePoints(stroke: Stroke, points: readonly StrokePoint[]): void {
  stroke.points.length = 0
  for (const point of points) {
    stroke.points.push(copyStrokePoint(point))
  }
}

/**
 * 用点列快照更新某条笔迹（对应桌面 UpdateStrokePointsCommand，Commands/UpdateStrokePointsCommand.cs），用于选择工具对笔迹做平移、缩放、旋转与属性面板精调。命令原地改同一个 Stroke 对象（选中集与渲染缓存按对象引用持有它），do 写 after、undo 写 before。
 * 与桌面的差异：桌面 Apply 末尾调 Stroke.RecalculateBoundsFromPoints() 刷新包围盒缓存，web 的包围盒按需计算（ink/geometry.ts 的 getStrokeBounds），没有缓存需要刷新。
 */
export class UpdateStrokePointsCommand implements BoardCommand {
  private readonly _stroke: Stroke
  private readonly _before: readonly StrokePoint[]
  private readonly _after: readonly StrokePoint[]

  constructor(stroke: Stroke, before: readonly StrokePoint[], after: readonly StrokePoint[]) {
    this._stroke = stroke
    // 桌面 `new List<StrokePoint>(before/after)` 是值拷贝；web 无 struct 语义，必须逐点深拷贝，
    // 否则命令历史会与活动笔迹共享点对象，下一次拖动会污染撤销数据。
    this._before = before.map(copyStrokePoint)
    this._after = after.map(copyStrokePoint)
  }

  do(_document: BoardDocument): void {
    applyStrokePoints(this._stroke, this._after)
  }

  undo(_document: BoardDocument): void {
    applyStrokePoints(this._stroke, this._before)
  }
}

/**
 * 用两点几何快照更新某个形状（对应桌面 UpdateShapeGeometryCommand，Commands/UpdateShapeGeometryCommand.cs），与 UpdateStrokePointsCommand 同构：do 写 after、undo 写 before，原地改同一个 Shape 对象。
 * 与桌面的差异：桌面 Apply 调 BoardShape.SetGeometry（内部重算 Bounds 缓存），web 的包围盒按需计算（ink/geometry.ts 的 getShapeBounds），直接写 start 与 end 即可。
 */
export class UpdateShapeGeometryCommand implements BoardCommand {
  private readonly _shape: Shape
  private readonly _before: ShapeGeometry
  private readonly _after: ShapeGeometry

  constructor(shape: Shape, before: ShapeGeometry, after: ShapeGeometry) {
    this._shape = shape
    // 桌面元组是 struct 值拷贝；web 侧深拷贝 Vec2，避免与调用方共享对象。
    this._before = copyShapeGeometry(before)
    this._after = copyShapeGeometry(after)
  }

  do(_document: BoardDocument): void {
    this._apply(this._after)
  }

  undo(_document: BoardDocument): void {
    this._apply(this._before)
  }

  private _apply(geometry: ShapeGeometry): void {
    // 对应桌面 `BoardShape.SetGeometry`（`BoardShape.cs:98-103`）：写入两点几何并重算包围盒。
    this._shape.start = copyVec2(geometry.start)
    this._shape.end = copyVec2(geometry.end)
  }
}
