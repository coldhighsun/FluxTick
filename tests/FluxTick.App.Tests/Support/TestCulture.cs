using System.Globalization;
using System.Runtime.CompilerServices;

namespace FluxTick.App.Tests;

/// <summary>
/// Sets the culture of the tests, so that the texts of numbers do not depend on the machine they run on. A test that is
/// about a culture sets its own.
/// </summary>
internal static class TestCulture
{
    /// <summary>
    /// Makes the invariant culture the culture of every thread of the tests.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Runs an action under a culture and puts the previous one back.
    /// </summary>
    /// <param name="name">
    /// The name of the culture, for example <c>de-DE</c>.
    /// </param>
    /// <param name="action">
    /// The action.
    /// </param>
    public static void Under(string name, Action action)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
