/**
 * 形状属性浮层的字段推导（纯函数，node 可测），桌面出处 Controls/BoardCanvasControl.ShapeProperties.cs 的 ConfigureShapePropertyFields（:123-160）与 SyncShapePropertyBoxes（:169-200），换算见 ShapePropertyMath（网页端在 board/editing/shape-property.ts）。
 * 规则（design F / R8）：line 与 arrow 的主字段为长度 (cm)、次字段为角度（角度不换算、不显示单位符号）；rect 与 ellipse 为宽度 (cm) 与高度 (cm)；长度类字段下限为 `worldToCentimeters(MIN_SHAPE_PROPERTY_LENGTH)`（桌面把世界坐标下限换算成厘米后再设为 NumberBox.Minimum，:127-133），上限无穷；角度范围 `[-360, 360]`（:142-146）。
 */

import {
  MIN_SHAPE_PROPERTY_LENGTH,
  readLineLengthAngle,
  readRectSize,
  worldToCentimeters,
} from '../../board/editing/shape-property.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'
import type { Shape } from '../../board/model/shape.ts'

/** 浮层里的两个字段槽位（桌面 `ShapeProperty1Box` / `ShapeProperty2Box`）。 */
export type ShapePropertyFieldId = 'primary' | 'secondary'

/** 一个字段：标签 + 当前值 + 单位（+ 桌面 `NumberBox` 的取值范围）。 */
export interface ShapePropertyField {
  readonly id: ShapePropertyFieldId
  /** 标签的文案 key（桌面从 resw 取词，已含单位，如「长度 (cm)」）；渲染交给 UI。 */
  readonly labelKey: MessageKey
  /** 当前值（长度类已换算为厘米）。 */
  readonly value: number
  /** 单位：长度类 `cm`、角度 `°`。桌面把单位写进标签文本，这里单独给出以便无障碍与后续格式化。 */
  readonly unit: string
  /** 下限（对应桌面 `NumberBox.Minimum`）。 */
  readonly min?: number
  /** 上限（对应桌面 `NumberBox.Maximum`；未给出表示无上限）。 */
  readonly max?: number
}

/** `ShapeProperties_Length`（`WindBoard/Strings/zh-CN/ShapeProperties.resw:20`）。 */
const LABEL_LENGTH: MessageKey = 'ShapeProperties_Length'
/** `ShapeProperties_Angle`（`:23`）。 */
const LABEL_ANGLE: MessageKey = 'ShapeProperties_Angle'
/** `ShapeProperties_Width`（`:26`）。 */
const LABEL_WIDTH: MessageKey = 'ShapeProperties_Width'
/** `ShapeProperties_Height`（`:29`）。 */
const LABEL_HEIGHT: MessageKey = 'ShapeProperties_Height'

/** 长度类字段下限（厘米）：桌面 `WorldToCentimeters(ShapePropertyMath.MinPropertyValue)`，`:127`。 */
const MIN_LENGTH_CM = worldToCentimeters(MIN_SHAPE_PROPERTY_LENGTH)

/** 角度下限 / 上限：桌面 `-360` / `360`（`:142-146`）。 */
const MIN_ANGLE = -360
const MAX_ANGLE = 360

/**
 * 显示值保留 2 位小数（桌面 SetShapePropertyValue 里的 Math.Round(value, 2)，:196）。C# Math.Round(double, int) 默认是"银行家舍入"（中点取偶），这里用 Math.round（中点远离零），仅在恰好 .005 的边界上可能差 0.01，属显示层差异。
 */
function roundForDisplay(value: number): number {
  return Math.round(value * 100) / 100
}

/**
 * 由形状几何推导浮层两个字段（桌面 `SyncShapePropertyBoxes`，`:169-200`）。
 *
 * 调用方只在"恰好选中一个形状"时调用（桌面 `TryGetSingleSelectedShape`，`:57-66`）。
 */
export function deriveShapePropertyFields(
  shape: Shape,
): readonly [ShapePropertyField, ShapePropertyField] {
  if (shape.kind === 'line' || shape.kind === 'arrow') {
    const { length, angleDegrees } = readLineLengthAngle(shape.start, shape.end)
    return [
      {
        id: 'primary',
        labelKey: LABEL_LENGTH,
        value: roundForDisplay(worldToCentimeters(length)),
        unit: 'cm',
        min: MIN_LENGTH_CM,
      },
      {
        id: 'secondary',
        labelKey: LABEL_ANGLE,
        value: roundForDisplay(angleDegrees),
        unit: '°',
        min: MIN_ANGLE,
        max: MAX_ANGLE,
      },
    ]
  }

  const { width, height } = readRectSize(shape.start, shape.end)
  return [
    {
      id: 'primary',
      labelKey: LABEL_WIDTH,
      value: roundForDisplay(worldToCentimeters(width)),
      unit: 'cm',
      min: MIN_LENGTH_CM,
    },
    {
      id: 'secondary',
      labelKey: LABEL_HEIGHT,
      value: roundForDisplay(worldToCentimeters(height)),
      unit: 'cm',
      min: MIN_LENGTH_CM,
    },
  ]
}
