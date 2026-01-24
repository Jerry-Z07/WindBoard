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
        private bool _isRenderingLoopActive;
        private bool _isRenderQueued;

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

            _session.StateChanged += OnSessionStateChanged;
            _input.StateChanged += OnInputStateChanged;
            _input.FrameInvalidated += OnFrameInvalidated;
            _input.InteractionStateChanged += OnInteractionStateChanged;

            UpdateViewportSize();

            _isInitialized = true;

            RaiseCommandStateChanged();
            RequestRender();
        }

        private void OnXamlRootChanged(Microsoft.UI.Xaml.XamlRoot sender, XamlRootChangedEventArgs args)
        {
            _renderer?.Resize();
            RequestRender();
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateViewportSize();
            _renderer?.Resize();
            RequestRender();
        }

        private void OnCanvasCompositionScaleChanged(SwapChainPanel sender, object args)
        {
            _renderer?.Resize();
            RequestRender();
        }

        private void UpdateViewportSize()
        {
            float w = (float)Math.Max(1.0, CanvasPanel.ActualWidth);
            float h = (float)Math.Max(1.0, CanvasPanel.ActualHeight);
            _viewport.UpdateViewportSize(new Vector2(w, h));
        }

        private void OnRendering(object? sender, object e)
        {
            RenderFrame();
        }

        private void RaiseCommandStateChanged()
        {
            CommandStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSessionStateChanged()
        {
            RaiseCommandStateChanged();
            RequestRender();
        }

        private void OnInputStateChanged()
        {
            RaiseCommandStateChanged();
            RequestRender();
        }

        private void OnFrameInvalidated()
        {
            RequestRender();
        }

        private void OnInteractionStateChanged(bool isInteracting)
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            if (isInteracting)
            {
                // 书写时保持全分辨率，避免笔迹模糊；仅在平移/捏合缩放等视口操作时降低分辨率以减轻 GPU 压力。
                if (_input?.ActiveStroke is not null)
                {
                    SetRenderingLoopActive(false);
                    _renderer.SetInteractiveMode(false);
                    RequestRender();
                    return;
                }

                _renderer.SetInteractiveMode(true);
                SetRenderingLoopActive(true);
                return;
            }

            SetRenderingLoopActive(false);
            _renderer.SetInteractiveMode(false);
            RequestRender();
        }

        private void SetRenderingLoopActive(bool active)
        {
            if (!_isInitialized || _isRenderingLoopActive == active)
            {
                return;
            }

            if (active)
            {
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
            }

            _isRenderingLoopActive = active;
        }

        private void RequestRender()
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            if (_isRenderingLoopActive)
            {
                return;
            }

            if (_isRenderQueued)
            {
                return;
            }

            _isRenderQueued = true;
            bool enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                _isRenderQueued = false;

                if (!_isInitialized || _renderer is null || _isRenderingLoopActive)
                {
                    return;
                }

                RenderFrame();
            });

            if (!enqueued)
            {
                _isRenderQueued = false;

                if (!_isInitialized || _renderer is null || _isRenderingLoopActive)
                {
                    return;
                }

                RenderFrame();
            }
        }

        private void RenderFrame()
        {
            if (!_isInitialized || _renderer is null)
            {
                return;
            }

            _renderer.Render(ctx =>
            {
                _sceneRenderer.Draw(ctx, _session.Document, _input?.ActiveStroke, _viewport);
            });
        }

        public void Dispose()
        {
            if (!_isInitialized)
            {
                return;
            }

            SetRenderingLoopActive(false);

            CanvasPanel.SizeChanged -= OnCanvasSizeChanged;
            CanvasPanel.CompositionScaleChanged -= OnCanvasCompositionScaleChanged;

            if (CanvasPanel.XamlRoot is not null)
            {
                CanvasPanel.XamlRoot.Changed -= OnXamlRootChanged;
            }

            _session.StateChanged -= OnSessionStateChanged;

            if (_input is not null)
            {
                _input.StateChanged -= OnInputStateChanged;
                _input.FrameInvalidated -= OnFrameInvalidated;
                _input.InteractionStateChanged -= OnInteractionStateChanged;
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
