using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for removing stored option values that equal the declared default.
/// </summary>
public class ProviderOptionCleanupTests
{
    /// <summary>
    /// A provider with one option of each kind that is compared by meaning, plus a text and a choice option.
    /// </summary>
    private static readonly ProviderDescriptor Descriptor = new(
        "src",
        "Source",
        "Test",
        [AssetClass.Crypto],
        ProviderUpdateMode.Polling,
        Options:
        [
            new ProviderOptionDefinition("Wait", "Wait", ProviderOptionType.Duration, "00:00:05", Min: 1, Max: 60),
            new ProviderOptionDefinition("Count", "Count", ProviderOptionType.Int, "5", Min: 1, Max: 10),
            new ProviderOptionDefinition("Flag", "Flag", ProviderOptionType.Bool, "true"),
            new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "https://a"),
            new ProviderOptionDefinition("Mode", "Mode", ProviderOptionType.Choice, "a", Choices: ["a", "b"]),
        ]);

    /// <summary>
    /// Creates settings for the test provider holding the given option values.
    /// </summary>
    /// <param name="options">
    /// The stored values.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings With(params (string Key, string Value)[] options)
    {
        var settings = new AppSettings();
        settings.Providers["src"] = new ProviderSettings { Options = options.ToDictionary(o => o.Key, o => o.Value) };
        return settings;
    }

    /// <summary>
    /// Verifies every explicit default, however it is written, is removed while real customisations stay.
    /// </summary>
    [Fact]
    public void DropOptionsAtDefault_RemovesDefaultsAndKeepsCustomValues()
    {
        var settings = With(
            ("Wait", "00:00:05.0000000"),
            ("Count", "05"),
            ("Flag", "True"),
            ("Url", "https://a"),
            ("Mode", "b"));

        var changed = ProviderOptionCleanup.DropOptionsAtDefault(settings, [Descriptor]);

        changed.Should().BeTrue();
        settings.Providers["src"].Options.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Mode", "b"));
    }

    /// <summary>
    /// Verifies values that are not valid for their option are kept: the provider ignores them, and deleting a value
    /// that is out of range today would be irreversible if the rules change.
    /// </summary>
    [Fact]
    public void DropOptionsAtDefault_KeepsValuesThatAreInvalid()
    {
        var settings = With(("Wait", "5"), ("Count", "99"), ("Flag", "yes"), ("Mode", "z"), ("Url", "https://custom"));

        var changed = ProviderOptionCleanup.DropOptionsAtDefault(settings, [Descriptor]);

        changed.Should().BeFalse();
        settings.Providers["src"].Options.Should().HaveCount(5);
    }

    /// <summary>
    /// Verifies a valid default next to invalid values is still removed, and the invalid ones stay.
    /// </summary>
    [Fact]
    public void DropOptionsAtDefault_RemovesOnlyTheDefaultsAmongInvalidValues()
    {
        var settings = With(("Count", "5"), ("Wait", "00:00:90"), ("Mode", "z"));

        var changed = ProviderOptionCleanup.DropOptionsAtDefault(settings, [Descriptor]);

        changed.Should().BeTrue();
        settings.Providers["src"].Options.Keys.Should().BeEquivalentTo(["Wait", "Mode"]);
    }

    /// <summary>
    /// Verifies settings that only hold customisations are reported as unchanged.
    /// </summary>
    [Fact]
    public void DropOptionsAtDefault_WhenNothingIsAtDefault_ChangesNothing()
    {
        var settings = With(("Wait", "00:00:10"), ("Count", "7"), ("Flag", "false"), ("Url", "https://b"), ("Mode", "b"));

        var changed = ProviderOptionCleanup.DropOptionsAtDefault(settings, [Descriptor]);

        changed.Should().BeFalse();
        settings.Providers["src"].Options.Should().HaveCount(5);
    }

    /// <summary>
    /// Verifies options a descriptor does not declare, and providers without a descriptor, are left alone.
    /// </summary>
    [Fact]
    public void DropOptionsAtDefault_LeavesUnknownOptionsAndProvidersAlone()
    {
        var settings = With(("Removed", "x"), ("Count", "5"));
        settings.Providers["gone"] = new ProviderSettings { Options = { ["Count"] = "5" } };

        ProviderOptionCleanup.DropOptionsAtDefault(settings, [Descriptor]);

        settings.Providers["src"].Options.Should().ContainSingle().Which.Key.Should().Be("Removed");
        settings.Providers["gone"].Options.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies the enabled flag of the provider is not touched.
    /// </summary>
    [Fact]
    public void DropOptionsAtDefault_KeepsTheEnabledFlag()
    {
        var settings = With(("Count", "5"));
        settings.Providers["src"].Enabled = false;

        ProviderOptionCleanup.DropOptionsAtDefault(settings, [Descriptor]);

        settings.Providers["src"].Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a cleaned file means the same to the provider as the original.
    /// </summary>
    [Fact]
    public void DropOptionsAtDefault_DoesNotChangeTheEffectiveValues()
    {
        var settings = With(("Wait", "00:00:05.0000000"), ("Count", "7"), ("Flag", "True"), ("Mode", "z"));
        var before = new ProviderOptions(Descriptor, settings.Providers["src"].Options);

        ProviderOptionCleanup.DropOptionsAtDefault(settings, [Descriptor]);
        var after = new ProviderOptions(Descriptor, settings.Providers["src"].Options);

        foreach (var definition in Descriptor.OptionDefinitions)
        {
            definition.Canonicalize(after.GetString(definition.Key)).Should().Be(definition.Canonicalize(before.GetString(definition.Key)), definition.Key);
        }
    }
}
