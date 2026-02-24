using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.Tests.Pipeline;

public sealed class StepEditorViewModelTests
{
    [Fact]
    public void CopyEditor_LoadFromBuildStep_RoundTripsDestination()
    {
        var editor = new CopyStepEditorViewModel();
        editor.LoadFrom(new PipelineStepViewModel(new CopyStep("/mem/out")));

        var step = Assert.IsType<CopyStep>(editor.BuildStep());
        Assert.Equal("/mem/out", step.DestinationPath);
    }

    [Fact]
    public void MoveEditor_LoadFromBuildStep_RoundTripsDestination()
    {
        var editor = new MoveStepEditorViewModel();
        editor.LoadFrom(new PipelineStepViewModel(new MoveStep("/mem/archive")));

        var step = Assert.IsType<MoveStep>(editor.BuildStep());
        Assert.Equal("/mem/archive", step.DestinationPath);
    }

    [Fact]
    public void DeleteEditor_ModeToggle_BuildsMatchingDeleteStep()
    {
        var editor = new DeleteStepEditorViewModel
        {
            DeleteMode = DeleteMode.Permanent,
        };

        var step = Assert.IsType<DeleteStep>(editor.BuildStep());
        Assert.Equal(DeleteMode.Permanent, step.Mode);
    }

    [Fact]
    public void FlattenEditor_ConflictStrategy_RoundTrips()
    {
        var editor = new FlattenStepEditorViewModel
        {
            ConflictStrategy = FlattenConflictStrategy.Skip,
        };

        var step = Assert.IsType<FlattenStep>(editor.BuildStep());
        Assert.Equal(FlattenConflictStrategy.Skip, step.ConflictStrategy);
    }

    [Fact]
    public void IsValid_GatesCopyAndRebaseEditors()
    {
        var copy = new CopyStepEditorViewModel { DestinationPath = "" };
        var rebase = new RebaseStepEditorViewModel { StripPrefix = "", AddPrefix = "" };

        Assert.False(copy.IsValid);
        Assert.False(rebase.IsValid);

        copy.DestinationPath = "/mem/out";
        rebase.AddPrefix = "target";

        Assert.True(copy.IsValid);
        Assert.True(rebase.IsValid);
    }

    [Fact]
    public void RenameEditor_LivePreview_UpdatesFromPattern()
    {
        var editor = new RenameStepEditorViewModel
        {
            Pattern = "{name}_converted.{ext}",
        };

        Assert.Contains("sample_track", editor.LivePreviewName);
        Assert.Contains("flac", editor.LivePreviewName);
        Assert.True(editor.IsValid);
    }
}
