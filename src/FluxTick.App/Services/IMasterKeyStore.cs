namespace FluxTick.App.Services;

/// <summary>
/// Keeps the key that protects the secret values of the settings, in a place the settings file is not: the Keychain of the
/// user on macOS.
/// </summary>
internal interface IMasterKeyStore
{
    /// <summary>
    /// Gets the key, making and keeping a new one when none exists yet.
    /// </summary>
    /// <returns>
    /// The key (32 bytes), or <see langword="null"/> when the store cannot be used, such as a Keychain that is locked or
    /// that the user did not allow the app to use. A key that exists is never replaced, because what it protected would
    /// then be lost.
    /// </returns>
    byte[]? GetOrCreate();

    /// <summary>
    /// Gets the key if one exists, without making one.
    /// </summary>
    /// <returns>
    /// The key, or <see langword="null"/> when there is none or the store cannot be used.
    /// </returns>
    byte[]? Get();
}
