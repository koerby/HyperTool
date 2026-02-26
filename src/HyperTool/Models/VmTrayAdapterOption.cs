namespace HyperTool.Models;

public sealed class VmTrayAdapterOption
{
    public string AdapterName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}
