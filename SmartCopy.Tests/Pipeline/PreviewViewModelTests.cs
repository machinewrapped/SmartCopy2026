using SmartCopy.Core.Pipeline;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Pipeline;

public sealed class PreviewViewModelTests
{
    // ─── helpers ─────────────────────────────────────────────────────────────

    private static PlannedAction MakeAction(
        SourceResult      src,
        DestinationResult dest = DestinationResult.None,
        string            sourcePath = "/src",
        string?           destPath   = "/dst",
        int               files   = 1,
        int               folders = 0,
        long              bytes   = 10) =>
        new(sourcePath, src, destPath, dest, files, folders, bytes, bytes);

    private static OperationPlan MakePlan(params PlannedAction[] actions) =>
        new()
        {
            Actions                   = actions,
            TotalInputBytes           = actions.Sum(a => a.InputBytes),
            TotalEstimatedOutputBytes = actions.Sum(a => a.OutputBytes),
        };

    // ─── LoadFrom: summary properties ────────────────────────────────────────

    [Fact]
    public void LoadFrom_SetsTotalActionCount()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created), 
            MakeAction(SourceResult.Copied, DestinationResult.Overwritten)));

        Assert.Equal(2, vm.TotalActionCount);
    }

    [Fact]
    public void LoadFrom_SetsTotalEstimatedInputAndOutputBytes()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created, bytes: 100),
            MakeAction(SourceResult.Copied, DestinationResult.Created, bytes: 200)));

        Assert.Equal(300, vm.TotalEstimatedInputBytes);
        Assert.Equal(300, vm.TotalEstimatedOutputBytes);
    }

    [Fact]
    public void LoadFrom_AggregatesTotalFilesAndFolders()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created, files: 3, folders: 2),
            MakeAction(SourceResult.Copied, DestinationResult.Created, files: 5, folders: 0)));

        Assert.Equal(8, vm.TotalFilesAffected);
        Assert.Equal(2, vm.TotalFoldersAffected);
    }

    // ─── LoadFrom: grouping ───────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_GroupsActionsByWarningLevel()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied,  DestinationResult.Created),
            MakeAction(SourceResult.Copied,  DestinationResult.Overwritten),
            MakeAction(SourceResult.Moved,   DestinationResult.Created),
            MakeAction(SourceResult.Deleted, DestinationResult.None, destPath: null)));

        Assert.Equal(4, vm.Groups.Count);
        Assert.Single(vm.Groups, g => g.Title.StartsWith("Will delete") && g.Actions.Count == 1);
        Assert.Single(vm.Groups, g => g.Title.StartsWith("Will overwrite") && g.Actions.Count == 1);
        Assert.Single(vm.Groups, g => g.Title.StartsWith("Will copy") && g.Actions.Count == 2);
        Assert.Single(vm.Groups, g => g.Title.StartsWith("Will move") && g.Actions.Count == 1);
    }

    [Fact]
    public void LoadFrom_DeleteGroupIsFirst_OverwriteGroupIsSecond()
    {
        // Even though actions are inserted copy-first, dangerous groups come first.
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied,  DestinationResult.Created),
            MakeAction(SourceResult.Copied,  DestinationResult.Overwritten),
            MakeAction(SourceResult.Deleted, DestinationResult.None, destPath: null)));

        Assert.StartsWith("Will delete",    vm.Groups[0].Title);
        Assert.StartsWith("Will overwrite", vm.Groups[1].Title);
        Assert.StartsWith("Will copy",      vm.Groups[2].Title);
    }

    [Fact]
    public void LoadFrom_TrashedAction_PlacedInDeleteGroup()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Trashed, DestinationResult.None, destPath: null)));

        Assert.Single(vm.Groups);
        Assert.StartsWith("Will delete", vm.Groups[0].Title);
    }

    [Fact]
    public void LoadFrom_EmptyPlan_ProducesNoGroups()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan());

        Assert.Empty(vm.Groups);
    }

    // ─── LoadFrom: group expansion ────────────────────────────────────────────

    [Fact]
    public void LoadFrom_DeleteAndOverwriteGroups_AreExpandedByDefault()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Deleted,  DestinationResult.None, destPath: null),
            MakeAction(SourceResult.Copied,   DestinationResult.Overwritten),
            MakeAction(SourceResult.Copied,   DestinationResult.Created),
            MakeAction(SourceResult.Moved,    DestinationResult.Created)));

        var delete    = vm.Groups.Single(g => g.Title.StartsWith("Will delete"));
        var overwrite = vm.Groups.Single(g => g.Title.StartsWith("Will overwrite"));
        var copy      = vm.Groups.Single(g => g.Title.StartsWith("Will copy"));
        var move      = vm.Groups.Single(g => g.Title.StartsWith("Will move"));

        Assert.True(delete.IsExpanded);
        Assert.True(overwrite.IsExpanded);
        Assert.False(copy.IsExpanded);
        Assert.False(move.IsExpanded);
    }

    // ─── LoadFrom: group title formatting ────────────────────────────────────

    [Fact]
    public void LoadFrom_GroupTitle_FilesOnly_OmitsFolderSuffix()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created, files: 2, folders: 0)));

        Assert.Equal("Will copy 2 files", vm.Groups[0].Title);
    }

    [Fact]
    public void LoadFrom_GroupTitle_IncludesFolders_WhenPresent()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created, files: 3, folders: 2)));

        Assert.Equal("Will copy 3 files and 2 folders", vm.Groups[0].Title);
    }

    [Fact]
    public void LoadFrom_GroupTitle_SumsFilesAcrossMultipleActionsInGroup()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created, files: 4, folders: 1),
            MakeAction(SourceResult.Copied, DestinationResult.Created, files: 2, folders: 3)));

        Assert.Equal("Will copy 6 files and 4 folders", vm.Groups[0].Title);
    }

    // ─── LoadFrom: item mapping ───────────────────────────────────────────────

    [Fact]
    public void LoadFrom_ItemsInGroup_HaveCorrectPaths()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created, sourcePath: "/src/foo.txt", destPath: "/dst/foo.txt")));

        var item = vm.Groups[0].Actions.Single();
        Assert.Equal("/src/foo.txt", item.SourcePath);
        Assert.Equal("/dst/foo.txt", item.DestinationPath);
    }

    [Fact]
    public void LoadFrom_ItemWithNullDestination_HasEmptyDestinationPath()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Deleted, DestinationResult.None, destPath: null)));

        var item = vm.Groups[0].Actions.Single();
        Assert.Equal(string.Empty, item.DestinationPath);
        Assert.False(item.HasDestination);
    }

    [Fact]
    public void LoadFrom_ItemWithDestination_HasDestinationTrue()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(MakeAction(SourceResult.Copied, DestinationResult.Created, destPath: "/dst/a")));

        Assert.True(vm.Groups[0].Actions.Single().HasDestination);
    }

    // ─── LoadFrom: idempotency on repeated calls ──────────────────────────────

    [Fact]
    public void LoadFrom_CalledTwice_ClearsAndRebuildsGroups()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
                MakeAction(SourceResult.Copied, DestinationResult.Created), 
                MakeAction(SourceResult.Copied, DestinationResult.Created)));
        vm.LoadFrom(MakePlan(
                MakeAction(SourceResult.Deleted, DestinationResult.None, destPath: null)));

        // Second load: only one delete group, no stale copy group.
        Assert.Single(vm.Groups);
        Assert.StartsWith("Will delete", vm.Groups[0].Title);
        Assert.Equal(1, vm.TotalActionCount);
    }

    // ─── IsDeletePipeline & ConfirmButtonText ─────────────────────────────────

    [Fact]
    public void IsDeletePipeline_TrueWhenPlanContainsDeleteAction()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied),
            MakeAction(SourceResult.Deleted, DestinationResult.None, destPath: null)));

        Assert.True(vm.IsDeletePipeline);
    }

    [Fact]
    public void IsDeletePipeline_TrueWhenPlanContainsTrashedAction()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Trashed, DestinationResult.None, destPath: null)));

        Assert.True(vm.IsDeletePipeline);
    }

    [Fact]
    public void IsDeletePipeline_FalseWhenNoDeletionsOrTrash()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created),
            MakeAction(SourceResult.Moved,  DestinationResult.Overwritten)));

        Assert.False(vm.IsDeletePipeline);
    }

    [Fact]
    public void ConfirmButtonText_NonDeletePipeline_UsesArrowAndFileCount()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, files: 5)));

        Assert.False(vm.IsDeletePipeline);
        Assert.Equal("▶ Run (5 files)", vm.ConfirmButtonText);
    }

    [Fact]
    public void ConfirmButtonText_DeletePipeline_UsesWarningGlyph()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Deleted, DestinationResult.None, destPath: null, files: 3)));

        Assert.Contains("⚠ Run", vm.ConfirmButtonText);
        Assert.Contains("3 files", vm.ConfirmButtonText);
    }

    [Fact]
    public void ConfirmButtonText_DeletePipelineWithFolders_IncludesFolderCount()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Deleted, DestinationResult.None, destPath: null, files: 4, folders: 2)));

        Assert.Equal("⚠ Run (4 files, 2 folders)", vm.ConfirmButtonText);
    }

    // ─── PreviewItemViewModel.GetActionText ───────────────────────────────────

    [Theory]
    [InlineData(SourceResult.Copied,  DestinationResult.Created,     "Copy")]
    [InlineData(SourceResult.Copied,  DestinationResult.Overwritten, "Copy (overwrite)")]
    [InlineData(SourceResult.Moved,   DestinationResult.Created,     "Move")]
    [InlineData(SourceResult.Moved,   DestinationResult.Overwritten, "Move (overwrite)")]
    [InlineData(SourceResult.Trashed, DestinationResult.None,        "Trash")]
    [InlineData(SourceResult.Deleted, DestinationResult.None,        "Delete")]
    [InlineData(SourceResult.None,    DestinationResult.None,        "")]
    public void GetActionText_ReturnsExpectedString(
        SourceResult src, DestinationResult dest, string expected)
    {
        Assert.Equal(expected, PreviewItemViewModel.GetActionText(src, dest));
    }

    // ─── PreviewGroupViewModel.Count ─────────────────────────────────────────

    [Fact]
    public void GroupCount_ReflectsNumberOfActionsInGroup()
    {
        var vm = new PreviewViewModel();
        vm.LoadFrom(MakePlan(
            MakeAction(SourceResult.Copied, DestinationResult.Created),
            MakeAction(SourceResult.Copied, DestinationResult.Created),
            MakeAction(SourceResult.Copied, DestinationResult.Created)));

        var copyGroup = vm.Groups.Single(g => g.Title.StartsWith("Will copy"));
        Assert.Equal(3, copyGroup.Count);
    }
}
