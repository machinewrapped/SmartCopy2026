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

## OS File Cache Boundaries

To accurately measure I/O performance, the benchmark requires a cold OS file
cache when crossing path-pool boundaries. The console pauses at those boundaries
and asks you to reboot before continuing.
