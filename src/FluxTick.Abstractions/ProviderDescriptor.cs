namespace FluxTick.Abstractions;

/// <summary>
/// How a provider obtains its data.
/// </summary>
public enum ProviderUpdateMode
{
    /// <summary>
    /// The provider receives pushed updates (for example over a WebSocket).
    /// </summary>
    Streaming,

    /// <summary>
    /// The provider polls a request/response API at an interval.
    /// </summary>
    Polling,
}

/// <summary>
/// Self-description of a market data provider. The application discovers capabilities and builds its settings UI from it.
/// </summary>
/// <param name="Id">
/// A stable, lower-case identifier such as <c>binance</c>. Stored in settings, so it must never change.
/// </param>
/// <param name="DisplayName">
/// The name shown to the user.
/// </param>
/// <param name="Description">
/// A short description of the data source.
/// </param>
/// <param name="SupportedAssetClasses">
/// The asset classes the provider can serve.
/// </param>
/// <param name="UpdateMode">
/// How the provider obtains data.
/// </param>
/// <param name="RequiresApiKey">
/// Whether the provider needs credentials to work.
/// </param>
/// <param name="Homepage">
/// A link to the data source or its terms of use.
/// </param>
/// <param name="Options">
/// The user-configurable options.
/// </param>
/// <param name="EnabledByDefault">
/// Whether the provider is enabled on first run. Demo or test providers set this to <see langword="false"/>.
/// </param>
public sealed record ProviderDescriptor(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<AssetClass> SupportedAssetClasses,
    ProviderUpdateMode UpdateMode,
    bool RequiresApiKey = false,
    Uri? Homepage = null,
    IReadOnlyList<ProviderOptionDefinition>? Options = null,
    bool EnabledByDefault = true)
{
    /// <summary>
    /// Gets the option definitions, never <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<ProviderOptionDefinition> OptionDefinitions => Options ?? [];

    /// <summary>
    /// Determines whether the provider can serve an asset class.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class to test.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the asset class is supported.
    /// </returns>
    public bool Supports(AssetClass assetClass) => SupportedAssetClasses.Contains(assetClass);
}
