using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class FlattenStepEditorViewModel : StepEditorViewModelBase
{
    // Example path: 5 directory levels + filename.
    private static readonly string[] _exampleSegments = ["a", "b", "c", "d", "e", "file.txt"];
    private const int ExampleDirDepth = 5;

    public IReadOnlyList<FlattenConflictStrategy> ConflictStrategies { get; } =
        Enum.GetValues<FlattenConflictStrategy>();

    [ObservableProperty]
    private FlattenConflictStrategy _conflictStrategy = FlattenConflictStrategy.AutoRenameCounter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStripLeadingMode))]
    [NotifyPropertyChangedFor(nameof(IsKeepTrailingMode))]
    [NotifyPropertyChangedFor(nameof(LevelsLabel))]
    [NotifyPropertyChangedFor(nameof(LivePreview))]
    private FlattenTrimMode _trimMode = FlattenTrimMode.KeepTrailing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(LivePreview))]
    private decimal? _levels = 1;

    public bool IsStripLeadingMode
    {
        get => TrimMode == FlattenTrimMode.StripLeading;
        set { if (value) TrimMode = FlattenTrimMode.StripLeading; }
    }

    public bool IsKeepTrailingMode
    {
        get => TrimMode == FlattenTrimMode.KeepTrailing;
        set { if (value) TrimMode = FlattenTrimMode.KeepTrailing; }
    }

    public string LevelsLabel => TrimMode == FlattenTrimMode.StripLeading
        ? "Levels to strip from start"
        : "Levels to keep from end";

    public override bool IsValid => (Levels ?? 0) >= 1;

    public string LivePreview
    {
        get
        {
            var n = (int)(Levels ?? 0);
            if (n < 1) return "a/b/c/d/e/file.txt  →  (invalid)";

            string result;
            if (TrimMode == FlattenTrimMode.StripLeading)
            {
                if (n >= ExampleDirDepth)
                {
                    // Indicates the pattern holds for arbitrarily deep paths too.
                    result = "\u2026/file.txt";
                }
                else
                {
                    var stripped = _exampleSegments[n..];
                    result = string.Join("/", stripped);
                }
            }
            else
            {
                var keep = Math.Min(n, _exampleSegments.Length);
                var kept = _exampleSegments[^keep..];
                result = string.Join("/", kept);
            }

            return $"a/b/c/d/e/file.txt  \u2192  {result}";
        }
    }

    public override IPipelineStep BuildStep() => new FlattenStep(
        ConflictStrategy,
        TrimMode,
        Math.Max(1, (int)(Levels ?? 1)));

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is FlattenStep flattenStep)
        {
            ConflictStrategy = flattenStep.ConflictStrategy;
            TrimMode = flattenStep.TrimMode;
            Levels = flattenStep.Levels;
        }
    }
}
