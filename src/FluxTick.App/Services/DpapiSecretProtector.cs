using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FluxTick.Core.Infrastructure;

namespace FluxTick.App.Services;

/// <summary>
/// Protects secrets with the Windows Data Protection API for the current user: the protected text can only be opened by
/// the same user on the same machine, and the key is kept by Windows, not by the app. It does not keep a program running as
/// the same user from reading the key; nothing on the machine does.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <inheritdoc />
    public bool IsEffective => true;

    /// <inheritdoc />
    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy(purpose), DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc />
    public bool TryUnprotect(string protectedText, string purpose, out string plaintext)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy(purpose), DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Gets the extra bytes the protected text is bound to, so that it only opens for the option it was made for.
    /// </summary>
    /// <param name="purpose">
    /// Where the value belongs.
    /// </param>
    /// <returns>
    /// The bytes.
    /// </returns>
    private static byte[] Entropy(string purpose) => Encoding.UTF8.GetBytes("FluxTick:" + purpose);
}
