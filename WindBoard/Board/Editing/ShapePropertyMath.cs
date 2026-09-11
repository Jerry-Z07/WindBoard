using System;
using System.Numerics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 形状属性面板字段换算（design F / R8）：属性字段（长度/角度、宽/高）与两点几何的互转。
    /// </summary>
    /// <remarks>
    /// - 纯函数，供属性浮层（BoardCanvasControl.ShapeProperties）与单测共用；
    /// - 长度/宽/高单位为世界坐标（缩放 100% 时与屏幕像素 1:1），返回值与域坐标同为 float；
    /// - 画布坐标系 Y 轴向下：角度以 Start→End 方向计，水平向右为 0°，顺时针为正（与画布视觉一致）；
    /// - 矩形/椭圆读侧按 Min/Max 规范化（design A：域内不强制归一化存储，读侧规范化）。
    /// </remarks>
    internal static class ShapePropertyMath
    {
        /// <summary>属性编辑允许的最小长度/宽/高（世界坐标），避免把形状编辑成退化几何。</summary>
        internal const float MinPropertyValue = 0.01f;

        /// <summary>读：直线/箭头两端点 → (长度, 角度°)。</summary>
        internal static (float Length, float AngleDegrees) ReadLineLengthAngle(Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            return ((float)Math.Sqrt(delta.LengthSquared()), (float)(Math.Atan2(delta.Y, delta.X) * 180.0 / Math.PI));
        }

        /// <summary>写：以 Start 为锚点，按长度/角度重设 End（design F：Line/Arrow 以 Start 为锚点）。</summary>
        internal static Vector2 ComputeLineEndFromLengthAngle(Vector2 start, double length, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return start + new Vector2((float)(length * Math.Cos(radians)), (float)(length * Math.Sin(radians)));
        }

        /// <summary>读：对角点 → (宽, 高)（按 Min/Max 规范化，负向拖拽也返回正值）。</summary>
        internal static (float Width, float Height) ReadRectSize(Vector2 start, Vector2 end)
        {
            return (Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        }

        /// <summary>写：以 TopLeft（Min）为锚点，按宽/高重设两点几何（design F）。</summary>
        internal static (Vector2 Start, Vector2 End) ComputeRectGeometryFromSize(Vector2 start, Vector2 end, double width, double height)
        {
            Vector2 topLeft = new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y));
            Vector2 newEnd = topLeft + new Vector2((float)width, (float)height);
            return (topLeft, newEnd);
        }
    }
}
