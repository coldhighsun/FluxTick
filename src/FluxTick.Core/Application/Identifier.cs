namespace FluxTick.Core.Application;

/// <summary>
/// Makes the identifiers of groups and alert rules.
/// </summary>
internal static class Identifier
{
    /// <summary>
    /// Makes a new identifier.
    /// </summary>
    /// <returns>
    /// A new unique identifier.
    /// </returns>
    public static string New() => Guid.NewGuid().ToString("N");
}
