using SmartCopy.Core.Selection;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Selection;

public sealed class SelectionSerializerTests
{
    [Fact]
    public async Task RoundTripsTxtM3uAndSc2Sel()
    {
        using var temp = new TempDirectory();
        var serializer = new SelectionSerializer();
        var snapshot = new SelectionSnapshot(["a/b/c.mp3", "x/y/z.flac"]);

        var txtPath = System.IO.Path.Combine(temp.Path, "selection.txt");
        var m3uPath = System.IO.Path.Combine(temp.Path, "selection.m3u");
        var sc2Path = System.IO.Path.Combine(temp.Path, "selection.sc2sel");

        await serializer.SaveTxtAsync(txtPath, snapshot, CancellationToken.None);
        await serializer.SaveM3uAsync(m3uPath, snapshot, CancellationToken.None);
        await serializer.SaveSc2SelAsync(sc2Path, snapshot, CancellationToken.None);

        var txt = await serializer.LoadTxtAsync(txtPath, CancellationToken.None);
        var m3u = await serializer.LoadM3uAsync(m3uPath, CancellationToken.None);
        var sc2 = await serializer.LoadSc2SelAsync(sc2Path, CancellationToken.None);

        Assert.Equal(snapshot.RelativePaths.Count, txt.RelativePaths.Count);
        Assert.Equal(snapshot.RelativePaths.Count, m3u.RelativePaths.Count);
        Assert.Equal(snapshot.RelativePaths.Count, sc2.RelativePaths.Count);
    }

    [Fact]
    public async Task M3u8_RoundTrip()
    {
        using var temp = new TempDirectory();
        var serializer = new SelectionSerializer();
        var snapshot = new SelectionSnapshot(["a/b/c.mp3", "x/y/z.flac"]);

        var m3u8Path = System.IO.Path.Combine(temp.Path, "selection.m3u8");
        await serializer.SaveM3u8Async(m3u8Path, snapshot, CancellationToken.None);
        var loaded = await serializer.LoadM3u8Async(m3u8Path, CancellationToken.None);

        Assert.Equal(snapshot.RelativePaths.Count, loaded.RelativePaths.Count);
        Assert.True(loaded.Contains("a/b/c.mp3"));
        Assert.True(loaded.Contains("x/y/z.flac"));
    }

    [Fact]
    public async Task LoadAsync_DispatchesByExtension()
    {
        using var temp = new TempDirectory();
        var serializer = new SelectionSerializer();
        var snapshot = new SelectionSnapshot(["songs/track.mp3"]);

        var txtPath  = System.IO.Path.Combine(temp.Path, "sel.txt");
        var m3uPath  = System.IO.Path.Combine(temp.Path, "sel.m3u");
        var m3u8Path = System.IO.Path.Combine(temp.Path, "sel.m3u8");
        var sc2Path  = System.IO.Path.Combine(temp.Path, "sel.sc2sel");

        await serializer.SaveAsync(txtPath,  snapshot);
        await serializer.SaveAsync(m3uPath,  snapshot);
        await serializer.SaveAsync(m3u8Path, snapshot);
        await serializer.SaveAsync(sc2Path,  snapshot);

        var fromTxt  = await serializer.LoadAsync(txtPath);
        var fromM3u  = await serializer.LoadAsync(m3uPath);
        var fromM3u8 = await serializer.LoadAsync(m3u8Path);
        var fromSc2  = await serializer.LoadAsync(sc2Path);

        Assert.True(fromTxt.Contains("songs/track.mp3"));
        Assert.True(fromM3u.Contains("songs/track.mp3"));
        Assert.True(fromM3u8.Contains("songs/track.mp3"));
        Assert.True(fromSc2.Contains("songs/track.mp3"));
    }

    [Fact]
    public async Task NonAscii_Paths_RoundTrip()
    {
        using var temp = new TempDirectory();
        var serializer = new SelectionSerializer();
        var snapshot = new SelectionSnapshot(["музыка/трек.mp3", "café/résumé.txt", "日本語/音楽.flac"]);

        var txtPath  = System.IO.Path.Combine(temp.Path, "sel.txt");
        var m3u8Path = System.IO.Path.Combine(temp.Path, "sel.m3u8");
        var sc2Path  = System.IO.Path.Combine(temp.Path, "sel.sc2sel");

        await serializer.SaveTxtAsync(txtPath, snapshot);
        await serializer.SaveM3u8Async(m3u8Path, snapshot);
        await serializer.SaveSc2SelAsync(sc2Path, snapshot);

        var fromTxt  = await serializer.LoadTxtAsync(txtPath);
        var fromM3u8 = await serializer.LoadM3u8Async(m3u8Path);
        var fromSc2  = await serializer.LoadSc2SelAsync(sc2Path);

        foreach (var path in snapshot.RelativePaths)
        {
            Assert.True(fromTxt.Contains(path),  $"txt missing: {path}");
            Assert.True(fromM3u8.Contains(path), $"m3u8 missing: {path}");
            Assert.True(fromSc2.Contains(path),  $"sc2sel missing: {path}");
        }
    }

    [Fact]
    public async Task MixedSeparators_NormalizedOnLoad()
    {
        using var temp = new TempDirectory();
        var serializer = new SelectionSerializer();

        // Write a .txt file with backslash-separated paths manually
        var txtPath = System.IO.Path.Combine(temp.Path, "sel.txt");
        await System.IO.File.WriteAllLinesAsync(txtPath, ["folder\\sub\\file.mp3", "other\\track.flac"]);

        var loaded = await serializer.LoadTxtAsync(txtPath);

        Assert.True(loaded.Contains("folder/sub/file.mp3"));
        Assert.True(loaded.Contains("other/track.flac"));
    }
}
