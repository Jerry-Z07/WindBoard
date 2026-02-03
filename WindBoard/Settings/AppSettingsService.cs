using System;
using System.Collections.Generic;
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
    /// - 对高频更新做保存防抖，减少频繁写磁盘
    /// </summary>
    internal sealed class AppSettingsService
    {
        private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromMilliseconds(350);

        internal static AppSettingsService Instance { get; } = new(AppSettingsStore.CreateDefault());

        private readonly object _gate = new();
        private readonly AppSettingsStore _store;
        private readonly SemaphoreSlim _ioGate = new(1, 1);
        private Timer? _saveDebounceTimer;

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

        internal DockSettings GetDockSettingsSnapshot()
        {
            lock (_gate)
            {
                // 注意：DockSettings 内包含引用类型列表，这里返回“深拷贝快照”，避免外部误改内部状态。
                List<ShortcutDockItemSettings> shortcutItems = new();
                if (Current.Dock?.ShortcutItems is not null)
                {
                    foreach (ShortcutDockItemSettings item in Current.Dock.ShortcutItems)
                    {
                        shortcutItems.Add(new ShortcutDockItemSettings
                        {
                            Id = item.Id,
                            Side = item.Side,
                            Type = item.Type,
                            Path = item.Path,
                            Arguments = item.Arguments,
                            IconSource = item.IconSource,
                            IconPath = item.IconPath,
                        });
                    }
                }

                return new DockSettings
                {
                    LeftOrder = new List<string>(Current.Dock?.LeftOrder ?? DockSettingsDefaults.LeftOrder),
                    ToolsOrder = new List<string>(Current.Dock?.ToolsOrder ?? DockSettingsDefaults.ToolsOrder),
                    UndoRedoOrder = new List<string>(Current.Dock?.UndoRedoOrder ?? DockSettingsDefaults.UndoRedoOrder),
                    PagesOrder = new List<string>(Current.Dock?.PagesOrder ?? DockSettingsDefaults.PagesOrder),
                    IsUndoRedoVisible = Current.Dock?.IsUndoRedoVisible ?? true,
                    IsShortcutDocksVisible = Current.Dock?.IsShortcutDocksVisible ?? false,
                    ShortcutItems = shortcutItems,
                };
            }
        }

        internal PenSettingsSnapshot GetPenSettingsSnapshot()
        {
            lock (_gate)
            {
                PenSettings? pen = Current.Writing?.Pen;

                List<string?> palette = pen?.PaletteHexes is null
                    ? new List<string?>(PenSettingsDefaults.DefaultPaletteHexes)
                    : new List<string?>(pen.PaletteHexes);

                float[] thicknessPresets;
                if (pen?.ThicknessPresets is { Count: 3 } presets)
                {
                    thicknessPresets = [presets[0], presets[1], presets[2]];
                }
                else
                {
                    thicknessPresets =
                    [
                        PenSettingsDefaults.DefaultThicknessPresets[0],
                        PenSettingsDefaults.DefaultThicknessPresets[1],
                        PenSettingsDefaults.DefaultThicknessPresets[2],
                    ];
                }

                return new PenSettingsSnapshot
                {
                    PaletteHexes = palette,
                    ThicknessPresets = thicknessPresets,
                    UseThicknessSlider = pen?.UseThicknessSlider ?? false,
                };
            }
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
                // 这里只做“快照”，不在这里做归一化：
                // - 归一化由 Update/Load 保证；
                // - 落盘前 Store.Save 会再次归一化兜底。
                snapshot = AppSettingsCloner.Clone(Current);
            }

            return SaveInternalAsync(snapshot, cancellationToken);
        }

        private void RequestSaveDebounced()
        {
            lock (_gate)
            {
                // 高频 Update 如果用 CancellationToken + Task.Delay 做防抖，
                // 频繁取消会抛出 TaskCanceledException（即使被 catch，调试输出仍会提示“异常已引发”）。
                // 这里改为 Timer 防抖：仅在一段时间内没有新的更新时才触发保存，不产生取消异常噪音。
                _saveDebounceTimer ??= new Timer(
                    static state => ((AppSettingsService)state!).OnSaveDebounceTimerTick(),
                    this,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);

                _saveDebounceTimer.Change(SaveDebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnSaveDebounceTimerTick()
        {
            // Timer 回调不能直接 async/await：转为后台任务，并确保异常被观察到（避免影响主流程）。
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveAsync().ConfigureAwait(false);
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

    }
}
