namespace HyperTool.Models;

public sealed class HostSharedFolderDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public string ShareName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool ReadOnly { get; set; }
}
