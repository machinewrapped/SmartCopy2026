using System.Text;

namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// Defines the canonical Phase 2 smoke dataset for repeatable verification.
/// Use <see cref="Seed"/> to populate a <see cref="MemoryFileSystemFixtureBuilder"/> in-process,
/// or <see cref="SeedTo"/> to write the same structure to a real directory on disk.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// {root}/
///   readme.txt            (real content, ~87 bytes)
///   hidden.txt            (real content; Hidden attribute set in both Seed and SeedTo)
///   documents/
///     report.txt          (real content, ~100 bytes)
///     notes.txt           (real content, ~52 bytes)
///     archive/
///       old-report.txt    (real content, ~69 bytes)
///   music/
///     artist1/
///       album1/
///         track01.mp3     (4 MB simulated)
///         track02.mp3     (4 MB simulated)
///       album2/
///         track01.flac    (8 MB simulated)
///     artist2/
///       single.mp3        (3 MB simulated)
/// </code>
/// </remarks>
internal static class SmokeDataset
{
    /// <summary>Total number of files seeded (hidden.txt included).</summary>
    public const int TotalFiles = 9;

    /// <summary>Number of directories seeded beneath the root (root itself excluded).</summary>
    public const int TotalDirectories = 7;

    /// <summary>
    /// Combined byte count of the four simulated music files (4 MB + 4 MB + 8 MB + 3 MB).
    /// Real-content files are negligible and excluded from this constant.
    /// </summary>
    public const long TotalSimulatedBytes = 19_922_944L;

    private const string ReadmeContent =
        "SmartCopy2026 Phase 2 smoke dataset — for repeatable automated and manual verification.";
    private const string HiddenContent =
        "This file has the Hidden attribute set on disk. In-memory it is a plain file.";
    private const string ReportContent =
        "Test report document. Lorem ipsum dolor sit amet, consectetur adipiscing elit.";
    private const string NotesContent =
        "Test notes. Short content for size testing.";
    private const string OldReportContent =
        "Archived test report. This is the old version of the report document.";

    /// <summary>
    /// Seeds the smoke dataset into a <see cref="MemoryFileSystemFixtureBuilder"/>.
    /// Music files are seeded as simulated (size only, no real content) for speed.
    /// </summary>
    public static void Seed(MemoryFileSystemFixtureBuilder builder)
    {
        builder
            .WithTextFile("/readme.txt", ReadmeContent)
            .WithTextFile("/hidden.txt", HiddenContent, FileAttributes.Hidden)
            .WithDirectory("/documents")
            .WithTextFile("/documents/report.txt", ReportContent)
            .WithTextFile("/documents/notes.txt", NotesContent)
            .WithDirectory("/documents/archive")
            .WithTextFile("/documents/archive/old-report.txt", OldReportContent)
            .WithDirectory("/music")
            .WithDirectory("/music/artist1")
            .WithDirectory("/music/artist1/album1")
            .WithSimulatedFile("/music/artist1/album1/track01.mp3", 4 * 1024 * 1024)
            .WithSimulatedFile("/music/artist1/album1/track02.mp3", 4 * 1024 * 1024)
            .WithDirectory("/music/artist1/album2")
            .WithSimulatedFile("/music/artist1/album2/track01.flac", 8 * 1024 * 1024)
            .WithDirectory("/music/artist2")
            .WithSimulatedFile("/music/artist2/single.mp3", 3 * 1024 * 1024);
    }

    /// <summary>
    /// Writes the smoke dataset to a real directory on disk.
    /// <paramref name="rootPath"/> must already exist.
    /// Sets <see cref="System.IO.FileAttributes.Hidden"/> on <c>hidden.txt</c>.
    /// Music files are written as zero-filled placeholders of the correct size.
    /// </summary>
    public static void SeedTo(string rootPath)
    {
        File.WriteAllText(Path.Combine(rootPath, "readme.txt"), ReadmeContent, Encoding.UTF8);
        var hiddenFile = Path.Combine(rootPath, "hidden.txt");
        File.WriteAllText(hiddenFile, HiddenContent, Encoding.UTF8);
        File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);

        var docs = Path.Combine(rootPath, "documents");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "report.txt"), ReportContent, Encoding.UTF8);
        File.WriteAllText(Path.Combine(docs, "notes.txt"), NotesContent, Encoding.UTF8);
        var archive = Path.Combine(docs, "archive");
        Directory.CreateDirectory(archive);
        File.WriteAllText(Path.Combine(archive, "old-report.txt"), OldReportContent, Encoding.UTF8);

        var album1 = Path.Combine(rootPath, "music", "artist1", "album1");
        var album2 = Path.Combine(rootPath, "music", "artist1", "album2");
        var artist2 = Path.Combine(rootPath, "music", "artist2");
        Directory.CreateDirectory(album1);
        Directory.CreateDirectory(album2);
        Directory.CreateDirectory(artist2);
        WriteZeroFile(Path.Combine(album1, "track01.mp3"), 4 * 1024 * 1024);
        WriteZeroFile(Path.Combine(album1, "track02.mp3"), 4 * 1024 * 1024);
        WriteZeroFile(Path.Combine(album2, "track01.flac"), 8 * 1024 * 1024);
        WriteZeroFile(Path.Combine(artist2, "single.mp3"), 3 * 1024 * 1024);
    }

    private static void WriteZeroFile(string path, long size)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.SetLength(size);
    }
}
