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

    /// <summary>A rotational drive's outliers run to hundreds of ms; the median must ignore them.</summary>
    [Fact]
    public void ClassifyLatencies_UsesMedianSoOutliersDoNotDecide()
    {
        double[] samples = [0.9, 1.0, 1.0, 1.1, 0.8, 1.2, 0.9, 304.0];

        Assert.Equal(DriveMediaType.SSD, MediaTypeProbe.ClassifyLatencies(samples));
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
