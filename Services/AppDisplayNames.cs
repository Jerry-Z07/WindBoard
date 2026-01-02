using WindBoard.Models;

namespace WindBoard.Services
{
    public static class AppDisplayNames
    {
        public const string EnglishName = "WindBoard";
        public const string ChineseName = "轻风白板";

        public static string GetAppName(AppLanguage language)
        {
            return language == AppLanguage.English
                ? EnglishName
                : ChineseName;
        }

        public static string GetAppNameFromSettings()
        {
            try
            {
                return GetAppName(SettingsService.Instance.GetLanguage());
            }
            catch
            {
                return ChineseName;
            }
        }
    }
}

