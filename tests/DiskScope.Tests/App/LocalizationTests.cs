using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DiskScope.App.Localization;

namespace DiskScope.Tests.App;

internal static class LocalizationSetup
{
    /// <summary>Assertions compare English UI text: pin the UI language whatever the machine's Windows language.</summary>
    [ModuleInitializer]
    internal static void PinEnglish() => Loc.Culture = CultureInfo.InvariantCulture;
}

public sealed partial class LocalizationTests
{
    private static Dictionary<string, string> Read(CultureInfo culture)
    {
        var rm = new ResourceManager("DiskScope.App.Resources.Strings", typeof(Loc).Assembly);
        ResourceSet set = rm.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for '{culture.Name}'");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void FrenchAndEnglish_HaveTheSameKeys()
    {
        Dictionary<string, string> en = Read(CultureInfo.InvariantCulture);
        Dictionary<string, string> fr = Read(CultureInfo.GetCultureInfo("fr"));

        Assert.Empty(en.Keys.Except(fr.Keys));
        Assert.Empty(fr.Keys.Except(en.Keys));
    }

    [Fact]
    public void FrenchAndEnglish_HaveTheSamePlaceholders()
    {
        Dictionary<string, string> en = Read(CultureInfo.InvariantCulture);
        Dictionary<string, string> fr = Read(CultureInfo.GetCultureInfo("fr"));

        foreach ((string key, string value) in en)
        {
            Assert.True(fr.TryGetValue(key, out string? translated), key);
            Assert.True(Placeholders(value).SetEquals(Placeholders(translated!)), $"Placeholders differ for {key}");
        }
    }

    [Fact]
    public void Lookup_FollowsCulture_AndShowsMissingKeys()
    {
        Assert.Equal("Home", Loc.Get("Nav_Home"));
        Assert.Equal("No_Such_Key", Loc.Get("No_Such_Key"));

        CultureInfo? saved = Loc.Culture;
        try
        {
            Loc.Culture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Accueil", Loc.Get("Nav_Home"));
        }
        finally
        {
            Loc.Culture = saved;
        }
    }

    [Theory]
    [InlineData("C: used", "C: used", "C: utilisé")]
    [InlineData("GPU 0 (NVIDIA GeForce RTX 5070) usage", "GPU 0 (NVIDIA GeForce RTX 5070) usage", "GPU 0 (NVIDIA GeForce RTX 5070) : utilisation")]
    [InlineData("GPU 1 (AMD) shared memory used", "GPU 1 (AMD) shared memory used", "GPU 1 (AMD) : mémoire partagée utilisée")]
    [InlineData(": used", ": used", ": used")]
    [InlineData("Disk read", "Disk read", "Disk read")]
    public void DynamicMetricNames_TranslateOnlyTheSuffix(string name, string english, string french)
    {
        Assert.Equal(english, Loc.TranslateSuffix(name));

        CultureInfo? saved = Loc.Culture;
        try
        {
            Loc.Culture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(french, Loc.TranslateSuffix(name));
        }
        finally
        {
            Loc.Culture = saved;
        }
    }

    private static HashSet<string> Placeholders(string s) =>
        PlaceholderRegex().Matches(s).Select(m => m.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderRegex();
}
