using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.Tests.Pipeline;

public sealed class StepEditorViewModelTests
{
    [Fact]
    public void CopyEditor_LoadFromBuildStep_RoundTripsDestination()
    {
        var editor = new CopyStepEditorViewModel(new TestAppContext());
        editor.LoadFrom(new PipelineStepViewModel(new CopyStep("mem://out")));

        var step = Assert.IsType<CopyStep>(editor.BuildStep());
        Assert.Equal("mem://out", step.DestinationPath);
    }

    [Fact]
    public void MoveEditor_LoadFromBuildStep_RoundTripsDestination()
    {
        var editor = new MoveStepEditorViewModel(new TestAppContext());
        editor.LoadFrom(new PipelineStepViewModel(new MoveStep("mem://archive")));

        var step = Assert.IsType<MoveStep>(editor.BuildStep());
        Assert.Equal("mem://archive", step.DestinationPath);
    }

    [Fact]
    public void DeleteEditor_ModeToggle_BuildsMatchingDeleteStep()
    {
        var settings = new AppSettings() { DefaultDeleteMode = DeleteMode.Permanent };

        var editor = new DeleteStepEditorViewModel(settings);

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
    public void IsValid_GatesCopyAndFlattenEditors()
    {
        var copy = new CopyStepEditorViewModel(new TestAppContext()) { DestinationPath = "" };
        var flatten = new FlattenStepEditorViewModel { Levels = null };

        Assert.False(copy.IsValid);
        Assert.False(flatten.IsValid);

        copy.DestinationPath = "mem://out";
        flatten.Levels = 1;

        Assert.True(copy.IsValid);
        Assert.True(flatten.IsValid);
    }

    [Fact]
    public void FlattenEditor_StripLeading_RoundTrips()
    {
        var editor = new FlattenStepEditorViewModel
        {
            TrimMode = FlattenTrimMode.StripLeading,
            Levels = 2,
        };

        var step = Assert.IsType<FlattenStep>(editor.BuildStep());
        Assert.Equal(FlattenTrimMode.StripLeading, step.TrimMode);
        Assert.Equal(2, step.Levels);
    }

    [Fact]
    public void FlattenEditor_LivePreview_ReflectsSettings()
    {
        var editor = new FlattenStepEditorViewModel
        {
            TrimMode = FlattenTrimMode.StripLeading,
            Levels = 1,
        };

        Assert.Contains("→", editor.LivePreview);
        Assert.Contains("b/c/d/e/file.txt", editor.LivePreview);
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

    // --- Selection-file editor default path tests ---

    private static TestAppContext MakeCtxWithSource(string sourcePath)
    {
        var provider = new MemoryFileSystemProvider(customRootPath: sourcePath);
        var settings = new AppSettings { LastSourcePath = sourcePath };
        var ctx = new TestAppContext(settings);
        ctx.Register(provider);
        return ctx;
    }

    [Fact]
    public void SaveSelectionToFileEditor_DefaultsToLastSourcePath_WhenProviderFound()
    {
        var ctx = MakeCtxWithSource("mem://Music");
        var editor = new SaveSelectionToFileStepEditorViewModel(ctx);
        Assert.Equal("mem://Music/selection.sc2sel", editor.FilePath);
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void SaveSelectionToFileEditor_NoDefault_WhenLastSourcePathAbsent()
    {
        var editor = new SaveSelectionToFileStepEditorViewModel(new TestAppContext());
        Assert.True(string.IsNullOrWhiteSpace(editor.FilePath));
        Assert.False(editor.IsValid);
    }

    [Fact]
    public void SaveSelectionToFileEditor_GetAutoName_ShowsFileName_WhenPathSet()
    {
        var ctx = MakeCtxWithSource("mem://Music");
        var editor = new SaveSelectionToFileStepEditorViewModel(ctx);
        Assert.Equal("Save to selection.sc2sel", editor.GetAutoName(ctx));
    }

    [Fact]
    public void SaveSelectionToFileEditor_GetAutoName_FallsBackToStaticSummary_WhenPathEmpty()
    {
        var ctx = new TestAppContext();
        var editor = new SaveSelectionToFileStepEditorViewModel(ctx);
        Assert.Equal(StepKind.SaveSelectionToFile.ForDisplay(), editor.GetAutoName(ctx));
    }

    [Fact]
    public void AddSelectionFromFileEditor_DefaultsToLastSourcePath_WhenProviderFound()
    {
        var ctx = MakeCtxWithSource("mem://Music");
        var editor = new AddSelectionFromFileStepEditorViewModel(ctx);
        Assert.Equal("mem://Music/selection.sc2sel", editor.FilePath);
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void AddSelectionFromFileEditor_NoDefault_WhenLastSourcePathAbsent()
    {
        var editor = new AddSelectionFromFileStepEditorViewModel(new TestAppContext());
        Assert.True(string.IsNullOrWhiteSpace(editor.FilePath));
        Assert.False(editor.IsValid);
    }

    [Fact]
    public void AddSelectionFromFileEditor_GetAutoName_ShowsFileName_WhenPathSet()
    {
        var ctx = MakeCtxWithSource("mem://Music");
        var editor = new AddSelectionFromFileStepEditorViewModel(ctx);
        Assert.Equal("Add from selection.sc2sel", editor.GetAutoName(ctx));
    }

    [Fact]
    public void RemoveSelectionFromFileEditor_DefaultsToLastSourcePath_WhenProviderFound()
    {
        var ctx = MakeCtxWithSource("mem://Music");
        var editor = new RemoveSelectionFromFileStepEditorViewModel(ctx);
        Assert.Equal("mem://Music/selection.sc2sel", editor.FilePath);
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void RemoveSelectionFromFileEditor_NoDefault_WhenLastSourcePathAbsent()
    {
        var editor = new RemoveSelectionFromFileStepEditorViewModel(new TestAppContext());
        Assert.True(string.IsNullOrWhiteSpace(editor.FilePath));
        Assert.False(editor.IsValid);
    }

    [Fact]
    public void RemoveSelectionFromFileEditor_GetAutoName_ShowsFileName_WhenPathSet()
    {
        var ctx = MakeCtxWithSource("mem://Music");
        var editor = new RemoveSelectionFromFileStepEditorViewModel(ctx);
        Assert.Equal("Remove from selection.sc2sel", editor.GetAutoName(ctx));
    }

    [Fact]
    public void MemoryProvider_GetFileName_ReturnsLastSegment()
    {
        IFileSystemProvider provider = new MemoryFileSystemProvider();
        Assert.Equal("selection.sc2sel", provider.GetFileName("mem://Music/selection.sc2sel"));
        Assert.Equal("Music", provider.GetFileName("mem://Music"));
    }

    [Fact]
    public void SelectionFileEditor_LoadFrom_OverridesDefault()
    {
        var ctx = MakeCtxWithSource("mem://Music");
        var editor = new SaveSelectionToFileStepEditorViewModel(ctx);
        // Default is set; LoadFrom should override it
        editor.LoadFrom(new PipelineStepViewModel(new SaveSelectionToFileStep("mem://Other/my.sc2sel")));
        Assert.Equal("mem://Other/my.sc2sel", editor.FilePath);
    }
}
