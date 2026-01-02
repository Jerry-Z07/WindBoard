using System.Collections.Generic;
using WindBoard.Models;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        public sealed class AppLanguageItem
        {
            public AppLanguage Language { get; }
            public string DisplayName { get; }

            public AppLanguageItem(AppLanguage language, string displayName)
            {
                Language = language;
                DisplayName = displayName;
            }
        }

        public IReadOnlyList<AppLanguageItem> AppLanguageItems { get; } = new[]
        {
            new AppLanguageItem(AppLanguage.English, "English"),
            new AppLanguageItem(AppLanguage.Chinese, "中文")
        };

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
    }
}

