using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FluxTick.App.Tests;

/// <summary>
/// Checks the framework behaviour the watchlist's right-click menus rely on.
/// </summary>
public class ContextRequestedRoutingTests
{
    /// <summary>
    /// Verifies the context request tunnels down to the clicked control before it bubbles up. The watchlist prepares
    /// a card's menu in a tunnelling handler, because the menu itself has no data context before it opened once.
    /// </summary>
    [Fact]
    public void ContextRequestedEvent_SupportsTunnelling()
    {
        Control.ContextRequestedEvent.RoutingStrategies.Should().HaveFlag(RoutingStrategies.Tunnel);
    }
}
