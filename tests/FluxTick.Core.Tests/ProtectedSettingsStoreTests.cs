using System.Text;
using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the store that keeps the secret options of the data sources protected in the settings file.
/// </summary>
public sealed class ProtectedSettingsStoreTests : IDisposable
{
    /// <summary>
    /// A key that must never appear in the file.
    /// </summary>
    private const string Key = "eu_test_1234567890";

    /// <summary>
    /// The folder of the settings file of one test.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-tests-{Guid.NewGuid():N}");

    /// <summary>
    /// The data source with a secret option, a plain one and an option that is not declared.
    /// </summary>
    private static readonly ProviderDescriptor Keyed = new(
        "keyed",
        "Keyed",
        "Test",
        [AssetClass.Equity],
        ProviderUpdateMode.Streaming,
        Options:
        [
            new ProviderOptionDefinition("ApiKey", "API key", ProviderOptionType.Secret, string.Empty, Required: true),
            new ProviderOptionDefinition("Endpoint", "Endpoint", ProviderOptionType.String, "https://example.test"),
        ]);

    /// <summary>
    /// Gets the path of the settings file.
    /// </summary>
    private string SettingsFile => Path.Combine(_directory, "settings.json");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the key is not in the file as written, only the protected form is, while the settings in use keep it.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WritesTheProtectedFormAndLeavesTheSettingsAlone()
    {
        var store = CreateStore(new ReversibleProtector());
        var settings = SettingsWithKey();

        await store.SaveAsync(settings);

        var file = await File.ReadAllTextAsync(SettingsFile);
        file.Should().NotContain(Key).And.Contain(ProtectedSettingsStore.Prefix);
        file.Should().Contain("https://elsewhere.test", "options that are not secret are written as they are");
        settings.Providers["keyed"].Options["ApiKey"].Should().Be(Key, "the settings in use are not touched");
    }

    /// <summary>
    /// Verifies the key comes back as it was written.
    /// </summary>
    [Fact]
    public async Task LoadAsync_OpensTheProtectedValue()
    {
        var store = CreateStore(new ReversibleProtector());
        await store.SaveAsync(SettingsWithKey());

        var loaded = await store.LoadAsync();

        loaded!.Providers["keyed"].Options["ApiKey"].Should().Be(Key);
        loaded.Providers["keyed"].Options["Endpoint"].Should().Be("https://elsewhere.test");
    }

    /// <summary>
    /// Verifies an empty key and options of a source that is not known are left as they are.
    /// </summary>
    [Fact]
    public async Task SaveAsync_EmptyKeyAndUnknownSources_AreNotProtected()
    {
        var store = CreateStore(new ReversibleProtector());
        var settings = new AppSettings();
        settings.Providers["keyed"] = new ProviderSettings { Options = { ["ApiKey"] = string.Empty } };
        settings.Providers["other"] = new ProviderSettings { Options = { ["ApiKey"] = "visible" } };

        await store.SaveAsync(settings);

        var file = await File.ReadAllTextAsync(SettingsFile);
        file.Should().NotContain(ProtectedSettingsStore.Prefix).And.Contain("visible");
    }

    /// <summary>
    /// Verifies a key stored as it is by an older version is read and then protected in the file.
    /// </summary>
    [Fact]
    public async Task LoadAsync_PlainValueFromAnOlderVersion_IsProtectedInTheFile()
    {
        Directory.CreateDirectory(_directory);
        var plain = new JsonSettingsStore(SettingsFile, NullLogger<JsonSettingsStore>.Instance);
        await plain.SaveAsync(SettingsWithKey());
        (await File.ReadAllTextAsync(SettingsFile)).Should().Contain(Key);
        var store = CreateStore(new ReversibleProtector());

        var loaded = await store.LoadAsync();

        loaded!.Providers["keyed"].Options["ApiKey"].Should().Be(Key);
        (await File.ReadAllTextAsync(SettingsFile)).Should().NotContain(Key).And.Contain(ProtectedSettingsStore.Prefix);
    }

    /// <summary>
    /// Verifies a value that cannot be opened is dropped without losing the rest of the settings or failing the load.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ValueThatCannotBeOpened_IsDroppedAndTheRestKept()
    {
        var store = CreateStore(new ReversibleProtector());
        await store.SaveAsync(SettingsWithKey());
        var otherUser = CreateStore(new ReversibleProtector(salt: "another user"));

        var loaded = await otherUser.LoadAsync();

        loaded.Should().NotBeNull();
        loaded!.Providers["keyed"].Options.Should().NotContainKey("ApiKey");
        loaded.Providers["keyed"].Options["Endpoint"].Should().Be("https://elsewhere.test");
    }

    /// <summary>
    /// Verifies a value that cannot be opened now is not lost by a save of something else: it stays in the file, as it was,
    /// so that whoever can open it later still can.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AfterAValueCouldNotBeOpened_KeepsItInTheFile()
    {
        await CreateStore(new ReversibleProtector()).SaveAsync(SettingsWithKey());
        var written = await File.ReadAllTextAsync(SettingsFile);
        var otherUser = CreateStore(new ReversibleProtector(salt: "another user"));
        var loaded = (await otherUser.LoadAsync())!;
        loaded.Appearance.AlwaysOnTop = !loaded.Appearance.AlwaysOnTop;

        await otherUser.SaveAsync(loaded);

        var after = await File.ReadAllTextAsync(SettingsFile);
        after.Should().Contain(ProtectedText(written), "the text that could not be opened is written back unchanged");
        (await CreateStore(new ReversibleProtector()).LoadAsync())!.Providers["keyed"].Options["ApiKey"].Should().Be(Key);
    }

    /// <summary>
    /// Verifies switching the data source off lets go of a value that cannot be opened: a source that is off has no use for it,
    /// and it is how the user gets rid of it.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SourceSwitchedOff_LetsGoOfAValueThatCouldNotBeOpened()
    {
        await CreateStore(new ReversibleProtector()).SaveAsync(SettingsWithKey());
        var otherUser = CreateStore(new ReversibleProtector(salt: "another user"));
        var loaded = (await otherUser.LoadAsync())!;
        loaded.Providers["keyed"].Enabled = false;

        await otherUser.SaveAsync(loaded);
        loaded.Providers["keyed"].Enabled = true;
        await otherUser.SaveAsync(loaded);

        (await File.ReadAllTextAsync(SettingsFile)).Should().NotContain(ProtectedSettingsStore.Prefix);
    }

    /// <summary>
    /// Verifies settings without a secret value are handed to the file store as they are, without a copy, since settings are
    /// saved after each small change.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NothingToProtect_DoesNotCopyTheSettings()
    {
        var recorder = new RecordingStore();
        var store = new ProtectedSettingsStore(recorder, new ReversibleProtector(), () => [Keyed], NullLogger<ProtectedSettingsStore>.Instance);
        var settings = new AppSettings();
        settings.Providers["keyed"] = new ProviderSettings { Options = { ["Endpoint"] = "https://elsewhere.test" } };

        await store.SaveAsync(settings);

        recorder.Saved.Should().BeSameAs(settings);
    }

    /// <summary>
    /// Verifies a value the user enters replaces the one that could not be opened.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NewValueAfterOneCouldNotBeOpened_ReplacesIt()
    {
        await CreateStore(new ReversibleProtector()).SaveAsync(SettingsWithKey());
        var written = await File.ReadAllTextAsync(SettingsFile);
        var otherUser = CreateStore(new ReversibleProtector(salt: "another user"));
        var loaded = (await otherUser.LoadAsync())!;
        loaded.Providers["keyed"].Options["ApiKey"] = "eu_new_key";

        await otherUser.SaveAsync(loaded);

        var after = await File.ReadAllTextAsync(SettingsFile);
        after.Should().NotContain(ProtectedText(written)).And.NotContain("eu_new_key");
        (await otherUser.LoadAsync())!.Providers["keyed"].Options["ApiKey"].Should().Be("eu_new_key");
    }

    /// <summary>
    /// Verifies a protector that does not protect keeps a value it cannot open in the file too.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithoutProtection_KeepsAValueFromAnotherMachine()
    {
        await CreateStore(new ReversibleProtector()).SaveAsync(SettingsWithKey());
        var written = await File.ReadAllTextAsync(SettingsFile);
        var store = CreateStore(new NoSecretProtector());
        var loaded = (await store.LoadAsync())!;

        await store.SaveAsync(loaded);

        (await File.ReadAllTextAsync(SettingsFile)).Should().Contain(ProtectedText(written));
    }

    /// <summary>
    /// Verifies a protected value does not open for another option: a copy moved to another source is lost.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ValueMovedToAnotherSource_IsDropped()
    {
        var protector = new ReversibleProtector();
        var store = CreateStore(protector, Keyed, Keyed with { Id = "twin" });
        var settings = SettingsWithKey();
        await store.SaveAsync(settings);
        var moved = (await File.ReadAllTextAsync(SettingsFile)).Replace("\"keyed\"", "\"twin\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(SettingsFile, moved);

        var loaded = await store.LoadAsync();

        loaded!.Providers["twin"].Options.Should().NotContainKey("ApiKey");
    }

    /// <summary>
    /// Verifies a protector that does not protect leaves the file as it was, and cannot open what another one protected.
    /// </summary>
    [Fact]
    public async Task NoSecretProtector_StoresTheValueAsItIs()
    {
        var store = CreateStore(new NoSecretProtector());
        await store.SaveAsync(SettingsWithKey());

        (await File.ReadAllTextAsync(SettingsFile)).Should().Contain(Key).And.NotContain(ProtectedSettingsStore.Prefix);
        (await store.LoadAsync())!.Providers["keyed"].Options["ApiKey"].Should().Be(Key);

        await CreateStore(new ReversibleProtector()).SaveAsync(SettingsWithKey());
        (await store.LoadAsync())!.Providers["keyed"].Options.Should().NotContainKey("ApiKey");
    }

    /// <summary>
    /// Verifies the settings manager works with the plain key on top of the store, and that the default-valued options are
    /// still dropped while a key is kept.
    /// </summary>
    [Fact]
    public async Task SettingsManager_KeepsWorkingWithThePlainKey()
    {
        var store = CreateStore(new ReversibleProtector());
        var manager = new SettingsManager(store, NullLogger<SettingsManager>.Instance);
        await manager.LoadAsync(new AppSettings());

        await manager.UpdateAsync(s => s.Providers["keyed"] = new ProviderSettings { Options = { ["ApiKey"] = Key } });
        await manager.TryUpdateAsync(s => ProviderOptionCleanup.DropOptionsAtDefault(s, [Keyed]));

        manager.Current.Providers["keyed"].Options["ApiKey"].Should().Be(Key);
        (await File.ReadAllTextAsync(SettingsFile)).Should().NotContain(Key);
    }

    /// <summary>
    /// Creates the store of a test on a real file.
    /// </summary>
    /// <param name="protector">
    /// The protector.
    /// </param>
    /// <param name="descriptors">
    /// The data sources, or the one with a secret option when omitted.
    /// </param>
    /// <returns>
    /// The store.
    /// </returns>
    private ProtectedSettingsStore CreateStore(ISecretProtector protector, params ProviderDescriptor[] descriptors) => new(
        new JsonSettingsStore(SettingsFile, NullLogger<JsonSettingsStore>.Instance),
        protector,
        () => descriptors.Length == 0 ? [Keyed] : descriptors,
        NullLogger<ProtectedSettingsStore>.Instance);

    /// <summary>
    /// Finds the protected text of the key in a settings file.
    /// </summary>
    /// <param name="file">
    /// The content of the file.
    /// </param>
    /// <returns>
    /// The protected value, with its mark.
    /// </returns>
    private static string ProtectedText(string file) =>
        System.Text.RegularExpressions.Regex.Match(file, "protected:v1:[A-Za-z0-9+/=]+").Value;

    /// <summary>
    /// Makes settings with a key and another option for the data source.
    /// </summary>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings SettingsWithKey()
    {
        var settings = new AppSettings();
        settings.Providers["keyed"] = new ProviderSettings
        {
            Options = { ["ApiKey"] = Key, ["Endpoint"] = "https://elsewhere.test" },
        };
        return settings;
    }

    /// <summary>
    /// A store that remembers what it was asked to save.
    /// </summary>
    private sealed class RecordingStore : ISettingsStore
    {
        /// <summary>
        /// Gets what was saved last.
        /// </summary>
        public AppSettings? Saved { get; private set; }

        /// <inheritdoc />
        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<AppSettings?>(null);

        /// <inheritdoc />
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A protector that can be undone in tests: it binds the text to the purpose and to a salt, which stands for the user
    /// and the machine.
    /// </summary>
    /// <param name="salt">
    /// What the text is bound to besides the purpose.
    /// </param>
    private sealed class ReversibleProtector(string salt = "user") : ISecretProtector
    {
        /// <inheritdoc />
        public bool IsEffective => true;

        /// <inheritdoc />
        public string Protect(string plaintext, string purpose) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{salt}|{purpose}|{plaintext}"));

        /// <inheritdoc />
        public bool TryUnprotect(string protectedText, string purpose, out string plaintext)
        {
            plaintext = string.Empty;
            try
            {
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(protectedText));
                var head = $"{salt}|{purpose}|";
                if (!text.StartsWith(head, StringComparison.Ordinal))
                {
                    return false;
                }

                plaintext = text[head.Length..];
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
