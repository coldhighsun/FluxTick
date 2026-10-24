using System.Globalization;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the interface language: choosing it, looking text up, and keeping the string tables complete.
/// </summary>
public class LocalizationTests
{
    /// <summary>
    /// Verifies the automatic setting follows the system: any Chinese culture gets Simplified Chinese, the rest English.
    /// </summary>
    [Theory]
    [InlineData("zh-CN", Loc.SimplifiedChinese)]
    [InlineData("zh-TW", Loc.SimplifiedChinese)]
    [InlineData("zh-Hans", Loc.SimplifiedChinese)]
    [InlineData("en-US", Loc.English)]
    [InlineData("fr-FR", Loc.English)]
    [InlineData("", Loc.English)]
    public void Resolve_Auto_FollowsTheSystemLanguage(string culture, string expected)
    {
        Loc.Resolve(LanguageMode.Auto, CultureInfo.GetCultureInfo(culture)).Should().Be(expected);
    }

    /// <summary>
    /// Verifies an explicit choice wins over the system language.
    /// </summary>
    [Fact]
    public void Resolve_ExplicitChoice_IgnoresTheSystemLanguage()
    {
        Loc.Resolve(LanguageMode.En, CultureInfo.GetCultureInfo("zh-CN")).Should().Be(Loc.English);
        Loc.Resolve(LanguageMode.ZhCN, CultureInfo.GetCultureInfo("en-US")).Should().Be(Loc.SimplifiedChinese);
    }

    /// <summary>
    /// Verifies every English text has a Chinese one, so a new string cannot ship untranslated.
    /// </summary>
    [Fact]
    public void Tables_EveryEnglishKey_HasAChineseText()
    {
        Loc.KeysOf(Loc.English).Except(Loc.KeysOf(Loc.SimplifiedChinese)).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the Chinese table only has extra keys for the data sources, whose English comes from the providers.
    /// </summary>
    [Fact]
    public void Tables_ExtraChineseKeys_AreDataSourceTexts()
    {
        Loc.KeysOf(Loc.SimplifiedChinese).Except(Loc.KeysOf(Loc.English))
            .Should().OnlyContain(key => key.StartsWith("Provider_") || key.StartsWith("Option_"));
    }

    /// <summary>
    /// Verifies the placeholders of a text are the same in both languages.
    /// </summary>
    [Fact]
    public void Tables_Placeholders_MatchBetweenLanguages()
    {
        var english = new Loc();
        var chinese = new Loc();
        chinese.Select(Loc.SimplifiedChinese);

        foreach (var key in Loc.KeysOf(Loc.English))
        {
            Placeholders(chinese[key]).Should().BeEquivalentTo(Placeholders(english[key]), key);
        }
    }

    /// <summary>
    /// Verifies switching the language changes the text and raises both change notifications once.
    /// </summary>
    [Fact]
    public void Select_NewLanguage_ChangesTextAndRaisesEvents()
    {
        var loc = new Loc();
        var properties = new List<string?>();
        var changes = 0;
        loc.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        loc.Changed += (_, _) => changes++;
        loc["Status_Stale"].Should().Be("Stale");

        loc.Select(Loc.SimplifiedChinese).Should().BeTrue();
        loc.Select(Loc.SimplifiedChinese).Should().BeFalse();

        loc["Status_Stale"].Should().Be("数据过期");
        loc.Language.Should().Be(Loc.SimplifiedChinese);
        properties.Should().Contain("Item[]");
        changes.Should().Be(1);
    }

    /// <summary>
    /// Verifies applying a setting also sets the UI culture, and leaves the thread as it found it afterwards.
    /// </summary>
    [Fact]
    public void Apply_Setting_SetsTheUiCulture()
    {
        var thread = CultureInfo.CurrentUICulture;
        var shared = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            new Loc().Apply(LanguageMode.ZhCN, CultureInfo.GetCultureInfo("en-US"));

            CultureInfo.CurrentUICulture.Name.Should().Be(Loc.SimplifiedChinese);
        }
        finally
        {
            CultureInfo.CurrentUICulture = thread;
            CultureInfo.DefaultThreadCurrentUICulture = shared;
        }
    }

    /// <summary>
    /// Verifies view models read the shared instance, so their texts follow the active language.
    /// </summary>
    [Fact]
    public void Select_Chinese_ReachesViewModels()
    {
        var row = new AssetRowViewModel(new Asset("p", "BTC", "Bitcoin", AssetClass.Crypto), "p");

        InChinese(() =>
        {
            row.Update(new AssetSnapshot(row.Asset, null, [], AssetHealth.Stale));
            row.StatusText.Should().Be("数据过期");
            row.RefreshLanguage("p");
            row.ClassLabel.Should().Be("加密");
        });

        row.RefreshLanguage("p");
        row.StatusText.Should().Be("Stale");
        row.ClassLabel.Should().Be("CRYPTO");
    }

    /// <summary>
    /// Verifies the reason an option value is rejected is worded in the active language.
    /// </summary>
    [Fact]
    public void ProviderText_Validation_FollowsTheLanguage()
    {
        var problem = new OptionValidation(OptionValidationError.BelowMinimum, "Must be at least 2.", 2);

        ProviderText.Validation(problem).Should().Be("Must be at least 2.");
        InChinese(() =>
        {
            ProviderText.Validation(problem).Should().Be("不能小于 2。");
            ProviderText.Validation(new OptionValidation(OptionValidationError.NotBool, "x")).Should().Be("必须是 true 或 false。");
        });
    }

    /// <summary>
    /// Verifies the add page rebuilds its title and message when the language changes.
    /// </summary>
    [Fact]
    public async Task AddAsset_LanguageChange_RebuildsTitleAndMessage()
    {
        await using var harness = new Harness();
        harness.AddAsset.BeginAdd();
        harness.AddAsset.Title.Should().Be("Add instrument");

        InChinese(() => harness.AddAsset.Title.Should().Be("添加资产"));

        harness.AddAsset.Title.Should().Be("Add instrument");
    }

    /// <summary>
    /// Verifies that for every enum that has at least one label, all of its values have a label in both languages.
    /// </summary>
    [Fact]
    public void EnumLabel_LabelledEnums_HaveEveryValueInBothLanguages()
    {
        var enums = new[] { typeof(ThemeMode), typeof(AssetClass), typeof(Loc) }
            .Select(t => t.Assembly)
            .Distinct()
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t.IsEnum)
            .ToLookup(t => t.Name);
        var english = Loc.KeysOf(Loc.English).ToHashSet();
        var chinese = Loc.KeysOf(Loc.SimplifiedChinese).ToHashSet();
        var labelled = english
            .Select(key => key[..Math.Max(key.IndexOf('_'), 0)])
            .Distinct()
            .SelectMany(prefix => enums[prefix])
            .ToList();

        labelled.Select(t => t.Name).Should().Contain(["ThemeMode", "LanguageMode", "ColorConvention", "WatchlistLayout", "CardGrouping", "AssetClass"]);
        foreach (var type in labelled)
        {
            foreach (var name in Enum.GetNames(type))
            {
                english.Should().Contain($"{type.Name}_{name}");
                chinese.Should().Contain($"{type.Name}_{name}");
            }
        }
    }

    /// <summary>
    /// Verifies a value without a label shows its own name, not a table key.
    /// </summary>
    [Fact]
    public void EnumLabel_Of_ValueWithoutLabel_ShowsItsName()
    {
        EnumLabel.Of(DayOfWeek.Friday).Should().Be("Friday");
    }

    /// <summary>
    /// Verifies a disposed view model no longer follows the language, and does not keep the shared instance calling it.
    /// </summary>
    [Fact]
    public async Task Dispose_ViewModels_StopFollowingTheLanguage()
    {
        await using var harness = new Harness();
        harness.AddAsset.BeginAdd();
        var changed = new List<string?>();
        harness.Main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Main.Dispose();
        harness.SettingsPage.Dispose();
        harness.AddAsset.Dispose();
        InChinese(() => { });

        harness.AddAsset.Title.Should().Be("Add instrument");
        changed.Should().NotContain(nameof(MainViewModel.LayoutToggleTip));
    }

    /// <summary>
    /// Verifies a view model that is still alive does follow the language.
    /// </summary>
    [Fact]
    public async Task ViewModels_WhileAlive_FollowTheLanguage()
    {
        await using var harness = new Harness();
        var changed = new List<string?>();
        harness.Main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        InChinese(() => changed.Should().Contain(nameof(MainViewModel.LayoutToggleTip)));
    }

    /// <summary>
    /// Verifies a status that carries a message reads state first and message second, in both languages.
    /// </summary>
    [Fact]
    public void UpdateStatus_WithMessage_JoinsStateAndMessage()
    {
        var descriptor = new ProviderDescriptor("custom", "Custom Feed", "A feed.", [AssetClass.Crypto], ProviderUpdateMode.Polling);
        var provider = new ProviderSettingsViewModel(descriptor, _ => { });

        provider.UpdateStatus(new ProviderStatus(ProviderConnectionState.Failed, "timeout"));
        provider.StatusText.Should().Be("Error: timeout");

        InChinese(() =>
        {
            provider.UpdateStatus(new ProviderStatus(ProviderConnectionState.Reconnecting, "timeout"));
            provider.StatusText.Should().Be("重新连接中：timeout");
        });
    }

    /// <summary>
    /// Verifies disposing a view model twice does not detach twice.
    /// </summary>
    [Fact]
    public async Task Dispose_CalledTwice_IsHarmless()
    {
        await using var harness = new Harness();

        harness.Main.Dispose();
        var second = () => harness.Main.Dispose();

        second.Should().NotThrow();
    }

    /// <summary>
    /// Verifies a key nobody defined shows as the key itself rather than as nothing.
    /// </summary>
    [Fact]
    public void Indexer_UnknownKey_ReturnsTheKey()
    {
        new Loc()["No_Such_Key"].Should().Be("No_Such_Key");
    }

    /// <summary>
    /// Verifies the labels of enum values come from the tables.
    /// </summary>
    [Fact]
    public void EnumLabel_Of_UsesTheTypeAndValueAsKey()
    {
        EnumLabel.Of(ThemeMode.Dark).Should().Be("Dark");
        EnumLabel.Of(LanguageMode.ZhCN).Should().Be("简体中文");
        EnumLabel.Of(CardGrouping.ByProvider).Should().Be("By data source");
    }

    /// <summary>
    /// Verifies the texts of data sources fall back to what the provider wrote when there is no translation.
    /// </summary>
    [Fact]
    public void ProviderText_WithoutTranslation_UsesTheProvidersOwnText()
    {
        var descriptor = new ProviderDescriptor("custom", "Custom Feed", "A feed.", [AssetClass.Crypto], ProviderUpdateMode.Polling);

        ProviderText.Name(descriptor).Should().Be("Custom Feed");
        ProviderText.Description(descriptor).Should().Be("A feed.");
    }

    /// <summary>
    /// Verifies the language setting reads as automatic when a settings file predates it.
    /// </summary>
    [Fact]
    public void AppearanceSettings_Language_DefaultsToAuto()
    {
        new AppSettings().Appearance.Language.Should().Be(LanguageMode.Auto);
    }

    /// <summary>
    /// Runs a check with the shared instance switched to Chinese, and puts English back afterwards. The tests of this
    /// assembly do not run in parallel, so no other test can see the switch.
    /// </summary>
    /// <param name="check">
    /// The check to run.
    /// </param>
    private static void InChinese(Action check)
    {
        Loc.Instance.Select(Loc.SimplifiedChinese);
        try
        {
            check();
        }
        finally
        {
            Loc.Instance.Select(Loc.English);
        }
    }

    /// <summary>
    /// Lists the placeholders such as <c>{0}</c> of a text.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <returns>
    /// The placeholders in order of appearance.
    /// </returns>
    private static IEnumerable<string> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order();
}
