using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Rendering;

namespace WindBoard
{
    public sealed partial class MainWindow : Window
    {
        private DxSwapChainPanelRenderer? _renderer;
        private readonly BoardDocument _document = new();
        private Stroke? _activeStroke;
        private uint? _activePointerId;
        private uint? _panPointerId;

        private ID2D1SolidColorBrush? _strokeBrush;
        private ID2D1SolidColorBrush? _gridMinorBrush;
        private ID2D1SolidColorBrush? _gridMajorBrush;
        private ID2D1SolidColorBrush? _axisBrush;

        private readonly Stack<IBoardCommand> _undoStack = new();
        private readonly Stack<IBoardCommand> _redoStack = new();

        // 视图（相机）状态：笔迹存“世界坐标”，窗口只是“视口”，用于缩放/平移与坐标换算
        private float _zoom = 1.0f;
        private Vector2 _cameraWorld = Vector2.Zero;
        private Vector2 _lastPanScreen = Vector2.Zero;
        private PointerDeviceType? _activeStrokeDeviceType;
        private readonly HashSet<uint> _activeTouchPointers = new();

        private const float MinZoom = 0.05f;
        private const float MaxZoom = 32.0f;

        public MainWindow()
        {
            InitializeComponent();

            CanvasPanel.Loaded += (_, _) => InitializeRenderer();
            CanvasPanel.SizeChanged += (_, _) => _renderer?.Resize();
            CanvasPanel.CompositionScaleChanged += (_, _) => _renderer?.Resize();

            CanvasPanel.PointerPressed += OnCanvasPointerPressed;
            CanvasPanel.PointerMoved += OnCanvasPointerMoved;
            CanvasPanel.PointerReleased += OnCanvasPointerReleased;
            CanvasPanel.PointerCanceled += OnCanvasPointerCanceled;
            CanvasPanel.PointerCaptureLost += OnCanvasPointerCaptureLost;
            CanvasPanel.PointerWheelChanged += OnCanvasPointerWheelChanged;

            // 触摸：单指画线；双指/多指拖动+捏合缩放（Pinch Zoom）
            CanvasPanel.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale;
            CanvasPanel.ManipulationStarting += OnCanvasManipulationStarting;
            CanvasPanel.ManipulationDelta += OnCanvasManipulationDelta;
            CanvasPanel.ManipulationCompleted += OnCanvasManipulationCompleted;

            UndoButton.Click += (_, _) => { DiscardActiveStroke(); Undo(); };
            RedoButton.Click += (_, _) => { DiscardActiveStroke(); Redo(); };
            ClearButton.Click += (_, _) => { DiscardActiveStroke(); ClearAll(); };
            UpdateCommandStates();

            Closed += (_, _) =>
            {
                CompositionTarget.Rendering -= OnRendering;
                _strokeBrush?.Dispose();
                _strokeBrush = null;
                _gridMinorBrush?.Dispose();
                _gridMinorBrush = null;
                _gridMajorBrush?.Dispose();
                _gridMajorBrush = null;
                _axisBrush?.Dispose();
                _axisBrush = null;
                _renderer?.Dispose();
                _renderer = null;
            };
        }

        private void InitializeRenderer()
        {
            if (_renderer is not null)
            {
                return;
            }

            _renderer = new DxSwapChainPanelRenderer(CanvasPanel);
            _renderer.Initialize();

            CompositionTarget.Rendering += OnRendering;

            if (CanvasPanel.XamlRoot is not null)
            {
                CanvasPanel.XamlRoot.Changed += (_, _) => _renderer?.Resize();
            }
        }

        private void OnRendering(object? sender, object e)
        {
            _renderer?.Render(DrawBoard);
        }

        private void DrawBoard(ID2D1DeviceContext ctx)
        {
            _strokeBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
            _gridMinorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.92f, 0.92f, 0.92f, 1.0f));
            _gridMajorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.86f, 0.86f, 0.86f, 1.0f));
            _axisBrush ??= ctx.CreateSolidColorBrush(new Color4(0.78f, 0.78f, 0.78f, 1.0f));

            Matrix3x2 oldTransform = ctx.Transform;
            ctx.Transform = GetWorldToScreenTransform();
            DrawInfiniteGrid(ctx);

            foreach (var stroke in _document.Strokes)
            {
                DrawStroke(ctx, stroke);
            }

            if (_activeStroke is not null)
            {
                DrawStroke(ctx, _activeStroke);
            }

            ctx.Transform = oldTransform;
        }

        private void DrawStroke(ID2D1DeviceContext ctx, Stroke stroke)
        {
            if (_strokeBrush is null)
            {
                return;
            }

            _strokeBrush.Color = stroke.Color;

            if (stroke.Points.Count == 1)
            {
                float radius = Math.Max(0.5f, stroke.BaseSize * GetStrokeWidthFactor(stroke.Points[0].Pressure) / 2.0f);
                ctx.FillEllipse(new Ellipse(stroke.Points[0].Position, radius, radius), _strokeBrush);
                return;
            }

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float widthFactor = stroke.EnablePressure
                    ? GetStrokeWidthFactor((p0.Pressure + p1.Pressure) / 2.0f)
                    : 1.0f;

                float strokeWidth = Math.Max(0.5f, stroke.BaseSize * widthFactor);
                ctx.DrawLine(p0.Position, p1.Position, _strokeBrush, strokeWidth);
            }
        }

        private void DrawInfiniteGrid(ID2D1DeviceContext ctx)
        {
            if (_gridMinorBrush is null || _gridMajorBrush is null || _axisBrush is null)
            {
                return;
            }

            Vector2 viewportSize = GetViewportSizeDip();
            Vector2 worldTopLeft = ScreenToWorld(Vector2.Zero);
            Vector2 worldBottomRight = ScreenToWorld(viewportSize);

            float minX = Math.Min(worldTopLeft.X, worldBottomRight.X);
            float maxX = Math.Max(worldTopLeft.X, worldBottomRight.X);
            float minY = Math.Min(worldTopLeft.Y, worldBottomRight.Y);
            float maxY = Math.Max(worldTopLeft.Y, worldBottomRight.Y);

            float step = GetAdaptiveGridStepWorld(_zoom);
            if (step <= 0.0f)
            {
                return;
            }

            const int majorEvery = 5;
            float minorThicknessWorld = 1.0f / Math.Max(0.0001f, _zoom);
            float majorThicknessWorld = 1.5f / Math.Max(0.0001f, _zoom);
            float axisThicknessWorld = 2.0f / Math.Max(0.0001f, _zoom);

            long firstX = (long)Math.Floor(minX / step);
            long lastX = (long)Math.Ceiling(maxX / step);
            long firstY = (long)Math.Floor(minY / step);
            long lastY = (long)Math.Ceiling(maxY / step);

            for (long ix = firstX; ix <= lastX; ix++)
            {
                float x = (float)(ix * step);
                bool isMajor = ix % majorEvery == 0;
                ctx.DrawLine(
                    new Vector2(x, minY),
                    new Vector2(x, maxY),
                    isMajor ? _gridMajorBrush : _gridMinorBrush,
                    isMajor ? majorThicknessWorld : minorThicknessWorld);
            }

            for (long iy = firstY; iy <= lastY; iy++)
            {
                float y = (float)(iy * step);
                bool isMajor = iy % majorEvery == 0;
                ctx.DrawLine(
                    new Vector2(minX, y),
                    new Vector2(maxX, y),
                    isMajor ? _gridMajorBrush : _gridMinorBrush,
                    isMajor ? majorThicknessWorld : minorThicknessWorld);
            }

            // 世界坐标原点轴（用于方向感）
            if (0.0f >= minX && 0.0f <= maxX)
            {
                ctx.DrawLine(new Vector2(0.0f, minY), new Vector2(0.0f, maxY), _axisBrush, axisThicknessWorld);
            }

            if (0.0f >= minY && 0.0f <= maxY)
            {
                ctx.DrawLine(new Vector2(minX, 0.0f), new Vector2(maxX, 0.0f), _axisBrush, axisThicknessWorld);
            }
        }

        private static float GetAdaptiveGridStepWorld(float zoom)
        {
            // 基准：zoom=1 时每 40 DIP 一格。根据缩放自适应，保证屏幕上网格密度大致稳定。
            float step = 40.0f;
            float stepScreen = step * zoom;

            while (stepScreen < 20.0f)
            {
                step *= 2.0f;
                stepScreen = step * zoom;
            }

            while (stepScreen > 80.0f)
            {
                step /= 2.0f;
                stepScreen = step * zoom;
            }

            return step;
        }

        private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(CanvasPanel);

            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Add(e.Pointer.PointerId);

                // 多指触摸：交给 Manipulation 处理缩放/拖动；如果正在用“触摸单指画线”，则先结束画线
                if (_activeTouchPointers.Count >= 2)
                {
                    if (_activeStroke is not null && _activeStrokeDeviceType == PointerDeviceType.Touch)
                    {
                        // 两指及以上时视为手势：如果只是按下的“单点”，不要留下点状笔迹
                        if (_activeStroke.Points.Count <= 1)
                        {
                            DiscardActiveStroke();
                        }
                        else
                        {
                            CommitActiveStroke();
                        }
                    }

                    e.Handled = true;
                    return;
                }

                // 单指触摸：画线
                if (_activePointerId is not null || _panPointerId is not null)
                {
                    return;
                }

                CanvasPanel.CapturePointer(e.Pointer);
                _activePointerId = e.Pointer.PointerId;
                _activeStrokeDeviceType = e.Pointer.PointerDeviceType;

                _activeStroke = new Stroke
                {
                    Color = new Color4(0, 0, 0, 1),
                    BaseSize = 3.0f,
                    EnablePressure = true,
                };

                AppendPoint(_activeStroke, e.Pointer, point);
                e.Handled = true;
                return;
            }

            if (_activePointerId is not null || _panPointerId is not null)
            {
                return;
            }

            if (ShouldStartPan(e.Pointer, point))
            {
                CanvasPanel.CapturePointer(e.Pointer);
                _panPointerId = e.Pointer.PointerId;
                _lastPanScreen = new Vector2((float)point.Position.X, (float)point.Position.Y);
                e.Handled = true;
                return;
            }

            if (!ShouldStartStroke(e.Pointer, point))
            {
                return;
            }

            CanvasPanel.CapturePointer(e.Pointer);
            _activePointerId = e.Pointer.PointerId;
            _activeStrokeDeviceType = e.Pointer.PointerDeviceType;

            _activeStroke = new Stroke
            {
                Color = new Color4(0, 0, 0, 1),
                BaseSize = 3.0f,
                EnablePressure = true,
            };

            AppendPoint(_activeStroke, e.Pointer, point);
            e.Handled = true;
        }

        private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_panPointerId == e.Pointer.PointerId)
            {
                PointerPoint panPoint = e.GetCurrentPoint(CanvasPanel);
                Vector2 screen = new((float)panPoint.Position.X, (float)panPoint.Position.Y);
                Vector2 delta = screen - _lastPanScreen;
                _lastPanScreen = screen;
                PanByScreenDelta(delta);
                e.Handled = true;
                return;
            }

            if (_activeStroke is null || _activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            PointerPoint point = e.GetCurrentPoint(CanvasPanel);
            if (!point.IsInContact)
            {
                return;
            }

            AppendPoint(_activeStroke, e.Pointer, point);
            e.Handled = true;
        }

        private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Remove(e.Pointer.PointerId);
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                CanvasPanel.ReleasePointerCaptures();
                e.Handled = true;
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CommitActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Remove(e.Pointer.PointerId);
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                CanvasPanel.ReleasePointerCaptures();
                e.Handled = true;
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            DiscardActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                _activeTouchPointers.Remove(e.Pointer.PointerId);
            }

            if (_panPointerId == e.Pointer.PointerId)
            {
                _panPointerId = null;
                e.Handled = true;
                return;
            }

            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CommitActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_activeStroke is not null || _panPointerId is not null)
            {
                return;
            }

            PointerPoint point = e.GetCurrentPoint(CanvasPanel);
            int delta = point.Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            // 以鼠标所在位置为锚点缩放，避免缩放时“跳动”
            float factor = (float)Math.Pow(1.1, delta / 120.0);
            ZoomAboutScreenPoint(new Vector2((float)point.Position.X, (float)point.Position.Y), factor);
            e.Handled = true;
        }

        private void OnCanvasManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            // 触摸手势以 CanvasPanel 为坐标系
            if (_activeStroke is not null || _panPointerId is not null)
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
        }

        private void OnCanvasManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            // 触摸：多指拖动 + 捏合缩放（以手势中心为缩放锚点）
            if (_activeStroke is not null || _panPointerId is not null)
            {
                e.Handled = true;
                return;
            }

            if (_activeTouchPointers.Count < 2)
            {
                e.Handled = true;
                return;
            }

            Vector2 anchor = new((float)e.Position.X, (float)e.Position.Y);

            float scale = (float)e.Delta.Scale;
            if (Math.Abs(scale - 1.0f) > 0.0001f)
            {
                ZoomAboutScreenPoint(anchor, scale);
            }

            Vector2 translation = new((float)e.Delta.Translation.X, (float)e.Delta.Translation.Y);
            if (translation.LengthSquared() > 0.0001f)
            {
                PanByScreenDelta(translation);
            }

            e.Handled = true;
        }

        private void OnCanvasManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            // 在三指及以上的复杂触摸手势下，系统可能不会为每个触点都完整触发 PointerReleased/PointerCanceled。
            // 为避免触点残留导致始终被判定为“多指”，这里在手势结束时强制清空触摸状态。
            _activeTouchPointers.Clear();
            e.Handled = true;
        }

        private void AppendPoint(Stroke stroke, Pointer pointer, PointerPoint point)
        {
            Vector2 screen = new((float)point.Position.X, (float)point.Position.Y);
            Vector2 pos = ScreenToWorld(screen);
            float pressure = GetNormalizedPressure(pointer.PointerDeviceType, point.Properties);

            if (stroke.Points.Count > 0)
            {
                Vector2 last = stroke.Points[^1].Position;
                float minDistWorld = 0.5f / Math.Max(0.0001f, _zoom);
                if (Vector2.DistanceSquared(last, pos) < minDistWorld * minDistWorld)
                {
                    return;
                }
            }

            stroke.Points.Add(new StrokePoint(pos, pressure));
        }

        private void CommitActiveStroke()
        {
            if (_activeStroke is not null && _activeStroke.Points.Count > 0)
            {
                ExecuteCommand(new AddStrokeCommand(_activeStroke));
            }

            _activeStroke = null;
            _activePointerId = null;
            _activeStrokeDeviceType = null;
            CanvasPanel.ReleasePointerCaptures();
        }

        private void DiscardActiveStroke()
        {
            _activeStroke = null;
            _activePointerId = null;
            _activeStrokeDeviceType = null;
            CanvasPanel.ReleasePointerCaptures();
            UpdateCommandStates();
        }

        private static bool ShouldStartStroke(Pointer pointer, PointerPoint point)
        {
            if (pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                return point.Properties.IsLeftButtonPressed;
            }

            // 触控笔 / 触摸：默认允许
            return true;
        }

        private static bool ShouldStartPan(Pointer pointer, PointerPoint point)
        {
            if (pointer.PointerDeviceType != PointerDeviceType.Mouse)
            {
                return false;
            }

            return point.Properties.IsRightButtonPressed;
        }

        private Vector2 GetViewportSizeDip()
        {
            float w = (float)Math.Max(1.0, CanvasPanel.ActualWidth);
            float h = (float)Math.Max(1.0, CanvasPanel.ActualHeight);
            return new Vector2(w, h);
        }

        private Vector2 GetViewportCenterDip()
        {
            return GetViewportSizeDip() / 2.0f;
        }

        private Matrix3x2 GetWorldToScreenTransform()
        {
            Vector2 viewportCenter = GetViewportCenterDip();
            return Matrix3x2.CreateTranslation(-_cameraWorld)
                * Matrix3x2.CreateScale(_zoom)
                * Matrix3x2.CreateTranslation(viewportCenter);
        }

        private Vector2 ScreenToWorld(Vector2 screenDip)
        {
            Vector2 viewportCenter = GetViewportCenterDip();
            return (screenDip - viewportCenter) / Math.Max(0.0001f, _zoom) + _cameraWorld;
        }

        private void PanByScreenDelta(Vector2 deltaScreenDip)
        {
            _cameraWorld -= deltaScreenDip / Math.Max(0.0001f, _zoom);
        }

        private void ZoomAboutScreenPoint(Vector2 anchorScreenDip, float zoomFactor)
        {
            if (zoomFactor <= 0.0f)
            {
                return;
            }

            float oldZoom = _zoom;
            float newZoom = Math.Clamp(oldZoom * zoomFactor, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - oldZoom) < 0.000001f)
            {
                return;
            }

            Vector2 viewportCenter = GetViewportCenterDip();
            Vector2 worldBefore = (anchorScreenDip - viewportCenter) / Math.Max(0.0001f, oldZoom) + _cameraWorld;

            _zoom = newZoom;
            _cameraWorld = worldBefore - (anchorScreenDip - viewportCenter) / Math.Max(0.0001f, _zoom);
        }

        private static float GetNormalizedPressure(PointerDeviceType pointerDeviceType, PointerPointProperties props)
        {
            if (pointerDeviceType != PointerDeviceType.Pen)
            {
                return 1.0f;
            }

            float p = (float)props.Pressure;
            return Math.Clamp(p, 0.1f, 1.0f);
        }

        private static float GetStrokeWidthFactor(float normalizedPressure)
        {
            return Math.Clamp(normalizedPressure, 0.1f, 1.0f);
        }

        private void ExecuteCommand(IBoardCommand command)
        {
            command.Do(_document);
            _undoStack.Push(command);
            _redoStack.Clear();
            UpdateCommandStates();
        }

        private void Undo()
        {
            if (!_undoStack.TryPop(out IBoardCommand? command))
            {
                return;
            }

            command.Undo(_document);
            _redoStack.Push(command);
            UpdateCommandStates();
        }

        private void Redo()
        {
            if (!_redoStack.TryPop(out IBoardCommand? command))
            {
                return;
            }

            command.Do(_document);
            _undoStack.Push(command);
            UpdateCommandStates();
        }

        private void ClearAll()
        {
            if (_document.Strokes.Count == 0)
            {
                return;
            }

            ExecuteCommand(new ClearCommand(new List<Stroke>(_document.Strokes)));
        }

        private void UpdateCommandStates()
        {
            UndoButton.IsEnabled = _undoStack.Count > 0;
            RedoButton.IsEnabled = _redoStack.Count > 0;
            ClearButton.IsEnabled = _document.Strokes.Count > 0 || _activeStroke is not null;
        }

        private interface IBoardCommand
        {
            void Do(BoardDocument document);

            void Undo(BoardDocument document);
        }

        private sealed class AddStrokeCommand(Stroke stroke) : IBoardCommand
        {
            private readonly Stroke _stroke = stroke;
            private int? _index;

            public void Do(BoardDocument document)
            {
                _index ??= document.Strokes.Count;
                document.Strokes.Insert(_index.Value, _stroke);
            }

            public void Undo(BoardDocument document)
            {
                if (_index is int index && index >= 0 && index < document.Strokes.Count && ReferenceEquals(document.Strokes[index], _stroke))
                {
                    document.Strokes.RemoveAt(index);
                    return;
                }

                document.Strokes.Remove(_stroke);
            }
        }

        private sealed class ClearCommand(List<Stroke> snapshot) : IBoardCommand
        {
            private readonly List<Stroke> _snapshot = snapshot;

            public void Do(BoardDocument document)
            {
                document.Strokes.Clear();
            }

            public void Undo(BoardDocument document)
            {
                document.Strokes.Clear();
                document.Strokes.AddRange(_snapshot);
            }
        }
    }
}
