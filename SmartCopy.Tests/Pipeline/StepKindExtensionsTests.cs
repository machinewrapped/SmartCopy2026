using SmartCopy.Core.Pipeline;
using SmartCopy.UI.ViewModels;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Tests.Pipeline;

public sealed class StepKindExtensionsTests
{
    [Theory]
    [InlineData(StepKind.Flatten, "⊞")]
[InlineData(StepKind.Rename, "✏")]
    [InlineData(StepKind.Convert, "⚙")]
    [InlineData(StepKind.Copy, "→")]
    [InlineData(StepKind.Move, "⇒")]
    [InlineData(StepKind.Delete, "🗑")]
    [InlineData(StepKind.Custom, "★")]
    [InlineData(StepKind.SelectAll, "☑")]
    [InlineData(StepKind.InvertSelection, "⇄")]
    [InlineData(StepKind.ClearSelection, "✖")]
    public void GetIcon_ReturnsExpected(StepKind kind, string expected)
    {
        Assert.Equal(expected, kind.GetIcon());
    }

    [Fact]
    public void ViewModel_IconDelegatesToExtensionMethod()
    {
        // use a real step to cover the simple forwarding logic
        var step = new CopyStep("/mem/target");
        var vm = new PipelineStepViewModel(step);
        Assert.Equal(step.StepType.GetIcon(), vm.Icon);
    }
}
