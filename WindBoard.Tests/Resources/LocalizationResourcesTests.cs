using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WindBoard;

namespace WindBoard.Tests.Resources;

public sealed class LocalizationResourcesTests
{
    [StaFact]
    public void StringsDictionaries_ContainSameKeys()
    {
        EnsureApplication();

        ResourceDictionary chinese = LoadStringsDictionary("zh-CN");
        ResourceDictionary english = LoadStringsDictionary("en-US");

        HashSet<string> chineseKeys = chinese.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);
        HashSet<string> englishKeys = english.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);

        Assert.Empty(chineseKeys.Except(englishKeys));
        Assert.Empty(englishKeys.Except(chineseKeys));
    }

    private static void EnsureApplication()
    {
        if (Application.Current is not null)
        {
            return;
        }

        _ = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
    }

    private static ResourceDictionary LoadStringsDictionary(string cultureName)
    {
        string assemblyName = typeof(App).Assembly.GetName().Name ?? "WindBoard";
        string source = $"/{assemblyName};component/Resources/Strings.{cultureName}.xaml";
        return new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
    }
}

