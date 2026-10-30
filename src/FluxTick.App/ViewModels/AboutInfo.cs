using System.Reflection;
using FluxTick.App.Services;

namespace FluxTick.App.ViewModels;

/// <summary>
/// What the about section of the settings page shows about the product. The texts never change while the app runs.
/// </summary>
public sealed class AboutInfo
{
    /// <summary>
    /// The product name, from the assembly metadata.
    /// </summary>
    private static readonly string ProductName =
        typeof(AboutInfo).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? UpdateService.Repo;

    /// <summary>
    /// The author, from the assembly metadata.
    /// </summary>
    private static readonly string AuthorName =
        typeof(AboutInfo).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? UpdateService.Owner;

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
