using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkHtmlReportGenerator
{
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
        IReadOnlyList<BenchmarkRunRecord> runs,
        IReadOnlyList<BenchmarkRunRecord> allRuns,
        IReadOnlyDictionary<string, string> matchedControlLookup)
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
        sb.AppendLine("table { border-collapse: collapse; margin-bottom: 2rem; min-width: 800px; }");
        sb.AppendLine("th, td { border: 1px solid #333; padding: 0.5rem 0.7rem; text-align: left; }");
        sb.AppendLine("th { background: #222; }");
        sb.AppendLine(".warning { border-left: 4px solid #ff9f40; background: #241b12; padding: 0.75rem 1rem; margin-bottom: 2rem; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>Benchmark Analysis: {scenarioName}</h1>");

        AppendRunDistributionSection(sb, variants, allRuns, matchedControlLookup);

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
                var medianSeconds = BenchmarkHelpers.Percentile(variantRuns, 0.50);
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
                var evidence = BenchmarkStatistics.BuildBucketEvidence(records, bucket, variant);
                dataAvg.Add(Math.Round(evidence.AggregateThroughputMiBPerSecond, 2));
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

        // --- Variant % vs Control (Bar Chart) ---
        sb.AppendLine($"<h2>Variant vs Control (%)</h2>");
        sb.AppendLine($"<div class=\"chart-container\">");
        var diffCanvasId = $"chart_{Guid.NewGuid():N}";
        sb.AppendLine($"<canvas id=\"{diffCanvasId}\"></canvas>");
        sb.AppendLine("</div>");

        var diffDatasetsJs = new List<string>();
        int diffColorIndex = 0;
        double maxAbsDiff = 0.0;

        foreach (var variant in variants)
        {
            if (!matchedControlLookup.TryGetValue(variant, out var controlVariant) || string.IsNullOrWhiteSpace(controlVariant))
                continue;

            // Only plot if the variant isn't its own control
            if (string.Equals(variant, controlVariant, StringComparison.OrdinalIgnoreCase))
                continue;

            var color = Palette[diffColorIndex % Palette.Length];
            diffColorIndex++;

            var dataDiff = new List<double>();
            foreach (var bucket in buckets)
            {
                var bucketRecords = records.Where(r => bucket.Contains(r.FileSizeBytes)).ToList();
                
                var variantRecords = bucketRecords
                    .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                var controlRecords = bucketRecords
                    .Where(r => string.Equals(r.VariantName, controlVariant, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var variantTotalBytes = variantRecords.Sum(r => r.FileSizeBytes);
                var variantTotalSeconds = variantRecords.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                var variantAvg = variantTotalBytes > 0 && variantTotalSeconds > 0 ? (variantTotalBytes / 1048576.0) / variantTotalSeconds : 0.0;

                var controlTotalBytes = controlRecords.Sum(r => r.FileSizeBytes);
                var controlTotalSeconds = controlRecords.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                var controlAvg = controlTotalBytes > 0 && controlTotalSeconds > 0 ? (controlTotalBytes / 1048576.0) / controlTotalSeconds : 0.0;

                if (controlAvg > 0 && variantAvg > 0)
                {
                    var diffPercent = ((variantAvg - controlAvg) / controlAvg) * 100.0;
                    dataDiff.Add(Math.Round(diffPercent, 2));
                    if (Math.Abs(diffPercent) > maxAbsDiff) maxAbsDiff = Math.Abs(diffPercent);
                }
                else
                {
                    dataDiff.Add(0.0);
                }
            }

            var dataDiffStr = string.Join(", ", dataDiff.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var borderColor = color.Replace("0.7", "1.0");
            var backgroundColor = color.Replace("0.7", "0.1");

            diffDatasetsJs.Add($@"
                {{
                    label: '{variant} vs {controlVariant}',
                    data: [{dataDiffStr}],
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

        if (diffDatasetsJs.Count > 0)
        {
            var allDiffDatasetsJs = string.Join(", ", diffDatasetsJs);

            var maxAbsDiffCeil = Math.Max(10.0, Math.Ceiling(maxAbsDiff / 10.0) * 10.0);

            sb.AppendLine("<script>");
            sb.AppendLine($@"
                new Chart(document.getElementById('{diffCanvasId}'), {{
                    type: 'line',
                    data: {{
                        labels: [{trendBucketLabelsJs}],
                        datasets: [{allDiffDatasetsJs}]
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
                                title: {{ display: true, text: '% Difference vs Control', color: '#eee' }},
                                min: {-maxAbsDiffCeil},
                                max: {maxAbsDiffCeil},
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
                        label: '{variant}',
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

    private static void AppendRunDistributionSection(
        StringBuilder sb,
        IReadOnlyList<string> variants,
        IReadOnlyList<BenchmarkRunRecord> allRuns,
        IReadOnlyDictionary<string, string> matchedControlLookup)
    {
        var successfulRuns = allRuns
            .Where(BenchmarkHelpers.IsSuccessfulRun)
            .ToList();
        if (successfulRuns.Count == 0)
            return;

        sb.AppendLine("<h2>Run Distribution (All Successful Runs)</h2>");
        sb.AppendLine("<p>Includes every successful terminal run for each variant, including runs discarded by the converged-window selector.</p>");

        var chartId = $"chart_{Guid.NewGuid():N}";
        sb.AppendLine("<div class=\"chart-container\">");
        sb.AppendLine($"<canvas id=\"{chartId}\"></canvas>");
        sb.AppendLine("</div>");

        var variantLabelsJs = string.Join(", ", variants.Select(JsonString));
        var datasets = new List<string>();
        var colorIndex = 0;

        for (var variantIndex = 0; variantIndex < variants.Count; variantIndex++)
        {
            var variant = variants[variantIndex];
            var color = Palette[colorIndex % Palette.Length];
            var borderColor = color.Replace("0.7", "1.0");
            colorIndex++;

            var points = successfulRuns
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.RunIndex)
                .Select(r =>
                {
                    var jitter = ((r.RunIndex % 7) - 3) * 0.025;
                    return "{ " +
                           $"x: {FormatInvariant(variantIndex + jitter)}, " +
                           $"y: {FormatInvariant(r.ExecuteDuration.TotalSeconds)}, " +
                           $"runIndex: {r.RunIndex}" +
                           " }";
                })
                .ToList();

            if (points.Count == 0)
                continue;

            datasets.Add($@"
                {{
                    label: {JsonString(variant)},
                    data: [{string.Join(", ", points)}],
                    backgroundColor: {color},
                    borderColor: {borderColor},
                    pointRadius: 4,
                    pointHoverRadius: 6
                }}");
        }

        if (datasets.Count > 0)
        {
            sb.AppendLine("<script>");
            sb.AppendLine($@"
                const variantLabels_{chartId} = [{variantLabelsJs}];
                new Chart(document.getElementById('{chartId}'), {{
                    type: 'scatter',
                    data: {{
                        datasets: [{string.Join(", ", datasets)}]
                    }},
                    options: {{
                        responsive: true,
                        maintainAspectRatio: false,
                        scales: {{
                            x: {{
                                type: 'linear',
                                min: -0.5,
                                max: {FormatInvariant(Math.Max(0, variants.Count - 0.5))},
                                ticks: {{
                                    color: '#eee',
                                    stepSize: 1,
                                    callback: value => variantLabels_{chartId}[Math.round(value)] ?? ''
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
                            legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }},
                            tooltip: {{
                                callbacks: {{
                                    label: ctx => `${{ctx.dataset.label}} run ${{ctx.raw.runIndex}}: ${{ctx.raw.y.toFixed(2)}}s`
                                }}
                            }}
                        }}
                    }}
                }});
            ");
            sb.AppendLine("</script>");
        }

        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>Variant</th><th>Successful/Ran</th><th>All-Run Median</th><th>All-Run Mean</th><th>Global Min</th><th>Global Max</th><th>Global Spread</th><th>Cluster Count</th><th>Clusters</th></tr></thead>");
        sb.AppendLine("<tbody>");

        var clustered = new List<(string Variant, BenchmarkConvergence.DistributionSummary Summary)>();
        var distributionSummaries = new Dictionary<string, BenchmarkConvergence.DistributionSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in variants)
        {
            var variantRuns = allRuns
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (variantRuns.Count == 0)
                continue;

            var durations = variantRuns
                .Where(BenchmarkHelpers.IsSuccessfulRun)
                .Select(r => r.ExecuteDuration.TotalSeconds)
                .ToList();
            var summary = BenchmarkConvergence.BuildDistributionSummary(durations);
            distributionSummaries[variant] = summary;
            if (summary.HasSeparatedClusters)
                clustered.Add((variant, summary));

            var clusters = summary.Clusters.Count == 0
                ? "-"
                : string.Join("; ", summary.Clusters.Select(FormatCluster));

            sb.AppendLine(
                "<tr>" +
                $"<td>{Html(variant)}</td>" +
                $"<td>{durations.Count}/{variantRuns.Count}</td>" +
                $"<td>{Html(FormatDistributionDuration(summary.MedianSeconds))}</td>" +
                $"<td>{Html(FormatDistributionDuration(summary.MeanSeconds))}</td>" +
                $"<td>{Html(FormatDistributionDuration(summary.MinSeconds))}</td>" +
                $"<td>{Html(FormatDistributionDuration(summary.MaxSeconds))}</td>" +
                $"<td>{Html(FormatDistributionDuration(summary.SpreadSeconds))}</td>" +
                $"<td>{summary.Clusters.Count}</td>" +
                $"<td>{Html(clusters)}</td>" +
                "</tr>");
        }

        sb.AppendLine("</tbody></table>");

        if (clustered.Count > 0)
        {
            sb.AppendLine("<h2>Run Mode Summary (All Successful Runs)</h2>");
            sb.AppendLine("<p>Only variants with separated duration clusters are listed here. Fast and slow bands are the first and last clusters for each variant. Delta compares the same band against the variant's configured matched control when both variants have separated clusters.</p>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Variant</th><th>Fast Runs</th><th>Fast Median</th><th>Fast Range</th><th>Fast Delta vs Control</th><th>Slow Runs</th><th>Slow Median</th><th>Slow Range</th><th>Slow Delta vs Control</th></tr></thead>");
            sb.AppendLine("<tbody>");

            foreach (var variant in variants)
            {
                if (!distributionSummaries.TryGetValue(variant, out var summary) || !summary.HasSeparatedClusters)
                    continue;

                var fast = summary.Clusters[0];
                var slow = summary.Clusters[^1];
                var fastDelta = FormatBandDelta(variant, RunBandSelector.Fast, distributionSummaries, matchedControlLookup);
                var slowDelta = FormatBandDelta(variant, RunBandSelector.Slow, distributionSummaries, matchedControlLookup);

                sb.AppendLine(
                    "<tr>" +
                    $"<td>{Html(variant)}</td>" +
                    $"<td>{fast.Count}</td>" +
                    $"<td>{Html(FormatDistributionDuration(fast.MedianSeconds))}</td>" +
                    $"<td>{Html(FormatClusterRange(fast))}</td>" +
                    $"<td>{Html(fastDelta)}</td>" +
                    $"<td>{slow.Count}</td>" +
                    $"<td>{Html(FormatDistributionDuration(slow.MedianSeconds))}</td>" +
                    $"<td>{Html(FormatClusterRange(slow))}</td>" +
                    $"<td>{Html(slowDelta)}</td>" +
                    "</tr>");
            }

            sb.AppendLine("</tbody></table>");

            sb.AppendLine("<div class=\"warning\"><strong>Clustered run distribution:</strong>");
            sb.AppendLine("<ul>");
            foreach (var (variant, summary) in clustered)
            {
                var clusters = string.Join("; ", summary.Clusters.Select(FormatCluster));
                sb.AppendLine($"<li><strong>{Html(variant)}</strong>: {Html(clusters)}</li>");
            }
            sb.AppendLine("</ul></div>");
        }

        AppendGcEvidenceSection(sb, variants, allRuns);
    }

    private static void AppendGcEvidenceSection(
        StringBuilder sb,
        IReadOnlyList<string> variants,
        IReadOnlyList<BenchmarkRunRecord> allRuns)
    {
        if (!allRuns.Any(r => BenchmarkHelpers.IsSuccessfulRun(r) && r.ExecuteAllocatedBytes.HasValue))
            return;

        sb.AppendLine("<h2>Execute GC Evidence (All Successful Runs)</h2>");
        sb.AppendLine("<p>Captured around the execute window only. Allocation bytes are process counters, so they include work on benchmark/reporting threads during that window. Collection counts are reported as per-run medians/means so variants with different completed-run counts remain comparable.</p>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>Variant</th><th>Runs with GC</th><th>Median Allocated</th><th>Mean Allocated</th><th>Max Allocated</th><th>Median Gen0</th><th>Mean Gen0</th><th>Median Gen1</th><th>Mean Gen1</th><th>Median Gen2</th><th>Mean Gen2</th><th>Median Heap Delta</th><th>Median Fragmentation Delta</th></tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var variant in variants)
        {
            var runs = allRuns
                .Where(BenchmarkHelpers.IsSuccessfulRun)
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.ExecuteAllocatedBytes.HasValue)
                .ToList();
            if (runs.Count == 0)
                continue;

            var allocated = runs.Select(r => (double)r.ExecuteAllocatedBytes!.Value).OrderBy(v => v).ToList();
            var gen0 = runs.Select(r => (double)(r.ExecuteGen0Collections ?? 0)).OrderBy(v => v).ToList();
            var gen1 = runs.Select(r => (double)(r.ExecuteGen1Collections ?? 0)).OrderBy(v => v).ToList();
            var gen2 = runs.Select(r => (double)(r.ExecuteGen2Collections ?? 0)).OrderBy(v => v).ToList();
            var heapDeltas = runs
                .Where(r => r.ExecuteHeapSizeDeltaBytes.HasValue)
                .Select(r => (double)r.ExecuteHeapSizeDeltaBytes!.Value)
                .OrderBy(v => v)
                .ToList();
            var fragmentedDeltas = runs
                .Where(r => r.ExecuteFragmentedDeltaBytes.HasValue)
                .Select(r => (double)r.ExecuteFragmentedDeltaBytes!.Value)
                .OrderBy(v => v)
                .ToList();

            sb.AppendLine(
                "<tr>" +
                $"<td>{Html(variant)}</td>" +
                $"<td>{runs.Count}</td>" +
                $"<td>{Html(FormatBytes(BenchmarkHelpers.Percentile(allocated, 0.5)))}</td>" +
                $"<td>{Html(FormatBytes(allocated.Average()))}</td>" +
                $"<td>{Html(FormatBytes(allocated[^1]))}</td>" +
                $"<td>{Html(FormatCount(BenchmarkHelpers.Percentile(gen0, 0.5)))}</td>" +
                $"<td>{Html(FormatCount(gen0.Average()))}</td>" +
                $"<td>{Html(FormatCount(BenchmarkHelpers.Percentile(gen1, 0.5)))}</td>" +
                $"<td>{Html(FormatCount(gen1.Average()))}</td>" +
                $"<td>{Html(FormatCount(BenchmarkHelpers.Percentile(gen2, 0.5)))}</td>" +
                $"<td>{Html(FormatCount(gen2.Average()))}</td>" +
                $"<td>{Html(FormatSignedBytes(heapDeltas.Count == 0 ? double.NaN : BenchmarkHelpers.Percentile(heapDeltas, 0.5)))}</td>" +
                $"<td>{Html(FormatSignedBytes(fragmentedDeltas.Count == 0 ? double.NaN : BenchmarkHelpers.Percentile(fragmentedDeltas, 0.5)))}</td>" +
                "</tr>");
        }

        sb.AppendLine("</tbody></table>");

        static string FormatBytes(double bytes) =>
            double.IsNaN(bytes) ? "-" : BenchmarkHelpers.FormatSize((long)Math.Round(bytes));

        static string FormatCount(double value) =>
            double.IsNaN(value) ? "-" : value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        static string FormatSignedBytes(double bytes)
        {
            if (double.IsNaN(bytes))
                return "-";

            var rounded = (long)Math.Round(bytes);
            if (rounded > 0)
                return $"+{BenchmarkHelpers.FormatSize(rounded)}";
            if (rounded < 0)
                return $"-{BenchmarkHelpers.FormatSize(Math.Abs(rounded))}";
            return "0 B";
        }
    }

    private static string FormatCluster(BenchmarkConvergence.DistributionCluster cluster)
    {
        return $"{cluster.Count} @ {FormatClusterRange(cluster)}";
    }

    private static string FormatClusterRange(BenchmarkConvergence.DistributionCluster cluster)
    {
        var min = BenchmarkHelpers.FormatDurationHuman(cluster.MinSeconds);
        var max = BenchmarkHelpers.FormatDurationHuman(cluster.MaxSeconds);
        return Math.Abs(cluster.MaxSeconds - cluster.MinSeconds) < 0.0005
            ? min
            : $"{min}-{max}";
    }

    private static string FormatBandDelta(
        string variant,
        RunBandSelector selector,
        IReadOnlyDictionary<string, BenchmarkConvergence.DistributionSummary> summaries,
        IReadOnlyDictionary<string, string> controls)
    {
        if (!controls.TryGetValue(variant, out var controlVariant))
            return "-";
        if (!summaries.TryGetValue(variant, out var candidateSummary) ||
            !summaries.TryGetValue(controlVariant, out var controlSummary))
            return "-";
        if (candidateSummary.Clusters.Count < 2 || controlSummary.Clusters.Count < 2)
            return "-";

        var candidate = selector == RunBandSelector.Fast
            ? candidateSummary.Clusters[0]
            : candidateSummary.Clusters[^1];
        var control = selector == RunBandSelector.Fast
            ? controlSummary.Clusters[0]
            : controlSummary.Clusters[^1];

        if (control.MedianSeconds <= 0)
            return "-";

        var deltaSeconds = control.MedianSeconds - candidate.MedianSeconds;
        var percent = deltaSeconds / control.MedianSeconds * 100.0;
        return $"{BenchmarkComparison.FormatSignedPercent(percent)} ({BenchmarkComparison.FormatSignedDurationSeconds(deltaSeconds)})";
    }

    private static string FormatDistributionDuration(double seconds) =>
        double.IsNaN(seconds) ? "-" : BenchmarkHelpers.FormatDurationHuman(seconds);

    private enum RunBandSelector
    {
        Fast,
        Slow,
    }

    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    private static string Html(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    private static string FormatInvariant(double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static async Task GenerateSummaryAsync(
        string outputPath,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<BenchmarkRunRecord> runs,
        IReadOnlyList<string> scenarios,
        IReadOnlyDictionary<string, string> matchedControlLookup)
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

        var validVariants = variants
            .Where(v => matchedControlLookup.TryGetValue(v, out var c) && !string.Equals(v, c, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var validVariantLabelsJs = string.Join(", ", validVariants.Select(v => $"'{v}'"));

        sb.AppendLine("<h2>Variant vs Control (%) Across Scenarios</h2>");
        sb.AppendLine("<div style=\"margin-bottom: 1rem;\">");
        sb.AppendLine("<label for=\"bucketSelector\" style=\"margin-right: 1rem; font-weight: bold;\">Filter by Bucket Size:</label>");
        sb.AppendLine("<select id=\"bucketSelector\" style=\"padding: 0.5rem; background: #333; color: #eee; border: 1px solid #555; border-radius: 4px;\">");
        sb.AppendLine("<option value=\"Aggregate\">Aggregate (All Buckets)</option>");
        foreach (var bucket in buckets)
        {
            sb.AppendLine($"<option value=\"{bucket.Label}\">{bucket.Label}</option>");
        }
        sb.AppendLine("</select>");
        sb.AppendLine("</div>");

        sb.AppendLine($"<div class=\"chart-container\">");
        var interactiveCanvasId = $"chart_{Guid.NewGuid():N}";
        sb.AppendLine($"<canvas id=\"{interactiveCanvasId}\"></canvas>");
        sb.AppendLine("</div>");

        sb.AppendLine("<script>");
        sb.AppendLine("var diffDatasetsByBucket = {};");

        var bucketOptions = new List<FileSizeBucket?> { null };
        bucketOptions.AddRange(buckets);
        double globalMaxAbsDiff = 0.0;

        foreach (var bucketOption in bucketOptions)
        {
            var bucketKey = bucketOption?.Label ?? "Aggregate";
            sb.AppendLine($"diffDatasetsByBucket['{bucketKey}'] = [");

            int scenarioColorIndex = 0;
            foreach (var scenario in scenarios)
            {
                var color = Palette[scenarioColorIndex % Palette.Length];
                var borderColor = color.Replace("0.7", "1.0");
                var backgroundColor = color.Replace("0.7", "0.1");
                scenarioColorIndex++;

                var dataDiffs = new List<double>();
                foreach (var variant in validVariants)
                {
                    var controlVariant = matchedControlLookup[variant];
                    var recordsForBucket = records
                        .Where(r => string.Equals(r.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase))
                        .Where(r => bucketOption == null || bucketOption.Contains(r.FileSizeBytes))
                        .ToList();

                    var variantRecords = recordsForBucket.Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase)).ToList();
                    var controlRecords = recordsForBucket.Where(r => string.Equals(r.VariantName, controlVariant, StringComparison.OrdinalIgnoreCase)).ToList();

                    var variantTotalBytes = variantRecords.Sum(r => r.FileSizeBytes);
                    var variantTotalSeconds = variantRecords.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                    var variantAvg = variantTotalBytes > 0 && variantTotalSeconds > 0 ? (variantTotalBytes / 1048576.0) / variantTotalSeconds : 0.0;

                    var controlTotalBytes = controlRecords.Sum(r => r.FileSizeBytes);
                    var controlTotalSeconds = controlRecords.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                    var controlAvg = controlTotalBytes > 0 && controlTotalSeconds > 0 ? (controlTotalBytes / 1048576.0) / controlTotalSeconds : 0.0;

                    if (controlAvg > 0 && variantAvg > 0)
                    {
                        var diffPercent = ((variantAvg - controlAvg) / controlAvg) * 100.0;
                        dataDiffs.Add(Math.Round(diffPercent, 2));
                        if (Math.Abs(diffPercent) > globalMaxAbsDiff) globalMaxAbsDiff = Math.Abs(diffPercent);
                    }
                    else
                    {
                        dataDiffs.Add(0.0);
                    }
                }

                var dataDiffStr = string.Join(", ", dataDiffs.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                sb.AppendLine($@"
                    {{
                        label: '{scenario}',
                        data: [{dataDiffStr}],
                        borderColor: {borderColor},
                        backgroundColor: {backgroundColor},
                        borderWidth: 2,
                        pointRadius: 4,
                        pointHoverRadius: 6,
                        tension: 0.15,
                        fill: false
                    }},");
            }
            sb.AppendLine("];");
        }

        var maxAbsDiffCeilSummary = Math.Max(10.0, Math.Ceiling(globalMaxAbsDiff / 10.0) * 10.0);

        sb.AppendLine($@"
            var ctxInteractive = document.getElementById('{interactiveCanvasId}');
            var interactiveChart = new Chart(ctxInteractive, {{
                type: 'line',
                data: {{
                    labels: [{validVariantLabelsJs}],
                    datasets: diffDatasetsByBucket['Aggregate']
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: {{
                        x: {{ ticks: {{ color: '#eee' }}, grid: {{ color: '#333' }} }},
                        y: {{ 
                            title: {{ display: true, text: '% Difference vs Control', color: '#eee' }},
                            min: {-maxAbsDiffCeilSummary},
                            max: {maxAbsDiffCeilSummary},
                            ticks: {{ color: '#eee' }}, 
                            grid: {{ color: '#333' }} 
                        }}
                    }},
                    plugins: {{ legend: {{ display: true, position: 'top', labels: {{ color: '#eee' }} }} }}
                }}
            }});

            document.getElementById('bucketSelector').addEventListener('change', function(e) {{
                var selectedBucket = e.target.value;
                interactiveChart.data.datasets = diffDatasetsByBucket[selectedBucket];
                interactiveChart.update();
            }});
        ");
        sb.AppendLine("</script>");

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
