using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WindBoard.Localization;
using WindBoard.Settings;

namespace WindBoard.Settings.Pages
{
    public sealed partial class DockSettingsPage : Page
    {
        private bool _isSyncingFromSettings;
        private bool _isSyncingShortcutDockItemsFromSettings;
        private bool _suppressNextShortcutDockItemsSyncFromSettings;
        private bool _isShortcutDockItemsDirty;
        private DispatcherQueueTimer? _shortcutDockItemsPersistTimer;

        public ObservableCollection<DockItemViewModel> LeftDockItems { get; } = new();

        public ObservableCollection<DockItemViewModel> ToolsDockItems { get; } = new();

        public ObservableCollection<DockItemViewModel> UndoRedoDockItems { get; } = new();

        public ObservableCollection<DockItemViewModel> PagesDockItems { get; } = new();

        public ObservableCollection<ShortcutDockItemEditorViewModel> ShortcutDockItems { get; } = new();

        public DockSettingsPage()
        {
            InitializeComponent();
            InitializeShortcutDockPersistTimer();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void InitializeShortcutDockPersistTimer()
        {
            // 输入框（路径/参数等）希望“边输入边保存”，但不要每次击键都触发一次设置变更：
            // 这里用 UI 线程计时器做防抖，减少刷新频率并避免出现闪烁循环。
            _shortcutDockItemsPersistTimer = DispatcherQueue.CreateTimer();
            _shortcutDockItemsPersistTimer.Interval = TimeSpan.FromMilliseconds(350);
            _shortcutDockItemsPersistTimer.IsRepeating = false;
            _shortcutDockItemsPersistTimer.Tick += (_, _) => PersistShortcutDockItemsToSettingsCore();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncUiFromSettings(includeShortcutItems: true);
            AppSettingsService.Instance.Changed += OnSettingsChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed -= OnSettingsChanged;
            DetachShortcutDockItemHandlers();

            // 页面关闭/离开时，尽量把最后一次编辑内容落盘，避免“没切焦点就退出导致没保存”。
            if (_isShortcutDockItemsDirty)
            {
                _shortcutDockItemsPersistTimer?.Stop();
                PersistShortcutDockItemsToSettingsCore();
            }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // 设置变更可能来自非 UI 线程，这里统一切回 UI 线程刷新。
            if (!DispatcherQueue.TryEnqueue(OnSettingsChangedOnUiThread))
            {
                OnSettingsChangedOnUiThread();
            }
        }

        private void OnSettingsChangedOnUiThread()
        {
            // 如果变更来自本页对“快捷入口项”的保存，不要立即用设置快照回写 UI：
            // 否则会在输入/选择时频繁重建 ItemTemplate，导致明显闪烁甚至进入刷新循环。
            if (_suppressNextShortcutDockItemsSyncFromSettings)
            {
                _suppressNextShortcutDockItemsSyncFromSettings = false;
                SyncUiFromSettings(includeShortcutItems: false);
                return;
            }

            SyncUiFromSettings(includeShortcutItems: true);
        }

        private void SyncUiFromSettings(bool includeShortcutItems)
        {
            _isSyncingFromSettings = true;
            _isSyncingShortcutDockItemsFromSettings = includeShortcutItems;
            try
            {
                DockSettings dock = AppSettingsService.Instance.GetDockSettingsSnapshot();

                ResetCollection(LeftDockItems, dock.LeftOrder.Select(CreateItem));
                ResetCollection(ToolsDockItems, dock.ToolsOrder.Select(CreateItem));
                ResetCollection(UndoRedoDockItems, dock.UndoRedoOrder.Select(CreateItem));
                ResetCollection(PagesDockItems, dock.PagesOrder.Select(CreateItem));

                UndoRedoVisibleToggleSwitch.IsOn = dock.IsUndoRedoVisible;
                UpdateUndoRedoPreviewVisibility(dock.IsUndoRedoVisible);

                ShortcutDocksVisibleToggleSwitch.IsOn = dock.IsShortcutDocksVisible;
                UpdateShortcutDockEditorVisibility(dock.IsShortcutDocksVisible);
                if (includeShortcutItems)
                {
                    DetachShortcutDockItemHandlers();
                    ResetCollection(ShortcutDockItems, dock.ShortcutItems.Select(i => new ShortcutDockItemEditorViewModel(i)));
                    AttachShortcutDockItemHandlers();
                    _isShortcutDockItemsDirty = false;
                }

                UpdateAddShortcutDockItemButtonState();
            }
            finally
            {
                _isSyncingShortcutDockItemsFromSettings = false;
                _isSyncingFromSettings = false;
            }
        }

        private void UpdateUndoRedoPreviewVisibility(bool isVisible)
        {
            UndoRedoSeparator.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            UndoRedoDockListView.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateShortcutDockEditorVisibility(bool isVisible)
        {
            ShortcutDockEditorPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnUndoRedoVisibleToggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            bool isVisible = UndoRedoVisibleToggleSwitch.IsOn;
            UpdateUndoRedoPreviewVisibility(isVisible);

            AppSettingsService.Instance.Update(s => s.Dock.IsUndoRedoVisible = isVisible);
        }

        private void OnShortcutDocksVisibleToggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            bool isVisible = ShortcutDocksVisibleToggleSwitch.IsOn;
            UpdateShortcutDockEditorVisibility(isVisible);
            UpdateAddShortcutDockItemButtonState();
            AppSettingsService.Instance.Update(s => s.Dock.IsShortcutDocksVisible = isVisible);
        }

        private void OnAddShortcutDockItemClicked(object sender, RoutedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            if (ShortcutDockItems.Count >= 5)
            {
                return;
            }

            ShortcutDockItemEditorViewModel newItem = ShortcutDockItemEditorViewModel.CreateDefault();
            newItem.PropertyChanged += OnShortcutDockItemPropertyChanged;
            ShortcutDockItems.Add(newItem);
            PersistShortcutDockItemsToSettingsNow();
            UpdateAddShortcutDockItemButtonState();
        }

        private void OnDeleteShortcutDockItemClicked(object sender, RoutedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            if (sender is not Button button || button.Tag is not ShortcutDockItemEditorViewModel item)
            {
                return;
            }

            item.PropertyChanged -= OnShortcutDockItemPropertyChanged;
            ShortcutDockItems.Remove(item);
            PersistShortcutDockItemsToSettingsNow();
            UpdateAddShortcutDockItemButtonState();
        }

        private void AttachShortcutDockItemHandlers()
        {
            foreach (ShortcutDockItemEditorViewModel item in ShortcutDockItems)
            {
                item.PropertyChanged += OnShortcutDockItemPropertyChanged;
            }
        }

        private void DetachShortcutDockItemHandlers()
        {
            foreach (ShortcutDockItemEditorViewModel item in ShortcutDockItems)
            {
                item.PropertyChanged -= OnShortcutDockItemPropertyChanged;
            }
        }

        private void OnShortcutDockItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isSyncingShortcutDockItemsFromSettings)
            {
                return;
            }

            // 任何字段变更都统一触发防抖保存，避免逐项判断。
            RequestPersistShortcutDockItemsDebounced();
        }

        private async void OnBrowseShortcutDockPathClicked(object sender, RoutedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            if (sender is not Button button || button.Tag is not ShortcutDockItemEditorViewModel item)
            {
                return;
            }

            IntPtr hwnd = TryGetHostWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // 注意：FileOpenPicker 必须至少有一个 FileTypeFilter，否则会抛异常。
            picker.FileTypeFilter.Clear();

            if (string.Equals(item.Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
            {
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add(".bat");
                picker.FileTypeFilter.Add(".cmd");
                picker.FileTypeFilter.Add(".lnk");
            }
            else
            {
                picker.FileTypeFilter.Add("*");
            }

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            item.Path = file.Path;
            PersistShortcutDockItemsToSettingsNow();
        }

        private async void OnBrowseShortcutDockIconClicked(object sender, RoutedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            if (sender is not Button button || button.Tag is not ShortcutDockItemEditorViewModel item)
            {
                return;
            }

            IntPtr hwnd = TryGetHostWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Clear();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            item.IconPath = file.Path;
            PersistShortcutDockItemsToSettingsNow();
        }

        private void RequestPersistShortcutDockItemsDebounced()
        {
            _isShortcutDockItemsDirty = true;

            if (_shortcutDockItemsPersistTimer is null)
            {
                PersistShortcutDockItemsToSettingsCore();
                return;
            }

            _shortcutDockItemsPersistTimer.Stop();
            _shortcutDockItemsPersistTimer.Start();
        }

        private void PersistShortcutDockItemsToSettingsNow()
        {
            _shortcutDockItemsPersistTimer?.Stop();
            PersistShortcutDockItemsToSettingsCore();
        }

        private void PersistShortcutDockItemsToSettingsCore()
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            _isShortcutDockItemsDirty = false;

            List<ShortcutDockItemSettings> items = ShortcutDockItems.Select(i => i.ToSettings()).ToList();

            // 保存后会触发 AppSettingsService.Changed：这里标记“忽略一次 UI 回写”，避免闪烁循环。
            _suppressNextShortcutDockItemsSyncFromSettings = true;
            AppSettingsService.Instance.Update(s => s.Dock.ShortcutItems = items);
        }

        private void UpdateAddShortcutDockItemButtonState()
        {
            AddShortcutDockItemButton.IsEnabled = ShortcutDocksVisibleToggleSwitch.IsOn && ShortcutDockItems.Count < 5;
        }

        private IntPtr TryGetHostWindowHandle()
        {
            try
            {
                // WinUI 3 桌面端 Page 无法直接拿到宿主 Window，这里用 SettingsWindow 的静态引用。
                // 如果未来设置页被放到其它窗口，可在此扩展更通用的 Window Handle 提供方式。
                if (SettingsWindow.Active is not null)
                {
                    return SettingsWindow.Active.Hwnd;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        private void OnDockItemsDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            PersistOrdersToSettings();
        }

        private void PersistOrdersToSettings()
        {
            List<string> left = LeftDockItems.Select(i => i.Id).ToList();
            List<string> tools = ToolsDockItems.Select(i => i.Id).ToList();
            List<string> undoRedo = UndoRedoDockItems.Select(i => i.Id).ToList();
            List<string> pages = PagesDockItems.Select(i => i.Id).ToList();

            AppSettingsService.Instance.Update(s =>
            {
                s.Dock.LeftOrder = left;
                s.Dock.ToolsOrder = tools;
                s.Dock.UndoRedoOrder = undoRedo;
                s.Dock.PagesOrder = pages;
            });
        }

        private void OnResetToDefaultClicked(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Update(s => s.Dock = new DockSettings());
        }

        private static void ResetCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();
            foreach (T item in items)
            {
                target.Add(item);
            }
        }

        private static DockItemViewModel CreateItem(string id)
        {
            // 这里不做“兜底 UI”的映射：设置在 Normalize 时已保证只保留合法项并补齐缺失项。
            return id switch
            {
                DockItemIds.More => new DockItemViewModel(id, L10n.Get("Common_More"), new SymbolIconSource { Symbol = Symbol.More }),
                DockItemIds.Minimize => new DockItemViewModel(id, L10n.Get("Common_Minimize"), new SymbolIconSource { Symbol = Symbol.BackToWindow }),
                DockItemIds.Import => new DockItemViewModel(id, L10n.Get("Common_Import"), new SymbolIconSource { Symbol = Symbol.Download }),

                DockItemIds.ToolSelect => new DockItemViewModel(id, L10n.Get("Tool_Select"), new SymbolIconSource { Symbol = Symbol.TouchPointer }),
                DockItemIds.ToolPen => new DockItemViewModel(id, L10n.Get("Tool_Pen"), new SymbolIconSource { Symbol = Symbol.Edit }),
                DockItemIds.ToolEraser => new DockItemViewModel(id, L10n.Get("Tool_Eraser"), new FontIconSource { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE75C" }),

                DockItemIds.Undo => new DockItemViewModel(id, L10n.Get("Common_Undo"), new SymbolIconSource { Symbol = Symbol.Undo }),
                DockItemIds.Redo => new DockItemViewModel(id, L10n.Get("Common_Redo"), new SymbolIconSource { Symbol = Symbol.Redo }),

                DockItemIds.PagePrev => new DockItemViewModel(id, L10n.Get("Common_PreviousPage"), new SymbolIconSource { Symbol = Symbol.Back }),
                DockItemIds.PageIndicator => new DockItemViewModel(id, L10n.Get("Common_PageNumber"), new FontIconSource { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE8A7" }),
                DockItemIds.PageNext => new DockItemViewModel(id, L10n.Get("Common_NextPage"), new SymbolIconSource { Symbol = Symbol.Forward }),
                DockItemIds.PageAdd => new DockItemViewModel(id, L10n.Get("Common_Add"), new SymbolIconSource { Symbol = Symbol.Add }),

                _ => throw new ArgumentOutOfRangeException(nameof(id), id, L10n.Get("Settings_Dock_UnknownItemId_Message")),
            };
        }
    }

    public sealed class DockItemViewModel
    {
        public string Id { get; }

        public string Title { get; }

        public IconSource IconSource { get; }

        public DockItemViewModel(string id, string title, IconSource iconSource)
        {
            Id = id;
            Title = title;
            IconSource = iconSource;
        }
    }

    public sealed class ShortcutDockItemEditorViewModel : INotifyPropertyChanged
    {
        private string _side = ShortcutDockSides.Left;
        private string _type = ShortcutDockItemTypes.File;
        private string _displayName = string.Empty;
        private string _path = string.Empty;
        private string _iconSource = ShortcutDockIconSources.Default;
        private string _iconPath = string.Empty;
        private string _iconSymbol = string.Empty;
        private string _arguments = string.Empty;
        private string _fontIconSearchText = string.Empty;
        private ShortcutDockFontIcon? _selectedFontIcon;

        public string Id { get; }

        public ObservableCollection<ShortcutDockFontIcon> FilteredFontIcons { get; } = new();

        public string Side
        {
            get => _side;
            set
            {
                if (string.Equals(_side, value, StringComparison.Ordinal))
                {
                    return;
                }

                _side = value ?? ShortcutDockSides.Left;
                OnPropertyChanged();
            }
        }

        public string Type
        {
            get => _type;
            set
            {
                if (string.Equals(_type, value, StringComparison.Ordinal))
                {
                    return;
                }

                _type = value ?? ShortcutDockItemTypes.File;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ArgumentsPanelVisibility));
                OnPropertyChanged(nameof(PathBrowseVisibility));
                OnPropertyChanged(nameof(PathHeader));
                OnPropertyChanged(nameof(PathPlaceholder));
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (string.Equals(_displayName, value, StringComparison.Ordinal))
                {
                    return;
                }

                _displayName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string Path
        {
            get => _path;
            set
            {
                if (string.Equals(_path, value, StringComparison.Ordinal))
                {
                    return;
                }

                _path = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string IconSource
        {
            get => _iconSource;
            set
            {
                if (string.Equals(_iconSource, value, StringComparison.Ordinal))
                {
                    return;
                }

                _iconSource = value ?? ShortcutDockIconSources.Default;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IconBrowseVisibility));
                OnPropertyChanged(nameof(IconPathTextVisibility));
                OnPropertyChanged(nameof(IconFontPanelVisibility));
                OnPropertyChanged(nameof(FontIconSelectionHint));
                OnPropertyChanged(nameof(IconHint));
            }
        }

        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (string.Equals(_iconPath, value, StringComparison.Ordinal))
                {
                    return;
                }

                _iconPath = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string IconSymbol
        {
            get => _iconSymbol;
            set
            {
                if (string.Equals(_iconSymbol, value, StringComparison.Ordinal))
                {
                    return;
                }

                _iconSymbol = value ?? string.Empty;
                OnPropertyChanged();
                SyncSelectedFontIconFromSymbol(raisePropertyChanged: true);
                OnPropertyChanged(nameof(FontIconSelectionHint));
            }
        }

        public string Arguments
        {
            get => _arguments;
            set
            {
                if (string.Equals(_arguments, value, StringComparison.Ordinal))
                {
                    return;
                }

                _arguments = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string FontIconSearchText
        {
            get => _fontIconSearchText;
            set
            {
                if (string.Equals(_fontIconSearchText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _fontIconSearchText = value ?? string.Empty;
                OnPropertyChanged();
                UpdateFontIconFilter();
            }
        }

        public ShortcutDockFontIcon? SelectedFontIcon
        {
            get => _selectedFontIcon;
            set => SetSelectedFontIcon(value);
        }

        public string PathHeader => string.Equals(Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal)
            ? L10n.Get("Common_Url")
            : L10n.Get("Common_Path");

        public string PathPlaceholder => string.Equals(Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal)
            ? L10n.Get("Common_UrlPlaceholder")
            : L10n.Get("Settings_Dock_Path_Placeholder");

        public Visibility ArgumentsPanelVisibility => string.Equals(Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility PathBrowseVisibility => string.Equals(Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public Visibility IconBrowseVisibility => string.Equals(IconSource, ShortcutDockIconSources.Icon, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility IconPathTextVisibility => string.Equals(IconSource, ShortcutDockIconSources.Icon, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility IconFontPanelVisibility => string.Equals(IconSource, ShortcutDockIconSources.Font, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

        public string FontIconSelectionHint => SelectedFontIcon is null
            ? L10n.Get("Settings_Dock_FontIcon_NotSelected")
            : L10n.Format("Settings_Dock_FontIcon_Selected_Fmt", SelectedFontIcon.Name);

        public string IconHint => string.Equals(IconSource, ShortcutDockIconSources.Icon, StringComparison.Ordinal)
            ? L10n.Get("Settings_Dock_IconHint_CustomFile")
            : string.Equals(IconSource, ShortcutDockIconSources.Font, StringComparison.Ordinal)
                ? L10n.Get("Settings_Dock_IconHint_FontIcon")
            : L10n.Get("Settings_Dock_IconHint_Default");

        public event PropertyChangedEventHandler? PropertyChanged;

        internal ShortcutDockItemEditorViewModel(ShortcutDockItemSettings settings)
        {
            Id = string.IsNullOrWhiteSpace(settings.Id) ? Guid.NewGuid().ToString("N") : settings.Id;

            // 注意：这里不要走属性 setter，避免初始化时触发 PropertyChanged，从而触发防抖保存。
            _side = string.IsNullOrWhiteSpace(settings.Side) ? ShortcutDockSides.Left : settings.Side;
            _type = string.IsNullOrWhiteSpace(settings.Type) ? ShortcutDockItemTypes.File : settings.Type;
            _displayName = settings.DisplayName ?? string.Empty;
            _path = settings.Path ?? string.Empty;
            _iconSource = string.IsNullOrWhiteSpace(settings.IconSource) ? ShortcutDockIconSources.Default : settings.IconSource;
            _iconPath = settings.IconPath ?? string.Empty;
            _iconSymbol = settings.IconSymbol ?? string.Empty;
            _arguments = settings.Arguments ?? string.Empty;
            UpdateFontIconFilter();
            SyncSelectedFontIconFromSymbol(raisePropertyChanged: false);
        }

        private ShortcutDockItemEditorViewModel(string id)
        {
            Id = id;
            UpdateFontIconFilter();
        }

        public static ShortcutDockItemEditorViewModel CreateDefault()
        {
            return new ShortcutDockItemEditorViewModel(Guid.NewGuid().ToString("N"))
            {
                Side = ShortcutDockSides.Left,
                Type = ShortcutDockItemTypes.File,
                IconSource = ShortcutDockIconSources.Default,
            };
        }

        internal ShortcutDockItemSettings ToSettings()
        {
            string side = Side?.Trim() ?? ShortcutDockSides.Left;
            string type = Type?.Trim() ?? ShortcutDockItemTypes.File;
            string displayName = DisplayName ?? string.Empty;
            string path = Path?.Trim() ?? string.Empty;
            string iconSource = IconSource?.Trim() ?? ShortcutDockIconSources.Default;
            string iconPath = IconPath?.Trim() ?? string.Empty;
            string iconSymbol = IconSymbol ?? string.Empty;
            string arguments = Arguments ?? string.Empty;

            return new ShortcutDockItemSettings
            {
                Id = Id,
                Side = side,
                Type = type,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                Path = path,
                Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim(),
                IconSource = iconSource,
                IconPath = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath,
                IconSymbol = string.IsNullOrWhiteSpace(iconSymbol) ? null : iconSymbol.Trim(),
            };
        }

        private void SetSelectedFontIcon(ShortcutDockFontIcon? value)
        {
            if (ReferenceEquals(_selectedFontIcon, value))
            {
                return;
            }

            _selectedFontIcon = value;
            _iconSymbol = value?.Symbol.ToString() ?? string.Empty;
            OnPropertyChanged(nameof(SelectedFontIcon));
            OnPropertyChanged(nameof(IconSymbol));
            OnPropertyChanged(nameof(FontIconSelectionHint));
        }

        private void SyncSelectedFontIconFromSymbol(bool raisePropertyChanged)
        {
            ShortcutDockFontIcon? match = ShortcutDockFontIconCatalog.FindBySymbolName(_iconSymbol);
            if (ReferenceEquals(_selectedFontIcon, match))
            {
                return;
            }

            _selectedFontIcon = match;
            if (raisePropertyChanged)
            {
                OnPropertyChanged(nameof(SelectedFontIcon));
            }
        }

        private void UpdateFontIconFilter()
        {
            FilteredFontIcons.Clear();

            string query = _fontIconSearchText.Trim();
            IEnumerable<ShortcutDockFontIcon> source = ShortcutDockFontIconCatalog.Icons;
            if (!string.IsNullOrWhiteSpace(query))
            {
                source = source.Where(item => item.Matches(query));
            }

            foreach (ShortcutDockFontIcon item in source)
            {
                FilteredFontIcons.Add(item);
            }

            if (_selectedFontIcon is not null && !FilteredFontIcons.Contains(_selectedFontIcon))
            {
                // 保持已选项可见，避免搜索后无法看到当前选择。
                FilteredFontIcons.Insert(0, _selectedFontIcon);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ShortcutDockFontIcon
    {
        public string Name { get; }

        public Symbol Symbol { get; }

        public ShortcutDockFontIcon(string name, Symbol symbol)
        {
            Name = name;
            Symbol = symbol;
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || Symbol.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class ShortcutDockFontIconCatalog
    {
        private static readonly IReadOnlyList<ShortcutDockFontIcon> AllIcons = BuildIcons();

        internal static IReadOnlyList<ShortcutDockFontIcon> Icons => AllIcons;

        internal static ShortcutDockFontIcon? FindBySymbolName(string? symbolName)
        {
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return null;
            }

            if (!Enum.TryParse(symbolName, out Symbol symbol))
            {
                return null;
            }

            return AllIcons.FirstOrDefault(icon => icon.Symbol == symbol);
        }

        private static IReadOnlyList<ShortcutDockFontIcon> BuildIcons()
        {
            var list = new List<ShortcutDockFontIcon>();
            foreach (Symbol symbol in Enum.GetValues<Symbol>())
            {
                list.Add(new ShortcutDockFontIcon(symbol.ToString(), symbol));
            }

            list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }
}
