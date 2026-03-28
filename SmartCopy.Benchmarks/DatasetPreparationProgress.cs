namespace SmartCopy.Benchmarks;

public record struct DatasetPreparationProgress(
    int TotalFilesScanned,
    int TotalFilesImported,
    long TotalBytesImported,
    string CurrentFile);
