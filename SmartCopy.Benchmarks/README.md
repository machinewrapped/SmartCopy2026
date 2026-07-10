# SmartCopy.Benchmarks

This project contains the standalone benchmark suite for measuring file copy performance and comparing optimisation strategies for SmartCopy2026.

The benchmark executes realistic dataset fixtures against different variant implementations (buffer sizes, direct write, staging, etc.) and produces NDJSON historical records of throughput and copy latency for analysis.

## Running Benchmarks

**CRITICAL RULE: NEVER launch benchmarks without EXPLICIT permission.**
Benchmarks require a stable system state (no background tasks, controlled temperature). Always prepare the configuration first.

```bash
# Run the benchmark suite (executes copies and writes to .benchmarks/)
dotnet run --project .\SmartCopy.Benchmarks --config <path-to-scenario.json>

# Analyze existing benchmark results without re-running copies
dotnet run --project .\SmartCopy.Benchmarks --config <path-to-scenario.json> --mode analyze
```

All modes require an explicit `--config <path>` argument. Prepare a configuration
for the machine, dataset fixtures, and workload being measured.

## OS File Cache / RAMMap Configuration

To accurately measure I/O performance, the benchmark requires a cold OS file cache between scenarios (and optionally between variants). By default, the console will pause and ask you to reboot.

To automate this, you can configure the suite to use Sysinternals [RAMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap) to instantly flush the standby list.

**Configuration (`BenchmarkConfig.json`):**
```json
{
  "clearCacheBetweenRuns": true,
  "ramMapPath": "D:\\Tools\\RAMMap\\RAMMap64.exe"
}
```

**Running with RAMMap:**
RAMMap requires **Administrator privileges** to clear the standby list. You must run the `dotnet run` benchmark command from an **elevated (Administrator) command prompt or PowerShell instance**.

If you run from a standard non-elevated terminal, Windows UAC will intercept the RAMMap execution and prompt you to click "Yes" every single time the cache is cleared, pausing your automated benchmark!
