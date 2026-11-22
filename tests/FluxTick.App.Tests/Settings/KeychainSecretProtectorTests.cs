using System.Security.Cryptography;
using FluxTick.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the protector that keeps its key in the macOS Keychain, with a key store in memory so that they run on every
/// system and never touch the real Keychain.
/// </summary>
public class KeychainSecretProtectorTests
{
    /// <summary>
    /// Verifies a value comes back as it was and is not readable in the protected text.
    /// </summary>
    [Fact]
    public void Protect_ThenTryUnprotect_RoundTrips()
    {
        var protector = Create(new MemoryKeyStore());

        var text = protector.Protect("eu_prod_secret_key", "eulerpool:ApiKey");

        text.Should().NotContain("secret").And.NotBeEmpty();
        protector.TryUnprotect(text, "eulerpool:ApiKey", out var plain).Should().BeTrue();
        plain.Should().Be("eu_prod_secret_key");
    }

    /// <summary>
    /// Verifies a value protected by an earlier run opens with the key the store kept.
    /// </summary>
    [Fact]
    public void TryUnprotect_NewInstanceWithSameStore_Opens()
    {
        var store = new MemoryKeyStore();
        var text = Create(store).Protect("value", "eulerpool:ApiKey");

        Create(store).TryUnprotect(text, "eulerpool:ApiKey", out var plain).Should().BeTrue();
        plain.Should().Be("value");
    }

    /// <summary>
    /// Verifies the protected text does not open for another option.
    /// </summary>
    [Fact]
    public void TryUnprotect_OtherPurpose_Fails()
    {
        var protector = Create(new MemoryKeyStore());
        var text = protector.Protect("value", "eulerpool:ApiKey");

        protector.TryUnprotect(text, "yahoo:ApiKey", out var plain).Should().BeFalse();
        plain.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the protected text does not open with another key, such as one from another user.
    /// </summary>
    [Fact]
    public void TryUnprotect_OtherKey_Fails()
    {
        var text = Create(new MemoryKeyStore()).Protect("value", "eulerpool:ApiKey");

        Create(new MemoryKeyStore()).TryUnprotect(text, "eulerpool:ApiKey", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies changed text is reported as not opened.
    /// </summary>
    [Fact]
    public void TryUnprotect_TamperedText_Fails()
    {
        var protector = Create(new MemoryKeyStore());
        var bytes = Convert.FromBase64String(protector.Protect("value", "eulerpool:ApiKey"));
        bytes[^1] ^= 1;

        protector.TryUnprotect(Convert.ToBase64String(bytes), "eulerpool:ApiKey", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies damaged text is reported as not opened, not as an exception.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void TryUnprotect_DamagedText_Fails(string text) =>
        Create(new MemoryKeyStore()).TryUnprotect(text, "eulerpool:ApiKey", out _).Should().BeFalse();

    /// <summary>
    /// Verifies two protections of one value differ, so that equal keys cannot be told from the file.
    /// </summary>
    [Fact]
    public void Protect_SameValueTwice_GivesDifferentText()
    {
        var protector = Create(new MemoryKeyStore());

        protector.Protect("value", "eulerpool:ApiKey").Should().NotBe(protector.Protect("value", "eulerpool:ApiKey"));
    }

    /// <summary>
    /// Verifies a Keychain that cannot be used makes the protector not effective, so that the values are stored as they are.
    /// </summary>
    [Fact]
    public void IsEffective_KeychainUnavailable_IsFalse()
    {
        var protector = Create(new MemoryKeyStore { Unavailable = true });

        protector.IsEffective.Should().BeFalse();
        protector.TryUnprotect("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "eulerpool:ApiKey", out _).Should().BeFalse();
        FluentActions.Invoking(() => protector.Protect("value", "eulerpool:ApiKey")).Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// Verifies opening a value never makes a key: a store without one stays empty.
    /// </summary>
    [Fact]
    public void TryUnprotect_NoKeyYet_DoesNotCreateOne()
    {
        var store = new MemoryKeyStore();
        var text = Create(new MemoryKeyStore()).Protect("value", "eulerpool:ApiKey");

        Create(store).TryUnprotect(text, "eulerpool:ApiKey", out _).Should().BeFalse();

        store.Created.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a Keychain that refuses is asked once when several values are opened, not once per value.
    /// </summary>
    [Fact]
    public void TryUnprotect_KeychainRefuses_AsksOnce()
    {
        var store = new MemoryKeyStore { Unavailable = true };
        var protector = Create(store);

        protector.TryUnprotect("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "a:Key", out _).Should().BeFalse();
        protector.TryUnprotect("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "b:Key", out _).Should().BeFalse();

        store.GetCalls.Should().Be(1);
    }

    /// <summary>
    /// Makes the protector under test.
    /// </summary>
    /// <param name="store">
    /// The key store.
    /// </param>
    /// <returns>
    /// The protector.
    /// </returns>
    private static KeychainSecretProtector Create(MemoryKeyStore store) =>
        new(store, NullLogger<KeychainSecretProtector>.Instance);

    /// <summary>
    /// A key store in memory.
    /// </summary>
    internal sealed class MemoryKeyStore : IMasterKeyStore
    {
        /// <summary>
        /// The key, once made.
        /// </summary>
        private byte[]? _key;

        /// <summary>
        /// Gets or sets a value indicating whether the store behaves like a Keychain that cannot be used.
        /// </summary>
        public bool Unavailable { get; set; }

        /// <summary>
        /// Gets a value indicating whether a key was made.
        /// </summary>
        public bool Created => _key is not null;

        /// <summary>
        /// Gets how many times <see cref="Get"/> was called.
        /// </summary>
        public int GetCalls { get; private set; }

        /// <inheritdoc />
        public byte[]? Get()
        {
            GetCalls++;
            return Unavailable ? null : _key;
        }

        /// <inheritdoc />
        public byte[]? GetOrCreate() => Unavailable ? null : _key ??= RandomNumberGenerator.GetBytes(32);
    }
}
