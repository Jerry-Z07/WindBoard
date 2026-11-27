using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Features.Dock.Models;
using WindBoard.Localization;

namespace WindBoard.Features.Dock.UI
{
    /// <summary>
    /// 快捷入口编辑模型：对话框与测试共用的字段转换与校验入口。
    /// </summary>
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

            // 这里不要走属性 setter，避免初始化时触发 PropertyChanged。
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
