using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

public partial class ExtensionFilterEditorViewModel : FilterEditorViewModelBase
{
    public ObservableCollection<string> Extensions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddExtensionCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _inputError = string.Empty;

    public override bool IsValid => Extensions.Count > 0;

    partial void OnInputTextChanged(string value)
    {
        InputError = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanAddExtension))]
    private void AddExtension()
    {
        var raw = InputText;
        InputText = string.Empty;

        var tokens = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rejected = new List<string>();

        foreach (var token in tokens)
        {
            var normalized = ExtensionFilter.Normalize(token);

            if (!ExtensionFilter.IsValidExtension(normalized))
            {
                rejected.Add(token.Trim());
                continue;
            }

            if (!Extensions.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                Extensions.Add(normalized);
            }
        }

        InputError = rejected.Count > 0
            ? $"Invalid extension{(rejected.Count > 1 ? "s" : "")}: {string.Join(", ", rejected)}"
            : string.Empty;

        OnPropertyChanged(nameof(IsValid));
        AutoUpdateName();
    }

    private bool CanAddExtension() => !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand]
    private void RemoveExtension(string ext)
    {
        Extensions.Remove(ext);
        OnPropertyChanged(nameof(IsValid));
        AutoUpdateName();
    }

    public override IFilter BuildFilter()
        => new ExtensionFilter(Extensions, Mode);

    public override void LoadFrom(IFilter filter)
    {
        if (filter is not ExtensionFilter ef)
        {
            return;
        }

        Mode = ef.Mode;
        Extensions.Clear();
        foreach (var ext in ef.Extensions)
        {
            Extensions.Add(ext);
        }

        FilterName = ef.CustomName ?? string.Empty;
    }

    public override string GenerateName()
    {
        var prefix = Mode.ToString();
        if (Extensions.Count == 0)
        {
            return prefix;
        }

        var list = string.Join(", ", Extensions.Select(e => "." + e));
        return $"{prefix} {list}";
    }
}
