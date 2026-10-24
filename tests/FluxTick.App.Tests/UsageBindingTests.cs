using FluxTick.App.Services;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="UsageBinding"/>.
/// </summary>
public sealed class UsageBindingTests
{
    /// <summary>
    /// Verifies prices count as shown only for a visible, restored window that is not parked at a screen edge.
    /// </summary>
    /// <param name="isVisible">
    /// Whether the window is shown.
    /// </param>
    /// <param name="isMinimized">
    /// Whether the window is minimized.
    /// </param>
    /// <param name="isDockedAway">
    /// Whether the window is docked away.
    /// </param>
    /// <param name="expected">
    /// The expected result.
    /// </param>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void IsShown_CombinesVisibilityMinimizingAndDock(bool isVisible, bool isMinimized, bool isDockedAway, bool expected) =>
        UsageBinding.IsShown(isVisible, isMinimized, isDockedAway).Should().Be(expected);

    /// <summary>
    /// Verifies a window that is about to open counts as shown, and a binding holds the usage from the start.
    /// </summary>
    [Fact]
    public async Task OpeningSource_BeforeTheWindowOpened_HoldsTheUsage()
    {
        await using var market = NewMarket();
        var inner = new FakeSource();

        using var binding = new UsageBinding(market, new OpeningShownSource(inner, opening: true));

        binding.IsHeld.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that once the opening ends the real state counts, so a window that never became visible lets go.
    /// </summary>
    [Fact]
    public async Task OpeningSource_EndOpening_FollowsTheRealState()
    {
        await using var market = NewMarket();
        var inner = new FakeSource();
        var source = new OpeningShownSource(inner, opening: true);
        using var binding = new UsageBinding(market, source);

        source.EndOpening();
        source.EndOpening();

        binding.IsHeld.Should().BeFalse();
        inner.Show(true);
        binding.IsHeld.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a window that is already visible when the opening ends keeps the usage.
    /// </summary>
    [Fact]
    public async Task OpeningSource_WindowVisibleWhenOpeningEnds_KeepsTheUsage()
    {
        await using var market = NewMarket();
        var inner = new FakeSource();
        var source = new OpeningShownSource(inner, opening: true);
        using var binding = new UsageBinding(market, source);
        inner.IsShown = true;

        source.EndOpening();

        binding.IsHeld.Should().BeTrue();
    }

    /// <summary>
    /// Verifies closing the window during the opening phase gives the usage back.
    /// </summary>
    [Fact]
    public async Task OpeningSource_ClosedWhileOpening_GivesBackTheUsage()
    {
        await using var market = NewMarket();
        var inner = new FakeSource();
        using var binding = new UsageBinding(market, new OpeningShownSource(inner, opening: true));

        inner.Close();

        binding.IsHeld.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a window that is not opening is reported as it really is.
    /// </summary>
    [Fact]
    public async Task OpeningSource_NotOpening_ReportsTheRealState()
    {
        await using var market = NewMarket();
        var inner = new FakeSource();

        using var binding = new UsageBinding(market, new OpeningShownSource(inner, opening: false));

        binding.IsHeld.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a source that is shown from the start holds the usage at once.
    /// </summary>
    [Fact]
    public async Task Constructor_SourceAlreadyShown_HoldsUsage()
    {
        await using var market = NewMarket();
        var source = new FakeSource { IsShown = true };

        using var binding = new UsageBinding(market, source);

        binding.IsHeld.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a source that is hidden from the start holds nothing.
    /// </summary>
    [Fact]
    public async Task Constructor_SourceHidden_HoldsNothing()
    {
        await using var market = NewMarket();

        using var binding = new UsageBinding(market, new FakeSource());

        binding.IsHeld.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the usage follows the changes of the source, and a repeated state changes nothing.
    /// </summary>
    [Fact]
    public async Task ShownChanged_TakesAndGivesBackTheUsage()
    {
        await using var market = NewMarket();
        var source = new FakeSource();
        using var binding = new UsageBinding(market, source);

        source.Show(true);
        source.Show(true);
        binding.IsHeld.Should().BeTrue();

        source.Show(false);
        source.Show(false);
        binding.IsHeld.Should().BeFalse();

        source.Show(true);
        binding.IsHeld.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a closed source gives the usage back and a late change does not take it again.
    /// </summary>
    [Fact]
    public async Task Closed_GivesBackTheUsageForGood()
    {
        await using var market = NewMarket();
        var source = new FakeSource { IsShown = true };
        using var binding = new UsageBinding(market, source);

        source.Close();
        source.Show(true);

        binding.IsHeld.Should().BeFalse();
    }

    /// <summary>
    /// Creates a service without providers.
    /// </summary>
    /// <returns>
    /// The service.
    /// </returns>
    private static MarketDataService NewMarket() => new(new ProviderRegistry([]), NullLoggerFactory.Instance, TimeProvider.System);

    /// <summary>
    /// A source whose state the test sets.
    /// </summary>
    private sealed class FakeSource : IShownSource
    {
        /// <inheritdoc />
        public event EventHandler? ShownChanged;

        /// <inheritdoc />
        public event EventHandler? Closed;

        /// <inheritdoc />
        public bool IsShown { get; set; }

        /// <summary>
        /// Sets the state and announces it.
        /// </summary>
        /// <param name="shown">
        /// Whether prices are shown.
        /// </param>
        public void Show(bool shown)
        {
            IsShown = shown;
            ShownChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Announces that the source is gone.
        /// </summary>
        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }
}
