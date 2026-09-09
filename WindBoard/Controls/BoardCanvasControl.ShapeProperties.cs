using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using WindBoard.Localization;
using Vortice.Mathematics;

namespace WindBoard.Controls
{
    /// <summary>
    /// 画布控件：形状属性浮层（design F，R8）。
    /// </summary>
    /// <remarks>
    /// - 仿 SelectionDock 的 overlay 模式：选中单个形状时显示，清选/多选/切换工具时隐藏，
    ///   锚定选择包围盒下方（Dock 排在其下方）；
    /// - 字段按 Kind：Line/Arrow = 长度+角度；Rectangle/Ellipse = 宽+高（圆 = 宽高相等的特例，不单设半径字段）；
    /// - 编辑语义：Rectangle/Ellipse 以 TopLeft 为锚点改宽高，Line/Arrow 以 Start 为锚点按长度/角度重设 End；
    /// - NumberBox 仅在提交时机（确认/失焦且值实际变化）触发 ValueChanged，经
    ///   <see cref="UpdateShapeGeometryCommand"/> 提交可撤销，避免高频 ValueChanged 膨胀撤销栈；
    /// - 撤销/重做/拖拽移动后面板数值随 UpdateSelectionOverlay 刷新。
    /// </remarks>
    public sealed partial class BoardCanvasControl
    {
        // 程序化写值（刷新面板）与用户编辑（触发提交）的区分旗：避免同步写值反向触发提交。
        private bool _isSyncingShapeProperties;

        // 当前属性浮层服务的形状（显示时设置；清选/多选时置空）。
        private BoardShape? _shapePropertiesTarget;

        /// <summary>当前选中集是否恰好为单个形状（是则返回该形状）。</summary>
        private BoardShape? TryGetSingleSelectedShape()
        {
            if (_input is null)
            {
                return null;
            }

            IReadOnlyList<IBoardInkItem> items = _input.SelectedItems;
            return items.Count == 1 ? items[0] as BoardShape : null;
        }

        /// <summary>
        /// 显示形状属性浮层并定位到选择包围盒下方；返回面板底边 y（用于把 Dock 排在其下方）。
        /// </summary>
        private double ShowShapePropertiesOverlay(Rect boundsDip, BoardShape shape)
        {
            if (ShapePropertiesBorder is null)
            {
                return boundsDip.Bottom;
            }

            _shapePropertiesTarget = shape;
            ConfigureShapePropertyFields(shape.Kind);
            SyncShapePropertyBoxes(shape);

            ShapePropertiesBorder.Visibility = Visibility.Visible;

            // 定位到选择包围盒下方居中（与 SelectionDock 同模式），并做边界钳制，避免跑出画布。
            double panelW = ShapePropertiesBorder.ActualWidth;
            double panelH = ShapePropertiesBorder.ActualHeight;
            if (panelW <= 0.0 || panelH <= 0.0)
            {
                ShapePropertiesBorder.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                panelW = ShapePropertiesBorder.DesiredSize.Width;
                panelH = ShapePropertiesBorder.DesiredSize.Height;
            }

            double left = boundsDip.Left + boundsDip.Width / 2.0 - panelW / 2.0;
            double top = boundsDip.Bottom + 8.0;

            double maxLeft = Math.Max(0.0, CanvasPanel.ActualWidth - panelW);
            double maxTop = Math.Max(0.0, CanvasPanel.ActualHeight - panelH);

            left = Math.Clamp(left, 0.0, maxLeft);
            top = Math.Clamp(top, 0.0, maxTop);

            Canvas.SetLeft(ShapePropertiesBorder, left);
            Canvas.SetTop(ShapePropertiesBorder, top);

            return top + panelH;
        }

        private void HideShapePropertiesOverlay()
        {
            _shapePropertiesTarget = null;

            if (ShapePropertiesBorder is not null)
            {
                ShapePropertiesBorder.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>按形状种类配置字段标签与取值范围（长度/角度 vs 宽/高）。</summary>
        private void ConfigureShapePropertyFields(BoardShapeKind kind)
        {
            if (ShapeProperty1Label is null || ShapeProperty2Label is null)
            {
                return;
            }

            if (kind is BoardShapeKind.Line or BoardShapeKind.Arrow)
            {
                ShapeProperty1Label.Text = L10n.Get("ShapeProperties_Length");
                ShapeProperty2Label.Text = L10n.Get("ShapeProperties_Angle");

                if (ShapeProperty1Box is not null)
                {
                    ShapeProperty1Box.Minimum = ShapePropertyMath.MinPropertyValue;
                    ShapeProperty1Box.Maximum = double.PositiveInfinity;
                }

                if (ShapeProperty2Box is not null)
                {
                    ShapeProperty2Box.Minimum = -360.0;
                    ShapeProperty2Box.Maximum = 360.0;
                }
            }
            else
            {
                ShapeProperty1Label.Text = L10n.Get("ShapeProperties_Width");
                ShapeProperty2Label.Text = L10n.Get("ShapeProperties_Height");

                if (ShapeProperty1Box is not null)
                {
                    ShapeProperty1Box.Minimum = ShapePropertyMath.MinPropertyValue;
                    ShapeProperty1Box.Maximum = double.PositiveInfinity;
                }

                if (ShapeProperty2Box is not null)
                {
                    ShapeProperty2Box.Minimum = ShapePropertyMath.MinPropertyValue;
                    ShapeProperty2Box.Maximum = double.PositiveInfinity;
                }
            }
        }

        /// <summary>从形状读当前值刷新 NumberBox（撤销/重做/拖拽移动后经 UpdateSelectionOverlay 到达）。</summary>
        /// <remarks>
        /// 任一 NumberBox 处于焦点编辑中时跳过同步，避免打断用户输入；编辑结束（失焦/确认）后由下一次刷新对齐。
        /// </remarks>
        private void SyncShapePropertyBoxes(BoardShape shape)
        {
            if (ShapeProperty1Box is null || ShapeProperty2Box is null)
            {
                return;
            }

            if (ShapeProperty1Box.FocusState != FocusState.Unfocused
                || ShapeProperty2Box.FocusState != FocusState.Unfocused)
            {
                return;
            }

            _isSyncingShapeProperties = true;
            try
            {
                if (shape.Kind is BoardShapeKind.Line or BoardShapeKind.Arrow)
                {
                    (double length, double angle) = ShapePropertyMath.ReadLineLengthAngle(shape.Start, shape.End);
                    SetShapePropertyValue(ShapeProperty1Box, length);
                    SetShapePropertyValue(ShapeProperty2Box, angle);
                }
                else
                {
                    (double width, double height) = ShapePropertyMath.ReadRectSize(shape.Start, shape.End);
                    SetShapePropertyValue(ShapeProperty1Box, width);
                    SetShapePropertyValue(ShapeProperty2Box, height);
                }
            }
            finally
            {
                _isSyncingShapeProperties = false;
            }
        }

        private static void SetShapePropertyValue(NumberBox box, double value)
        {
            double rounded = Math.Round(value, 2);
            // box.Value 为 NaN（用户清空/无效输入）时也写回有效值，保证撤销/重做后的回读刷新生效。
            if (double.IsNaN(box.Value) || Math.Abs(box.Value - rounded) > 0.001)
            {
                box.Value = rounded;
            }
        }

        private void OnShapeProperty1ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            CommitShapePropertiesFromBoxes();
        }

        private void OnShapeProperty2ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            CommitShapePropertiesFromBoxes();
        }

        /// <summary>
        /// NumberBox 提交时机（确认/失焦且值实际变化）统一入口：按 Kind 换算新几何并经命令栈提交。
        /// </summary>
        private void CommitShapePropertiesFromBoxes()
        {
            if (_isSyncingShapeProperties)
            {
                return;
            }

            BoardShape? shape = _shapePropertiesTarget;
            if (shape is null || ShapeProperty1Box is null || ShapeProperty2Box is null)
            {
                return;
            }

            double value1 = ShapeProperty1Box.Value;
            double value2 = ShapeProperty2Box.Value;

            // NumberBox 清空/无效输入时 Value 可能为 NaN：NaN 会穿透 Min clamp 与“值未变化”比较，
            // 直接写入会把 NaN 几何带进域模型（渲染/序列化全部失效）。这里仅回读刷新一次对齐显示。
            if (!double.IsFinite(value1) || !double.IsFinite(value2))
            {
                SyncShapePropertyBoxes(shape);
                return;
            }

            Vector2 newStart;
            Vector2 newEnd;
            if (shape.Kind is BoardShapeKind.Line or BoardShapeKind.Arrow)
            {
                // Line/Arrow 以 Start 为锚点，按长度/角度重设 End（design F）。
                double length = Math.Max(ShapePropertyMath.MinPropertyValue, value1);
                newStart = shape.Start;
                newEnd = ShapePropertyMath.ComputeLineEndFromLengthAngle(shape.Start, length, value2);
            }
            else
            {
                // Rectangle/Ellipse 以 TopLeft 为锚点改宽高（design F）。
                double width = Math.Max(ShapePropertyMath.MinPropertyValue, value1);
                double height = Math.Max(ShapePropertyMath.MinPropertyValue, value2);
                (newStart, newEnd) = ShapePropertyMath.ComputeRectGeometryFromSize(shape.Start, shape.End, width, height);
            }

            // 值未变化时不重复入栈（NumberBox 失焦/同步回读等场景）。
            if (Vector2.DistanceSquared(shape.Start, newStart) <= 0.000001f
                && Vector2.DistanceSquared(shape.End, newEnd) <= 0.000001f)
            {
                return;
            }

            // 经命令栈提交（R8：可撤销）；Session.StateChanged 链路会触发渲染与 overlay 刷新（数值回读）。
            _session.Execute(new UpdateShapeGeometryCommand(shape, (shape.Start, shape.End), (newStart, newEnd)));
            UpdateSelectionOverlay();
            RequestRender();
        }
    }
}
