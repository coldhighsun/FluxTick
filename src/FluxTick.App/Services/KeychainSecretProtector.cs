using System.Security.Cryptography;
using System.Text;
using FluxTick.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Services;

/// <summary>
/// Protects secrets on macOS: a random master key is kept in the Keychain of the user, and each value is encrypted with it
/// (AES-256-GCM) before it is written to the settings file. The protected text is bound to its option, so it does not open
/// for another. Like DPAPI, it does not keep a program that the user allows to use the Keychain item from reading the key.
/// When the Keychain cannot be used the protector says it is not effective, and the values are stored as they are.
/// </summary>
/// <param name="keys">
/// Keeps the master key.
/// </param>
/// <param name="logger">
/// The logger; it never gets a value or the key.
/// </param>
internal sealed class KeychainSecretProtector(IMasterKeyStore keys, ILogger<KeychainSecretProtector> logger) : ISecretProtector
{
    /// <summary>
    /// The size of the nonce in bytes.
    /// </summary>
    private const int NonceSize = 12;

    /// <summary>
    /// The size of the authentication tag in bytes.
    /// </summary>
    private const int TagSize = 16;

    /// <summary>
    /// Guards <see cref="_key"/>, <see cref="_resolved"/> and <see cref="_lookedUp"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// The master key, once it was found or made.
    /// </summary>
    private byte[]? _key;

    /// <summary>
    /// Whether the master key was looked for already; the Keychain is asked once.
    /// </summary>
    private bool _resolved;

    /// <summary>
    /// Whether the Keychain was asked for an existing key, to open a value, already.
    /// </summary>
    private bool _lookedUp;

    /// <inheritdoc />
    public bool IsEffective => MasterKey() is not null;

    /// <inheritdoc />
    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var key = MasterKey() ?? throw new CryptographicException("The Keychain key is not available.");
        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, data, cipher, tag, AssociatedData(purpose));
        }

        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <inheritdoc />
    public bool TryUnprotect(string protectedText, string purpose, out string plaintext)
    {
        plaintext = string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(protectedText);
            if (bytes.Length < NonceSize + TagSize)
            {
                return false;
            }

            // An existing item is read, never made here: a value that cannot be opened must not make a key that cannot open it.
            var key = ExistingKey();
            if (key is null)
            {
                return false;
            }

            var data = new byte[bytes.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                bytes.AsSpan(0, NonceSize),
                bytes.AsSpan(NonceSize + TagSize),
                bytes.AsSpan(NonceSize, TagSize),
                data,
                AssociatedData(purpose));
            plaintext = Encoding.UTF8.GetString(data);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Gets what the protected text is bound to, so that it only opens for the option it was made for.
    /// </summary>
    /// <param name="purpose">
    /// Where the value belongs.
    /// </param>
    /// <returns>
    /// The bytes.
    /// </returns>
    private static byte[] AssociatedData(string purpose) => Encoding.UTF8.GetBytes("FluxTick:" + purpose);

    /// <summary>
    /// Gets the master key, making one on first use. The Keychain is asked once; if it cannot be used, the answer stays.
    /// </summary>
    /// <returns>
    /// The key, or <see langword="null"/> when the Keychain cannot be used.
    /// </returns>
    private byte[]? MasterKey()
    {
        lock (_lock)
        {
            if (!_resolved)
            {
                _key = keys.GetOrCreate();
                _resolved = true;
                if (_key is null)
                {
                    logger.LogWarning("The macOS Keychain cannot be used; API keys are stored as they are in the settings file");
                }
            }

            return _key;
        }
    }

    /// <summary>
    /// Gets the master key for opening a value: the one in use, or one that exists in the Keychain.
    /// </summary>
    /// <returns>
    /// The key, or <see langword="null"/> when there is none.
    /// </returns>
    private byte[]? ExistingKey()
    {
        lock (_lock)
        {
            if (_key is not null)
            {
                return _key;
            }

            // The Keychain is asked once for opening values, whatever the answer: a refused or locked Keychain would
            // otherwise ask the user again for every protected value.
            if (!_resolved && !_lookedUp)
            {
                _key = keys.Get();
                _lookedUp = true;
            }

            return _key;
        }
    }
}
