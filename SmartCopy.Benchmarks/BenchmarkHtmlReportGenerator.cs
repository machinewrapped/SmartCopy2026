using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkHtmlReportGenerator
{


    private static string GetWriteMode(string variantName)
    {
        if (variantName.Contains("Direct", StringComparison.OrdinalIgnoreCase)) return "Direct Write";
        if (variantName.Contains("Staged", StringComparison.OrdinalIgnoreCase)) return "Staged Write";
        if (variantName.Contains("Baseline", StringComparison.OrdinalIgnoreCase)) return "Staged Write (Baseline)";
        return "Unknown";
    }

    private static readonly string[] Palette = new[] 
    { 
        "'rgba(54, 162, 235, 0.7)'",   // Blue
        "'rgba(255, 99, 132, 0.7)'",   // Red
        "'rgba(75, 192, 192, 0.7)'",   // Green
        "'rgba(255, 159, 64, 0.7)'",   // Orange
        "'rgba(153, 102, 255, 0.7)'",  // Purple
        "'rgba(255, 205, 86, 0.7)'",   // Yellow
        "'rgba(201, 203, 207, 0.7)'"   // Grey
    };

    public static async Task GenerateAsync(
        string outputPath, 
        string scenarioName,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<BenchmarkRunRecord> runs)
    {
        buckets = buckets
            .Where(b => records.Any(r => b.Contains(r.FileSizeBytes)))
            .ToList();

        // Map each variant to its batch category using the run data config
        var variantCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in variants)
        {
            var runForVariant = runs.FirstOrDefault(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase));
            if (runForVariant != null && runForVariant.BufferBatchBytes.HasValue && runForVariant.BufferBatchBytes.Value > 0)
            {
                var mib = runForVariant.BufferBatchBytes.Value / (1024.0 * 1024.0);
                variantCategories[variant] = $"{mib:0.##}MiB Buffer";
            }
            else
            {
                variantCategories[variant] = "Unbatched";
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine($"<title>Benchmark Analysis - {scenarioName}</title>");
        sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: sans-serif; margin: 2rem; background: #121212; color: #eee; }");
        sb.AppendLine(".chart-container { width: 800px; height: 400px; margin-bottom: 3rem; background: #1e1e1e; padding: 1rem; border-radius: 8px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>Benchmark Analysis: {scenarioName}</h1>");

        // --- Overall Run Duration Chart ---
        sb.AppendLine($"<h2>Overall Median Run Duration</h2>");
        sb.AppendLine($"<div class=\"chart-container\">");
        var runCanvasId = $"chart_{Guid.NewGuid():N}";
        sb.AppendLine($"<canvas id=\"{runCanvasId}\"></canvas>");
        sb.AppendLine("</div>");

        var runLabelsJs = new List<string>();
        var runDataJs = new List<string>();
        var runColorsJs = new List<string>();

        var runItems = new List<(string Variant, double MedianSeconds, string Color)>();
        int variantIndex = 0;
        foreach (var variant in variants)
        {
            var variantRuns = runs
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ExecuteDuration.TotalSeconds)
                .OrderBy(x => x)
                .ToList();

            if (variantRuns.Count > 0)
            {
                var medianSeconds = variantRuns[variantRuns.Count / 2];
                runItems.Add((variant, medianSeconds, Palette[variantIndex % Palette.Length]));
            }
            variantIndex++;
        }

        foreach (var item in runItems.OrderBy(x => x.MedianSeconds))
        {
            runLabelsJs.Add($"'{item.Variant}'");
            runDataJs.Add(Math.Round(item.MedianSeconds, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
            runColorsJs.Add(item.Color);
        }

        if (runDataJs.Count > 0)
        {
            sb.AppendLine("<script>");
            sb.AppendLine($@"
                new Chart(document.getElementById('{runCanvasId}'), {{
                    type: 'bar',
                    data: {{
                        labels: [{string.Join(", ", runLabelsJs)}],
                        datasets: [{{
                            label: 'Median Duration (Seconds)',
                            data: [{string.Join(", ", runDataJs)}],
                            backgroundColor: [{string.Join(", ", runColorsJs)}],
                            borderColor: 'rgba(255, 255, 255, 0.2)',
                            borderWidth: 1,
                            minBarLength: 1
                        }}]
                    }},
                    options: {{
                        responsive: true,
                        maintainAspectRatio: false,
                        scales: {{
                            x: {{ 
                                ticks: {{ 
                                    color: (ctx) => ctx.tick && ctx.tick.label && String(ctx.tick.label).indexOf('Control') > -1 ? '#fff' : '#eee',
                                    font: (ctx) => ctx.tick && ctx.tick.label && String(ctx.tick.label).indexOf('Control') > -1 ? {{ weight: 'bold' }} : undefined
                                }},
                                grid: {{ color: '#333' }}
                            }},
                            y: {{ 
                                beginAtZero: true, 
                                title: {{ display: true, text: 'Seconds', color: '#eee' }},
                                ticks: {{ color: '#eee' }},
                                grid: {{ color: '#333' }}
                            }}
                        }},
                        plugins: {{
                            legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }}
                        }}
                    }}
                }});
            ");
            sb.AppendLine("</script>");
        }

        // --- Throughput Trend by File Size (Line Chart) ---
        sb.AppendLine($"<h2>Throughput Trend by File Size</h2>");
        sb.AppendLine($"<div class=\"chart-container\">");
        var trendCanvasId = $"chart_{Guid.NewGuid():N}";
        sb.AppendLine($"<canvas id=\"{trendCanvasId}\"></canvas>");
        sb.AppendLine("</div>");

        var trendBucketLabelsJs = string.Join(", ", buckets.Select(b => $"'{b.Label}'"));
        var trendDatasetsJs = new List<string>();
        int trendColorIndex = 0;

        foreach (var variant in variants)
        {
            var color = Palette[trendColorIndex % Palette.Length];
            var borderColor = color.Replace("0.7", "1.0");
            var backgroundColor = color.Replace("0.7", "0.1");
            trendColorIndex++;

            var dataAvg = new List<double>();
            foreach (var bucket in buckets)
            {
                var bucketRecords = records.Where(r => bucket.Contains(r.FileSizeBytes)).ToList();
                var speeds = bucketRecords
                    .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.ThroughputMiBPerSecond)
                    .Where(v => v is not null)
                    .Select(v => v!.Value)
                    .ToList();

                var avg = speeds.Count > 0 ? speeds.Average() : 0.0;
                dataAvg.Add(Math.Round(avg, 2));
            }

            var dataAvgStr = string.Join(", ", dataAvg.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            trendDatasetsJs.Add($@"
                {{
                    label: '{variant}',
                    data: [{dataAvgStr}],
                    borderColor: {borderColor},
                    backgroundColor: {backgroundColor},
                    borderWidth: 1.5,
                    pointRadius: 2,
                    pointHoverRadius: 4,
                    tension: 0.15,
                    fill: false
                }}
            ");
        }

        var allTrendDatasetsJs = string.Join(", ", trendDatasetsJs);

        sb.AppendLine("<script>");
        sb.AppendLine($@"
            new Chart(document.getElementById('{trendCanvasId}'), {{
                type: 'line',
                data: {{
                    labels: [{trendBucketLabelsJs}],
                    datasets: [{allTrendDatasetsJs}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: {{
                        x: {{ 
                            ticks: {{ color: '#eee' }},
                            grid: {{ color: '#333' }}
                        }},
                        y: {{ 
                            beginAtZero: true, 
                            title: {{ display: true, text: 'MiB/s', color: '#eee' }},
                            ticks: {{ color: '#eee' }},
                            grid: {{ color: '#333' }}
                        }}
                    }},
                    plugins: {{
                        legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }}
                    }}
                }}
            }});
        ");
        sb.AppendLine("</script>");

        var categories = variants
            .Select(v => variantCategories.TryGetValue(v, out var cat) ? cat : "Unbatched")
            .Distinct()
            .ToList();

        // X-axis labels
        var bucketLabelsJs = string.Join(", ", buckets.Select(b => $"'{b.Label}'"));

        foreach (var category in categories)
        {
            var categoryVariants = variants
                .Where(v => (variantCategories.TryGetValue(v, out var cat) ? cat : "Unbatched") == category)
                .ToList();
            if (categoryVariants.Count == 0) continue;

            sb.AppendLine($"<h2>Batch Size: {category}</h2>");
            sb.AppendLine($"<div class=\"chart-container\">");
            var canvasId = $"chart_{Guid.NewGuid():N}";
            sb.AppendLine($"<canvas id=\"{canvasId}\"></canvas>");
            sb.AppendLine("</div>");

            var datasetsJs = new List<string>();
            int colorIndex = 0;

            foreach (var variant in categoryVariants)
            {
                var mode = GetWriteMode(variant);
                var color = Palette[colorIndex % Palette.Length];
                colorIndex++;
                var dataAvg = new List<double>();

                foreach (var bucket in buckets)
                {
                    var bucketRecords = records.Where(r => bucket.Contains(r.FileSizeBytes)).ToList();
                    var speeds = bucketRecords
                        .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                        .Select(r => r.ThroughputMiBPerSecond)
                        .Where(v => v is not null)
                        .Select(v => v!.Value)
                        .ToList();

                    var avg = speeds.Count > 0 ? speeds.Average() : 0.0;
                    dataAvg.Add(Math.Round(avg, 2));
                }

                var dataAvgStr = string.Join(", ", dataAvg);
                datasetsJs.Add($@"
                    {{
                        label: '{mode} ({variant})',
                        data: [{dataAvgStr}],
                        backgroundColor: {color},
                        borderColor: 'rgba(255, 255, 255, 0.2)',
                        borderWidth: 1,
                        minBarLength: 1
                    }}
                ");
            }

            var allDatasetsJs = string.Join(", ", datasetsJs);

            sb.AppendLine("<script>");
            sb.AppendLine($@"
                new Chart(document.getElementById('{canvasId}'), {{
                    type: 'bar',
                    data: {{
                        labels: [{bucketLabelsJs}],
                        datasets: [{allDatasetsJs}]
                    }},
                    options: {{
                        responsive: true,
                        maintainAspectRatio: false,
                        scales: {{
                            x: {{ 
                                ticks: {{ color: '#eee' }},
                                grid: {{ color: '#333' }}
                            }},
                            y: {{ 
                                beginAtZero: true, 
                                title: {{ display: true, text: 'MiB/s', color: '#eee' }},
                                ticks: {{ color: '#eee' }},
                                grid: {{ color: '#333' }}
                            }}
                        }},
                        plugins: {{
                            legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }}
                        }}
                    }}
                }});
            ");
            sb.AppendLine("</script>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    public static async Task GenerateSummaryAsync(
        string outputPath,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<BenchmarkRunRecord> runs,
        IReadOnlyList<string> scenarios)
    {
        buckets = buckets
            .Where(b => records.Any(r => b.Contains(r.FileSizeBytes)))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine($"<title>Benchmark Analysis - All Scenarios Summary</title>");
        sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: sans-serif; margin: 2rem; background: #121212; color: #eee; }");
        sb.AppendLine(".chart-container { width: 1000px; height: 500px; margin-bottom: 3rem; background: #1e1e1e; padding: 1rem; border-radius: 8px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>All Scenarios Summary</h1>");

        var variantLabelsJs = string.Join(", ", variants.Select(v => $"'{v}'"));

        // --- Run-Level Median Throughput Line Chart ---
        sb.AppendLine($"<h2>Run-Level Median Throughput</h2>");
        sb.AppendLine($"<div class=\"chart-container\">");
        var lineCanvasId = $"chart_{Guid.NewGuid():N}";
        sb.AppendLine($"<canvas id=\"{lineCanvasId}\"></canvas>");
        sb.AppendLine("</div>");

        var lineDatasetsJs = new List<string>();
        int colorIndex = 0;
        foreach (var scenario in scenarios)
        {
            var color = Palette[colorIndex % Palette.Length];
            var borderColor = color.Replace("0.7", "1.0");
            var backgroundColor = color.Replace("0.7", "0.1");
            colorIndex++;
            var dataList = new List<double>();
            
            var scenarioRecords = records.Where(r => string.Equals(r.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var variant in variants)
            {
                var variantRecords = scenarioRecords
                    .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (variantRecords.Count > 0)
                {
                    var throughputs = variantRecords
                        .GroupBy(r => r.RunIndex)
                        .Select(g => 
                        {
                            var runBytes = g.Sum(r => r.FileSizeBytes);
                            var runSeconds = g.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                            return runBytes > 0 && runSeconds > 0 ? (runBytes / 1048576.0) / runSeconds : 0.0;
                        })
                        .OrderBy(t => t)
                        .ToList();
                    var p50Throughput = throughputs.Count > 0 ? BenchmarkHelpers.Percentile(throughputs, 0.50) : 0.0;
                    dataList.Add(Math.Round(p50Throughput, 2));
                }
                else
                {
                    dataList.Add(0.0);
                }
            }

            lineDatasetsJs.Add($@"
                {{
                    label: '{scenario}',
                    data: [{string.Join(", ", dataList)}],
                    borderColor: {borderColor},
                    backgroundColor: {backgroundColor},
                    borderWidth: 2,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    tension: 0.15,
                    fill: false
                }}");
        }

        sb.AppendLine("<script>");
        sb.AppendLine($@"
            new Chart(document.getElementById('{lineCanvasId}'), {{
                type: 'line',
                data: {{
                    labels: [{variantLabelsJs}],
                    datasets: [{string.Join(", ", lineDatasetsJs)}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: {{
                        x: {{ ticks: {{ color: '#eee' }}, grid: {{ color: '#333' }} }},
                        y: {{ beginAtZero: true, title: {{ display: true, text: 'MiB/s', color: '#eee' }}, ticks: {{ color: '#eee' }}, grid: {{ color: '#333' }} }}
                    }},
                    plugins: {{ legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }} }}
                }}
            }});
        ");
        sb.AppendLine("</script>");

        // --- Run-Level Median Duration Grouped Bar Chart ---
        sb.AppendLine($"<h2>Run-Level Median Duration</h2>");
        sb.AppendLine($"<div class=\"chart-container\">");
        var runCanvasId = $"chart_{Guid.NewGuid():N}";
        sb.AppendLine($"<canvas id=\"{runCanvasId}\"></canvas>");
        sb.AppendLine("</div>");

        var runDatasetsJs = new List<string>();
        colorIndex = 0;
        foreach (var scenario in scenarios)
        {
            var color = Palette[colorIndex % Palette.Length];
            colorIndex++;
            var dataList = new List<double>();
            
            var scenarioRuns = runs.Where(r => string.Equals(r.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var variant in variants)
            {
                var variantRuns = scenarioRuns
                    .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase) && string.Equals(r.RunStatus, "Completed", StringComparison.OrdinalIgnoreCase) && r.FailedFiles == 0 && r.ExceptionType == null)
                    .Select(r => r.ExecuteDuration.TotalSeconds)
                    .OrderBy(v => v)
                    .ToList();

                if (variantRuns.Count > 0)
                {
                    dataList.Add(Math.Round(BenchmarkHelpers.Percentile(variantRuns, 0.50), 2));
                }
                else
                {
                    dataList.Add(0.0);
                }
            }

            runDatasetsJs.Add($@"
                {{
                    label: '{scenario}',
                    data: [{string.Join(", ", dataList)}],
                    backgroundColor: {color},
                    borderColor: 'rgba(255, 255, 255, 0.2)',
                    borderWidth: 1
                }}");
        }

        sb.AppendLine("<script>");
        sb.AppendLine($@"
            new Chart(document.getElementById('{runCanvasId}'), {{
                type: 'bar',
                data: {{
                    labels: [{variantLabelsJs}],
                    datasets: [{string.Join(", ", runDatasetsJs)}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: {{
                        x: {{ ticks: {{ color: '#eee' }}, grid: {{ color: '#333' }} }},
                        y: {{ beginAtZero: true, title: {{ display: true, text: 'Seconds', color: '#eee' }}, ticks: {{ color: '#eee' }}, grid: {{ color: '#333' }} }}
                    }},
                    plugins: {{ legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }} }}
                }}
            }});
        ");
        sb.AppendLine("</script>");

        // --- Bucket Throughput Grouped Bar Charts ---
        foreach (var bucket in buckets)
        {
            sb.AppendLine($"<h2>Bucket Throughput (MiB/s): {bucket.Label}</h2>");
            sb.AppendLine($"<div class=\"chart-container\">");
            var bucketCanvasId = $"chart_{Guid.NewGuid():N}";
            sb.AppendLine($"<canvas id=\"{bucketCanvasId}\"></canvas>");
            sb.AppendLine("</div>");

            var bucketDatasetsJs = new List<string>();
            colorIndex = 0;
            
            foreach (var scenario in scenarios)
            {
                var color = Palette[colorIndex % Palette.Length];
                colorIndex++;
                var dataList = new List<double>();
                var scenarioRecords = records.Where(r => string.Equals(r.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var variant in variants)
                {
                    var bucketRecords = scenarioRecords
                        .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase) && bucket.Contains(r.FileSizeBytes))
                        .ToList();

                    if (bucketRecords.Count > 0)
                    {
                        var totalBytes = bucketRecords.Sum(r => r.FileSizeBytes);
                        var totalSeconds = bucketRecords.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                        var aggregateThroughput = totalBytes > 0 && totalSeconds > 0
                            ? (totalBytes / 1048576.0) / totalSeconds
                            : 0.0;
                        dataList.Add(Math.Round(aggregateThroughput, 2));
                    }
                    else
                    {
                        dataList.Add(0.0);
                    }
                }

                bucketDatasetsJs.Add($@"
                    {{
                        label: '{scenario}',
                        data: [{string.Join(", ", dataList)}],
                        backgroundColor: {color},
                        borderColor: 'rgba(255, 255, 255, 0.2)',
                        borderWidth: 1
                    }}");
            }

            sb.AppendLine("<script>");
            sb.AppendLine($@"
                new Chart(document.getElementById('{bucketCanvasId}'), {{
                    type: 'bar',
                    data: {{
                        labels: [{variantLabelsJs}],
                        datasets: [{string.Join(", ", bucketDatasetsJs)}]
                    }},
                    options: {{
                        responsive: true,
                        maintainAspectRatio: false,
                        scales: {{
                            x: {{ ticks: {{ color: '#eee' }}, grid: {{ color: '#333' }} }},
                            y: {{ beginAtZero: true, title: {{ display: true, text: 'MiB/s', color: '#eee' }}, ticks: {{ color: '#eee' }}, grid: {{ color: '#333' }} }}
                        }},
                        plugins: {{ legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }} }}
                    }}
                }});
            ");
            sb.AppendLine("</script>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }
}
