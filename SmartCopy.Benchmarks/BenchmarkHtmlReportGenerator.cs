using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkHtmlReportGenerator
{
    private static string GetBatchCategory(string variantName)
    {
        if (variantName.Contains("Unbatched", StringComparison.OrdinalIgnoreCase) || 
            variantName.Contains("Baseline", StringComparison.OrdinalIgnoreCase) || 
            variantName.Contains("Control", StringComparison.OrdinalIgnoreCase))
            return "Unbatched";

        var match = System.Text.RegularExpressions.Regex.Match(variantName, @"\d+MiB");
        if (match.Success)
        {
            return $"{match.Value} Buffer";
        }

        return "Other";
    }

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

        var categories = variants.Select(GetBatchCategory).Distinct().ToList();

        // X-axis labels
        var bucketLabelsJs = string.Join(", ", buckets.Select(b => $"'{b.Label}'"));

        foreach (var category in categories)
        {
            var categoryVariants = variants.Where(v => GetBatchCategory(v) == category).ToList();
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
}
