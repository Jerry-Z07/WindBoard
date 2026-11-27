import type { Vec2 } from '../model/stroke.ts'

/**
 * 形状属性面板字段换算（移植桌面 ShapePropertyMath.cs，design F / R8）。纯函数，供属性浮层与单测共用；长度、宽、高单位为世界坐标（缩放 100% 时与屏幕像素 1:1），浮层以厘米呈现数值，域存储、渲染、导出仍为世界坐标。
 * 画布坐标系 Y 轴向下：角度以 Start→End 方向计，水平向右为 0°，顺时针为正；矩形与椭圆读侧按 Min/Max 规范化（域内不强制归一化存储）。
 */

/** 属性编辑允许的最小长度/宽/高（世界坐标），避免把形状编辑成退化几何（`ShapePropertyMath.cs:19`）。 */
export const MIN_SHAPE_PROPERTY_LENGTH = 0.01

/**
 * 1 世界单位对应的厘米数：世界单位取 DIP 的名义定义（1/96 英寸），
 * 换算系数固定、不引入设置项（`ShapePropertyMath.cs:24`）。
 */
export const CENTIMETERS_PER_WORLD_UNIT = 2.54 / 96

/** 读：世界坐标长度 → 厘米（属性浮层显示用；与视口缩放无关，`ShapePropertyMath.cs:27-30`）。 */
export function worldToCentimeters(worldUnits: number): number {
  return worldUnits * CENTIMETERS_PER_WORLD_UNIT
}

/** 写：厘米 → 世界坐标长度（属性浮层输入用；与视口缩放无关，`ShapePropertyMath.cs:33-36`）。 */
export function centimetersToWorld(centimeters: number): number {
  return centimeters / CENTIMETERS_PER_WORLD_UNIT
}

/** 读：直线/箭头两端点 → (长度, 角度°)，`ShapePropertyMath.cs:39-43`。 */
export function readLineLengthAngle(
  start: Vec2,
  end: Vec2,
): { length: number; angleDegrees: number } {
  const deltaX = end.x - start.x
  const deltaY = end.y - start.y

  return {
    // 桌面：Math.Sqrt(delta.LengthSquared())
    length: Math.sqrt(deltaX * deltaX + deltaY * deltaY),
    // 桌面：Math.Atan2(delta.Y, delta.X) * 180 / PI
    angleDegrees: (Math.atan2(deltaY, deltaX) * 180) / Math.PI,
  }
}

/** 写：以 Start 为锚点，按长度/角度重设 End（Line/Arrow 以 Start 为锚点，`ShapePropertyMath.cs:46-50`）。 */
export function computeLineEndFromLengthAngle(
  start: Vec2,
  length: number,
  angleDegrees: number,
): Vec2 {
  const radians = (angleDegrees * Math.PI) / 180

  return {
    x: start.x + length * Math.cos(radians),
    y: start.y + length * Math.sin(radians),
  }
}

/** 读：对角点 → (宽, 高)，按 Min/Max 规范化，负向拖拽也返回正值（`ShapePropertyMath.cs:53-56`）。 */
export function readRectSize(start: Vec2, end: Vec2): { width: number; height: number } {
  return {
    width: Math.abs(end.x - start.x),
    height: Math.abs(end.y - start.y),
  }
}

/**
 * 写：以 TopLeft（Min）为锚点，按宽/高重设两点几何（Rect/Ellipse 以 TopLeft 为锚点，
 * `ShapePropertyMath.cs:59-64`）。
 */
export function computeRectGeometryFromSize(
  start: Vec2,
  end: Vec2,
  width: number,
  height: number,
): { start: Vec2; end: Vec2 } {
  const topLeftX = Math.min(start.x, end.x)
  const topLeftY = Math.min(start.y, end.y)

  return {
    start: { x: topLeftX, y: topLeftY },
    end: { x: topLeftX + width, y: topLeftY + height },
  }
}
