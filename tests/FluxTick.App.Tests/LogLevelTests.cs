using FluxTick.App.Services;
using Serilog.Events;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the minimum level of the log file.
/// </summary>
public class LogLevelTests
{
    /// <summary>
    /// Verifies that the names of levels are read without regard to case or surrounding spaces.
    /// </summary>
    [Theory]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData(" Warning ", LogEventLevel.Warning)]
    [InlineData("Verbose", LogEventLevel.Verbose)]
    public void ParseLogLevel_KnownName_ReturnsThatLevel(string value, LogEventLevel expected)
    {
        AppHost.ParseLogLevel(value).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that a missing, empty, unknown or numeric value falls back to information.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("loud")]
    [InlineData("42")]
    public void ParseLogLevel_InvalidValue_ReturnsInformation(string? value)
    {
        AppHost.ParseLogLevel(value).Should().Be(LogEventLevel.Information);
    }
}
