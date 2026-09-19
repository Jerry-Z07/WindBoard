using System;
using System.Numerics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 形状属性面板字段换算（design F / R8）：属性字段（长度/角度、宽/高）与两点几何的互转，以及世界坐标 ↔ 厘米换算。
    /// </summary>
    /// <remarks>
    /// - 纯函数，供属性浮层（BoardCanvasControl.ShapeProperties）与单测共用；
    /// - 长度/宽/高单位为世界坐标（缩放 100% 时与屏幕像素 1:1），返回值与域坐标同为 float；
    ///   浮层以厘米呈现数值（见 <see cref="WorldToCentimeters"/>），域存储/渲染/导出仍为世界坐标；
    /// - 画布坐标系 Y 轴向下：角度以 Start→End 方向计，水平向右为 0°，顺时针为正（与画布视觉一致）；
    /// - 矩形/椭圆读侧按 Min/Max 规范化（design A：域内不强制归一化存储，读侧规范化）。
    /// </remarks>
    internal static class ShapePropertyMath
    {
        /// <summary>属性编辑允许的最小长度/宽/高（世界坐标），避免把形状编辑成退化几何。</summary>
        internal const float MinPropertyValue = 0.01f;

        /// <summary>
        /// 1 世界单位对应的厘米数：世界单位取 DIP 的名义定义（1/96 英寸），换算系数固定不引入设置项。
        /// </summary>
        internal const double CentimetersPerWorldUnit = 2.54 / 96.0;

        /// <summary>读：世界坐标长度 → 厘米（属性浮层显示用；与视口缩放无关）。</summary>
        internal static double WorldToCentimeters(double worldUnits)
        {
            return worldUnits * CentimetersPerWorldUnit;
        }

        /// <summary>写：厘米 → 世界坐标长度（属性浮层输入用；与视口缩放无关）。</summary>
        internal static double CentimetersToWorld(double centimeters)
        {
            return centimeters / CentimetersPerWorldUnit;
        }

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
