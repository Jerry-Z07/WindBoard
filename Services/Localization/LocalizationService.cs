using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;
using WindBoard.Models;

namespace WindBoard.Services
{
    public sealed class LocalizationService
    {
        private const string StringsDictionaryPrefix = "Resources/Strings.";

        private static readonly Lazy<LocalizationService> _lazy = new(() => new LocalizationService());
        public static LocalizationService Instance => _lazy.Value;

        public event EventHandler<AppLanguage>? LanguageChanged;

        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Chinese;

        private readonly object _stringsLock = new();
        private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        private AppLanguage? _stringsLanguage;

        private LocalizationService()
        {
        }

        public void ApplyLanguage(AppLanguage language)
        {
            CurrentLanguage = language;

            CultureInfo culture = GetCulture(language);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            TryLoadStrings(language);

            var app = Application.Current;
            if (app != null)
            {
                SwapStringsDictionary(app, language);
            }

            LanguageChanged?.Invoke(this, language);
        }

        public string GetString(string key)
        {
            EnsureStringsLoaded();
            IReadOnlyDictionary<string, string> strings = Volatile.Read(ref _strings);
            if (strings.TryGetValue(key, out string? value))
            {
                return value;
            }

            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                return key;
            }

            var app = Application.Current;
            return app?.TryFindResource(key) as string ?? key;
        }

        public string Format(string key, params object?[] args)
        {
            string template = GetString(key);
            return string.Format(CultureInfo.CurrentUICulture, template, args);
        }

        private static CultureInfo GetCulture(AppLanguage language)
        {
            return language == AppLanguage.English
                ? CultureInfo.GetCultureInfo("en-US")
                : CultureInfo.GetCultureInfo("zh-CN");
        }

        private void EnsureStringsLoaded()
        {
            if (Volatile.Read(ref _strings).Count != 0 && _stringsLanguage == CurrentLanguage)
            {
                return;
            }

            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                return;
            }

            TryLoadStrings(CurrentLanguage);
        }

        private void TryLoadStrings(AppLanguage language)
        {
            if (Volatile.Read(ref _strings).Count != 0 && _stringsLanguage == language)
            {
                return;
            }

            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                return;
            }

            lock (_stringsLock)
            {
                if (Volatile.Read(ref _strings).Count != 0 && _stringsLanguage == language)
                {
                    return;
                }

                try
                {
                    ResourceDictionary dict = LoadStringsDictionary(language);
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (object key in dict.Keys)
                    {
                        if (key is not string stringKey)
                        {
                            continue;
                        }

                        if (dict[key] is not string stringValue)
                        {
                            continue;
                        }

                        map[stringKey] = stringValue;
                    }

                    Volatile.Write(ref _strings, map);
                    _stringsLanguage = language;
                }
                catch
                {
                    // Ignore localization load errors; fallback to key.
                }
            }
        }

        private static ResourceDictionary LoadStringsDictionary(AppLanguage language)
        {
            string assemblyName = typeof(LocalizationService).Assembly.GetName().Name ?? "WindBoard";
            string source = language == AppLanguage.English
                ? $"/{assemblyName};component/Resources/Strings.en-US.xaml"
                : $"/{assemblyName};component/Resources/Strings.zh-CN.xaml";

            return (ResourceDictionary)Application.LoadComponent(new Uri(source, UriKind.Relative));
        }

        private static void SwapStringsDictionary(Application app, AppLanguage language)
        {
            var dictionaries = app.Resources.MergedDictionaries;

            string assemblyName = typeof(LocalizationService).Assembly.GetName().Name ?? "WindBoard";
            string source = language == AppLanguage.English
                ? $"/{assemblyName};component/Resources/Strings.en-US.xaml"
                : $"/{assemblyName};component/Resources/Strings.zh-CN.xaml";
            var sourceUri = new Uri(source, UriKind.Relative);

            int existingIndex = -1;
            for (int i = 0; i < dictionaries.Count; i++)
            {
                ResourceDictionary candidate = dictionaries[i];
                if (candidate.Source == null)
                {
                    continue;
                }

                if (!candidate.Source.OriginalString.Contains(StringsDictionaryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(candidate.Source.OriginalString, source, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                existingIndex = i;
                break;
            }

            ResourceDictionary newDictionary;
            try
            {
                newDictionary = new ResourceDictionary { Source = sourceUri };
            }
            catch
            {
                return;
            }

            if (existingIndex >= 0)
            {
                dictionaries.Insert(existingIndex + 1, newDictionary);
                dictionaries.RemoveAt(existingIndex);
                return;
            }

            dictionaries.Add(newDictionary);
        }
    }
}
