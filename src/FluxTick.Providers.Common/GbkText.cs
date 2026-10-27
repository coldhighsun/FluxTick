using System.Text;

namespace FluxTick.Providers.Common;

/// <summary>
/// Decodes the GBK text that the Chinese quote services answer in.
/// </summary>
public static class GbkText
{
    /// <summary>
    /// The GBK encoding, looked up once; it needs the code pages provider on .NET.
    /// </summary>
    private static readonly Lazy<Encoding> Gbk = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    });

    /// <summary>
    /// Decodes a service response.
    /// </summary>
    /// <param name="bytes">
    /// The response body.
    /// </param>
    /// <returns>
    /// The text.
    /// </returns>
    public static string Decode(byte[] bytes) => Gbk.Value.GetString(bytes);
}
