using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace WindBoard.UI.Common
{
    /// <summary>
    /// 清空画布滑动确认条的交互控制器，统一管理拖拽、阈值判断与回弹动画。
    /// </summary>
    internal sealed class ClearCanvasSlideController
    {
        private const double ThumbInset = 6.0;
        private const double CompleteRatio = 0.90;
        private const int ResetAnimationMs = 160;

        internal sealed class UiRefs
        {
            public FrameworkElement? Host { get; init; }

            public FrameworkElement? Thumb { get; init; }

            public TranslateTransform? ThumbTransform { get; init; }
        }

        private readonly FrameworkElement _host;
        private readonly FrameworkElement _thumb;
        private readonly TranslateTransform _thumbTransform;
        private readonly Func<bool> _canCompleteClear;
        private readonly Action _onCompleted;

        private bool _isEnabled;
        private uint? _pointerId;
        private double _pointerStartX;
        private double _thumbStartX;
        private Storyboard? _resetStoryboard;

        internal ClearCanvasSlideController(UiRefs ui, Func<bool> canCompleteClear, Action onCompleted)
        {
            if (ui is null)
            {
                throw new ArgumentNullException(nameof(ui));
            }

            _host = ui.Host ?? throw new ArgumentNullException(nameof(ui.Host));
            _thumb = ui.Thumb ?? throw new ArgumentNullException(nameof(ui.Thumb));
            _thumbTransform = ui.ThumbTransform ?? throw new ArgumentNullException(nameof(ui.ThumbTransform));
            _canCompleteClear = canCompleteClear ?? throw new ArgumentNullException(nameof(canCompleteClear));
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        }

        internal void OnPointerPressed(PointerRoutedEventArgs e)
        {
            if (!_isEnabled || _pointerId is not null)
            {
                return;
            }

            _resetStoryboard?.Stop();
            _resetStoryboard = null;

            _pointerId = e.Pointer.PointerId;
            _thumbStartX = _thumbTransform.X;
            _pointerStartX = e.GetCurrentPoint(_host).Position.X;

            _thumb.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        internal void OnPointerMoved(PointerRoutedEventArgs e)
        {
            if (_pointerId != e.Pointer.PointerId)
            {
                return;
            }

            double maxX = GetThumbMaxX();
            double currentX = e.GetCurrentPoint(_host).Position.X;
            double nextX = Math.Clamp(_thumbStartX + (currentX - _pointerStartX), 0, maxX);
            _thumbTransform.X = nextX;
            e.Handled = true;
        }

        internal void OnPointerReleased(PointerRoutedEventArgs e)
        {
            if (_pointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteGesture(shouldEvaluate: true);
            e.Handled = true;
        }

        internal void OnPointerCanceled(PointerRoutedEventArgs e)
        {
            if (_pointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteGesture(shouldEvaluate: false);
            e.Handled = true;
        }

        internal void OnPointerCaptureLost(PointerRoutedEventArgs e)
        {
            if (_pointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteGesture(shouldEvaluate: false);
            e.Handled = true;
        }

        internal void UpdateEnabledState(bool canClear)
        {
            _isEnabled = canClear;
            _thumb.IsHitTestVisible = canClear;
            _thumb.Opacity = canClear ? 1.0 : 0.55;
            _host.Opacity = canClear ? 1.0 : 0.55;

            if (!canClear && _pointerId is not null)
            {
                // 过程中状态变更时，强制结束拖动，避免滑块残留在捕获态。
                _pointerId = null;
                _thumb.ReleasePointerCaptures();
                Reset(animated: false);
            }
        }

        internal void Reset(bool animated)
        {
            _resetStoryboard?.Stop();
            _resetStoryboard = null;

            if (!animated)
            {
                _thumbTransform.X = 0;
                return;
            }

            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(ResetAnimationMs)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };

            Storyboard.SetTarget(animation, _thumbTransform);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);
            _resetStoryboard = storyboard;
            storyboard.Begin();
        }

        private void CompleteGesture(bool shouldEvaluate)
        {
            _pointerId = null;
            _thumb.ReleasePointerCaptures();

            if (shouldEvaluate)
            {
                double maxX = GetThumbMaxX();
                bool reached = maxX > 0 && _thumbTransform.X >= maxX * CompleteRatio;
                if (reached && _canCompleteClear())
                {
                    _onCompleted();
                    return;
                }
            }

            Reset(animated: true);
        }

        private double GetThumbMaxX()
        {
            double hostWidth = _host.ActualWidth > 0 ? _host.ActualWidth : _host.Width;
            double thumbWidth = _thumb.ActualWidth > 0 ? _thumb.ActualWidth : _thumb.Width;
            return Math.Max(0, hostWidth - thumbWidth - ThumbInset * 2);
        }
    }
}
