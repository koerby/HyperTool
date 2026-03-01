namespace HyperTool.Models;

public sealed class HyperVCheckpointInfo
{
    public string Id { get; set; } = string.Empty;

    public string ParentId { get; set; } = string.Empty;

    public bool IsCurrent { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime Created { get; set; }

    public string Type { get; set; } = string.Empty;
}