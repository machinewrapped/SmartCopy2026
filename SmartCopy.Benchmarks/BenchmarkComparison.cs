namespace SmartCopy.Benchmarks;

/// <summary>
/// Shared comparison logic for run-level and bucket-level evidence. Used by both the
/// benchmark analysis report and the validation gate/conclusion, so they always agree
/// on verdicts for the same data.
/// </summary>
internal static class BenchmarkComparison
{
    internal sealed record EvidenceComparison(string Verdict, string DeltaText, string NoiseText);

    internal static EvidenceComparison CompareRunEvidence(
        RunVariantEvidence candidate,
        RunVariantEvidence? control,
        double gatePercent,
        bool isControl)
    {
        if (candidate.ValidRuns == 0)
            return new EvidenceComparison("INVALID", "-", "-");

        if (control is null)
            return isControl
                ? new EvidenceComparison("CONTROL", "-", "-")
                : new EvidenceComparison("INCONCLUSIVE", "-", "-");

        if (control.ValidRuns == 0)
            return new EvidenceComparison("INCONCLUSIVE", "-", "-");

        var deltaSeconds = control.MedianSeconds - candidate.MedianSeconds;
        var deltaPercent = control.MedianSeconds > 0 ? deltaSeconds / control.MedianSeconds * 100.0 : 0.0;
        var noiseFloor = (control.SpreadSeconds + candidate.SpreadSeconds) / 2.0;
        var verdict = GetDeltaVerdict(deltaSeconds, deltaPercent, noiseFloor, gatePercent);
        return new EvidenceComparison(
            verdict,
            $"{FormatSignedPercent(deltaPercent)} ({FormatSignedDurationSeconds(deltaSeconds)})",
            BenchmarkHelpers.FormatDurationHuman(noiseFloor));
    }

    internal static EvidenceComparison CompareBucketEvidence(
        BucketVariantEvidence candidate,
        BucketVariantEvidence? control,
        double gatePercent,
        bool isControl)
    {
        if (candidate.RecordCount == 0)
            return new EvidenceComparison("INVALID", "-", "-");

        if (control is null)
            return isControl
                ? new EvidenceComparison("CONTROL", "-", "-")
                : new EvidenceComparison("INCONCLUSIVE", "-", "-");

        if (control.RecordCount == 0)
            return new EvidenceComparison("INCONCLUSIVE", "-", "-");

        var deltaMiBPerSecond = candidate.AggregateThroughputMiBPerSecond - control.AggregateThroughputMiBPerSecond;
        var deltaPercent = control.AggregateThroughputMiBPerSecond > 0
            ? deltaMiBPerSecond / control.AggregateThroughputMiBPerSecond * 100.0
            : 0.0;
        var noiseFloor = (control.RunThroughputSpreadMiBPerSecond + candidate.RunThroughputSpreadMiBPerSecond) / 2.0;
        var verdict = GetDeltaVerdict(deltaMiBPerSecond, deltaPercent, noiseFloor, gatePercent);
        return new EvidenceComparison(
            verdict,
            $"{FormatSignedPercent(deltaPercent)} ({deltaMiBPerSecond:+0.00;-0.00;0} MiB/s)",
            $"{noiseFloor:0.00} MiB/s");
    }

    internal static string GetDeltaVerdict(double delta, double deltaPercent, double noiseFloor, double gatePercent)
    {
        if (delta < -noiseFloor)
            return "REGRESSION";
        if (delta <= noiseFloor)
            return "INCONCLUSIVE";
        return deltaPercent >= gatePercent ? "PASS" : "BELOW_THRESHOLD";
    }

    internal static string FormatSignedPercent(double value) =>
        value switch
        {
            > 0 => $"+{value:0.0}%",
            < 0 => $"{value:0.0}%",
            _ => "0.0%",
        };

    internal static string FormatSignedDurationSeconds(double seconds) =>
        seconds switch
        {
            > 0 => $"+{BenchmarkHelpers.FormatDurationHuman(seconds)}",
            < 0 => $"-{BenchmarkHelpers.FormatDurationHuman(Math.Abs(seconds))}",
            _ => "0s",
        };
}
