using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

public sealed class FontSettingTests
{
    /// <summary>
    /// Verifies the box lists the installed families it was given.
    /// </summary>
    [Fact]
    public async Task FontFamilies_AreTheInstalledOnes()
    {
        await using var h = new Harness();

        h.SettingsPage.FontFamilies.Should().Equal("Arial", "Consolas", "Segoe UI");
    }

    /// <summary>
    /// Verifies typing a name, even one that is an installed family, saves nothing until it is committed.
    /// </summary>
    [Fact]
    public async Task FontText_TypedFamily_IsNotSavedUntilCommitted()
    {
        await using var h = new Harness();

        h.SettingsPage.FontText = "Consolas";

        await h.Settings.WhenIdleAsync();
        h.Settings.Current.Appearance.Font.Should().BeEmpty();
        h.SettingsPage.IsFontListOpen.Should().BeTrue();
    }

    /// <summary>
    /// Verifies Enter on a name that is an installed family saves it, spelled as the family is, whatever the case typed.
    /// </summary>
    [Fact]
    public async Task CommitFont_InstalledFamily_IsSavedWithItsOwnSpelling()
    {
        await using var h = new Harness();
        h.SettingsPage.FontText = "consolas";

        h.SettingsPage.CommitFont();

        h.SettingsPage.FontText.Should().Be("Consolas");
        h.SettingsPage.IsFontListOpen.Should().BeFalse();
        await h.Settings.WhenIdleAsync();
        h.Store.Stored!.Appearance.Font.Should().Be("Consolas");
    }

    /// <summary>
    /// Verifies Enter uses the only font the typed text leaves in the list.
    /// </summary>
    [Fact]
    public async Task CommitFont_OnlyOneSuggestion_UsesIt()
    {
        await using var h = new Harness();
        h.SettingsPage.FontText = "ON";

        h.SettingsPage.CommitFont();

        h.SettingsPage.FontText.Should().Be("Consolas");
        await h.Settings.WhenIdleAsync();
        h.Store.Stored!.Appearance.Font.Should().Be("Consolas");
    }

    /// <summary>
    /// Verifies Enter on a name that leaves several fonts or none saves nothing.
    /// </summary>
    [Theory]
    [InlineData("s")]
    [InlineData("a")]
    [InlineData("zzz")]
    public async Task CommitFont_NoSingleMatch_SavesNothing(string text)
    {
        await using var h = new Harness();
        h.SettingsPage.FontText = text;

        h.SettingsPage.CommitFont();

        await h.Settings.WhenIdleAsync();
        h.Settings.Current.Appearance.Font.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies Enter on an empty box goes back to the font of the system.
    /// </summary>
    [Fact]
    public async Task CommitFont_Empty_GoesBackToTheFontOfTheSystem()
    {
        var settings = new AppSettings();
        settings.Appearance.Font = "Arial";
        await using var h = new Harness(settings);
        h.SettingsPage.FontText = " ";

        h.SettingsPage.CommitFont();

        await h.Settings.WhenIdleAsync();
        h.Store.Stored!.Appearance.Font.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a saved name that no installed family has shows as the font of the system, which is what the app uses.
    /// </summary>
    [Fact]
    public async Task FontText_SavedFontThatIsNotInstalled_IsShownAsTheSystemFont()
    {
        var settings = new AppSettings();
        settings.Appearance.Font = "Gone Sans";
        await using var h = new Harness(settings);

        h.SettingsPage.OpenFontList();

        h.SettingsPage.FontText.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies leaving the box with the name of an installed family typed does not use it.
    /// </summary>
    [Fact]
    public async Task LeaveFontBox_InstalledFamilyTyped_IsNotSaved()
    {
        await using var h = new Harness();
        h.SettingsPage.FontText = "consolas";

        h.SettingsPage.LeaveFontBox();

        h.SettingsPage.FontText.Should().BeEmpty();
        h.SettingsPage.IsFontListOpen.Should().BeFalse();
        await h.Settings.WhenIdleAsync();
        h.Settings.Current.Appearance.Font.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies leaving the box with any other text shows the font in use and saves nothing.
    /// </summary>
    [Fact]
    public async Task LeaveFontBox_UnknownText_ShowsTheFontInUse()
    {
        var settings = new AppSettings();
        settings.Appearance.Font = "Arial";
        await using var h = new Harness(settings);
        h.SettingsPage.FontText = "Cons";

        h.SettingsPage.LeaveFontBox();

        h.SettingsPage.FontText.Should().Be("Arial");
        h.SettingsPage.IsFontListOpen.Should().BeFalse();
        await h.Settings.WhenIdleAsync();
        h.Settings.Current.Appearance.Font.Should().Be("Arial");
    }

    /// <summary>
    /// Verifies restoring the text of the box is not taken for typing, so it does not open the list.
    /// </summary>
    [Fact]
    public async Task LeaveFontBox_DoesNotOpenTheList()
    {
        await using var h = new Harness();
        h.SettingsPage.FontText = "Cons";
        h.SettingsPage.IsFontListOpen = false;

        h.SettingsPage.LeaveFontBox();

        h.SettingsPage.IsFontListOpen.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the saved font is shown in the box when the page loads.
    /// </summary>
    [Fact]
    public async Task FontText_SavedFont_IsShownOnLoad()
    {
        var settings = new AppSettings();
        settings.Appearance.Font = "Consolas";
        await using var h = new Harness(settings);

        h.SettingsPage.FontText.Should().Be("Consolas");
    }

    /// <summary>
    /// Verifies the list shows every font while the box is empty.
    /// </summary>
    [Fact]
    public async Task FontSuggestions_EmptyBox_ListsEveryFont()
    {
        await using var h = new Harness();

        h.SettingsPage.FontSuggestions.Should().Equal("Arial", "Consolas", "Segoe UI");
    }

    /// <summary>
    /// Verifies the list keeps the fonts whose name contains the typed text, whatever the case.
    /// </summary>
    [Fact]
    public async Task FontSuggestions_TypedText_KeepsTheFontsThatContainIt()
    {
        await using var h = new Harness();

        h.SettingsPage.FontText = "ON";

        h.SettingsPage.FontSuggestions.Should().Equal("Consolas");
    }

    /// <summary>
    /// Verifies the list shows every font again when the box shows the font in use, so that another can be picked.
    /// </summary>
    [Fact]
    public async Task FontSuggestions_BoxShowsTheFontInUse_ListsEveryFont()
    {
        var settings = new AppSettings();
        settings.Appearance.Font = "Consolas";
        await using var h = new Harness(settings);

        h.SettingsPage.FontSuggestions.Should().Equal("Arial", "Consolas", "Segoe UI");
    }

    /// <summary>
    /// Verifies typing opens the list of fonts, and loading the page does not.
    /// </summary>
    [Fact]
    public async Task IsFontListOpen_OpensWhenTyping_NotWhenLoading()
    {
        var settings = new AppSettings();
        settings.Appearance.Font = "Consolas";
        await using var h = new Harness(settings);
        h.SettingsPage.IsFontListOpen.Should().BeFalse();

        h.SettingsPage.FontText = "Ar";

        h.SettingsPage.IsFontListOpen.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the list stays open while the text is edited, also when the box is cleared.
    /// </summary>
    [Fact]
    public async Task IsFontListOpen_StaysOpenWhileTyping()
    {
        var settings = new AppSettings();
        settings.Appearance.Font = "Arial";
        await using var h = new Harness(settings);
        h.SettingsPage.OpenFontList();

        h.SettingsPage.FontText = string.Empty;

        h.SettingsPage.IsFontListOpen.Should().BeTrue();
        await h.Settings.WhenIdleAsync();
        h.Settings.Current.Appearance.Font.Should().Be("Arial");
    }

    /// <summary>
    /// Verifies a font chosen from the list is saved, shown, and closes the list, even when it is the one in use.
    /// </summary>
    [Fact]
    public async Task ChooseFont_SavesShowsAndCloses()
    {
        await using var h = new Harness();
        h.SettingsPage.OpenFontList();

        h.SettingsPage.ChooseFont("Segoe UI");

        h.SettingsPage.FontText.Should().Be("Segoe UI");
        h.SettingsPage.IsFontListOpen.Should().BeFalse();
        await h.Settings.WhenIdleAsync();
        h.Settings.Current.Appearance.Font.Should().Be("Segoe UI");
    }

    /// <summary>
    /// Verifies the box goes back to a font chosen a moment ago, even before the save of it has finished.
    /// </summary>
    [Fact]
    public async Task LeaveFontBox_AfterChoosingAFont_KeepsIt()
    {
        await using var h = new Harness();
        h.SettingsPage.ChooseFont("Consolas");

        h.SettingsPage.LeaveFontBox();

        h.SettingsPage.FontText.Should().Be("Consolas");
    }
}
