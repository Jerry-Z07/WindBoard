using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.Settings;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：清空画布滑动确认（滑块）相关代码。
    /// </summary>
    public sealed partial class MainWindow
    {
        private void OnClearCanvasThumbPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isClearCanvasSlideEnabled || ClearCanvasSlideThumbTransform is null || ClearCanvasSlideHost is null)
            {
                return;
            }

            if (_clearCanvasSlidePointerId is not null)
            {
                return;
            }

            _clearCanvasSlideResetStoryboard?.Stop();
            _clearCanvasSlideResetStoryboard = null;

            _clearCanvasSlidePointerId = e.Pointer.PointerId;
            _clearCanvasSlideThumbStartX = ClearCanvasSlideThumbTransform.X;
            _clearCanvasSlidePointerStartX = e.GetCurrentPoint(ClearCanvasSlideHost).Position.X;

            ClearCanvasSlideThumb?.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId || ClearCanvasSlideThumbTransform is null || ClearCanvasSlideHost is null)
            {
                return;
            }

            double maxX = GetClearCanvasThumbMaxX();
            double currentX = e.GetCurrentPoint(ClearCanvasSlideHost).Position.X;
            double nextX = Math.Clamp(_clearCanvasSlideThumbStartX + (currentX - _clearCanvasSlidePointerStartX), 0, maxX);
            ClearCanvasSlideThumbTransform.X = nextX;
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteClearCanvasSlideGesture(shouldEvaluate: true);
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteClearCanvasSlideGesture(shouldEvaluate: false);
            e.Handled = true;
        }

        private void OnClearCanvasThumbPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_clearCanvasSlidePointerId != e.Pointer.PointerId)
            {
                return;
            }

            CompleteClearCanvasSlideGesture(shouldEvaluate: false);
            e.Handled = true;
        }

        private void CompleteClearCanvasSlideGesture(bool shouldEvaluate)
        {
            _clearCanvasSlidePointerId = null;
            ClearCanvasSlideThumb?.ReleasePointerCaptures();

            if (ClearCanvasSlideThumbTransform is null)
            {
                return;
            }

            if (shouldEvaluate)
            {
                double maxX = GetClearCanvasThumbMaxX();
                bool reached = maxX > 0 && ClearCanvasSlideThumbTransform.X >= maxX * ClearCanvasSlideCompleteRatio;

                // 只有达到阈值时才执行清空，同时仍可通过撤销恢复。
                if (reached && BoardCanvas.CanClear)
                {
                    BoardCanvas.ClearAll();
                    TryHideEraserFlyout();
                    return;
                }
            }

            ResetClearCanvasSlide(true);
        }

        private void UpdateClearCanvasSlideState()
        {
            if (ClearCanvasSlideThumb is null || ClearCanvasSlideHost is null)
            {
                return;
            }

            bool canClear = BoardCanvas.CanClear;
            _isClearCanvasSlideEnabled = canClear;
            ClearCanvasSlideThumb.IsHitTestVisible = canClear;
            ClearCanvasSlideThumb.Opacity = canClear ? 1.0 : 0.55;
            ClearCanvasSlideHost.Opacity = canClear ? 1.0 : 0.55;

            if (!canClear && _clearCanvasSlidePointerId is not null)
            {
                // 过程中状态变更（例如清空/撤销后没有笔迹）时，强制结束拖动，避免卡住捕获。
                _clearCanvasSlidePointerId = null;
                ClearCanvasSlideThumb.ReleasePointerCaptures();
                ResetClearCanvasSlide(false);
            }
        }

        private double GetClearCanvasThumbMaxX()
        {
            if (ClearCanvasSlideHost is null || ClearCanvasSlideThumb is null)
            {
                return 0;
            }

            double hostWidth = ClearCanvasSlideHost.ActualWidth > 0 ? ClearCanvasSlideHost.ActualWidth : ClearCanvasSlideHost.Width;
            double thumbWidth = ClearCanvasSlideThumb.ActualWidth > 0 ? ClearCanvasSlideThumb.ActualWidth : ClearCanvasSlideThumb.Width;
            return Math.Max(0, hostWidth - thumbWidth - ClearCanvasSlideThumbInset * 2);
        }

        private void ResetClearCanvasSlide(bool animated)
        {
            if (ClearCanvasSlideThumbTransform is null)
            {
                return;
            }

            _clearCanvasSlideResetStoryboard?.Stop();
            _clearCanvasSlideResetStoryboard = null;

            if (!animated)
            {
                ClearCanvasSlideThumbTransform.X = 0;
                return;
            }

            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(ClearCanvasSlideResetAnimationMs)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };

            Storyboard.SetTarget(animation, ClearCanvasSlideThumbTransform);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);
            _clearCanvasSlideResetStoryboard = storyboard;
            storyboard.Begin();
        }

    }
}
