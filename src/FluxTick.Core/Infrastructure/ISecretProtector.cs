namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Protects the secret values of the settings, such as the API key of a data source, while they are stored on disk. In
/// memory the values stay as they are; only what is written to the settings file is protected.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Gets a value indicating whether the protector really protects. The one that does not (<see cref="NoSecretProtector"/>)
    /// leaves the values in the settings file as they are.
    /// </summary>
    bool IsEffective { get; }

    /// <summary>
    /// Protects a value for storage.
    /// </summary>
    /// <param name="plaintext">
    /// The value.
    /// </param>
    /// <param name="purpose">
    /// Where the value belongs, such as <c>eulerpool:ApiKey</c>. The protected text only opens for the same purpose, so a
    /// value copied to another option does not.
    /// </param>
    /// <returns>
    /// The protected text, safe to write to a text file.
    /// </returns>
    string Protect(string plaintext, string purpose);

    /// <summary>
    /// Opens a value that <see cref="Protect"/> protected.
    /// </summary>
    /// <param name="protectedText">
    /// The protected text.
    /// </param>
    /// <param name="purpose">
    /// The purpose it was protected for.
    /// </param>
    /// <param name="plaintext">
    /// The value, or an empty string when it cannot be opened.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it was opened; <see langword="false"/> when the text is damaged, was protected for
    /// another purpose, or for another user or machine.
    /// </returns>
    bool TryUnprotect(string protectedText, string purpose, out string plaintext);
}
