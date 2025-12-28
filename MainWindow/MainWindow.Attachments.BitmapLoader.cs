using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WindBoard
{
    public partial class MainWindow
    {
        private sealed class StaBitmapLoader : IDisposable
        {
            private readonly Thread _thread;
            private System.Windows.Threading.Dispatcher? _dispatcher;
            private readonly ManualResetEventSlim _ready = new(false);
            private readonly ManualResetEventSlim _stopped = new(false);
            private bool _disposed;

            public StaBitmapLoader()
            {
                _thread = new Thread(ThreadStart)
                {
                    IsBackground = true,
                    Name = "WindBoard.StaBitmapLoader"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait();
            }

            private void ThreadStart()
            {
                try
                {
                    _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    _ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                }
                finally
                {
                    _stopped.Set();
                }
            }

            public Task<BitmapSource> LoadAsync(string path, int decodePixelWidth)
            {
                if (_disposed)
                {
                    return Task.FromException<BitmapSource>(new ObjectDisposedException(nameof(StaBitmapLoader)));
                }

                var tcs = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);

                var dispatcher = _dispatcher;
                if (dispatcher == null)
                {
                    tcs.TrySetException(new InvalidOperationException("Bitmap loader dispatcher not initialized."));
                    return tcs.Task;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        bi.StreamSource = fs;
                        if (decodePixelWidth > 0) bi.DecodePixelWidth = decodePixelWidth;
                        bi.EndInit();
                        bi.Freeze();
                        tcs.TrySetResult(bi);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }));

                return tcs.Task;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    var dispatcher = _dispatcher;
                    if (dispatcher != null)
                    {
                        dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                catch
                {
                }

                try
                {
                    if (!_stopped.Wait(2000) && _thread.IsAlive)
                    {
                        _thread.Join(TimeSpan.FromSeconds(2));
                    }
                }
                catch
                {
                }

                _ready.Dispose();
                _stopped.Dispose();
            }
        }
    }
}

