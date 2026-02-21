using SmartCopy.Core.Sync;

namespace SmartCopy.Tests.Sync;

public sealed class SyncWorkflowTests
{
    [Fact]
    public void FindOrphans_ReturnsOnlyTargetsMissingInSource()
    {
        var source = new[] { "a.mp3", "b.mp3" };
        var target = new[] { "a.mp3", "c.mp3" };

        var orphans = SyncWorkflow.FindOrphans(source, target);

        Assert.Single(orphans);
        Assert.Equal("c.mp3", orphans[0]);
    }
}

