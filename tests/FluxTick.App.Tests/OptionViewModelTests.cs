using FluxTick.Abstractions;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for how an option editor compares its value with the built-in default.
/// </summary>
public class OptionViewModelTests
{
    /// <summary>
    /// Verifies a whole number is the default however it is written, and a different number is not.
    /// </summary>
    [Theory]
    [InlineData("5", true)]
    [InlineData("05", true)]
    [InlineData(" 5 ", true)]
    [InlineData("6", false)]
    public void NothingToPersist_ForInt_ComparesTheNumber(string text, bool expected)
    {
        var option = Create(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10));

        option.Text = text;

        (option.ValueToPersist is null).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a number is stored in its canonical form.
    /// </summary>
    [Fact]
    public void StoredValue_ForInt_IsCanonical()
    {
        var option = Create(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10));

        option.Text = "007";

        option.StoredValue.Should().Be("7");
    }

    /// <summary>
    /// Verifies a switch compares with a default that is spelled with capitals.
    /// </summary>
    [Theory]
    [InlineData("True", true, true)]
    [InlineData("True", false, false)]
    [InlineData("false", false, true)]
    [InlineData("false", true, false)]
    public void NothingToPersist_ForBool_IgnoresHowTheDefaultIsSpelled(string defaultValue, bool value, bool expected)
    {
        var option = Create(new ProviderOptionDefinition("Flag", "Flag", ProviderOptionType.Bool, defaultValue));

        option.BoolValue = value;

        (option.ValueToPersist is null).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a duration with a fractional default compares by its value rather than its text.
    /// </summary>
    [Theory]
    [InlineData("0.5", true)]
    [InlineData("0.50", true)]
    [InlineData("1", false)]
    public void NothingToPersist_ForDuration_ComparesTheTime(string seconds, bool expected)
    {
        var option = Create(new ProviderOptionDefinition("Wait", "Wait", ProviderOptionType.Duration, "00:00:00.5", Min: 0.1, Max: 60));

        option.Text = seconds;

        (option.ValueToPersist is null).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a pick list is the default only while the default entry is selected.
    /// </summary>
    [Theory]
    [InlineData("b", true)]
    [InlineData("a", false)]
    public void NothingToPersist_ForChoice_ComparesTheSelection(string choice, bool expected)
    {
        var option = Create(new ProviderOptionDefinition("Mode", "Mode", ProviderOptionType.Choice, "b", Choices: ["a", "b"]));

        option.SelectedChoice = choice;

        (option.ValueToPersist is null).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a text value is the default only when it is equal to the default text.
    /// </summary>
    [Theory]
    [InlineData("https://a", true)]
    [InlineData("https://b", false)]
    public void NothingToPersist_ForString_ComparesTheText(string text, bool expected)
    {
        var option = Create(new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "https://a"));

        option.Text = text;

        (option.ValueToPersist is null).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a hand-edited stored value that is not valid shows the default, like the provider that runs with it,
    /// and neither claims an error nor is adopted as a new setting on the next save.
    /// </summary>
    [Theory]
    [InlineData("x")]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("")]
    public void Load_OfAnInvalidInt_ShowsTheDefault(string stored)
    {
        var option = new OptionViewModel(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10), stored, () => { });

        option.Text.Should().Be("5");
        option.Error.Should().BeEmpty();
        option.ValueToPersist.Should().BeNull();
    }

    /// <summary>
    /// Verifies a bare number stored for a duration is not read as days and shows the default.
    /// </summary>
    [Fact]
    public void Load_OfABareNumberForADuration_ShowsTheDefault()
    {
        var option = new OptionViewModel(new ProviderOptionDefinition("Wait", "Wait", ProviderOptionType.Duration, "00:00:30", Min: 1, Max: 60), "5", () => { });

        option.Text.Should().Be("30");
        option.ValueToPersist.Should().BeNull();
    }

    /// <summary>
    /// Verifies an unknown switch or choice value shows the default.
    /// </summary>
    [Fact]
    public void Load_OfAnInvalidSwitchOrChoice_ShowsTheDefault()
    {
        var flag = new OptionViewModel(new ProviderOptionDefinition("Flag", "Flag", ProviderOptionType.Bool, "true"), "yes", () => { });
        var mode = new OptionViewModel(new ProviderOptionDefinition("Mode", "Mode", ProviderOptionType.Choice, "b", Choices: ["a", "b"]), "z", () => { });

        flag.BoolValue.Should().BeTrue();
        mode.SelectedChoice.Should().Be("b");
        flag.ValueToPersist.Should().BeNull();
        mode.ValueToPersist.Should().BeNull();
    }

    /// <summary>
    /// Verifies a valid stored value is shown as stored, in seconds for a duration.
    /// </summary>
    [Fact]
    public void Load_OfAValidStoredValue_ShowsIt()
    {
        var wait = new OptionViewModel(new ProviderOptionDefinition("Wait", "Wait", ProviderOptionType.Duration, "00:00:30", Min: 1, Max: 60), "00:00:10", () => { });
        var count = new OptionViewModel(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10), "7", () => { });

        wait.Text.Should().Be("10");
        wait.ValueToPersist.Should().Be("00:00:10");
        count.Text.Should().Be("7");
        count.ValueToPersist.Should().Be("7");
    }

    /// <summary>
    /// Verifies a default that is itself written in a non-canonical way (extra zeros, other spelling) still counts as the default.
    /// </summary>
    [Fact]
    public void NothingToPersist_WhenTheDeclaredDefaultIsWrittenNonCanonically()
    {
        var number = Create(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "05", Min: 1, Max: 10));
        var wait = Create(new ProviderOptionDefinition("Wait", "Wait", ProviderOptionType.Duration, "00:00:05.0000000", Min: 1, Max: 60));

        number.Text = "5";
        wait.Text = "5";

        number.ValueToPersist.Should().BeNull();
        wait.ValueToPersist.Should().BeNull();
    }

    /// <summary>
    /// Verifies an invalid input is not a stored value, while a valid one next to it still is.
    /// </summary>
    [Theory]
    [InlineData("five")]
    [InlineData("0")]
    [InlineData("11")]
    public void StoredValue_ForInvalidInt_IsNull(string text)
    {
        var option = Create(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10));

        option.Text = text;

        option.StoredValue.Should().BeNull();
        option.Error.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies half-typed or invalid input keeps the last valid value to persist instead of dropping the setting.
    /// </summary>
    [Fact]
    public void ValueToPersist_WhileTheInputIsInvalid_KeepsTheLastValidValue()
    {
        var option = new OptionViewModel(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10), "7", () => { });
        option.ValueToPersist.Should().Be("7");

        option.Text = "abc";

        option.StoredValue.Should().BeNull();
        option.ValueToPersist.Should().Be("7");
        option.Text = "8";
        option.ValueToPersist.Should().Be("8");
    }

    /// <summary>
    /// Verifies invalid input on an option that is at its default leaves nothing to persist.
    /// </summary>
    [Fact]
    public void ValueToPersist_WhenInvalidAndNothingWasSaved_IsNothing()
    {
        var option = Create(new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10));

        option.Text = "abc";

        option.ValueToPersist.Should().BeNull();
    }

    /// <summary>
    /// Creates an option editor showing its default.
    /// </summary>
    /// <param name="definition">
    /// The option definition.
    /// </param>
    /// <returns>
    /// The editor.
    /// </returns>
    private static OptionViewModel Create(ProviderOptionDefinition definition) => new(definition, null, () => { });
}
