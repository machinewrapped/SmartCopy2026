using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Scanning;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Scanning;

public sealed class DirectoryWatcherTests : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly TempDirectory _temp;
    private readonly LocalFileSystemProvider _provider;

    public DirectoryWatcherTests()
    {
        _temp = new TempDirectory();
        _provider = new LocalFileSystemProvider(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task FileCreated_EmitsBatchWithInsert()
    {
        using var watcher = new DirectoryWatcher(_provider, _temp.Path, Debounce);
        var batchReady = new TaskCompletionSource();
        watcher.PendingBatchesAvailable += (_, _) => batchReady.TrySetResult();
        watcher.Start();

        await File.WriteAllTextAsync(Path.Combine(_temp.Path, "new.txt"), "x");

        await batchReady.Task.WaitAsync(WaitTimeout);
        var batches = watcher.DrainPendingBatches();

        Assert.Single(batches);
        Assert.Contains(batches[0].Inserts, i => i.CanonicalRelativePath == "new.txt");
    }

    [Fact]
    public async Task FileDeleted_EmitsBatchWithDeletion()
    {
        var file = Path.Combine(_temp.Path, "delete-me.txt");
        await File.WriteAllTextAsync(file, "x");

        using var watcher = new DirectoryWatcher(_provider, _temp.Path, Debounce);
        var batchReady = new TaskCompletionSource();
        watcher.PendingBatchesAvailable += (_, _) => batchReady.TrySetResult();
        watcher.Start();

        File.Delete(file);

        await batchReady.Task.WaitAsync(WaitTimeout);
        var batches = watcher.DrainPendingBatches();

        Assert.Single(batches);
        Assert.Contains(batches[0].Deletions, d => d.CanonicalRelativePath == "delete-me.txt");
    }

    [Fact]
    public async Task MultipleFilesCreatedWithinDebounceWindow_ProduceSingleBatch()
    {
        using var watcher = new DirectoryWatcher(_provider, _temp.Path, Debounce);
        int batchCount = 0;
        var firstBatch = new TaskCompletionSource();
        watcher.PendingBatchesAvailable += (_, _) =>
        {
            batchCount++;
            firstBatch.TrySetResult();
        };
        watcher.Start();

        for (int i = 0; i < 5; i++)
            await File.WriteAllTextAsync(Path.Combine(_temp.Path, $"file{i}.txt"), "x");

        await firstBatch.Task.WaitAsync(WaitTimeout);
        await Task.Delay(Debounce * 3); // confirm no second batch fires

        Assert.Equal(1, batchCount);
        var batches = watcher.DrainPendingBatches();
        Assert.Single(batches);
        Assert.Equal(5, batches[0].Inserts.Length);
    }

    [Fact]
    public async Task DrainPendingBatches_AfterDrain_HasNoPendingBatches()
    {
        using var watcher = new DirectoryWatcher(_provider, _temp.Path, Debounce);
        var batchReady = new TaskCompletionSource();
        watcher.PendingBatchesAvailable += (_, _) => batchReady.TrySetResult();
        watcher.Start();

        await File.WriteAllTextAsync(Path.Combine(_temp.Path, "drain.txt"), "x");

        await batchReady.Task.WaitAsync(WaitTimeout);
        Assert.True(watcher.HasPendingBatches);

        watcher.DrainPendingBatches();
        Assert.False(watcher.HasPendingBatches);
    }
}
