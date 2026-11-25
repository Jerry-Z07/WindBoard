using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Features.Dock.Models;
using WindBoard.Features.Dock.Services;

namespace WindBoard.Features.Dock.UI
{
    /// <summary>
    /// 编辑层中的内置 Dock 项。
    /// </summary>
    public sealed class DockEditItemViewModel : INotifyPropertyChanged
    {
        private bool _isHidden;

        public string Id { get; }

        public string Title { get; }

        public UIElement Icon { get; }

        public bool CanHide { get; }

        public bool IsHidden
        {
            get => _isHidden;
            set
            {
                if (_isHidden == value)
                {
                    return;
                }

                _isHidden = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ItemOpacity));
                OnPropertyChanged(nameof(HideButtonVisibility));
                OnPropertyChanged(nameof(RestoreButtonVisibility));
                OnPropertyChanged(nameof(HiddenOutlineVisibility));
            }
        }

        public double ItemOpacity => _isHidden ? 0.35 : 1.0;

        public Visibility HideButtonVisibility => CanHide && !_isHidden ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RestoreButtonVisibility => CanHide && _isHidden ? Visibility.Visible : Visibility.Collapsed;

        public Visibility HiddenOutlineVisibility => _isHidden ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>「更多」是编辑/设置入口，用锁图标替代隐藏按钮。</summary>
        public Visibility LockIconVisibility => CanHide ? Visibility.Collapsed : Visibility.Visible;

        public string HideAutomationId => $"Dock_Edit_Hide_{Id}";

        public string RestoreAutomationId => $"Dock_Edit_Restore_{Id}";

        public event PropertyChangedEventHandler? PropertyChanged;

        public DockEditItemViewModel(string id, string title, UIElement icon, bool canHide, bool isHidden)
        {
            Id = id;
            Title = title;
            Icon = icon;
            CanHide = canHide;
            _isHidden = isHidden;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 编辑层中的快捷入口项。
    /// </summary>
    public sealed class ShortcutDockEditItemViewModel
    {
        internal ShortcutDockItemSettings Settings { get; }

        public string Id => Settings.Id;

        public string Title { get; }

        public IconSource IconSource { get; }

        internal ShortcutDockEditItemViewModel(ShortcutDockItemSettings settings)
        {
            Settings = settings;
            Title = DockSettingsApplier.GetShortcutTitle(settings);
            IconSource = CreateIcon(settings);
        }

        private static IconSource CreateIcon(ShortcutDockItemSettings item)
        {
            if (string.Equals(item.IconSource, ShortcutDockIconSources.Font, StringComparison.Ordinal)
                && Enum.TryParse(item.IconSymbol, out Symbol symbol)
                && Enum.IsDefined(symbol))
            {
                return new SymbolIconSource { Symbol = symbol };
            }

            return new SymbolIconSource { Symbol = DockSettingsApplier.GetShortcutFallbackSymbol(item) };
        }
    }
}
