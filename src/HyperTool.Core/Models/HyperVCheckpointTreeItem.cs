using System.Collections.ObjectModel;

namespace HyperTool.Models;

public sealed class HyperVCheckpointTreeItem
{
    public HyperVCheckpointInfo Checkpoint { get; set; } = new();

    public ObservableCollection<HyperVCheckpointTreeItem> Children { get; } = [];

    public bool IsLatest { get; set; }

    public bool IsCurrent => Checkpoint.IsCurrent;

    public string Name => Checkpoint.Name;

    public DateTime Created => Checkpoint.Created;

    public string Type => Checkpoint.Type;
}
