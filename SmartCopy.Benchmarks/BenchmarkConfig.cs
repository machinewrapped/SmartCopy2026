using SmartCopy.Core.FileSystem;

namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkConfig
{
    public string SourcePath { get; set; } = @"R:\TestData\MixedDataset";
    public string? ArtifactPath { get; set; }
    public bool IncludeHidden { get; set; }
    public double ConvergenceSpreadPercent { get; set; } = 3.0;
    public double GatePercent { get; set; } = 3.0;
    public int MaxConvergenceRuns { get; set; } = 5;
    public bool Converge { get; set; } = true;
    public bool FailFast { get; set; } = true;
    public List<BenchmarkScenario> Scenarios { get; set; } = [];
    public List<string> ScenarioExecutionOrder { get; set; } = [];
    public List<BenchmarkVariant> Variants { get; set; } = [];
    public DatasetPreparationConfig? DatasetPreparation { get; set; }
    public int CooldownSeconds { get; set; } = 60;
    public bool ClearCacheBetweenRuns { get; set; } = false;
    public string? RamMapPath { get; set; }

    /// <summary>
    /// Keyed by normalized base source path. Each value is the (shuffled) list of pool
    /// paths for that source root, shared across all scenarios that use that source.
    /// Populated on first benchmark run; persisted to the config file.
    /// </summary>
    public Dictionary<string, List<string>> SourcePools { get; set; } = new();

    public static BenchmarkConfig CreateTemplate() =>
        new()
        {
            Scenarios =
            [
                new BenchmarkScenario { Name = "SameDriveTest", DestinationPath = @"R:\TestData\SameDriveTest" },
                new BenchmarkScenario { Name = "SSDtoSSD", DestinationPath = @"D:\TestData\SSDtoSSD" },
                new BenchmarkScenario { Name = "SSDtoHDD", DestinationPath = @"L:\TestData\SSDtoHDD" },
                new BenchmarkScenario { Name = "SSDtoUSBFlash", DestinationPath = @"T:\TestData\SSDtoUSBFlash" },
            ],
            ScenarioExecutionOrder =
            [
                "SSDtoSSD",
                "SameDriveTest",
                "SSDtoHDD",
                "SSDtoUSBFlash",
            ],
            Variants =
            [
                new BenchmarkVariant
                {
                    Name = "BaselineAuto",
                    Notes = "Current heuristic defaults.",
                    DesiredRunCount = 5,
                },
                new BenchmarkVariant
                {
                    Name = "Buffer512KiB",
                    Notes = "Uses a 512 KiB copy buffer.",
                    DesiredRunCount = 3,
                    ProviderCopyBufferSizeBytes = 512 * 1024,
                },
            ],
            DatasetPreparation = new DatasetPreparationConfig
            {
                SourcePath = @"R:\CandidateData",
                DestinationPath = @"R:\TestData\MixedDataset",
                Buckets =
                [
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Tiny",
                        MinimumFileSizeBytes = 0,
                        MaximumFileSizeBytes = 64 * 1024,
                        TargetTotalBytes = 256L * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Small",
                        MinimumFileSizeBytes = 64 * 1024 + 1,
                        MaximumFileSizeBytes = 512 * 1024,
                        TargetTotalBytes = 512L * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Medium",
                        MinimumFileSizeBytes = 512 * 1024 + 1,
                        MaximumFileSizeBytes = 4 * 1024 * 1024,
                        TargetTotalBytes = 2L * 1024 * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Large",
                        MinimumFileSizeBytes = 4 * 1024 * 1024 + 1,
                        MaximumFileSizeBytes = 32 * 1024 * 1024,
                        TargetTotalBytes = 3L * 1024 * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "XLarge",
                        MinimumFileSizeBytes = 32 * 1024 * 1024 + 1,
                        MaximumFileSizeBytes = 256 * 1024 * 1024,
                        TargetTotalBytes = 4L * 1024 * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Huge",
                        MinimumFileSizeBytes = 256 * 1024 * 1024 + 1,
                        MaximumFileSizeBytes = 2L * 1024 * 1024 * 1024,
                        TargetTotalBytes = 4L * 1024 * 1024 * 1024,
                    },
                ],
            },
        };

    public void Normalize()
    {
        SourcePath = Path.GetFullPath(SourcePath);
        ArtifactPath = string.IsNullOrWhiteSpace(ArtifactPath)
            ? null
            : Path.GetFullPath(ArtifactPath);

        foreach (var scenario in Scenarios)
        {
            scenario.Normalize();
        }

        ScenarioExecutionOrder = ScenarioExecutionOrder
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Variants.Count == 0)
        {
            Variants.Add(new BenchmarkVariant
            {
                Name = "ScenarioDefaults",
                Notes = "Synthesized for legacy configs without a variants section.",
                DesiredRunCount = 1,
            });
        }

        foreach (var variant in Variants)
        {
            variant.Normalize();
        }

        DatasetPreparation?.Normalize();
    }
}
