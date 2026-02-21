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
}

