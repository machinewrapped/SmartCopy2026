using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Tests.DirectoryTree;

public sealed class NaturalNameComparerTests
{
    [Fact]
    public void Sort_OrdersDigitRunsNumericallyAndKeepsCaseVariantsAdjacent()
    {
        string[] names = ["file10", "b", "file2", "Zeta", "file02", "a1b", "photos", "a", "file1", "Photos", "a01b", "a1"];

        var sorted = names.Order(NaturalNameComparer.Instance);

        Assert.Equal(
            ["a", "a1", "a1b", "a01b", "b", "file1", "file2", "file02", "file10", "Photos", "photos", "Zeta"],
            sorted);
    }

    [Theory]
    [InlineData("x99999999999999999999999", "x100000000000000000000000")] // longer than any integer type
    [InlineData("track 9.mp3", "track 10.mp3")]
    [InlineData("0", "00")]
    public void Compare_IsAntisymmetric(string smaller, string larger)
    {
        Assert.True(NaturalNameComparer.Instance.Compare(smaller, larger) < 0);
        Assert.True(NaturalNameComparer.Instance.Compare(larger, smaller) > 0);
    }
}
