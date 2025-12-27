using System;
using System.Diagnostics;
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
        private const uint GC_PAN_WITH_SINGLE_FINGER_HORIZONTALLY = 0x00000002;
        private const uint GC_PAN_WITH_SINGLE_FINGER_VERTICALLY = 0x00000004;
        private const uint GC_PAN_WITH_GUTTER = 0x00000008;
        private const uint GC_PAN_WITH_INERTIA = 0x00000010;
        private const int GestureIdCount = 5;

        private static readonly GESTURECONFIG[] DisableConfigs =
        {
            new GESTURECONFIG { dwID = GID_ZOOM, dwWant = 0, dwBlock = GC_ALL },
            new GESTURECONFIG
            {
                dwID = GID_PAN,
                dwWant = 0,
                dwBlock = GC_ALL
                         | GC_PAN_WITH_SINGLE_FINGER_HORIZONTALLY
                         | GC_PAN_WITH_SINGLE_FINGER_VERTICALLY
                         | GC_PAN_WITH_GUTTER
                         | GC_PAN_WITH_INERTIA
            },
            new GESTURECONFIG { dwID = GID_ROTATE, dwWant = 0, dwBlock = GC_ALL },
            new GESTURECONFIG { dwID = GID_TWOFINGERTAP, dwWant = 0, dwBlock = GC_ALL },
            new GESTURECONFIG { dwID = GID_PRESSANDTAP, dwWant = 0, dwBlock = GC_ALL }
        };

        private static readonly GESTURECONFIG[] GestureIdSeeds =
        {
            new GESTURECONFIG { dwID = GID_ZOOM },
            new GESTURECONFIG { dwID = GID_PAN },
            new GESTURECONFIG { dwID = GID_ROTATE },
            new GESTURECONFIG { dwID = GID_TWOFINGERTAP },
            new GESTURECONFIG { dwID = GID_PRESSANDTAP }
        };

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
        private static extern bool GetGestureConfig(IntPtr hWnd, uint dwReserved, uint dwFlags, ref uint pcIDs, [Out] GESTURECONFIG[] pGestureConfig, int cbSize);

        public TouchGestureService()
        {
        }

        public void DisableSystemGestures(IntPtr windowHandle)
        {
            if (_originalConfigs.Length > 0) return;

            try
            {
                SaveOriginalConfig(windowHandle);

                var configs = CreateDisableConfigs();

                bool result = SetGestureConfig(windowHandle, 0, configs.Length, configs, Marshal.SizeOf(typeof(GESTURECONFIG)));

                if (!result)
                {
                    _originalConfigs = Array.Empty<GESTURECONFIG>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TouchGesture] Failed to disable system gestures: {ex}");
                _originalConfigs = Array.Empty<GESTURECONFIG>();
            }
        }

        public void RestoreSystemGestures(IntPtr windowHandle)
        {
            if (_originalConfigs.Length == 0) return;

            try
            {
                SetGestureConfig(windowHandle, 0, _originalConfigs.Length, _originalConfigs, Marshal.SizeOf(typeof(GESTURECONFIG)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TouchGesture] Failed to restore system gestures: {ex}");
            }
            finally
            {
                _originalConfigs = Array.Empty<GESTURECONFIG>();
            }
        }

        private void SaveOriginalConfig(IntPtr windowHandle)
        {
            try
            {
                var tempConfigs = CreateGestureIdBuffer();

                uint count = (uint)tempConfigs.Length;
                bool result = GetGestureConfig(windowHandle, 0, 0, ref count, tempConfigs, Marshal.SizeOf(typeof(GESTURECONFIG)));

                if (result)
                {
                    var actualCount = (int)Math.Min(count, (uint)tempConfigs.Length);
                    if (actualCount > 0)
                    {
                        _originalConfigs = new GESTURECONFIG[actualCount];
                        Array.Copy(tempConfigs, _originalConfigs, actualCount);
                    }
                    else
                    {
                        _originalConfigs = Array.Empty<GESTURECONFIG>();
                    }
                }
                else
                {
                    _originalConfigs = Array.Empty<GESTURECONFIG>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TouchGesture] Failed to read existing gesture config: {ex}");
                _originalConfigs = Array.Empty<GESTURECONFIG>();
            }
        }

        private static GESTURECONFIG[] CreateDisableConfigs()
        {
            var copy = new GESTURECONFIG[DisableConfigs.Length];
            Array.Copy(DisableConfigs, copy, DisableConfigs.Length);
            return copy;
        }

        private static GESTURECONFIG[] CreateGestureIdBuffer()
        {
            var copy = new GESTURECONFIG[GestureIdCount];
            Array.Copy(GestureIdSeeds, copy, GestureIdCount);
            return copy;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _originalConfigs = Array.Empty<GESTURECONFIG>();
        }
    }
}
