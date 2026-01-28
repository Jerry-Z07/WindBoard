using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WindBoard.Settings;

namespace WindBoard.Settings.Pages
{
    public sealed partial class DockSettingsPage : Page
    {
        private bool _isSyncingFromSettings;

        public ObservableCollection<DockItemViewModel> LeftDockItems { get; } = new();

        public ObservableCollection<DockItemViewModel> ToolsDockItems { get; } = new();

        public ObservableCollection<DockItemViewModel> UndoRedoDockItems { get; } = new();

        public ObservableCollection<DockItemViewModel> PagesDockItems { get; } = new();

        public DockSettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncUiFromSettings();
            AppSettingsService.Instance.Changed += OnSettingsChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // 设置变更可能来自非 UI 线程，这里统一切回 UI 线程刷新。
            if (!DispatcherQueue.TryEnqueue(SyncUiFromSettings))
            {
                SyncUiFromSettings();
            }
        }

        private void SyncUiFromSettings()
        {
            _isSyncingFromSettings = true;
            try
            {
                DockSettings dock = AppSettingsService.Instance.GetDockSettingsSnapshot();

                ResetCollection(LeftDockItems, dock.LeftOrder.Select(CreateItem));
                ResetCollection(ToolsDockItems, dock.ToolsOrder.Select(CreateItem));
                ResetCollection(UndoRedoDockItems, dock.UndoRedoOrder.Select(CreateItem));
                ResetCollection(PagesDockItems, dock.PagesOrder.Select(CreateItem));

                UndoRedoVisibleToggleSwitch.IsOn = dock.IsUndoRedoVisible;
                UpdateUndoRedoPreviewVisibility(dock.IsUndoRedoVisible);
            }
            finally
            {
                _isSyncingFromSettings = false;
            }
        }

        private void UpdateUndoRedoPreviewVisibility(bool isVisible)
        {
            UndoRedoSeparator.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            UndoRedoDockListView.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
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
                DockItemIds.More => new DockItemViewModel(id, "更多", new SymbolIconSource { Symbol = Symbol.More }),
                DockItemIds.Minimize => new DockItemViewModel(id, "最小化", new SymbolIconSource { Symbol = Symbol.BackToWindow }),
                DockItemIds.Import => new DockItemViewModel(id, "导入", new SymbolIconSource { Symbol = Symbol.Download }),

                DockItemIds.ToolSelect => new DockItemViewModel(id, "选择", new SymbolIconSource { Symbol = Symbol.TouchPointer }),
                DockItemIds.ToolPen => new DockItemViewModel(id, "书写", new SymbolIconSource { Symbol = Symbol.Edit }),
                DockItemIds.ToolEraser => new DockItemViewModel(id, "擦除", new FontIconSource { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE75C" }),

                DockItemIds.Undo => new DockItemViewModel(id, "撤销", new SymbolIconSource { Symbol = Symbol.Undo }),
                DockItemIds.Redo => new DockItemViewModel(id, "重做", new SymbolIconSource { Symbol = Symbol.Redo }),

                DockItemIds.PagePrev => new DockItemViewModel(id, "上一页", new SymbolIconSource { Symbol = Symbol.Back }),
                DockItemIds.PageIndicator => new DockItemViewModel(id, "页码", new FontIconSource { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE8A7" }),
                DockItemIds.PageNext => new DockItemViewModel(id, "下一页", new SymbolIconSource { Symbol = Symbol.Forward }),
                DockItemIds.PageAdd => new DockItemViewModel(id, "新增", new SymbolIconSource { Symbol = Symbol.Add }),

                _ => throw new ArgumentOutOfRangeException(nameof(id), id, "未知的 Dock 项标识符。"),
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
}
