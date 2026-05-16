using SmartCopy.Benchmarks;

namespace SmartCopy.Tests.Benchmarks;

public sealed class BenchmarkSizeScalingAnalysisTests
{
    [Fact]
    public void Analyze_uses_power_of_two_bucket_boundaries_inclusively()
    {
        var records = new[]
        {
            Record("a.bin", 1024, 1, 10),
            Record("b.bin", 1025, 1, 20),
            Record("c.bin", 4096, 1, 40),
            Record("d.bin", 4097, 1, 80),
        };

        var report = BenchmarkSizeScalingAnalysis.Analyze(records, new SizeScalingAnalysisOptions(MinimumSampleCount: 1));
        var group = Assert.Single(report.Groups);

        var oneKiB = Assert.Single(group.Buckets, b => b.Label == "1 B-1 KiB");
        var twoKiB = Assert.Single(group.Buckets, b => b.Label == ">1 KiB-2 KiB");
        var fourKiB = Assert.Single(group.Buckets, b => b.Label == ">2 KiB-4 KiB");
        var eightKiB = Assert.Single(group.Buckets, b => b.Label == ">4 KiB-8 KiB");

        Assert.Equal(1, oneKiB.RecordCount);
        Assert.Equal(1, twoKiB.RecordCount);
        Assert.Equal(1, fourKiB.RecordCount);
        Assert.Equal(1, eightKiB.RecordCount);
    }

    [Fact]
    public void Analyze_reports_adjacent_bucket_throughput_ratios()
    {
        var records = new[]
        {
            Record("a.bin", 1024, 1, 10),
            Record("b.bin", 1536, 1, 20),
            Record("c.bin", 3072, 1, 30),
        };

        var report = BenchmarkSizeScalingAnalysis.Analyze(records, new SizeScalingAnalysisOptions(MinimumSampleCount: 1));
        var group = Assert.Single(report.Groups);

        var comparison = Assert.Single(group.AdjacentComparisons, c =>
            c.FromBucket == "1 B-1 KiB" &&
            c.ToBucket == ">1 KiB-2 KiB");

        Assert.Equal(2.0, comparison.P50ThroughputRatio);
        Assert.Equal(SizeScalingComparisonConfidence.Measured, comparison.Confidence);
    }

    [Fact]
    public void Analyze_marks_adjacent_comparisons_with_small_samples_as_insufficient()
    {
        var records = new[]
        {
            Record("a.bin", 1024, 1, 10),
            Record("b.bin", 1536, 1, 20),
        };

        var report = BenchmarkSizeScalingAnalysis.Analyze(records, new SizeScalingAnalysisOptions(MinimumSampleCount: 2));
        var group = Assert.Single(report.Groups);

        var comparison = Assert.Single(group.AdjacentComparisons);

        Assert.Equal(SizeScalingComparisonConfidence.InsufficientSamples, comparison.Confidence);
    }

    [Fact]
    public void Analyze_reports_threshold_coverage()
    {
        var records = new[]
        {
            Record("small.bin", 1024, 1, 10),
            Record("large.bin", 536870912, 1000, 512),
            Record("larger.bin", 1073741824, 2000, 512),
        };

        var report = BenchmarkSizeScalingAnalysis.Analyze(records, new SizeScalingAnalysisOptions(MinimumSampleCount: 1));

        var at512MiB = Assert.Single(report.Coverage, c => c.Label == ">=512 MiB");

        Assert.Equal(2, at512MiB.RecordCount);
        Assert.Equal(2, at512MiB.UniqueFileCount);
        Assert.Equal(1073741824, at512MiB.MaxFileSizeBytes);
    }

    private static SizeScalingInputRecord Record(
        string path,
        long sizeBytes,
        double durationMilliseconds,
        double throughputMiBPerSecond,
        string scenario = "SSDtoSSD",
        string variant = "BaselineAuto") =>
        new(
            scenario,
            variant,
            path,
            sizeBytes,
            durationMilliseconds,
            throughputMiBPerSecond);
}
