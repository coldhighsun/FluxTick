using FluxTick.App.Services;
using Microsoft.Extensions.Time.Testing;
using static FluxTick.App.Services.TrayClickInterpreter;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for what a click on the tray icon asks for.
/// </summary>
public class TrayClickInterpreterTests
{
    /// <summary>
    /// The longest time between two clicks that make a double-click in these tests.
    /// </summary>
    private static readonly TimeSpan DoubleClick = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long a click after the list closed by itself does not open it again in these tests.
    /// </summary>
    private static readonly TimeSpan Guard = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Verifies a single click lists the instruments at once.
    /// </summary>
    [Fact]
    public void Click_Single_ShowsTheList()
    {
        var (interpreter, _) = Create();

        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.ShowList);
    }

    /// <summary>
    /// Verifies a second click within the double-click time opens the window.
    /// </summary>
    [Fact]
    public void Click_SecondWithinDoubleClickTime_OpensTheWindow()
    {
        var (interpreter, time) = Create();

        interpreter.Click(listVisible: false);
        time.Advance(TimeSpan.FromMilliseconds(200));

        interpreter.Click(listVisible: true).Should().Be(TrayClickAction.OpenWindow);
    }

    /// <summary>
    /// Verifies a second click after the double-click time is a click of its own: it closes the list that is open.
    /// </summary>
    [Fact]
    public void Click_SecondAfterDoubleClickTime_HidesTheOpenList()
    {
        var (interpreter, time) = Create();

        interpreter.Click(listVisible: false);
        time.Advance(DoubleClick + TimeSpan.FromMilliseconds(1));

        interpreter.Click(listVisible: true).Should().Be(TrayClickAction.HideList);
    }

    /// <summary>
    /// Verifies a double-click is used up by the window it opens, so a third quick click starts over.
    /// </summary>
    [Fact]
    public void Click_ThirdQuickClick_StartsOver()
    {
        var (interpreter, time) = Create();

        interpreter.Click(listVisible: false);
        time.Advance(TimeSpan.FromMilliseconds(100));
        interpreter.Click(listVisible: true);
        time.Advance(TimeSpan.FromMilliseconds(100));

        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.ShowList);
    }

    /// <summary>
    /// Verifies the click that took the focus from the list, and so closed it, does not bring the list straight back.
    /// </summary>
    [Fact]
    public void Click_RightAfterTheListClosedByItself_DoesNothing()
    {
        var (interpreter, time) = Create();

        interpreter.NotifyDismissed();
        time.Advance(TimeSpan.FromMilliseconds(50));

        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.None);
    }

    /// <summary>
    /// Verifies the guard ends with its time, and holds for one click only.
    /// </summary>
    [Fact]
    public void Click_AfterTheGuard_ShowsTheListAgain()
    {
        var (interpreter, time) = Create();

        interpreter.NotifyDismissed();
        time.Advance(Guard + TimeSpan.FromMilliseconds(1));
        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.ShowList);

        time.Advance(DoubleClick + TimeSpan.FromMilliseconds(1));
        interpreter.NotifyDismissed();
        time.Advance(TimeSpan.FromMilliseconds(50));
        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.None);

        time.Advance(DoubleClick + TimeSpan.FromMilliseconds(1));
        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.ShowList);
    }

    /// <summary>
    /// Verifies a double-click on the icon while the list is open still opens the window: the first press closes the list,
    /// which is ignored as a click, and the second click completes the double-click.
    /// </summary>
    [Fact]
    public void Click_DoubleClickWhileTheListIsOpen_OpensTheWindow()
    {
        var (interpreter, time) = Create();

        interpreter.NotifyDismissed();
        time.Advance(TimeSpan.FromMilliseconds(50));
        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.None);
        time.Advance(TimeSpan.FromMilliseconds(100));

        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.OpenWindow);
    }

    /// <summary>
    /// Verifies a double-click on the icon opens the window although the second press took the focus from the list the first
    /// click had opened.
    /// </summary>
    [Fact]
    public void Click_DoubleClickThatClosesTheListOnTheWay_OpensTheWindow()
    {
        var (interpreter, time) = Create();

        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.ShowList);
        time.Advance(TimeSpan.FromMilliseconds(100));
        interpreter.NotifyDismissed();
        time.Advance(TimeSpan.FromMilliseconds(20));

        interpreter.Click(listVisible: false).Should().Be(TrayClickAction.OpenWindow);
    }

    /// <summary>
    /// Creates an interpreter with a clock the test drives.
    /// </summary>
    /// <returns>
    /// The interpreter and its clock.
    /// </returns>
    private static (TrayClickInterpreter Interpreter, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider();
        return (new TrayClickInterpreter(time, DoubleClick, Guard), time);
    }
}
