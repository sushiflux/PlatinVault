using Companion.Api.Settings;

namespace Companion.Api.Tests;

/// <summary>The set of games the trophy cabinet has already presented: parsing, merging and the "no state yet" rule.</summary>
public sealed class CabinetPresentedTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void A_missing_or_unreadable_value_is_no_state_at_all(string? stored)
    {
        Assert.Null(CabinetPresented.Parse(stored));
    }

    [Fact]
    public void An_empty_array_is_a_real_state_with_nothing_presented()
    {
        var set = CabinetPresented.Parse("[]");
        Assert.NotNull(set);
        Assert.Empty(set);
    }

    [Fact]
    public void Parse_drops_ids_that_are_not_positive_and_duplicates()
    {
        var set = CabinetPresented.Parse("[620, 620, 0, -5, 1245620]");
        Assert.Equal([620, 1245620], set!.Order().ToArray());
    }

    [Fact]
    public void Merge_only_adds_and_never_removes()
    {
        var merged = CabinetPresented.Merge([620, 70], [1245620, 70]);
        Assert.Equal([70, 620, 1245620], merged.Order().ToArray());
    }

    [Fact]
    public void Merge_ignores_invalid_submitted_ids_and_works_without_a_stored_set()
    {
        Assert.Equal([1], CabinetPresented.Merge(null, [0, -1, 1]).Order().ToArray());
        Assert.Empty(CabinetPresented.Merge(null, null));
    }

    [Fact]
    public void Merge_never_grows_beyond_the_cap()
    {
        var merged = CabinetPresented.Merge(null, Enumerable.Range(1, CabinetPresented.MaxIds + 500));
        Assert.Equal(CabinetPresented.MaxIds, merged.Count);
    }

    [Fact]
    public void Serialize_and_parse_round_trip_in_a_stable_order()
    {
        var json = CabinetPresented.Serialize([300, 5, 42]);
        Assert.Equal("[5,42,300]", json);
        Assert.Equal([5, 42, 300], CabinetPresented.Parse(json)!.Order().ToArray());
    }
}
