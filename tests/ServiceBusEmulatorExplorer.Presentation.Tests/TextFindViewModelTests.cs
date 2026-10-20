using ServiceBusEmulatorExplorer.Presentation.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class TextFindViewModelTests
{
    [Fact]
    public void Finds_every_case_insensitive_match_without_overlaps_and_selects_the_first()
    {
        var find = new TextFindViewModel();

        Assert.True(find.Search("Order aaaa ORDER order", "order", force: false));

        Assert.Equal([0, 11, 17], find.Matches);
        Assert.Equal(0, find.Current);
        Assert.Equal(5, find.QueryLength);
        Assert.Equal("1 of 3", find.CountText);

        find.Search("aaaa", "aa", force: false);
        Assert.Equal([0, 2], find.Matches);
    }

    [Fact]
    public void Without_a_query_or_a_match_it_says_so()
    {
        var find = new TextFindViewModel();

        find.Search("body", "", force: false);
        Assert.Empty(find.Matches);
        Assert.Equal(-1, find.Current);
        Assert.Equal("", find.CountText);

        find.Search("body", "missing", force: false);
        Assert.Equal("No matches", find.CountText);
        Assert.False(find.Move(1));
        Assert.Equal(-1, find.Current);

        find.Search("ab", "abc", force: false);
        Assert.Empty(find.Matches);
    }

    [Fact]
    public void Searching_the_same_text_again_changes_nothing_unless_forced()
    {
        var find = new TextFindViewModel();
        find.Search("one one", "one", force: false);
        find.Move(1);

        Assert.False(find.Search("one one", "one", force: false));
        Assert.Equal(1, find.Current);
        Assert.True(find.Search("one one", "one", force: true));
        Assert.Equal(0, find.Current);

        find.Move(1);
        find.Forget();
        Assert.True(find.Search("one one", "one", force: false));
        Assert.Equal(0, find.Current);
    }

    [Fact]
    public void Moving_wraps_around_in_both_directions()
    {
        var find = new TextFindViewModel();
        find.Search("x x x", "x", force: false);

        Assert.True(find.Move(1));
        Assert.Equal(1, find.Current);
        find.Move(1);
        find.Move(1);
        Assert.Equal(0, find.Current);
        find.Move(-1);
        Assert.Equal(2, find.Current);
        Assert.Equal("3 of 3", find.CountText);
    }

    [Fact]
    public void Unselecting_keeps_the_matches_and_the_next_move_starts_at_an_end()
    {
        var find = new TextFindViewModel();
        find.Search("x x x", "x", force: false);

        find.Unselect();
        Assert.Equal("3 matches", find.CountText);
        find.Move(-1);
        Assert.Equal(2, find.Current);

        find.Unselect();
        find.Move(1);
        Assert.Equal(0, find.Current);
    }
}
