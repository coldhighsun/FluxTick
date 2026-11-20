using FluxTick.App.ViewModels;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="RecommendationStars"/>.
/// </summary>
public class RecommendationStarsTests
{
    /// <summary>
    /// Verifies a score is shown as that many filled stars and nothing more.
    /// </summary>
    [Theory]
    [InlineData(1, "★")]
    [InlineData(3, "★★★")]
    [InlineData(5, "★★★★★")]
    public void Format_Score_FillsThatManyStars(int score, string expected)
    {
        RecommendationStars.Format(score).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a source without a score shows no stars at all.
    /// </summary>
    [Fact]
    public void Format_NoScore_IsEmpty()
    {
        RecommendationStars.Format(null).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies an out-of-range score never produces a negative or more than the full number of stars.
    /// </summary>
    [Theory]
    [InlineData(-2, "")]
    [InlineData(9, "★★★★★")]
    public void Format_OutOfRange_IsClamped(int score, string expected)
    {
        RecommendationStars.Format(score).Should().Be(expected);
    }
}
