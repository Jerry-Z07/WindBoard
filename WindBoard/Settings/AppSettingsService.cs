using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace WindBoard.Settings
{
    /// <summary>
    /// 应用级设置服务（单例）。
    /// 
    /// 职责：
    /// - 启动时加载设置
    /// - 运行时提供更新入口并广播变更事件
    /// - 对高频更新（ColorPicker 拖动）做保存防抖，减少频繁写磁盘
    /// </summary>
    internal sealed class AppSettingsService
    {
        private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromMilliseconds(350);

        internal static AppSettingsService Instance { get; } = new(AppSettingsStore.CreateDefault());

        private readonly object _gate = new();
        private readonly AppSettingsStore _store;
        private readonly SemaphoreSlim _ioGate = new(1, 1);
        private CancellationTokenSource? _saveDebounceCts;

        internal AppSettings Current { get; private set; } = new();

        internal event EventHandler? Changed;

        private AppSettingsService(AppSettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        internal void Load()
        {
            lock (_gate)
            {
                Current = _store.LoadOrDefault();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        internal Color GetCanvasBackgroundColor()
        {
            string? hex;
            lock (_gate)
            {
                hex = Current.Appearance?.CanvasBackgroundHex;
            }

            return ColorHex.ParseOrDefault(hex, ColorHex.DefaultCanvasBackgroundColor);
        }

        internal void Update(Action<AppSettings> update)
        {
            if (update is null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            lock (_gate)
            {
                update(Current);
                AppSettingsStore.NormalizeInPlace(Current);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            RequestSaveDebounced();
        }

        internal Task SaveAsync(CancellationToken cancellationToken = default)
        {
            AppSettings snapshot;
            lock (_gate)
            {
                snapshot = Clone(Current);
            }

            return SaveInternalAsync(snapshot, cancellationToken);
        }

        private void RequestSaveDebounced()
        {
            CancellationTokenSource cts;
            lock (_gate)
            {
                _saveDebounceCts?.Cancel();
                _saveDebounceCts?.Dispose();
                _saveDebounceCts = new CancellationTokenSource();
                cts = _saveDebounceCts;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SaveDebounceDelay, cts.Token).ConfigureAwait(false);
                    await SaveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // 防抖取消：忽略
                }
                catch
                {
                    // 设置保存失败不应影响应用主流程，这里吞掉异常。
                }
            });
        }

        private async Task SaveInternalAsync(AppSettings snapshot, CancellationToken cancellationToken)
        {
            await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _store.Save(snapshot);
            }
            finally
            {
                _ioGate.Release();
            }
        }

        private static AppSettings Clone(AppSettings settings)
        {
            // 与 AppSettingsStore.CloneAndNormalize 保持一致：这里只做“快照”，不在这里做归一化。
            // 归一化由 Update/Load 保证，落盘前 Store.Save 会再次归一化兜底。
            return new AppSettings
            {
                Appearance = new AppearanceSettings
                {
                    CanvasBackgroundHex = settings.Appearance?.CanvasBackgroundHex ?? ColorHex.DefaultCanvasBackgroundHex,
                },
            };
        }
    }
}

