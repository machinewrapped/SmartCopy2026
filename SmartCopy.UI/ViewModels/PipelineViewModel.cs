using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class PipelineStepViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _description = string.Empty;
}

public partial class PipelineViewModel : ViewModelBase
{
    public ObservableCollection<PipelineStepViewModel> Steps { get; } = new();

    public PipelineViewModel()
    {
        Steps.Add(new PipelineStepViewModel { Description = "⊞ Flatten" });
        Steps.Add(new PipelineStepViewModel { Description = "⚙ Convert: mp3 320k" });
        Steps.Add(new PipelineStepViewModel { Description = "→ Copy" });
    }
}
