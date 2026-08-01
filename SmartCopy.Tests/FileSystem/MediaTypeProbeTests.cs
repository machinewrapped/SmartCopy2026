using SmartCopy.Core.FileSystem.Hardware;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Covers the two decision points of the media-type fallback used when the OS does not report
/// rotation: the device-name reading and the latency threshold. The measurement itself is I/O and is
/// exercised against real hardware, not here.
/// </summary>
public class MediaTypeProbeTests
{
    [Theory]
    [InlineData("SanDisk Portable SSD Media", DriveMediaType.SSD)]
    [InlineData("Samsung PSSD T7", DriveMediaType.SSD)]
    [InlineData("Crucial X9 Pro Solid State Drive", DriveMediaType.SSD)]
    [InlineData("WD_BLACK SN850X NVMe", DriveMediaType.SSD)]
    [InlineData("Toshiba External HDD", DriveMediaType.HDD)]
    [InlineData("Seagate Portable Hard Drive", DriveMediaType.HDD)]
    [InlineData("Seagate BUP BK Media", DriveMediaType.Unknown)]
    [InlineData("WD Elements 25A2", DriveMediaType.Unknown)]
    [InlineData("", DriveMediaType.Unknown)]
    [InlineData(null, DriveMediaType.Unknown)]
    public void FromDeviceName_ReadsMediaTypeStatedInTheProductName(string? name, DriveMediaType expected)
    {
        Assert.Equal(expected, MediaTypeProbe.FromDeviceName(name));
    }

    /// <summary>"SSD" inside a lowercase word is a coincidence, not a claim about the hardware.</summary>
    [Theory]
    [InlineData("CrossDrive Portable")]
    [InlineData("Lossd Media")]
    public void FromDeviceName_IgnoresAcronymsEmbeddedInWords(string name)
    {
        Assert.Equal(DriveMediaType.Unknown, MediaTypeProbe.FromDeviceName(name));
    }

    /// <summary>Medians measured on real hardware: internal NVMe 0.04ms, USB SSD 1.0ms, USB HDD 10.0ms.</summary>
    [Theory]
    [InlineData(0.04, DriveMediaType.SSD)]
    [InlineData(1.0, DriveMediaType.SSD)]
    [InlineData(1.5, DriveMediaType.SSD)]
    [InlineData(3.0, DriveMediaType.HDD)]
    [InlineData(10.0, DriveMediaType.HDD)]
    public void ClassifyLatencies_SeparatesMediaByMedianSeekTime(double medianMs, DriveMediaType expected)
    {
        var samples = Enumerable.Repeat(medianMs, MediaTypeProbe.MinimumSampleCount).ToArray();

        Assert.Equal(expected, MediaTypeProbe.ClassifyLatencies(samples));
    }

    /// <summary>Between the thresholds nothing is claimed rather than a coin flip being reported.</summary>
    [Fact]
    public void ClassifyLatencies_ReturnsUnknownBetweenThresholds()
    {
        var samples = Enumerable.Repeat(2.2, MediaTypeProbe.MinimumSampleCount).ToArray();

        Assert.Equal(DriveMediaType.Unknown, MediaTypeProbe.ClassifyLatencies(samples));
    }

    [Fact]
    public void ClassifyLatencies_ReturnsUnknownWhenTooFewSamples()
    {
        var samples = Enumerable.Repeat(0.1, MediaTypeProbe.MinimumSampleCount - 1).ToArray();

        Assert.Equal(DriveMediaType.Unknown, MediaTypeProbe.ClassifyLatencies(samples));
    }

    /// <summary>One slow sample is a bus or scheduler hiccup, not a seek pattern.</summary>
    [Fact]
    public void ClassifyLatencies_ToleratesASingleOutlier()
    {
        double[] samples = [.. Enumerable.Repeat(1.0, MediaTypeProbe.MinimumSampleCount - 1), 304.0];

        Assert.Equal(DriveMediaType.SSD, MediaTypeProbe.ClassifyLatencies(samples));
    }

    /// <summary>
    /// Repeats a measured set up to the minimum a verdict requires. The recorded runs stopped at 12
    /// samples, which the probe no longer treats as enough; repeating preserves the seek ratio and the
    /// median that the thresholds actually read, without inventing readings the hardware never produced.
    /// </summary>
    private static double[] Repeated(double[] measured) =>
        [.. Enumerable.Range(0, MediaTypeProbe.MinimumSampleCount).Select(i => measured[i % measured.Length])];

    /// <summary>
    /// Verbatim samples from a USB rotational drive whose cache had been warmed by an earlier probe.
    /// The median is 0.48ms — well inside solid-state range, and what previously made this drive
    /// report SSD — but 5 of 12 reads still took a seek. Counting seeks is what survives caching.
    /// </summary>
    [Fact]
    public void ClassifyLatencies_ReportsRotationalWhenCachingFlattensTheMedian()
    {
        double[] samples = [0.48, 0.47, 37.2, 0.49, 0.46, 30.1, 0.48, 0.51, 34.8, 0.47, 36.5, 0.49];

        Assert.InRange(samples.Order().ElementAt(samples.Length / 2), 0, MediaTypeProbe.SsdMedianCeilingMs);
        Assert.Equal(DriveMediaType.HDD, MediaTypeProbe.ClassifyLatencies(Repeated(samples)));
    }

    /// <summary>
    /// A rotational drive whose opening reads all landed in cache. The first
    /// <see cref="MediaTypeProbe.MinimumSampleCount"/> samples clear it as solid state; the full run
    /// convicts it. This is why sampling stops early only on a rotational verdict — an early
    /// solid-state exit would end the run before the reads carrying the evidence were ever taken.
    /// </summary>
    [Fact]
    public void ClassifyLatencies_ReportsRotationalWhenSeeksAppearLateInTheRun()
    {
        double[] cached = [.. Enumerable.Repeat(0.5, MediaTypeProbe.MinimumSampleCount)];
        double[] full =
        [
            .. cached,
            .. Enumerable.Repeat(35.0, MediaTypeProbe.TargetSampleCount - MediaTypeProbe.MinimumSampleCount)
        ];

        Assert.Equal(DriveMediaType.SSD, MediaTypeProbe.ClassifyLatencies(cached));
        Assert.Equal(DriveMediaType.HDD, MediaTypeProbe.ClassifyLatencies(full));
    }

    /// <summary>
    /// Verbatim samples from a USB SSD on the same bus. Its worst read is 2.0ms, so no tightening of
    /// the seek floor is needed to separate it from the rotational case above.
    /// </summary>
    [Fact]
    public void ClassifyLatencies_ReportsSolidStateForATightDistribution()
    {
        double[] samples = [0.89, 0.97, 1.00, 0.95, 0.94, 2.03, 0.88, 0.91, 1.10, 0.93, 0.90, 1.70];

        Assert.Equal(DriveMediaType.SSD, MediaTypeProbe.ClassifyLatencies(Repeated(samples)));
    }

    /// <summary>A drive whose every sampled block was already cached cannot be judged.</summary>
    [Fact]
    public void ClassifyLatencies_ReturnsUnknownWhenNothingIsFastAndNothingSeeks()
    {
        double[] samples = [2.0, 2.1, 2.2, 2.0, 2.3, 2.1, 2.0, 2.2, 2.1, 2.0, 2.4, 2.1];

        Assert.Equal(DriveMediaType.Unknown, MediaTypeProbe.ClassifyLatencies(Repeated(samples)));
    }

    [Fact]
    public async Task MeasureAsync_ReturnsUnknownWhenNoSampleFilesExist()
    {
        var empty = Directory.CreateTempSubdirectory("smartcopy-probe-");
        try
        {
            var mediaType = await MediaTypeProbe.MeasureAsync(empty.FullName, [empty.FullName]);

            Assert.Equal(DriveMediaType.Unknown, mediaType);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }
}
