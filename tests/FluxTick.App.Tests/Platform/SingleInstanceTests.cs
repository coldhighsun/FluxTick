using AwesomeAssertions;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of <see cref="SingleInstance"/>.
/// </summary>
public sealed class SingleInstanceTests
{
    /// <summary>
    /// Makes a name no other test or app uses. It is short, because on macOS the name becomes part of a socket path that
    /// may hold only 104 characters, after a long temp directory.
    /// </summary>
    /// <returns>
    /// The name.
    /// </returns>
    private static string TestName() => $"ft-{Guid.NewGuid():N}"[..11];

    /// <summary>
    /// Verifies the demo mode excludes only other demo copies.
    /// </summary>
    [Fact]
    public void NameFor_Demo_DiffersFromNormal() =>
        SingleInstance.NameFor(demo: true).Should().NotBe(SingleInstance.NameFor(demo: false));

    /// <summary>
    /// Verifies the name holds only characters that every platform accepts, whatever the user name is.
    /// </summary>
    [Fact]
    public void NameFor_AnyUser_UsesPlainCharactersOnly() =>
        SingleInstance.NameFor(demo: false).Should().MatchRegex("^[A-Za-z0-9-]{1,64}$");

    /// <summary>
    /// Verifies a second copy cannot start while the first runs, and can once the first ended.
    /// </summary>
    [Fact]
    public void TryAcquire_WhileRunning_RefusesSecondCopy()
    {
        var name = TestName();
        using var first = SingleInstance.TryAcquire(name);

        first.Should().NotBeNull();
        SingleInstance.TryAcquire(name).Should().BeNull();

        first!.Dispose();
        using var again = SingleInstance.TryAcquire(name);
        again.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies a second copy can ask the first to show its window.
    /// </summary>
    [Fact]
    public void NotifyRunning_FirstListening_RaisesActivated()
    {
        var name = TestName();
        using var first = SingleInstance.TryAcquire(name);
        using var activated = new ManualResetEventSlim();
        first!.Activated += (_, _) => activated.Set();
        first.StartListening();

        var told = SingleInstance.NotifyRunning(name);

        told.Should().BeTrue();
        activated.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    /// <summary>
    /// Verifies several requests in a row are all served, as the listener takes one connection after the other.
    /// </summary>
    [Fact]
    public void NotifyRunning_Repeated_RaisesActivatedEachTime()
    {
        var name = TestName();
        using var first = SingleInstance.TryAcquire(name);
        var count = 0;
        first!.Activated += (_, _) => Interlocked.Increment(ref count);
        first.StartListening();

        SingleInstance.NotifyRunning(name).Should().BeTrue();
        SingleInstance.NotifyRunning(name).Should().BeTrue();
        SingleInstance.NotifyRunning(name).Should().BeTrue();

        Volatile.Read(ref count).Should().Be(3);
    }

    /// <summary>
    /// Verifies asking fails quickly when no copy listens.
    /// </summary>
    [Fact]
    public void NotifyRunning_NobodyListening_ReturnsFalse() =>
        SingleInstance.NotifyRunning(TestName()).Should().BeFalse();
}
