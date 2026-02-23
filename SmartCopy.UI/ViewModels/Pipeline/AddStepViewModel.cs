using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels.Pipeline;

public sealed record StepTypeItem(StepKind Kind, string DisplayName, string Description);

public partial class AddStepViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isLevel2Visible;

    [ObservableProperty]
    private StepCategory? _selectedCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStepTypeItems))]
    private IReadOnlyList<StepTypeItem> _stepTypeItems = [];

    public bool HasStepTypeItems => StepTypeItems.Count > 0;

    public event Action<StepKind>? StepTypeSelected;

    public event Action<StepCategory>? CategoryNavigated;

    public event Action? CloseRequested;

    [RelayCommand]
    private void NavigateToCategory(StepCategory category)
    {
        SelectedCategory = category;
        StepTypeItems = GetItemsForCategory(category);
        IsLevel2Visible = true;
        CategoryNavigated?.Invoke(category);
    }

    [RelayCommand]
    private void SelectStepType(StepKind kind)
    {
        StepTypeSelected?.Invoke(kind);
        GoBack();
    }

    [RelayCommand]
    private void GoBack()
    {
        IsLevel2Visible = false;
        SelectedCategory = null;
        StepTypeItems = [];
    }

    [RelayCommand]
    private void Close()
    {
        GoBack();
        CloseRequested?.Invoke();
    }

    private static IReadOnlyList<StepTypeItem> GetItemsForCategory(StepCategory category)
    {
        return category switch
        {
            StepCategory.Path =>
            [
                new(StepKind.Flatten, "Flatten", "Strip directory structure"),
                new(StepKind.Rebase, "Rebase", "Adjust path roots and prefixes"),
                new(StepKind.Rename, "Rename", "Rename using a pattern"),
            ],
            StepCategory.Content =>
            [
                new(StepKind.Convert, "Convert", "Convert content format"),
            ],
            StepCategory.Executable =>
            [
                new(StepKind.Copy, "Copy", "Copy to destination"),
                new(StepKind.Move, "Move", "Move to destination"),
                new(StepKind.Delete, "Delete", "Delete source file"),
            ],
            _ => [],
        };
    }
}
