using System;
using System.Collections.Generic;
using WindBoard.Core.Input;
using WindBoard.Core.Modes;

namespace WindBoard.Core.Filters
{
    public class GestureEraserFilter : InputFilterBase
    {
        private readonly IInteractionMode _eraserMode;
        private readonly double _sizeThreshold;
        private readonly Func<bool> _shouldSuppressActivation;
        private readonly HashSet<int> _activeTouchIds = new();
        private bool _gestureActive;
        private int? _gesturePointerId;

        public GestureEraserFilter(
            IInteractionMode eraserMode,
            double sizeThreshold = 45.0,
            Func<bool>? shouldSuppressActivation = null)
        {
            _eraserMode = eraserMode;
            _sizeThreshold = sizeThreshold;
            _shouldSuppressActivation = shouldSuppressActivation ?? (() => false);
        }

        public override int Priority => 100;

        public override bool Handle(InputStage stage, InputEventArgs args, ModeController modeController)
        {
            if (args.DeviceType != InputDeviceType.Touch)
            {
                // 触控手势橡皮擦是“临时模式”：当其它设备开始输入时，避免误伤（例如：手掌触控触发后，笔继续写会被擦除）。
                if (_gestureActive && stage == InputStage.Down)
                {
                    ResetGesture(modeController);
                }
                return false;
            }

            int pointerId = args.PointerId ?? -1;

            if (stage == InputStage.Down)
            {
                _activeTouchIds.Add(pointerId);

                if (_shouldSuppressActivation())
                {
                    return false;
                }

                // 多指手势（缩放/平移）期间不启用“触控面积擦除”，避免书写模式被意外切到橡皮擦。
                if (_activeTouchIds.Count != 1)
                {
                    if (_gestureActive)
                    {
                        ResetGesture(modeController);
                    }
                    return false;
                }

                double size = Math.Max(args.ContactSize?.Width ?? 0, args.ContactSize?.Height ?? 0);
                if (size >= _sizeThreshold)
                {
                    _gestureActive = true;
                    _gesturePointerId = pointerId;
                    modeController.ActivateMode(_eraserMode);
                }
            }
            else if (stage == InputStage.Move)
            {
                if (_gestureActive)
                {
                    return false;
                }

                if (_shouldSuppressActivation())
                {
                    return false;
                }

                if (_activeTouchIds.Count != 1)
                {
                    return false;
                }

                // 少数设备在 TouchDown 时 ContactSize 不稳定，这里在 Move 做一次兜底判定。
                double size = Math.Max(args.ContactSize?.Width ?? 0, args.ContactSize?.Height ?? 0);
                if (size >= _sizeThreshold)
                {
                    _gestureActive = true;
                    _gesturePointerId = pointerId;
                    modeController.ActivateMode(_eraserMode);
                }
            }
            else if (stage == InputStage.Up)
            {
                _activeTouchIds.Remove(pointerId);

                if (_gestureActive && _gesturePointerId == pointerId)
                {
                    ResetGesture(modeController);
                }
                else if (_activeTouchIds.Count == 0)
                {
                    _gesturePointerId = null;
                }
            }

            return false;
        }

        private void ResetGesture(ModeController modeController)
        {
            _gestureActive = false;
            _gesturePointerId = null;
            _activeTouchIds.Clear();
            modeController.ClearActiveMode();
        }
    }
}
