using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindBoard.Board.Editing;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using WindBoard.Rendering;
using WindBoard.Rendering.Board;

namespace WindBoard.Controls
{
    public sealed partial class BoardCanvasControl : UserControl, IDisposable
    {
        private DxSwapChainPanelRenderer? _renderer;
        private readonly BoardSession _session = new();
        private readonly BoardViewport _viewport = new();
        private readonly BoardSceneRenderer _sceneRenderer = new();
        private BoardInputController? _input;
        private bool _isInitialized;

        public BoardCanvasControl()
        {
            InitializeComponent();

            Loaded += (_, _) => EnsureInitialized();
            Unloaded += (_, _) => Dispose();
        }

        public event EventHandler? CommandStateChanged;

        internal bool CanUndo => _session.CanUndo;

        internal bool CanRedo => _session.CanRedo;

        internal bool CanClear => _session.HasStrokes || _input?.ActiveStroke is not null;

        internal void Undo()
        {
            _input?.DiscardActiveStroke();
            _session.Undo();
        }

        internal void Redo()
        {
            _input?.DiscardActiveStroke();
            _session.Redo();
        }

        internal void ClearAll()
        {
            _input?.DiscardActiveStroke();
            _session.ClearAll();
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            _renderer = new DxSwapChainPanelRenderer(CanvasPanel);
            _renderer.Initialize();

            _input = new BoardInputController(CanvasPanel, _session, _viewport);
            _input.Attach();

            CanvasPanel.SizeChanged += OnCanvasSizeChanged;
            CanvasPanel.CompositionScaleChanged += OnCanvasCompositionScaleChanged;

            if (CanvasPanel.XamlRoot is not null)
            {
                CanvasPanel.XamlRoot.Changed += OnXamlRootChanged;
            }

            _session.StateChanged += RaiseCommandStateChanged;
            _input.StateChanged += RaiseCommandStateChanged;

            UpdateViewportSize();

            CompositionTarget.Rendering += OnRendering;
            _isInitialized = true;

            RaiseCommandStateChanged();
        }

        private void OnXamlRootChanged(Microsoft.UI.Xaml.XamlRoot sender, XamlRootChangedEventArgs args)
        {
            _renderer?.Resize();
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateViewportSize();
            _renderer?.Resize();
        }

        private void OnCanvasCompositionScaleChanged(SwapChainPanel sender, object args)
        {
            _renderer?.Resize();
        }

        private void UpdateViewportSize()
        {
            float w = (float)Math.Max(1.0, CanvasPanel.ActualWidth);
            float h = (float)Math.Max(1.0, CanvasPanel.ActualHeight);
            _viewport.UpdateViewportSize(new Vector2(w, h));
        }

        private void OnRendering(object? sender, object e)
        {
            if (_renderer is null)
            {
                return;
            }

            _renderer.Render(ctx =>
            {
                _sceneRenderer.Draw(ctx, _session.Document, _input?.ActiveStroke, _viewport);
            });
        }

        private void RaiseCommandStateChanged()
        {
            CommandStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (!_isInitialized)
            {
                return;
            }

            CompositionTarget.Rendering -= OnRendering;

            CanvasPanel.SizeChanged -= OnCanvasSizeChanged;
            CanvasPanel.CompositionScaleChanged -= OnCanvasCompositionScaleChanged;

            if (CanvasPanel.XamlRoot is not null)
            {
                CanvasPanel.XamlRoot.Changed -= OnXamlRootChanged;
            }

            _session.StateChanged -= RaiseCommandStateChanged;

            if (_input is not null)
            {
                _input.StateChanged -= RaiseCommandStateChanged;
                _input.Detach();
            }

            _input = null;

            _sceneRenderer.Dispose();

            _renderer?.Dispose();
            _renderer = null;

            _isInitialized = false;
        }
    }
}
