using System.Globalization;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for reading and writing typed numbers.
/// </summary>
public class NumberTextTests
{
    /// <summary>
    /// Verifies that typed numbers are read with the decimal separator of the current culture or the point, and not
    /// mistaken for a thousands separator.
    /// </summary>
    /// <param name="culture">
    /// The current culture.
    /// </param>
    /// <param name="text">
    /// The typed text.
    /// </param>
    /// <param name="expected">
    /// The number, or <see langword="null"/> when the text is not one.
    /// </param>
    [Theory]
    [InlineData("de-DE", "12,5", "12.5")]
    [InlineData("de-DE", "12.5", "12.5")]
    [InlineData("de-DE", "1.000.000", "1000000")]
    [InlineData("en-US", "12.5", "12.5")]
    [InlineData("en-US", "1,000", "1000")]
    [InlineData("en-US", " 7 ", "7")]
    [InlineData("en-US", "abc", null)]
    [InlineData("en-US", "1,5", null)]
    [InlineData("en-US", "12,34", null)]
    [InlineData("en-US", "1,000.5", "1000.5")]
    [InlineData("de-DE", "1.000,5", "1000.5")]
    [InlineData("de-DE", "1.000", "1000")]
    [InlineData("de-DE", "0.500", "0.5")]
    [InlineData("sv-SE", "\u22121\u00A0000", "-1000")]
    [InlineData("en-US", "-1,000", "-1000")]
    public void TryParse_TypedNumber_UsesTheDecimalSeparatorOfTheCulture(string culture, string text, string? expected)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var parsed = NumberText.TryParse(text, out var value);

            parsed.Should().Be(expected is not null);
            if (expected is not null)
            {
                value.Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>
    /// Verifies that a number written into a form is read back as the same number, whatever the culture writes it with.
    /// </summary>
    /// <param name="culture">
    /// The current culture.
    /// </param>
    /// <param name="value">
    /// The number.
    /// </param>
    /// <param name="written">
    /// The text the culture writes.
    /// </param>
    [Theory]
    [InlineData("de-DE", "1.234", "1,234")]
    [InlineData("de-DE", "1234.5", "1234,5")]
    [InlineData("en-US", "1.234", "1.234")]
    [InlineData("en-US", "0.000000001", "0.000000001")]
    public void Format_ThenTryParse_RoundTripsInTheCulture(string culture, string value, string written)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var number = decimal.Parse(value, CultureInfo.InvariantCulture);

            NumberText.Format(number).Should().Be(written);
            NumberText.TryParse(NumberText.Format(number), out var back).Should().BeTrue();
            back.Should().Be(number);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>
    /// Verifies which texts may be typed into a box for a real number.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <param name="expected">
    /// Whether the box may show it.
    /// </param>
    [Theory]
    [InlineData("", true)]
    [InlineData("12", true)]
    [InlineData("-", false)]
    [InlineData("-12.5", false)]
    [InlineData("12.", true)]
    [InlineData(".5", true)]
    [InlineData("1.2.3", false)]
    [InlineData("1-2", false)]
    [InlineData("abc", false)]
    [InlineData("1e5", false)]
    [InlineData("1,5", false)]
    [InlineData("１２", false)]
    public void IsPartialNumber_PointCulture_AcceptsOnlyRealNumbersAndTheirBeginnings(string text, bool expected) =>
        NumberText.IsPartialNumber(text).Should().Be(expected);


    /// <summary>
    /// Verifies that a box may show the decimal separator of the culture, so that a number written with
    /// <see cref="NumberText.Format"/> can be edited.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <param name="expected">
    /// Whether the box may show it.
    /// </param>
    [Theory]
    [InlineData("12,5", true)]
    [InlineData("12.5", true)]
    [InlineData(",5", true)]
    [InlineData("1,2,3", false)]
    [InlineData("1,2.3", false)]
    [InlineData("1.000", true)]
    [InlineData("abc", false)]
    public void IsPartialNumber_CommaCulture_AcceptsTheCommaAndThePointOnce(string text, bool expected)
    {
        TestCulture.Under("de-DE", () => NumberText.IsPartialNumber(text).Should().Be(expected));
    }

    /// <summary>
    /// Verifies a box for a whole number accepts digits, and a leading minus sign only when negative numbers are allowed.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <param name="allowNegative">
    /// Whether negative numbers are allowed.
    /// </param>
    /// <param name="expected">
    /// Whether the box may show the text.
    /// </param>
    [Theory]
    [InlineData("", false, true)]
    [InlineData("123", false, true)]
    [InlineData("-", true, true)]
    [InlineData("-12", true, true)]
    [InlineData("-12", false, false)]
    [InlineData("1-2", true, false)]
    [InlineData("1.5", true, false)]
    [InlineData("1a", false, false)]
    [InlineData("--1", true, false)]
    public void IsPartialWholeNumber_AcceptsDigitsAndAllowedSign(string text, bool allowNegative, bool expected) =>
        NumberText.IsPartialWholeNumber(text, allowNegative).Should().Be(expected);

    /// <summary>
    /// Verifies a box for seconds accepts digits and one point, whatever the culture, and nothing else.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <param name="expected">
    /// Whether the box may show the text.
    /// </param>
    [Theory]
    [InlineData("", true)]
    [InlineData("5", true)]
    [InlineData("0.5", true)]
    [InlineData(".5", true)]
    [InlineData("1.2.3", false)]
    [InlineData("1,5", false)]
    [InlineData("-1", false)]
    [InlineData("1e3", false)]
    public void IsPartialSeconds_AcceptsDigitsAndOnePoint(string text, bool expected) =>
        NumberText.IsPartialSeconds(text).Should().Be(expected);
}
