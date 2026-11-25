using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WindBoard.Features.Dock.Models;
using WindBoard.Features.Dock.Services;
using WindBoard.Fonts;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Settings;
using WindBoard.UI.Common;

namespace WindBoard.Features.Dock.UI
{
    /// <summary>
    /// 主界面 Dock 的就地编辑层：遮罩、工具栏、四组排序与快捷入口编辑。
    /// </summary>
    public sealed partial class DockEditOverlay : UserControl
    {
        private const int MaxShortcutItems = 5;

        private readonly List<ShortcutDockItemSettings> _shortcutItems = new();
        private IntPtr _hostWindowHandle;
        private bool _isEditing;
        private bool _isSyncing;

        public ObservableCollection<DockEditItemViewModel> LeftItems { get; } = new();

        public ObservableCollection<DockEditItemViewModel> ToolsItems { get; } = new();

        public ObservableCollection<DockEditItemViewModel> UndoRedoItems { get; } = new();

        public ObservableCollection<DockEditItemViewModel> PagesItems { get; } = new();

        public ObservableCollection<ShortcutDockEditItemViewModel> LeftShortcutItems { get; } = new();

        public ObservableCollection<ShortcutDockEditItemViewModel> RightShortcutItems { get; } = new();

        public bool IsEditing => _isEditing;

        internal event EventHandler? EditModeExited;

        public DockEditOverlay()
        {
            InitializeComponent();
        }

        internal void EnterEditMode(IntPtr hostWindowHandle)
        {
            _hostWindowHandle = hostWindowHandle;
            LoadFromSettings(AppSettingsService.Instance.GetDockSettingsSnapshot());

            _isEditing = true;
            Visibility = Visibility.Visible;

            // 等待编辑层完成布局后再聚焦，保证 Esc 能立刻退出。
            if (!DispatcherQueue.TryEnqueue(() => DoneButton.Focus(FocusState.Programmatic)))
            {
                DoneButton.Focus(FocusState.Programmatic);
            }
        }

        internal void ExitEditMode()
        {
            if (!_isEditing)
            {
                return;
            }

            _isEditing = false;
            Visibility = Visibility.Collapsed;
            EditModeExited?.Invoke(this, EventArgs.Empty);
        }

        private void LoadFromSettings(DockSettings dock)
        {
            _isSyncing = true;
            try
            {
                HashSet<string> hiddenIds = new(dock.HiddenItems ?? new List<string>(), StringComparer.Ordinal);

                ResetCollection(LeftItems, dock.LeftOrder.Select(id => CreateItem(id, hiddenIds)));
                ResetCollection(ToolsItems, dock.ToolsOrder.Select(id => CreateItem(id, hiddenIds)));
                ResetCollection(UndoRedoItems, dock.UndoRedoOrder.Select(id => CreateItem(id, hiddenIds)));
                ResetCollection(PagesItems, dock.PagesOrder.Select(id => CreateItem(id, hiddenIds)));

                _shortcutItems.Clear();
                if (dock.ShortcutItems is not null)
                {
                    _shortcutItems.AddRange(dock.ShortcutItems);
                }

                RebuildShortcutCollections();

                ShortcutDocksVisibleToggle.IsOn = dock.IsShortcutDocksVisible;
                UpdateShortcutGroupsVisibility();
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private static void ResetCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();
            foreach (T item in items)
            {
                target.Add(item);
            }
        }

        private static DockEditItemViewModel CreateItem(string id, IReadOnlySet<string> hiddenIds)
        {
            string title;
            UIElement icon;
            switch (id)
            {
                case DockItemIds.More:
                    title = L10n.Get("Common_More");
                    icon = new SymbolIcon { Symbol = Symbol.More };
                    break;
                case DockItemIds.Minimize:
                    title = L10n.Get("Common_Minimize");
                    icon = new SymbolIcon { Symbol = Symbol.BackToWindow };
                    break;
                case DockItemIds.Import:
                    title = L10n.Get("Common_Import");
                    icon = new SymbolIcon { Symbol = Symbol.Download };
                    break;

                case DockItemIds.ToolSelect:
                    title = L10n.Get("Tool_Select");
                    icon = new SymbolIcon { Symbol = Symbol.TouchPointer };
                    break;
                case DockItemIds.ToolPen:
                    title = L10n.Get("Tool_Pen");
                    icon = new SymbolIcon { Symbol = Symbol.Edit };
                    break;
                case DockItemIds.ToolEraser:
                    title = L10n.Get("Tool_Eraser");
                    icon = new FontIcon
                    {
                        FontFamily = new FontFamily(SegoeFluentIconsFontLoader.EffectiveIconFontFamilyName),
                        FontSize = 20,
                        Glyph = "\uE75C",
                    };
                    break;
                case DockItemIds.ToolShape:
                    title = L10n.Get("Tool_Shape");
                    // 与主 Dock 的 ShapeToolIconPath 完全一致：Segoe Fluent Icons 没有 Shapes 语义，必须自绘。
                    icon = CreateShapeIcon();
                    break;

                case DockItemIds.Undo:
                    title = L10n.Get("Common_Undo");
                    icon = new SymbolIcon { Symbol = Symbol.Undo };
                    break;
                case DockItemIds.Redo:
                    title = L10n.Get("Common_Redo");
                    icon = new SymbolIcon { Symbol = Symbol.Redo };
                    break;

                case DockItemIds.PagePrev:
                    title = L10n.Get("Common_PreviousPage");
                    icon = new SymbolIcon { Symbol = Symbol.Back };
                    break;
                case DockItemIds.PageIndicator:
                    title = L10n.Get("Common_PageNumber");
                    icon = new FontIcon
                    {
                        FontFamily = new FontFamily(SegoeFluentIconsFontLoader.EffectiveIconFontFamilyName),
                        FontSize = 20,
                        Glyph = "\uE8A7",
                    };
                    break;
                case DockItemIds.PageNext:
                    title = L10n.Get("Common_NextPage");
                    icon = new SymbolIcon { Symbol = Symbol.Forward };
                    break;
                case DockItemIds.PageAdd:
                    title = L10n.Get("Common_Add");
                    icon = new SymbolIcon { Symbol = Symbol.Add };
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, L10n.Get("Settings_Dock_UnknownItemId_Message"));
            }

            return new DockEditItemViewModel(id, title, icon, DockLayoutRules.CanHide(id), hiddenIds.Contains(id));
        }

        /// <summary>
        /// 主 Dock 的形状图标是自绘几何（矩形 + 圆），这里复用同一组坐标与描边参数，
        /// 保证编辑层与真实 Dock 的图标一致。
        /// </summary>
        private static UIElement CreateShapeIcon()
        {
            var geometry = new GeometryGroup();
            geometry.Children.Add(new RectangleGeometry { Rect = new Rect(10.5, 1.5, 8, 8) });
            geometry.Children.Add(new EllipseGeometry { Center = new Point(5.5, 14.5), RadiusX = 4, RadiusY = 4 });

            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Width = 20,
                Height = 20,
                Data = geometry,
                Stroke = new SolidColorBrush(Color.FromArgb(0xE4, 0, 0, 0)),
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
        }

        private void RebuildShortcutCollections()
        {
            ResetCollection(
                LeftShortcutItems,
                _shortcutItems
                    .Where(item => !string.Equals(item.Side, ShortcutDockSides.Right, StringComparison.Ordinal))
                    .Select(item => new ShortcutDockEditItemViewModel(item)));

            ResetCollection(
                RightShortcutItems,
                _shortcutItems
                    .Where(item => string.Equals(item.Side, ShortcutDockSides.Right, StringComparison.Ordinal))
                    .Select(item => new ShortcutDockEditItemViewModel(item)));

            UpdateShortcutAddButtons();
            UpdateShortcutGroupsVisibility();
        }

        /// <summary>
        /// 没有快捷入口且关闭显示时收起两侧编辑区，避免在窄窗口占用主 Dock 的空间；
        /// 打开工具栏开关或已有入口时始终显示，便于管理。
        /// </summary>
        private void UpdateShortcutGroupsVisibility()
        {
            Visibility visibility = ShortcutDocksVisibleToggle.IsOn || _shortcutItems.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            LeftShortcutDockGroup.Visibility = visibility;
            RightShortcutDockGroup.Visibility = visibility;
        }

        private void UpdateShortcutAddButtons()
        {
            bool canAdd = _shortcutItems.Count < MaxShortcutItems;
            AddLeftShortcutButton.IsEnabled = canAdd;
            AddRightShortcutButton.IsEnabled = canAdd;

            string tooltip = canAdd
                ? L10n.Get("Dock_Edit_AddShortcut_Tooltip")
                : L10n.Get("Dock_Edit_MaxShortcuts_Hint");
            ToolTipService.SetToolTip(AddLeftShortcutButton, tooltip);
            ToolTipService.SetToolTip(AddRightShortcutButton, tooltip);
        }

        private void PersistLayout()
        {
            if (_isSyncing)
            {
                return;
            }

            AppSettingsService.Instance.Update(s =>
            {
                s.Dock ??= new DockSettings();
                s.Dock.LeftOrder = LeftItems.Select(item => item.Id).ToList();
                s.Dock.ToolsOrder = ToolsItems.Select(item => item.Id).ToList();
                s.Dock.UndoRedoOrder = UndoRedoItems.Select(item => item.Id).ToList();
                s.Dock.PagesOrder = PagesItems.Select(item => item.Id).ToList();
                s.Dock.HiddenItems = EnumerateBuiltInItems()
                    .Where(item => item.IsHidden)
                    .Select(item => item.Id)
                    .ToList();
                s.Dock.IsShortcutDocksVisible = ShortcutDocksVisibleToggle.IsOn;
                s.Dock.ShortcutItems = _shortcutItems.Select(CloneShortcut).ToList();
            });
        }

        private IEnumerable<DockEditItemViewModel> EnumerateBuiltInItems()
        {
            foreach (DockEditItemViewModel item in LeftItems)
            {
                yield return item;
            }

            foreach (DockEditItemViewModel item in ToolsItems)
            {
                yield return item;
            }

            foreach (DockEditItemViewModel item in UndoRedoItems)
            {
                yield return item;
            }

            foreach (DockEditItemViewModel item in PagesItems)
            {
                yield return item;
            }
        }

        private static ShortcutDockItemSettings CloneShortcut(ShortcutDockItemSettings item)
        {
            return new ShortcutDockItemSettings
            {
                Id = item.Id,
                Side = item.Side,
                Type = item.Type,
                DisplayName = item.DisplayName,
                Path = item.Path,
                Arguments = item.Arguments,
                IconSource = item.IconSource,
                IconPath = item.IconPath,
                IconSymbol = item.IconSymbol,
            };
        }

        private void OnShortcutDocksVisibleToggled(object sender, RoutedEventArgs e)
        {
            if (!_isSyncing)
            {
                UpdateShortcutGroupsVisibility();
                PersistLayout();
            }
        }

        private void OnHideItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: DockEditItemViewModel item })
            {
                item.IsHidden = true;
                PersistLayout();
            }
        }

        private void OnRestoreItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: DockEditItemViewModel item })
            {
                item.IsHidden = false;
                PersistLayout();
            }
        }

        private void OnDockItemsDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            PersistLayout();
        }

        private void OnDockItemsKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_isSyncing || sender is not ListViewBase list)
            {
                return;
            }

            if (e.Key is not (VirtualKey.Left or VirtualKey.Right))
            {
                return;
            }

            bool controlDown = InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down);
            if (!controlDown || list.SelectedItem is not DockEditItemViewModel item)
            {
                return;
            }

            ObservableCollection<DockEditItemViewModel>? items = (list.Tag as string) switch
            {
                "left" => LeftItems,
                "tools" => ToolsItems,
                "undoRedo" => UndoRedoItems,
                "pages" => PagesItems,
                _ => null,
            };
            if (items is null)
            {
                return;
            }

            int index = items.IndexOf(item);
            int targetIndex = e.Key == VirtualKey.Left ? index - 1 : index + 1;
            if (index < 0 || targetIndex < 0 || targetIndex >= items.Count)
            {
                return;
            }

            e.Handled = true;
            items.Move(index, targetIndex);
            list.SelectedIndex = targetIndex;
            PersistLayout();

            if (list.ContainerFromIndex(targetIndex) is ListViewItem container)
            {
                container.Focus(FocusState.Keyboard);
            }
        }

        private async void OnResetClicked(object sender, RoutedEventArgs e)
        {
            XamlRoot? xamlRoot = XamlRoot;
            if (xamlRoot is null)
            {
                return;
            }

            bool confirmed = await DialogHelpers.ShowConfirmAsync(
                xamlRoot,
                L10n.Get("Settings_Dock_ResetLayout_Button"),
                L10n.Get("Settings_Dock_ResetConfirm_Content"),
                L10n.Get("Common_ResetToDefault"),
                L10n.Get("Common_Cancel"));
            if (!confirmed)
            {
                return;
            }

            AppSettingsService.Instance.Update(s => s.Dock = new DockSettings());
            LoadFromSettings(AppSettingsService.Instance.GetDockSettingsSnapshot());
        }

        private void OnDoneClicked(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
        }

        private void OnOverlayKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                ExitEditMode();
            }
        }

        private async void OnAddShortcutMenuItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string tag })
            {
                return;
            }

            string[] parts = tag.Split(':');
            if (parts.Length != 2)
            {
                return;
            }

            string side = string.Equals(parts[0], "right", StringComparison.Ordinal)
                ? ShortcutDockSides.Right
                : ShortcutDockSides.Left;
            string type = parts[1];

            if (_shortcutItems.Count >= MaxShortcutItems)
            {
                await ShowShortcutFullMessageAsync();
                return;
            }

            if (string.Equals(type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
            {
                await AddLinkShortcutAsync(side);
                return;
            }

            StorageFile? file = await PickShortcutFileAsync(type);
            if (file is null)
            {
                return;
            }

            AddShortcut(side, file.Path, type);
        }

        private async Task AddLinkShortcutAsync(string side)
        {
            var draft = new ShortcutDockItemSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Side = side,
                Type = ShortcutDockItemTypes.Link,
                Path = string.Empty,
            };

            var editor = new ShortcutDockItemEditorViewModel(draft);
            if (await ShowShortcutEditorAsync(editor))
            {
                AddShortcut(editor.ToSettings());
            }
        }

        private async Task<bool> ShowShortcutEditorAsync(ShortcutDockItemEditorViewModel editor)
        {
            XamlRoot? xamlRoot = XamlRoot;
            if (xamlRoot is null)
            {
                return false;
            }

            var dialog = new ShortcutDockEditDialog(editor, _hostWindowHandle)
            {
                XamlRoot = xamlRoot,
            };

            try
            {
                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            }
            catch (Exception ex)
            {
                AppLog.Error("Dock", "显示快捷入口编辑对话框失败。", ex);
                return false;
            }
        }

        private async Task<StorageFile?> PickShortcutFileAsync(string type)
        {
            if (_hostWindowHandle == IntPtr.Zero)
            {
                await ShowShortcutErrorAsync(L10n.Get("Common_WindowHandleFailed_Message"));
                return null;
            }

            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _hostWindowHandle);
                picker.FileTypeFilter.Clear();

                if (string.Equals(type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
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

                return await picker.PickSingleFileAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Dock", "打开快捷入口文件选择器失败。", ex);
                await ShowShortcutErrorAsync(ex.Message);
                return null;
            }
        }

        private void OnRemoveShortcutClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ShortcutDockEditItemViewModel item })
            {
                RemoveShortcut(item);
            }
        }

        private void OnDeleteShortcutMenuClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: ShortcutDockEditItemViewModel item })
            {
                RemoveShortcut(item);
            }
        }

        private void RemoveShortcut(ShortcutDockEditItemViewModel item)
        {
            ShortcutDockItemSettings? existing = _shortcutItems.FirstOrDefault(i => i.Id == item.Id);
            if (existing is null)
            {
                return;
            }

            _shortcutItems.Remove(existing);
            RebuildShortcutCollections();
            PersistLayout();
        }

        private async void OnEditShortcutMenuClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: ShortcutDockEditItemViewModel item })
            {
                await EditShortcutAsync(item);
            }
        }

        private void OnMoveShortcutLeftClicked(object sender, RoutedEventArgs e)
        {
            MoveShortcutToSide(sender, ShortcutDockSides.Left);
        }

        private void OnMoveShortcutRightClicked(object sender, RoutedEventArgs e)
        {
            MoveShortcutToSide(sender, ShortcutDockSides.Right);
        }

        private void MoveShortcutToSide(object sender, string side)
        {
            if (sender is not MenuFlyoutItem { Tag: ShortcutDockEditItemViewModel item })
            {
                return;
            }

            ShortcutDockItemSettings? existing = _shortcutItems.FirstOrDefault(i => i.Id == item.Id);
            if (existing is null || string.Equals(existing.Side, side, StringComparison.Ordinal))
            {
                return;
            }

            int index = string.Equals(side, ShortcutDockSides.Right, StringComparison.Ordinal)
                ? RightShortcutItems.Count
                : LeftShortcutItems.Count;
            MoveShortcut(existing, side, index);
        }

        private async void OnShortcutItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ShortcutDockEditItemViewModel item)
            {
                await EditShortcutAsync(item);
            }
        }

        private async Task EditShortcutAsync(ShortcutDockEditItemViewModel item)
        {
            ShortcutDockItemSettings? existing = _shortcutItems.FirstOrDefault(i => i.Id == item.Id);
            if (existing is null)
            {
                return;
            }

            var editor = new ShortcutDockItemEditorViewModel(existing);
            if (!await ShowShortcutEditorAsync(editor))
            {
                return;
            }

            ShortcutDockItemSettings updated = editor.ToSettings();
            int index = _shortcutItems.FindIndex(i => i.Id == existing.Id);
            if (index >= 0)
            {
                _shortcutItems[index] = updated;
            }

            RebuildShortcutCollections();
            PersistLayout();
        }

        private void OnShortcutDragItemsStarting(object sender, DragItemsStartingEventArgs args)
        {
            if (args.Items.FirstOrDefault() is not ShortcutDockEditItemViewModel item)
            {
                return;
            }

            args.Data.SetText(item.Id);
            args.Data.RequestedOperation = DataPackageOperation.Move;
        }

        private void OnShortcutDragOver(object sender, DragEventArgs e)
        {
            ApplyShortcutDragOver(e);
        }

        private void OnShortcutDropTargetDragOver(object sender, DragEventArgs e)
        {
            ApplyShortcutDragOver(e);
        }

        private static void ApplyShortcutDragOver(DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.Text)
                || e.DataView.Contains(StandardDataFormats.StorageItems)
                || e.DataView.Contains(StandardDataFormats.WebLink))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async void OnShortcutDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            try
            {
                await HandleShortcutDropAsync(sender, e);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Dock", "处理快捷入口拖放失败。", ex);
            }
        }

        private async void OnShortcutDropTargetDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            try
            {
                await HandleShortcutDropAsync(sender, e);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Dock", "处理快捷入口拖放失败。", ex);
            }
        }

        private async Task HandleShortcutDropAsync(object sender, DragEventArgs e)
        {
            string? side = (sender as FrameworkElement)?.Tag as string;
            if (side is null)
            {
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                string text = await e.DataView.GetTextAsync();
                ShortcutDockItemSettings? internalItem = _shortcutItems.FirstOrDefault(item => item.Id == text);
                if (internalItem is not null)
                {
                    ListView targetList = string.Equals(side, ShortcutDockSides.Right, StringComparison.Ordinal)
                        ? RightShortcutItemsListView
                        : LeftShortcutItemsListView;
                    int insertIndex = ComputeInsertIndex(targetList, e.GetPosition(targetList));
                    MoveShortcut(internalItem, side, insertIndex);
                    return;
                }

                if (ShortcutDockLaunchHelper.TryNormalizeLinkUri(text, out _))
                {
                    if (!AddShortcut(side, text, ShortcutDockItemTypes.Link))
                    {
                        await ShowShortcutFullMessageAsync();
                    }

                    return;
                }
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
                foreach (IStorageItem storageItem in items)
                {
                    if (_shortcutItems.Count >= MaxShortcutItems)
                    {
                        await ShowShortcutFullMessageAsync();
                        break;
                    }

                    string type = IsProgramPath(storageItem.Path)
                        ? ShortcutDockItemTypes.Program
                        : ShortcutDockItemTypes.File;
                    AddShortcut(side, storageItem.Path, type);
                }

                return;
            }

            if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                Uri? link = await e.DataView.GetWebLinkAsync();
                if (link is not null)
                {
                    AddShortcut(side, link.ToString(), ShortcutDockItemTypes.Link);
                }
            }
        }

        private void MoveShortcut(ShortcutDockItemSettings item, string targetSide, int targetIndex)
        {
            List<ShortcutDockItemSettings> reordered = ShortcutDockOrder.Move(
                _shortcutItems,
                item.Id,
                targetSide,
                targetIndex);

            _shortcutItems.Clear();
            _shortcutItems.AddRange(reordered);
            RebuildShortcutCollections();
            PersistLayout();
        }

        private static int ComputeInsertIndex(ListView listView, Point position)
        {
            for (int index = 0; index < listView.Items.Count; index++)
            {
                if (listView.ContainerFromIndex(index) is not ListViewItem container)
                {
                    continue;
                }

                GeneralTransform transform = container.TransformToVisual(listView);
                Rect bounds = transform.TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (position.X < bounds.Left + bounds.Width / 2)
                {
                    return index;
                }
            }

            return listView.Items.Count;
        }

        private static bool IsProgramPath(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
        }

        private bool AddShortcut(string side, string path, string type)
        {
            if (_shortcutItems.Count >= MaxShortcutItems || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            AddShortcut(new ShortcutDockItemSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Side = side,
                Type = type,
                Path = path,
            });

            return true;
        }

        private void AddShortcut(ShortcutDockItemSettings item)
        {
            _shortcutItems.Add(item);

            if (!ShortcutDocksVisibleToggle.IsOn)
            {
                _isSyncing = true;
                ShortcutDocksVisibleToggle.IsOn = true;
                _isSyncing = false;
            }

            RebuildShortcutCollections();
            PersistLayout();
        }

        private async Task ShowShortcutFullMessageAsync()
        {
            XamlRoot? xamlRoot = XamlRoot;
            if (xamlRoot is null)
            {
                return;
            }

            await DialogHelpers.ShowMessageAsync(
                xamlRoot,
                L10n.Get("Dock_Edit_MaxShortcuts_Hint"),
                L10n.Get("Dock_Edit_ShortcutFull_Message"));
        }

        private async Task ShowShortcutErrorAsync(string message)
        {
            XamlRoot? xamlRoot = XamlRoot;
            if (xamlRoot is null)
            {
                return;
            }

            await DialogHelpers.ShowMessageAsync(
                xamlRoot,
                L10n.Get("Common_OpenFailed_Title"),
                message);
        }
    }
}
