using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Tests for <see cref="FreshDirectoryTracker"/>: it remembers only the last known/fresh
/// directory, resets on demand, and honours its configured string comparison.
/// </summary>
public sealed class FreshDirectoryTrackerTests
{
    [Fact]
    public void TracksKnownDirectoryWithoutAssumingItIsFresh()
    {
        var tracker = new FreshDirectoryTracker(StringComparison.Ordinal);
        Assert.False(tracker.IsKnown("/a"));
        Assert.False(tracker.IsFreshlyCreated("/a"));

        tracker.MarkKnown("/a");
        Assert.True(tracker.IsKnown("/a"));
        Assert.False(tracker.IsFreshlyCreated("/a"));
        Assert.False(tracker.IsKnown("/b"));
    }

    [Fact]
    public void TracksOnlyTheLastFreshlyCreatedDirectory()
    {
        var tracker = new FreshDirectoryTracker(StringComparison.Ordinal);
        Assert.False(tracker.IsKnown("/a"));             // nothing marked yet
        Assert.False(tracker.IsFreshlyCreated("/a"));    // nothing marked yet
        Assert.False(tracker.IsFreshlyCreated(null));    // null is never fresh

        tracker.MarkCreated("/a");
        Assert.True(tracker.IsKnown("/a"));
        Assert.True(tracker.IsFreshlyCreated("/a"));
        Assert.False(tracker.IsKnown("/b"));             // only the marked directory
        Assert.False(tracker.IsFreshlyCreated("/b"));    // only the marked directory

        tracker.MarkCreated("/b");                        // overwrites the previous
        Assert.False(tracker.IsKnown("/a"));
        Assert.False(tracker.IsFreshlyCreated("/a"));
        Assert.True(tracker.IsKnown("/b"));
        Assert.True(tracker.IsFreshlyCreated("/b"));

        tracker.Reset();
        Assert.False(tracker.IsKnown("/b"));              // reset clears
        Assert.False(tracker.IsFreshlyCreated("/b"));    // reset clears
    }

    [Theory]
    [InlineData(StringComparison.Ordinal, false)]
    [InlineData(StringComparison.OrdinalIgnoreCase, true)]
    public void HonoursConfiguredComparison(StringComparison comparison, bool matchesDifferentCase)
    {
        var tracker = new FreshDirectoryTracker(comparison);
        tracker.MarkCreated("/A");
        Assert.Equal(matchesDifferentCase, tracker.IsKnown("/a"));
        Assert.Equal(matchesDifferentCase, tracker.IsFreshlyCreated("/a"));
    }
}
