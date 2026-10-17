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

    [Fact]
    public void Sort_PutsPunctuationBeforeDigitsBeforeLetters()
    {
        string[] names = ["Zeta", "alpha", "~temp", "1st", "_private", "[Draft]"];

        var sorted = names.Order(NaturalNameComparer.Instance);

        Assert.Equal(["[Draft]", "_private", "~temp", "1st", "alpha", "Zeta"], sorted);
    }

    [Fact]
    public void Sort_KeepsCaseVariantsOutsideTheBmpAdjacent()
    {
        // Deseret 𐐀 (U+10400) and 𐐨 (U+10428) are case variants; 𐐁 (U+10401) falls between them by code unit.
        string[] names = ["\U00010428", "\U00010401", "\U00010400"];

        var sorted = names.Order(NaturalNameComparer.Instance);

        Assert.Equal(["\U00010400", "\U00010428", "\U00010401"], sorted);
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
