namespace HyperTool.Models;

public sealed class VmDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    public string RuntimeState { get; set; } = "Unbekannt";

    public string RuntimeSwitchName { get; set; } = "-";

    public string TrayAdapterName { get; set; } = string.Empty;

    public override string ToString() => DisplayLabel;
}