using System;
using System.Runtime.InteropServices;

namespace WindBoard.Services
{
    public class TouchGestureService : IDisposable
    {
        private const uint GID_BEGIN = 1;
        private const uint GID_END = 2;
        private const uint GID_ZOOM = 3;
        private const uint GID_PAN = 4;
        private const uint GID_ROTATE = 5;
        private const uint GID_TWOFINGERTAP = 6;
        private const uint GID_PRESSANDTAP = 7;

        private const uint GC_ALL = 0x00000001;
        private const uint GC_ZOOM = 0x00000001;
        private const uint GC_PAN = 0x00000001;
        private const uint GC_PAN_WITH_SINGLE_FINGER_HORIZONTALLY = 0x00000002;
        private const uint GC_PAN_WITH_SINGLE_FINGER_VERTICALLY = 0x00000004;
        private const uint GC_PAN_WITH_GUTTER = 0x00000008;
        private const uint GC_PAN_WITH_INERTIA = 0x00000010;
        private const uint GC_ROTATE = 0x00000001;
        private const uint GC_TWOFINGERTAP = 0x00000001;
        private const uint GC_PRESSANDTAP = 0x00000001;

        private bool _isEnabled;
        private bool _disposed;
        private GESTURECONFIG[] _originalConfigs = Array.Empty<GESTURECONFIG>();

        [StructLayout(LayoutKind.Sequential)]
        private struct GESTURECONFIG
        {
            public uint dwID;
            public uint dwWant;
            public uint dwBlock;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetGestureConfig(IntPtr hWnd, int dwReserved, int cIDs, [In] GESTURECONFIG[] pGestureConfig, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetGestureConfig(IntPtr hWnd, uint dwReserved, uint dwFlags, int pcIDs, [Out] GESTURECONFIG[] pGestureConfig, int cbSize);

        public TouchGestureService()
        {
            _isEnabled = false;
        }

        public void DisableSystemGestures(IntPtr windowHandle)
        {
            if (_isEnabled) return;

            try
            {
                SaveOriginalConfig(windowHandle);

                var configs = new GESTURECONFIG[]
                {
                    new GESTURECONFIG
                    {
                        dwID = GID_ZOOM,
                        dwWant = 0,
                        dwBlock = GC_ZOOM
                    },
                    new GESTURECONFIG
                    {
                        dwID = GID_PAN,
                        dwWant = 0,
                        dwBlock = GC_PAN | GC_PAN_WITH_SINGLE_FINGER_HORIZONTALLY | GC_PAN_WITH_SINGLE_FINGER_VERTICALLY | GC_PAN_WITH_GUTTER | GC_PAN_WITH_INERTIA
                    },
                    new GESTURECONFIG
                    {
                        dwID = GID_ROTATE,
                        dwWant = 0,
                        dwBlock = GC_ROTATE
                    },
                    new GESTURECONFIG
                    {
                        dwID = GID_TWOFINGERTAP,
                        dwWant = 0,
                        dwBlock = GC_TWOFINGERTAP
                    },
                    new GESTURECONFIG
                    {
                        dwID = GID_PRESSANDTAP,
                        dwWant = 0,
                        dwBlock = GC_PRESSANDTAP
                    }
                };

                bool result = SetGestureConfig(windowHandle, 0, configs.Length, configs, Marshal.SizeOf(typeof(GESTURECONFIG)));

                if (result)
                {
                    _isEnabled = true;
                }
            }
            catch (Exception)
            {
                _isEnabled = false;
            }
        }

        public void RestoreSystemGestures(IntPtr windowHandle)
        {
            if (!_isEnabled) return;

            try
            {
                if (_originalConfigs.Length > 0)
                {
                    SetGestureConfig(windowHandle, 0, _originalConfigs.Length, _originalConfigs, Marshal.SizeOf(typeof(GESTURECONFIG)));
                }

                _isEnabled = false;
            }
            catch (Exception)
            {
                _isEnabled = false;
            }
        }

        private void SaveOriginalConfig(IntPtr windowHandle)
        {
            try
            {
                var tempConfigs = new GESTURECONFIG[5];
                tempConfigs[0] = new GESTURECONFIG { dwID = GID_ZOOM };
                tempConfigs[1] = new GESTURECONFIG { dwID = GID_PAN };
                tempConfigs[2] = new GESTURECONFIG { dwID = GID_ROTATE };
                tempConfigs[3] = new GESTURECONFIG { dwID = GID_TWOFINGERTAP };
                tempConfigs[4] = new GESTURECONFIG { dwID = GID_PRESSANDTAP };

                bool result = GetGestureConfig(windowHandle, 0, 0, tempConfigs.Length, tempConfigs, Marshal.SizeOf(typeof(GESTURECONFIG)));

                if (result)
                {
                    _originalConfigs = new GESTURECONFIG[5];
                    Array.Copy(tempConfigs, _originalConfigs, 5);
                }
            }
            catch (Exception)
            {
                _originalConfigs = Array.Empty<GESTURECONFIG>();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _originalConfigs = Array.Empty<GESTURECONFIG>();
        }
    }
}
