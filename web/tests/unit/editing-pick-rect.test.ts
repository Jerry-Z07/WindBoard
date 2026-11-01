/**
 * 选择工具三层命中判定的单元测试：
 * - 条目包围盒（`src/board/editing/ink-bounds.ts` ← `InkItemScreenBounds.cs`）；
 * - 点选命中（`src/board/editing/pick-test.ts` ← `InkItemPickTest.cs`）；
 * - 框选相交（`src/board/editing/rect-select.ts` ← `InkItemRectSelectTest.cs`）。
 *
 * 期望值均按桌面公式手工推导；每个用例的注释标出对应的桌面分支。
 */

import { describe, expect, it } from 'vitest'
import {
  getInkItemsScreenBounds,
} from '../../src/board/editing/ink-bounds.ts'
import {
  hitTestTopMostInkItem,
  isInkItemHitByPoint,
  isShapeHitByPoint,
  isStrokeHitByPoint,
} from '../../src/board/editing/pick-test.ts'
import {
  hitTestInkItemsInWorldRect,
  hitTestTopMostInkItemInWorldRect,
  isInkItemIntersectWorldRect,
  isStrokeIntersectWorldRect,
} from '../../src/board/editing/rect-select.ts'
import { STROKE_KIND } from '../../src/board/model/stroke.ts'
import type { ShapeKind } from '../../src/board/model/shape.ts'
import type { Shape } from '../../src/board/model/shape.ts'
import type { Stroke, Vec2 } from '../../src/board/model/stroke.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import { BoardViewport } from '../../src/board/viewport.ts'

const COLOR = { x: 0, y: 0, z: 0, w: 1 }

/** 构造笔迹：`points` 为 `[x, y, pressure]` 三元组。 */
function createStroke(
  points: ReadonlyArray<readonly [number, number, number]>,
  baseSize = 3,
  enablePressure = true,
): Stroke {
  return {
    kind: STROKE_KIND,
    points: points.map(([x, y, pressure]) => ({ x, y, pressure })),
    colorRgba: { ...COLOR },
    baseSize,
    enablePressure,
  }
}

/** 构造形状。 */
function createShape(kind: ShapeKind, start: Vec2, end: Vec2, width = 3): Shape {
  return { kind, start, end, colorRgba: { ...COLOR }, width }
}

/** 构造视口：`screen = (world − camera) · zoom + sizeDip / 2`。 */
function createViewport(zoom: number, camera: Vec2, sizeDip: Vec2): BoardViewport {
  const viewport = new BoardViewport()
  viewport.updateViewportSize(sizeDip)
  viewport.setView(camera, zoom)
  return viewport
}

function point(x: number, y: number): Vec2 {
  return { x, y }
}

describe('getInkItemsScreenBounds（InkItemScreenBounds.cs）', () => {
  // zoom=2、camera=(0,0)、size=(100,100)：screen = world * 2 + 50
  const viewport = createViewport(2, point(0, 0), point(100, 100))

  it('空列表返回 null（:35-38）', () => {
    expect(getInkItemsScreenBounds([], viewport)).toBeNull()
  })

  it('全部条目无有效包围盒时返回 null（:78-81）', () => {
    // 空点集笔迹在 :92-97 被跳过，hasAny 始终为 false。
    expect(getInkItemsScreenBounds([createStroke([])], viewport)).toBeNull()
  })

  it('单点笔迹按自身半宽外扩后变换到屏幕（:92-113）', () => {
    // baseSize=3、pressure=1 → 半宽 1.5 → 世界 [-1.5,1.5] → 屏幕 [47,53]。
    const bounds = getInkItemsScreenBounds([createStroke([[0, 0, 1]])], viewport)
    expect(bounds).toEqual({ min: { x: 47, y: 47 }, max: { x: 53, y: 53 } })
  })

  it('多点笔迹取包围盒（:92-113）', () => {
    // 世界 x∈[-1.5,11.5]、y∈[-1.5,1.5] → 屏幕 x∈[47,73]、y∈[47,53]。
    const bounds = getInkItemsScreenBounds([createStroke([[0, 0, 1], [10, 0, 1]])], viewport)
    expect(bounds).toEqual({ min: { x: 47, y: 47 }, max: { x: 73, y: 53 } })
  })

  it('形状按 getShapeBounds 的外扩口径（:118-124）', () => {
    // 与桌面 BoardShape.RecalculateBounds 一致：半宽 max(0.25, width/2) = 1.5。
    const bounds = getInkItemsScreenBounds(
      [createShape('line', point(0, 0), point(10, 0))],
      viewport,
    )
    expect(bounds).toEqual({ min: { x: 47, y: 47 }, max: { x: 73, y: 53 } })
  })

  it('多个条目取并集（:56-63）', () => {
    const items: InkItem[] = [
      createStroke([[0, 0, 1]]),
      createShape('rect', point(20, 20), point(30, 30)),
    ]
    // 形状世界 x∈[18.5,31.5] → 屏幕 [87,113]。
    expect(getInkItemsScreenBounds(items, viewport)).toEqual({
      min: { x: 47, y: 47 },
      max: { x: 113, y: 113 },
    })
  })

  it('跳过空点集笔迹但保留其它条目（:92-97）', () => {
    const items: InkItem[] = [createStroke([]), createStroke([[0, 0, 1]])]
    expect(getInkItemsScreenBounds(items, viewport)).toEqual({
      min: { x: 47, y: 47 },
      max: { x: 53, y: 53 },
    })
  })

  it('相机平移与缩放参与变换（:50-53）', () => {
    // zoom=1、camera=(10,10)、size=(100,100)：screen = world − 10 + 50 = world + 40。
    const shifted = createViewport(1, point(10, 10), point(100, 100))
    expect(getInkItemsScreenBounds([createStroke([[0, 0, 1]])], shifted)).toEqual({
      min: { x: 38.5, y: 38.5 },
      max: { x: 41.5, y: 41.5 },
    })
  })
})

describe('pick-test（InkItemPickTest.cs）', () => {
  describe('hitTestTopMostInkItem（:23-38）', () => {
    it('空列表返回 null', () => {
      expect(hitTestTopMostInkItem([], point(0, 0), 0)).toBeNull()
    })

    it('无命中返回 null', () => {
      expect(hitTestTopMostInkItem([createStroke([[0, 0, 1]])], point(100, 100), 0)).toBeNull()
    })

    it('反向遍历：重叠时返回最上层（列表末尾）', () => {
      const lower = createStroke([[0, 0, 1]])
      const upper = createStroke([[0, 0, 1]])
      expect(hitTestTopMostInkItem([lower, upper], point(0, 0), 0)).toBe(upper)
    })

    it('跳过不命中项后返回命中的那一层', () => {
      const hit = createStroke([[0, 0, 1]])
      const miss = createStroke([[50, 50, 1]])
      expect(hitTestTopMostInkItem([hit, miss], point(0, 0), 0)).toBe(hit)
    })
  })

  describe('isShapeHitByPoint（:67-77）', () => {
    // 同一条 (0,0)→(10,10) 线段：Line/Arrow 走距离判定，Rect/Ellipse 走 AABB。
    // width=3 → 半宽 1.5；点 (0,10) 到线段距离 10/√2≈7.07 > 1.5，但在 AABB [-1.5,11.5]² 内。
    const shapeKindCases: ReadonlyArray<{
      kind: ShapeKind
      point: Vec2
      expected: boolean
      note: string
    }> = [
      { kind: 'line', point: point(0, 10), expected: false, note: 'Line：斜线远离线段' },
      { kind: 'arrow', point: point(0, 10), expected: false, note: 'Arrow：与 Line 同口径' },
      { kind: 'rect', point: point(0, 10), expected: true, note: 'Rect：AABB 内即命中' },
      { kind: 'ellipse', point: point(0, 10), expected: true, note: 'Ellipse：AABB 内即命中' },
      { kind: 'line', point: point(5, 5), expected: true, note: 'Line：点在线段上' },
      { kind: 'rect', point: point(5, 5), expected: true, note: 'Rect：内部命中' },
      { kind: 'ellipse', point: point(5, 5), expected: true, note: 'Ellipse：内部命中' },
      { kind: 'line', point: point(5, 7), expected: true, note: 'Line：距离 1.414 ≤ 1.5' },
      { kind: 'line', point: point(5, 7.2), expected: false, note: 'Line：距离 1.556 > 1.5' },
    ]

    for (const testCase of shapeKindCases) {
      it(`${testCase.kind} @ (${testCase.point.x}, ${testCase.point.y}) → ${testCase.expected}（${testCase.note}）`, () => {
        const shape = createShape(testCase.kind, point(0, 0), point(10, 10))
        expect(isShapeHitByPoint(shape, testCase.point, 0)).toBe(testCase.expected)
      })
    }

    it('Arrow 忽略箭头翼：翼尖附近按线段距离判定（:69-74）', () => {
      // (0,0)→(10,0) 的箭头翼长约 3×3=9、±30°，翼尖约在 (2.2, ±4.5)；
      // 桌面只做点到线段距离，故翼尖处不命中（半宽 1.5 < 4.5）。
      const arrow = createShape('arrow', point(0, 0), point(10, 0))
      expect(isShapeHitByPoint(arrow, point(2.2, 4.5), 0)).toBe(false)
      expect(isShapeHitByPoint(arrow, point(2.2, 1.4), 0)).toBe(true)
    })

    it('Rect 容差边界：贴容差内命中、贴容差外不命中（:82-94）', () => {
      // AABB 为 [-1.5,11.5]²，容差 0.5 → 右边界 12.0。
      const rect = createShape('rect', point(0, 0), point(10, 10))
      expect(isShapeHitByPoint(rect, point(12, 5), 0.5)).toBe(true)
      expect(isShapeHitByPoint(rect, point(12.0001, 5), 0.5)).toBe(false)
    })

    it('Rect 负容差按 0 处理（:89 `Math.Max(0.0f, toleranceWorld)`）', () => {
      const rect = createShape('rect', point(0, 0), point(10, 10))
      expect(isShapeHitByPoint(rect, point(11.5, 5), -5)).toBe(true)
      expect(isShapeHitByPoint(rect, point(12, 5), -5)).toBe(false)
    })
  })

  describe('isStrokeHitByPoint（:105-152）', () => {
    it('空点集不命中（:109-112）', () => {
      expect(isStrokeHitByPoint(createStroke([]), point(0, 0), 8)).toBe(false)
    })

    it('单点笔迹按圆点判定（:128-134）', () => {
      // baseSize=3 → 半宽 1.5；容差 0.5 → 半径 2。
      const stroke = createStroke([[5, 5, 1]])
      expect(isStrokeHitByPoint(stroke, point(7, 5), 0.5)).toBe(true)
      expect(isStrokeHitByPoint(stroke, point(7.0001, 5), 0.5)).toBe(false)
    })

    it('单点笔迹用该点自身压感算半径（:131）', () => {
      // baseSize=4、pressure=0.2 → 半宽 max(0.25, 4×0.2/2) = 0.4。
      // 0.4 在二进制浮点下不可精确表示，故取 0.39 / 0.41 避开"贴边界"的 ulp 噪声。
      const stroke = createStroke([[5, 5, 0.2]], 4)
      expect(isStrokeHitByPoint(stroke, point(5.39, 5), 0)).toBe(true)
      expect(isStrokeHitByPoint(stroke, point(5.41, 5), 0)).toBe(false)
    })

    it('enablePressure=false 时半径取 baseSize/2（:156-161）', () => {
      // baseSize=4、关闭压感 → 半宽 2。
      const stroke = createStroke([[5, 5, 0.2]], 4, false)
      expect(isStrokeHitByPoint(stroke, point(7, 5), 0)).toBe(true)
      expect(isStrokeHitByPoint(stroke, point(7.0001, 5), 0)).toBe(false)
    })

    it('多点笔迹逐段判定（:137-149）', () => {
      // 两端 pressure=1 → 半宽 1.5；容差 0.5 → 半径 2。
      const stroke = createStroke([[0, 0, 1], [10, 0, 1]])
      expect(isStrokeHitByPoint(stroke, point(5, 2), 0.5)).toBe(true)
      expect(isStrokeHitByPoint(stroke, point(5, 2.0001), 0.5)).toBe(false)
    })

    it('多点笔迹用两端压感平均值（:142、:154-162）', () => {
      // baseSize=4、(0.2+1)/2=0.6 → 半宽 max(0.25, 4×0.6/2) = 1.2；容差 0.3 → 半径 1.5。
      // 若误用最大压感 1 → 半径 2.3，(5,2) 会命中；若误用首点压感 0.2 → 半径 0.7，(5,1.5) 会漏。
      const stroke = createStroke([[0, 0, 0.2], [10, 0, 1]], 4)
      expect(isStrokeHitByPoint(stroke, point(5, 1.5), 0.3)).toBe(true)
      expect(isStrokeHitByPoint(stroke, point(5, 1.5001), 0.3)).toBe(false)
      expect(isStrokeHitByPoint(stroke, point(5, 2), 0.3)).toBe(false)
    })

    it('快速过滤：包围盒 + 最大笔宽 + 容差之外直接不命中（:114-125）', () => {
      const stroke = createStroke([[0, 0, 1], [10, 0, 1]])
      expect(isStrokeHitByPoint(stroke, point(100, 100), 0)).toBe(false)
    })

    it('折线拐点附近按最近的段判定（:137-149）', () => {
      const stroke = createStroke([[0, 0, 1], [10, 0, 1], [10, 10, 1]])
      expect(isStrokeHitByPoint(stroke, point(10.5, 5), 0)).toBe(true)
      expect(isStrokeHitByPoint(stroke, point(11.6, 5), 0)).toBe(false)
    })
  })

  describe('isInkItemHitByPoint 类型分发（:43-58）', () => {
    it('笔迹走 Stroke 分支', () => {
      expect(isInkItemHitByPoint(createStroke([[0, 0, 1]]), point(0, 0), 0)).toBe(true)
      expect(isInkItemHitByPoint(createStroke([[0, 0, 1]]), point(50, 50), 0)).toBe(false)
    })

    it('形状走 Shape 分支', () => {
      const shape = createShape('line', point(0, 0), point(10, 0))
      expect(isInkItemHitByPoint(shape, point(5, 0), 0)).toBe(true)
      expect(isInkItemHitByPoint(shape, point(5, 5), 0)).toBe(false)
    })
  })
})

describe('rect-select（InkItemRectSelectTest.cs）', () => {
  // 选择矩形：世界坐标 (2,3)-(8,9)。
  const RECT_MIN = point(2, 3)
  const RECT_MAX = point(8, 9)

  describe('isStrokeIntersectWorldRect（:97-114）', () => {
    it('空点集不命中（:101-104）', () => {
      expect(isStrokeIntersectWorldRect(createStroke([]), RECT_MIN, RECT_MAX)).toBe(false)
    })

    it('笔迹完全在矩形内 → 命中（端点在内，:198-201）', () => {
      expect(
        isStrokeIntersectWorldRect(createStroke([[3, 3, 1], [7, 7, 1]]), RECT_MIN, RECT_MAX),
      ).toBe(true)
    })

    it('笔迹穿过矩形（两端在外）→ 命中（线段与边相交，:203-214）', () => {
      expect(
        isStrokeIntersectWorldRect(createStroke([[0, 0, 1], [10, 10, 1]]), RECT_MIN, RECT_MAX),
      ).toBe(true)
    })

    it('包围盒重叠但几何不相交 → 不命中（:106-111 快速剔除后仍判几何）', () => {
      // (0,0)→(10,10) 的包围盒 [-1.5,11.5]² 与 (8,1)-(9,2) 重叠，
      // 但线段到矩形最近距离 ≈4.95 > 半宽 1.5。
      expect(
        isStrokeIntersectWorldRect(
          createStroke([[0, 0, 1], [10, 10, 1]]),
          point(8, 1),
          point(9, 2),
        ),
      ).toBe(false)
    })

    it('笔迹在矩形外（包围盒快速剔除）→ 不命中（:107-111）', () => {
      // y 包围盒 [-1.5,1.5] 与矩形 y≥3 不相交。
      expect(
        isStrokeIntersectWorldRect(createStroke([[0, 0, 1], [10, 0, 1]]), RECT_MIN, RECT_MAX),
      ).toBe(false)
    })

    it('单点笔迹到矩形距离 ≤ 半宽 → 命中（:125-133）', () => {
      // 点 (1,2) 到矩形角 (2,3) 距离 √2≈1.414 ≤ 1.5。
      expect(isStrokeIntersectWorldRect(createStroke([[1, 2, 1]]), RECT_MIN, RECT_MAX)).toBe(true)
    })

    it('单点笔迹到矩形距离 > 半宽 → 不命中（:125-133）', () => {
      // 点 (0.6,1.6) 到矩形角距离 √(1.4²+1.4²)≈1.98 > 1.5；包围盒仍与矩形相交。
      expect(isStrokeIntersectWorldRect(createStroke([[0.6, 1.6, 1]]), RECT_MIN, RECT_MAX)).toBe(
        false,
      )
    })

    it('线段半宽使笔迹"擦边"矩形 → 命中（:216-226 角点到线段距离）', () => {
      // 线段 y=1.2、x∈[0,10] 与矩形下边 y=3 相距 1.8；半宽 1.5 < 1.8 → 不命中。
      // 下移到 y=1.5 时距离 1.5 = 半径 → 命中（闭区间）。
      expect(
        isStrokeIntersectWorldRect(createStroke([[0, 1.2, 1], [10, 1.2, 1]]), RECT_MIN, RECT_MAX),
      ).toBe(false)
      expect(
        isStrokeIntersectWorldRect(createStroke([[0, 1.5, 1], [10, 1.5, 1]]), RECT_MIN, RECT_MAX),
      ).toBe(true)
    })
  })

  describe('isInkItemIntersectWorldRect 形状分支（:76-90）', () => {
    it('空心形状内部与矩形相交即命中（AABB 语义，不看真实轮廓）', () => {
      // rect 形状 (0,0)-(10,10) 的轮廓在矩形外，但 AABB [-1.5,11.5]² 与选择矩形相交。
      expect(
        isInkItemIntersectWorldRect(
          createShape('rect', point(0, 0), point(10, 10)),
          RECT_MIN,
          RECT_MAX,
        ),
      ).toBe(true)
    })

    it('形状完全在矩形外 → 不命中', () => {
      expect(
        isInkItemIntersectWorldRect(
          createShape('line', point(100, 100), point(110, 110)),
          RECT_MIN,
          RECT_MAX,
        ),
      ).toBe(false)
    })

    it('形状与矩形边界相接 → 命中（闭区间 AABB，:188-194）', () => {
      // line (0,3)-(1,3) 的 AABB 为 x∈[-1.5,2.5]、y∈[1.5,4.5]，与矩形 x≥2、y≥3 相接。
      expect(
        isInkItemIntersectWorldRect(
          createShape('line', point(0, 3), point(1, 3)),
          RECT_MIN,
          RECT_MAX,
        ),
      ).toBe(true)
    })

    it('笔迹分发到 Stroke 分支', () => {
      expect(isInkItemIntersectWorldRect(createStroke([]), RECT_MIN, RECT_MAX)).toBe(false)
      expect(
        isInkItemIntersectWorldRect(createStroke([[3, 3, 1], [7, 7, 1]]), RECT_MIN, RECT_MAX),
      ).toBe(true)
    })
  })

  describe('hitTestInkItemsInWorldRect（:27-42）', () => {
    it('空列表返回空数组', () => {
      expect(hitTestInkItemsInWorldRect([], RECT_MIN, RECT_MAX)).toEqual([])
    })

    it('全部不相交返回空数组', () => {
      const items: InkItem[] = [createShape('line', point(100, 100), point(110, 110))]
      expect(hitTestInkItemsInWorldRect(items, RECT_MIN, RECT_MAX)).toEqual([])
    })

    it('保持列表顺序收集所有相交条目', () => {
      const inside = createStroke([[3, 3, 1], [7, 7, 1]])
      const outside = createShape('line', point(100, 100), point(110, 110))
      const hollowRect = createShape('rect', point(0, 0), point(10, 10))
      expect(hitTestInkItemsInWorldRect([inside, outside, hollowRect], RECT_MIN, RECT_MAX)).toEqual([
        inside,
        hollowRect,
      ])
    })
  })

  describe('hitTestTopMostInkItemInWorldRect（:47-62）', () => {
    it('无命中返回 null', () => {
      const items: InkItem[] = [createShape('line', point(100, 100), point(110, 110))]
      expect(hitTestTopMostInkItemInWorldRect(items, RECT_MIN, RECT_MAX)).toBeNull()
    })

    it('反向遍历：返回最上层相交条目', () => {
      const hollowRect = createShape('rect', point(0, 0), point(10, 10))
      const inside = createStroke([[3, 3, 1], [7, 7, 1]])
      const outside = createShape('line', point(100, 100), point(110, 110))
      expect(hitTestTopMostInkItemInWorldRect([hollowRect, inside, outside], RECT_MIN, RECT_MAX)).toBe(
        inside,
      )
    })
  })

  describe('矩形未归一化时的行为（桌面怪癖，:107-111 与 :118-123）', () => {
    // 桌面只在 IntersectsStrokeGeometry 内部归一化矩形，快速剔除与形状分支都用原始 min/max。
    const reversedMin = point(8, 9)
    const reversedMax = point(2, 3)

    it('跨越大范围的大笔迹仍能命中（快速剔除通过后归一化）', () => {
      expect(
        isStrokeIntersectWorldRect(
          createStroke([[0, 0, 1], [10, 10, 1]]),
          reversedMin,
          reversedMax,
        ),
      ).toBe(true)
    })

    it('小笔迹在逻辑矩形内也会被快速剔除掉（不归一化）', () => {
      expect(
        isStrokeIntersectWorldRect(
          createStroke([[4, 4, 1], [5, 5, 1]]),
          reversedMin,
          reversedMax,
        ),
      ).toBe(false)
    })

    it('形状分支全程不归一化，小形状同样不命中', () => {
      expect(
        isInkItemIntersectWorldRect(
          createShape('rect', point(4, 4), point(5, 5)),
          reversedMin,
          reversedMax,
        ),
      ).toBe(false)
    })
  })
})
