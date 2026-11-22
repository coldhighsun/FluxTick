using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Services;

/// <summary>
/// Keeps the master key as a generic password in the macOS Keychain, as one item for the whole app, so that the user is
/// asked to allow access at most once and no item is left behind when a data source is switched off.
/// </summary>
/// <param name="logger">
/// The logger; it never gets the key.
/// </param>
[SupportedOSPlatform("macos")]
internal sealed class KeychainMasterKeyStore(ILogger<KeychainMasterKeyStore> logger) : IMasterKeyStore
{
    /// <summary>
    /// The service of the Keychain item.
    /// </summary>
    internal const string Service = "FluxTick";

    /// <summary>
    /// The account of the Keychain item.
    /// </summary>
    internal const string Account = "SecretMasterKey";

    /// <summary>
    /// The size of the key in bytes (AES-256).
    /// </summary>
    internal const int KeySize = 32;

    /// <inheritdoc />
    public byte[]? Get() => Guarded(() => Read(out var key) == MacKeychain.Success ? key : null);

    /// <inheritdoc />
    public byte[]? GetOrCreate() => Guarded(GetOrCreateCore);

    /// <summary>
    /// Runs a Keychain access, treating a failure of the native calls as a Keychain that cannot be used, so that the
    /// settings still load and the values are stored as they are instead of the app failing.
    /// </summary>
    /// <param name="access">
    /// The access.
    /// </param>
    /// <returns>
    /// What the access returned, or <see langword="null"/> when a native call failed.
    /// </returns>
    private byte[]? Guarded(Func<byte[]?> access)
    {
        try
        {
            return access();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OverflowException
                                       or InvalidOperationException or MarshalDirectiveException)
        {
            logger.LogWarning(ex, "The macOS Keychain could not be called; secret options are stored as they are");
            return null;
        }
    }

    /// <summary>
    /// Finds the key, or makes and keeps a new one.
    /// </summary>
    /// <returns>
    /// The key, or <see langword="null"/> when the Keychain cannot be used.
    /// </returns>
    private byte[]? GetOrCreateCore()
    {
        var status = Read(out var key);
        if (status == MacKeychain.Success)
        {
            return key;
        }

        if (status != MacKeychain.ItemNotFound)
        {
            logger.LogWarning("The Keychain could not be read (status {Status}); secret options are stored as they are", status);
            return null;
        }

        var created = RandomNumberGenerator.GetBytes(KeySize);
        status = MacKeychain.TryAdd(Service, Account, System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(created)));
        if (status == MacKeychain.Success)
        {
            return created;
        }

        if (status == MacKeychain.DuplicateItem)
        {
            // Another instance added it between the read and the add; use theirs, so both protect with the same key.
            return Read(out key) == MacKeychain.Success ? key : null;
        }

        logger.LogWarning("The Keychain refused the key (status {Status}); secret options are stored as they are", status);
        return null;
    }

    /// <summary>
    /// Reads the key from the Keychain.
    /// </summary>
    /// <param name="key">
    /// The key, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The status of the Keychain call.
    /// </returns>
    private static int Read(out byte[]? key)
    {
        key = null;
        var status = MacKeychain.TryGet(Service, Account, out var data);
        if (status != MacKeychain.Success)
        {
            return status;
        }

        try
        {
            key = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(data));
        }
        catch (FormatException)
        {
            key = null;
        }

        // An item of another size is not ours; treat it as unusable, never as a reason to replace it.
        if (key is { Length: not KeySize })
        {
            key = null;
        }

        return key is null ? -1 : MacKeychain.Success;
    }
}
