namespace HyperTool.Models;

public sealed class ImportVmResult
{
    public string VmName { get; set; } = string.Empty;

    public bool RenamedDueToConflict { get; set; }

    public string OriginalName { get; set; } = string.Empty;
}
