using System;
using System.Collections.ObjectModel;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.ViewModels;

public class DirectoryTreeViewModel : ViewModelBase
{
    public ObservableCollection<FileSystemNode> RootNodes { get; } = new();

    public DirectoryTreeViewModel()
    {
        // Stub data
        var rock = new FileSystemNode { Name = "Rock", IsDirectory = true, RelativePath = "Rock", CheckState = CheckState.Indeterminate };
        var classicRock = new FileSystemNode { Name = "Classic Rock", IsDirectory = true, RelativePath = "Rock/Classic Rock", CheckState = CheckState.Checked, Parent = rock };
        var beatles = new FileSystemNode { Name = "Beatles", IsDirectory = true, RelativePath = "Rock/Classic Rock/Beatles", CheckState = CheckState.Checked, Parent = classicRock };
        var rollingStones = new FileSystemNode { Name = "Rolling Stones", IsDirectory = true, RelativePath = "Rock/Classic Rock/Rolling Stones", CheckState = CheckState.Unchecked, Parent = classicRock };
        
        classicRock.Children.Add(beatles);
        classicRock.Children.Add(rollingStones);
        rock.Children.Add(classicRock);
        
        var metal = new FileSystemNode { Name = "Metal", IsDirectory = true, RelativePath = "Rock/Metal", CheckState = CheckState.Indeterminate, Parent = rock };
        rock.Children.Add(metal);
        
        var jazz = new FileSystemNode { Name = "Jazz", IsDirectory = true, RelativePath = "Jazz", CheckState = CheckState.Checked };
        var classical = new FileSystemNode { Name = "Classical", IsDirectory = true, RelativePath = "Classical", CheckState = CheckState.Unchecked };

        RootNodes.Add(rock);
        RootNodes.Add(jazz);
        RootNodes.Add(classical);
    }
}
