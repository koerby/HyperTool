namespace HyperTool.Models;

public sealed class HyperVVmNetworkAdapterInfo
{
    public string Name { get; set; } = string.Empty;

    public string SwitchName { get; set; } = string.Empty;

    public string MacAddress { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Network Adapter" : Name;

    public override string ToString() => DisplayName;
}