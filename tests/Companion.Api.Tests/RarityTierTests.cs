using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>The trophy tier (gold/silver/bronze) from Steam's worldwide share: limits are inclusive at the top of each tier.</summary>
public sealed class RarityTierTests
{
    [Theory]
    [InlineData(0.0, "Gold")]
    [InlineData(1.8, "Gold")]
    [InlineData(5.6, "Gold")]
    [InlineData(10.0, "Gold")]
    [InlineData(10.1, "Silver")]
    [InlineData(25.0, "Silver")]
    [InlineData(25.1, "Silver")]
    [InlineData(50.0, "Silver")]
    [InlineData(50.1, "Bronze")]
    [InlineData(86.7, "Bronze")]
    public void The_tier_follows_the_worldwide_share(double percent, string expected)
    {
        Assert.Equal(expected, SteamLiveTrackerService.CalculateRarityTier(percent));
    }
}
