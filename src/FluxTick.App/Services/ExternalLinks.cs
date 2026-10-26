using System.ComponentModel;
using System.Diagnostics;

namespace FluxTick.App.Services;

/// <summary>
/// Opens web pages of the project in the browser. Only https addresses on github.com are opened: the shell starts
/// whatever it is given, and an address that came over the network must not be able to start a file or a protocol
/// handler.
/// </summary>
public static class ExternalLinks
{
    /// <summary>
    /// The host the pages are on.
    /// </summary>
    private const string GitHubHost = "github.com";

    /// <summary>
    /// Tells whether an address is an https address on github.com.
    /// </summary>
    /// <param name="url">
    /// The address.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it may be opened.
    /// </returns>
    public static bool IsGitHubUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.Equals(uri.Host, GitHubHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens a page on github.com in the browser.
    /// </summary>
    /// <param name="url">
    /// The address.
    /// </param>
    /// <param name="start">
    /// Starts the process, or <see langword="null"/> for the real one; replaceable for tests.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the browser was asked to open the page; <see langword="false"/> when the address may
    /// not be opened or no browser could be started.
    /// </returns>
    public static bool TryOpenGitHub(string? url, Action<ProcessStartInfo>? start = null)
    {
        if (!IsGitHubUrl(url))
        {
            return false;
        }

        try
        {
            var info = new ProcessStartInfo(new Uri(url!).AbsoluteUri) { UseShellExecute = true };
            if (start is null)
            {
                using var process = Process.Start(info);
            }
            else
            {
                start(info);
            }

            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No browser is registered; there is nothing the caller can do about it.
            return false;
        }
    }
}
