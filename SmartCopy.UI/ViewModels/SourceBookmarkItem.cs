namespace SmartCopy.UI.ViewModels;

public record SourceBookmarkItem(string Path, bool IsBookmark)
{
    public override string ToString() => Path;
}
