using System.Reflection;
using FluxTick.App.Localization;
using FluxTick.App.Services;

namespace FluxTick.App.ViewModels;

/// <summary>
/// What the about window shows. The window is modal and short-lived, so the texts are fixed when it opens.
/// </summary>
public sealed class AboutViewModel
{
    /// <summary>
    /// The product name, from the assembly metadata.
    /// </summary>
    private static readonly string ProductName =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? UpdateService.Repo;

    /// <summary>
    /// The author, from the assembly metadata.
    /// </summary>
    private static readonly string AuthorName =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? UpdateService.Owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViewModel"/> class.
    /// </summary>
    /// <param name="updates">
    /// The update section that knows the running version, or <see langword="null"/> when there is none.
    /// </param>
    public AboutViewModel(UpdateViewModel? updates) =>
        VersionText = updates?.CurrentVersionText ?? Loc.Get("Update_UnknownVersion");

    /// <summary>
    /// Gets the running version as text.
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// Gets the product name.
    /// </summary>
    public string Product => ProductName;

    /// <summary>
    /// Gets the author.
    /// </summary>
    public string Author => AuthorName;

    /// <summary>
    /// Gets the license of the project.
    /// </summary>
    public string License => "MIT";

    /// <summary>
    /// Gets the project home page.
    /// </summary>
    public string HomepageUrl => $"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}";
}
