namespace SmartCopy.Core.DirectoryTree;

/// <summary>
/// Display order for sibling names in the directory tree: case-insensitive ordinal, except that runs
/// of ASCII digits compare by numeric value, so "file2" sorts before "file10". Names that differ only
/// by case stay adjacent, ordered case-sensitively ("Photos" before "photos"). Numerically equal runs
/// fall back to the shorter run first ("a1" before "a01"), keeping the order total.
/// </summary>
public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    private NaturalNameComparer() { }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var ignoringCase = Compare(x, y, ignoreCase: true);
        return ignoringCase != 0 ? ignoringCase : Compare(x, y, ignoreCase: false);
    }

    private static int Compare(string x, string y, bool ignoreCase)
    {
        var i = 0;
        var j = 0;
        var leadingZeroTieBreak = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var endX = EndOfDigitRun(x, i);
                var endY = EndOfDigitRun(y, j);
                var significantX = SkipLeadingZeros(x, i, endX);
                var significantY = SkipLeadingZeros(y, j, endY);

                // More significant digits means a larger number; equal lengths compare digit by digit.
                var byValue = (endX - significantX).CompareTo(endY - significantY);
                if (byValue == 0)
                    byValue = x.AsSpan(significantX, endX - significantX)
                        .SequenceCompareTo(y.AsSpan(significantY, endY - significantY));
                if (byValue != 0) return byValue;

                if (leadingZeroTieBreak == 0)
                    leadingZeroTieBreak = (endX - i).CompareTo(endY - j);

                i = endX;
                j = endY;
                continue;
            }

            var byChar = ignoreCase
                ? char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]))
                : x[i].CompareTo(y[j]);
            if (byChar != 0) return byChar;
            i++;
            j++;
        }

        var byRemaining = (x.Length - i).CompareTo(y.Length - j);
        return byRemaining != 0 ? byRemaining : leadingZeroTieBreak;
    }

    private static int EndOfDigitRun(string s, int start)
    {
        while (start < s.Length && char.IsAsciiDigit(s[start])) start++;
        return start;
    }

    private static int SkipLeadingZeros(string s, int start, int end)
    {
        while (start < end - 1 && s[start] == '0') start++;
        return start;
    }
}
