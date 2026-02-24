namespace HyperTool.Models;

public sealed class VmDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string RuntimeState { get; set; } = "Unbekannt";

    public string RuntimeSwitchName { get; set; } = "-";
}