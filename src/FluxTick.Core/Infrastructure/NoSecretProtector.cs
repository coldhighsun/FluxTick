namespace FluxTick.Core.Infrastructure;

/// <summary>
/// The protector for a platform that has no protection built in: values are stored as they are. The settings file is then
/// only as private as the permissions of the file.
/// </summary>
public sealed class NoSecretProtector : ISecretProtector
{
    /// <inheritdoc />
    public bool IsEffective => false;

    /// <inheritdoc />
    public string Protect(string plaintext, string purpose) => plaintext;

    /// <inheritdoc />
    public bool TryUnprotect(string protectedText, string purpose, out string plaintext)
    {
        // Nothing was protected, so nothing can be opened: a value that carries the mark of a protector was written elsewhere.
        plaintext = string.Empty;
        return false;
    }
}
