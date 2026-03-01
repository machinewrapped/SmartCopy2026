using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class RenameStepEditorViewModel : StepEditorViewModelBase
{
    private const string SampleFileName = "sample_track.flac";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(LivePreviewName))]
    private string _pattern = "{name}";

    public override bool IsValid => !string.IsNullOrWhiteSpace(Pattern);

    public string LivePreviewName
    {
        get
        {
            var candidate = Pattern.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return SampleFileName;
            }

            return candidate
                .Replace("{name}", "sample_track", StringComparison.OrdinalIgnoreCase)
                .Replace("{ext}", "flac", StringComparison.OrdinalIgnoreCase);
        }
    }

    public override IPipelineStep BuildStep() => new RenameStep(Pattern.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is RenameStep renameStep)
        {
            Pattern = renameStep.Pattern;
        }
    }
}
