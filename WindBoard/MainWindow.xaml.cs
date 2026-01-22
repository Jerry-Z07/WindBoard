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

        private ID2D1SolidColorBrush? _strokeBrush;

        private readonly Stack<IBoardCommand> _undoStack = new();
        private readonly Stack<IBoardCommand> _redoStack = new();

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

            UndoButton.Click += (_, _) => { DiscardActiveStroke(); Undo(); };
            RedoButton.Click += (_, _) => { DiscardActiveStroke(); Redo(); };
            ClearButton.Click += (_, _) => { DiscardActiveStroke(); ClearAll(); };
            UpdateCommandStates();

            Closed += (_, _) =>
            {
                CompositionTarget.Rendering -= OnRendering;
                _strokeBrush?.Dispose();
                _strokeBrush = null;
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

            foreach (var stroke in _document.Strokes)
            {
                DrawStroke(ctx, stroke);
            }

            if (_activeStroke is not null)
            {
                DrawStroke(ctx, _activeStroke);
            }
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

        private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_activePointerId is not null)
            {
                return;
            }

            PointerPoint point = e.GetCurrentPoint(CanvasPanel);
            if (!ShouldStartStroke(e.Pointer, point))
            {
                return;
            }

            CanvasPanel.CapturePointer(e.Pointer);
            _activePointerId = e.Pointer.PointerId;

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
            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CommitActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            DiscardActiveStroke();
            e.Handled = true;
        }

        private void OnCanvasPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_activePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CommitActiveStroke();
            e.Handled = true;
        }

        private void AppendPoint(Stroke stroke, Pointer pointer, PointerPoint point)
        {
            Vector2 pos = new((float)point.Position.X, (float)point.Position.Y);
            float pressure = GetNormalizedPressure(pointer.PointerDeviceType, point.Properties);

            if (stroke.Points.Count > 0)
            {
                Vector2 last = stroke.Points[^1].Position;
                if (Vector2.DistanceSquared(last, pos) < 0.25f)
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
            CanvasPanel.ReleasePointerCaptures();
        }

        private void DiscardActiveStroke()
        {
            _activeStroke = null;
            _activePointerId = null;
            CanvasPanel.ReleasePointerCaptures();
            UpdateCommandStates();
        }

        private static bool ShouldStartStroke(Pointer pointer, PointerPoint point)
        {
            if (pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                return point.Properties.IsLeftButtonPressed;
            }

            return true;
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
