using System.Runtime.CompilerServices;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

internal static class DisplayInit
{
    // The box tests expect the settings spelling whatever machine runs them.
    [ModuleInitializer]
    internal static void Init() => HotkeyDisplay.UseMacNames = false;
}
