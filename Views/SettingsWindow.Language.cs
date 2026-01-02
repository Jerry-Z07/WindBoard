using System.Collections.ObjectModel;
using System.ComponentModel;
using WindBoard.Models;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        public sealed class AppLanguageItem : INotifyPropertyChanged
        {
            public AppLanguage Language { get; }

            private string _displayName;
            public string DisplayName
            {
                get => _displayName;
                set
                {
                    if (_displayName != value)
                    {
                        _displayName = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public AppLanguageItem(AppLanguage language, string displayName)
            {
                Language = language;
                _displayName = displayName;
            }
        }

        private readonly ObservableCollection<AppLanguageItem> _appLanguageItems = new();
        public ObservableCollection<AppLanguageItem> AppLanguageItems => _appLanguageItems;

        public AppLanguage AppLanguage
        {
            get => _appLanguage;
            set
            {
                if (_appLanguage != value)
                {
                    _appLanguage = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetLanguage(value); } catch { }
                }
            }
        }

        private void RefreshAppLanguageItems()
        {
            var l = LocalizationService.Instance;
            string english = l.GetString("Language_Display_English");
            string chinese = l.GetString("Language_Display_Chinese");

            if (_appLanguageItems.Count == 0)
            {
                _appLanguageItems.Add(new AppLanguageItem(AppLanguage.English, english));
                _appLanguageItems.Add(new AppLanguageItem(AppLanguage.Chinese, chinese));
                return;
            }

            foreach (AppLanguageItem item in _appLanguageItems)
            {
                item.DisplayName = item.Language == AppLanguage.English ? english : chinese;
            }
        }
    }
}
