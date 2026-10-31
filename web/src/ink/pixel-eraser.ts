import {
  distanceSquaredPointToSegment,
  distanceSquaredSegmentToSegment,
  getEraserCapsuleInv,
  getMaxHalfStrokeWidth,
  getStrokeHalfWidth,
  isItemHitByEraserSegment,
  scalePointByAxes,
} from './hit-test.ts'
import type { Stroke, StrokePoint, Vec2 } from '../board/model/stroke.ts'
import { STROKE_KIND } from '../board/model/stroke.ts'
import type { InkItem } from '../board/model/workspace.ts'

/**
 * 像素级（局部）擦除（纯函数，无 DOM / React 依赖）。
 *
 * 事实源：`WindBoard/Board/Editing/PixelStrokeEraser.cs`（439 行）——逐条移植，不做简化。
 *
 * 算法要点（与桌面一致）：
 * 1. 先用命中测试粗筛（`hit-test.ts` 的 `isItemHitByEraserSegment`），不命中则原样保留；
 * 2. 形状等不可分割条目命中即整条删除；折线笔迹进入“逐段采样 + 边界二分”的裁剪流程；
 * 3. 折线裁剪按线段采样：靠近橡皮擦的线段按 0.6 世界单位步长采样（每段上限 256 个样本），
 *    远离的线段只采样终点（1 个样本）；
 * 4. 采样点“保留/擦除”状态发生翻转时，用 10 次二分求出**保留侧**的边界点插入笔迹，
 *    从而让裁剪边界贴合橡皮擦轮廓，而不是贴到采样点网格上；
 * 5. 保留的连续片段各自生成一条派生笔迹（新对象），原笔迹不被修改。
 *
 * 已知的桌面行为（照搬，不“修”）：
 * - 采样上限 256 意味着**极长线段上的小橡皮擦可能漏擦**：步长被拉长到 `len / 256` 后，
 *   橡皮擦可能整个落在两个采样点之间；此时命中测试会判定命中，但裁剪流程没有采样点被擦除，
 *   于是 `removedAny` 保持 false、笔迹原样返回，`changed` 为 false（桌面同样如此）。
 * - 采样从 `t = 1 / samples` 开始（跳过 `t = 0`，避免与上一段末点重复），到 `t = 1` 结束。
 */

/** 近邻线段采样步长（世界单位；桌面 `NearSegmentSampleStepWorld`，`PixelStrokeEraser.cs:36`）。 */
const NEAR_SEGMENT_SAMPLE_STEP_WORLD = 0.6

/** 单段最大采样数（桌面 `MaxSamplesPerSegment`，`PixelStrokeEraser.cs:39`）。 */
const MAX_SAMPLES_PER_SEGMENT = 256

/** 擦除边界二分迭代次数（桌面 `BoundarySearchIterations`，`PixelStrokeEraser.cs:42`）。 */
const BOUNDARY_SEARCH_ITERATIONS = 10

/**
 * 采样步长的下限（桌面 `Math.Max(0.0001f, NearSegmentSampleStepWorld)` 的除零保护）。
 * 步长常量本身是 0.6，此处只保留原式的语义。
 */
const MIN_SAMPLE_STEP_WORLD = 0.0001

/**
 * 相邻点去重阈值（桌面 `AddPointWithBounds` 的 `0.0000001f`）：
 * 派生笔迹末点与新点距离平方不大于它时跳过，避免压出重复点。
 */
const DUPLICATE_POINT_DISTANCE_SQUARED = 1e-7

/** 归一化空间里的命中阈值：距离平方 `<= 1` 即被擦除（桌面 `<= 1.0f`）。 */
const CAPSULE_HIT_DISTANCE_SQUARED = 1

/** 橡皮擦轨迹的胶囊几何（一条线段 + X/Y 半径，世界坐标）。 */
interface EraserCapsule {
  fromWorld: Vec2
  toWorld: Vec2
  radiusWorld: Vec2
}

/** 擦除结果：`changed` 为 false 时 `items` 与入参逐项同引用且同序。 */
export interface EraseResult {
  changed: boolean
  items: InkItem[]
}

/**
 * 擦除裁剪过程的可变状态（对应桌面 `EraseSingleStroke` 内的局部变量：
 * `state.Current` / `state.Result` / `state.RemovedAny` / `state.PrevSample` / `state.PrevKeep`）。
 */
interface StrokeEraseState {
  /** 被裁剪的源笔迹（只读，不修改）。 */
  source: Stroke
  /** 本次擦除的胶囊几何。 */
  eraser: EraserCapsule
  /** 正在累积的“保留片段”派生笔迹；null 表示当前不在保留区间内。 */
  current: Stroke | null
  /** 已完成的保留片段（顺序与原始笔迹方向一致）。 */
  result: Stroke[]
  /** 是否至少擦除过一个采样点（决定是否真的产生了变化）。 */
  removedAny: boolean
  /** 上一个采样点（边界二分的起点）。 */
  prevSample: StrokePoint
  /** 上一个采样点是否保留。 */
  prevKeep: boolean
}

/**
 * 单点是否被擦除（桌面 `IsPointErased`，`PixelStrokeEraser.cs:403-420`）。
 *
 * 关键口径：半宽取**该采样点自身的压感**（不是两端平均），扩展半径
 * `r = max(0, radiusWorld) + halfWidth`，逐轴归一化后做点-线段距离判定。
 */
function isPointErased(stroke: Stroke, point: StrokePoint, eraser: EraserCapsule): boolean {
  const inv = getEraserCapsuleInv(eraser.radiusWorld, getStrokeHalfWidth(stroke, point.pressure))
  const distanceSquared = distanceSquaredPointToSegment(
    scalePointByAxes(point, inv),
    scalePointByAxes(eraser.fromWorld, inv),
    scalePointByAxes(eraser.toWorld, inv),
  )
  return distanceSquared <= CAPSULE_HIT_DISTANCE_SQUARED
}

/**
 * 线段是否“靠近”橡皮擦（桌面 `IsSegmentNearEraser`，`PixelStrokeEraser.cs:214-227`）。
 *
 * 用**最大可能半宽**做归一化粗筛（`maxInv`），因此这是“可能被擦除”的上界：
 * 粗筛通过才值得细采样，粗筛不通过则整段只需要 1 个采样点。
 */
function isSegmentNearEraser(
  start: StrokePoint,
  end: StrokePoint,
  eraser: EraserCapsule,
  maxInv: Vec2,
): boolean {
  const distanceSquared = distanceSquaredSegmentToSegment(
    scalePointByAxes(eraser.fromWorld, maxInv),
    scalePointByAxes(eraser.toWorld, maxInv),
    scalePointByAxes(start, maxInv),
    scalePointByAxes(end, maxInv),
  )
  return distanceSquared <= CAPSULE_HIT_DISTANCE_SQUARED
}

/**
 * 计算线段采样数（桌面 `GetSegmentSamplesCount`，`PixelStrokeEraser.cs:229-240`）。
 *
 * - 不靠近橡皮擦：1（只采终点）；
 * - 靠近：`ceil(长度 / 0.6)`，下限 2，上限 256。
 *
 * 注意：上限 256 是硬上限，长线段会被拉长步长（见文件头“已知的桌面行为”）。
 */
function getSegmentSamplesCount(start: StrokePoint, end: StrokePoint, isNear: boolean): number {
  if (!isNear) {
    return 1
  }

  const dx = end.x - start.x
  const dy = end.y - start.y
  const length = Math.sqrt(dx * dx + dy * dy)
  const samples = Math.ceil(length / Math.max(MIN_SAMPLE_STEP_WORLD, NEAR_SEGMENT_SAMPLE_STEP_WORLD))
  return Math.min(MAX_SAMPLES_PER_SEGMENT, Math.max(2, samples))
}

/**
 * 采样点线性插值（桌面 `Lerp`，`PixelStrokeEraser.cs:396-401`）。
 *
 * 位置按 `a + (b - a) · t` 插值（与 `Vector2.Lerp` 同式），
 * **压感同样线性插值**——这是派生笔迹上新增点的压感来源。
 */
function lerpStrokePoint(start: StrokePoint, end: StrokePoint, t: number): StrokePoint {
  return {
    x: start.x + (end.x - start.x) * t,
    y: start.y + (end.y - start.y) * t,
    pressure: start.pressure + (end.pressure - start.pressure) * t,
  }
}

/**
 * 创建派生笔迹（桌面 `CreateDerivedStroke`，`PixelStrokeEraser.cs:322-330`）。
 *
 * 只继承 `colorRgba` / `baseSize` / `enablePressure` 三项；`points` 从空开始。
 * 桌面 `Color` 是值类型，这里浅拷贝一份，避免与原笔迹共享同一个可变对象。
 */
function createDerivedStroke(source: Stroke): Stroke {
  return {
    kind: STROKE_KIND,
    points: [],
    colorRgba: { ...source.colorRgba },
    baseSize: source.baseSize,
    enablePressure: source.enablePressure,
  }
}

/**
 * 追加一个点，并跳过与末点几乎重合的点（桌面 `AddPointWithBounds`，`PixelStrokeEraser.cs:332-346`）。
 *
 * 桌面在追加时同步扩包围盒；web 侧 `Stroke` 不缓存包围盒（由 `geometry.ts` 的
 * `getStrokeBounds` 按需计算），所以这里只需做去重与追加。
 */
function appendPointIfDistinct(stroke: Stroke, point: StrokePoint): void {
  const points = stroke.points
  if (points.length > 0) {
    const last = points[points.length - 1]
    const dx = point.x - last.x
    const dy = point.y - last.y
    if (dx * dx + dy * dy <= DUPLICATE_POINT_DISTANCE_SQUARED) {
      return
    }
  }
  points.push(point)
}

/**
 * 二分求“保留侧”的边界点（桌面 `FindBoundaryOutside`，`PixelStrokeEraser.cs:348-394`）。
 *
 * 已知 `start` 处状态为 `keepAtStart`、`end` 处状态相反，做 10 次二分后取
 * **保留侧**那一端的 `t`，因此插入的边界点仍在保留区间内（宁多留、不误擦）。
 */
function findBoundaryOutside(
  stroke: Stroke,
  start: StrokePoint,
  end: StrokePoint,
  keepAtStart: boolean,
  eraser: EraserCapsule,
): StrokePoint {
  let low = 0
  let high = 1

  for (let iteration = 0; iteration < BOUNDARY_SEARCH_ITERATIONS; iteration += 1) {
    const mid = (low + high) / 2
    const point = lerpStrokePoint(start, end, mid)
    const keep = !isPointErased(stroke, point, eraser)
    if (keepAtStart) {
      // 起点保留：保留段在左，向 high 收缩。
      if (keep) {
        low = mid
      } else {
        high = mid
      }
    } else {
      // 起点被擦：保留段在右，向 low 收缩。
      if (keep) {
        high = mid
      } else {
        low = mid
      }
    }
  }

  const t = keepAtStart ? low : high
  return lerpStrokePoint(start, end, t)
}

/** 收尾当前保留片段（桌面 `CommitCurrentIfNotEmpty`，`PixelStrokeEraser.cs:307-320`）。 */
function commitCurrentIfNotEmpty(state: StrokeEraseState): void {
  if (state.current === null) {
    return
  }

  if (state.current.points.length > 0) {
    state.result.push(state.current)
  }
  state.current = null
}

/** “保留 → 擦除”翻转：把边界点补进当前片段并收尾（桌面 `CommitKeptToErasedTransition`，`PixelStrokeEraser.cs:289-298`）。 */
function commitKeptToErasedTransition(state: StrokeEraseState, boundary: StrokePoint): void {
  if (state.current === null) {
    return
  }

  appendPointIfDistinct(state.current, boundary)
  commitCurrentIfNotEmpty(state)
}

/** “擦除 → 保留”翻转：新开片段并写入边界点与当前采样点（桌面 `StartKeptAfterErasedTransition`，`PixelStrokeEraser.cs:300-305`）。 */
function startKeptAfterErasedTransition(
  state: StrokeEraseState,
  boundary: StrokePoint,
  sample: StrokePoint,
): void {
  if (state.current === null) {
    state.current = createDerivedStroke(state.source)
  }

  appendPointIfDistinct(state.current, boundary)
  appendPointIfDistinct(state.current, sample)
}

/**
 * 处理单个采样点（桌面 `ProcessSample`，`PixelStrokeEraser.cs:242-287`）。
 *
 * 状态未翻转时：保留则把采样点追加到当前片段，擦除则什么都不做（但仍记录 `removedAny`）。
 * 状态翻转时：先二分出边界点，再按方向提交 / 新开片段。
 */
function processSample(state: StrokeEraseState, sample: StrokePoint): void {
  const keep = !isPointErased(state.source, sample, state.eraser)
  if (!keep) {
    state.removedAny = true
  }

  if (keep === state.prevKeep) {
    if (keep && state.current !== null) {
      appendPointIfDistinct(state.current, sample)
    }
    state.prevSample = sample
    state.prevKeep = keep
    return
  }

  const boundary = findBoundaryOutside(state.source, state.prevSample, sample, state.prevKeep, state.eraser)
  if (state.prevKeep) {
    commitKeptToErasedTransition(state, boundary)
  } else {
    startKeptAfterErasedTransition(state, boundary, sample)
  }

  state.prevSample = sample
  state.prevKeep = keep
}

/**
 * 逐段采样整条笔迹（桌面 `ProcessStrokeSegments`，`PixelStrokeEraser.cs:183-212`）。
 *
 * `t` 从 `1 / samples` 递增到 `1`（**跳过 `t = 0`**，避免与上一段末点重复），
 * 所以每段的终点总会被采样一次。
 */
function processStrokeSegments(state: StrokeEraseState, maxInv: Vec2): void {
  const points = state.source.points
  for (let index = 1; index < points.length; index += 1) {
    const start = points[index - 1]
    const end = points[index]
    const isNear = isSegmentNearEraser(start, end, state.eraser, maxInv)
    const samples = getSegmentSamplesCount(start, end, isNear)

    for (let sampleIndex = 1; sampleIndex <= samples; sampleIndex += 1) {
      const t = sampleIndex / samples
      processSample(state, lerpStrokePoint(start, end, t))
    }
  }
}

/**
 * 裁剪单点笔迹（桌面 `EraseSinglePointStroke`，`PixelStrokeEraser.cs:161-166`）：
 * 点被擦除则整条消失，否则原样返回。
 */
function eraseSinglePointStroke(stroke: Stroke, eraser: EraserCapsule): Stroke[] {
  const point = stroke.points[0]
  return isPointErased(stroke, point, eraser) ? [] : [stroke]
}

/**
 * 裁剪一条折线笔迹（桌面 `EraseSingleStroke`，`PixelStrokeEraser.cs:94-141`）。
 *
 * 返回保留的片段序列：
 * - 完全没有采样点被擦除时返回 `[stroke]`（**同一个引用**，调用方据此判定“无变化”）；
 * - 否则返回 0..N 条派生笔迹。
 */
function eraseSingleStroke(stroke: Stroke, eraser: EraserCapsule): Stroke[] {
  const points = stroke.points
  if (points.length === 0) {
    return [stroke]
  }
  if (points.length === 1) {
    return eraseSinglePointStroke(stroke, eraser)
  }

  // 粗筛用的“最大橡皮擦逆半径”（桌面 `ComputeMaxEraserInvWorld`，`PixelStrokeEraser.cs:168-181`）：
  // 用忽略压感的最大半宽，保证这是上界，粗筛不会漏掉真正被擦除的线段。
  const maxInv = getEraserCapsuleInv(eraser.radiusWorld, getMaxHalfStrokeWidth(stroke))

  const first = points[0]
  const firstKeep = !isPointErased(stroke, first, eraser)
  const state: StrokeEraseState = {
    source: stroke,
    eraser,
    current: null,
    result: [],
    removedAny: !firstKeep,
    prevSample: first,
    prevKeep: firstKeep,
  }

  if (firstKeep) {
    state.current = createDerivedStroke(stroke)
    appendPointIfDistinct(state.current, first)
  }

  processStrokeSegments(state, maxInv)
  commitCurrentIfNotEmpty(state)

  if (!state.removedAny) {
    return [stroke]
  }
  return state.result
}

/**
 * 用一条“橡皮擦轨迹线段 + 椭圆半径”擦除一批条目（桌面 `PixelStrokeEraser.Erase`，`PixelStrokeEraser.cs:44-92`）。
 *
 * 行为：
 * - 折线笔迹：按采样 + 二分裁剪成若干派生片段（可能为 0 段）；
 * - 形状等不可分割条目：命中即整条删除；
 * - 完全未命中时 `changed` 为 false，`items` 与入参**逐项同引用**且同序。
 *
 * 纯函数：入参数组与其中的笔迹都不会被修改。
 */
export function eraseItemsWithCapsule(
  items: readonly InkItem[],
  fromWorld: Vec2,
  toWorld: Vec2,
  radiusWorld: Vec2,
): EraseResult {
  const rebuilt: InkItem[] = []
  if (items.length === 0) {
    return { changed: false, items: rebuilt }
  }

  const eraser: EraserCapsule = { fromWorld, toWorld, radiusWorld }
  let changed = false

  for (const item of items) {
    if (!isItemHitByEraserSegment(item, fromWorld, toWorld, radiusWorld)) {
      rebuilt.push(item)
      continue
    }

    if (item.kind !== STROKE_KIND) {
      // 形状等不可分割条目：命中即整条删除（桌面 `Erase` 的非 Stroke 分支）。
      changed = true
      continue
    }

    const keptSegments = eraseSingleStroke(item, eraser)
    if (keptSegments.length === 1 && keptSegments[0] === item) {
      // 裁剪后与原笔迹同引用：说明没有实际擦除，保持原样且不标记变化。
      rebuilt.push(item)
      continue
    }

    changed = true
    for (const segment of keptSegments) {
      rebuilt.push(segment)
    }
  }

  return { changed, items: rebuilt }
}
