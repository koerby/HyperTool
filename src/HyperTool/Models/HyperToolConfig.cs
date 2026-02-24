namespace HyperTool.Models;

public sealed class HyperToolConfig
{
    public string DefaultVmName { get; set; } = "DER005Z085000_W10_FS20";

    public List<VmDefinition> Vms { get; set; } =
    [
        new VmDefinition
        {
            Name = "DER005Z085000_W10_FS20",
            Label = "FS20"
        },
        new VmDefinition
        {
            Name = "DER005Z054370_W10_XWP_v6.3SP3_ABT_v6.0_MR2025_03",
            Label = "XWP/ABT MR2025_03"
        }
    ];

    public string DefaultSwitchName { get; set; } = "Default Switch";

    public string VmConnectComputerName { get; set; } = "localhost";

    public HnsSettings Hns { get; set; } = new();

    public UiSettings Ui { get; set; } = new();

    public static HyperToolConfig CreateDefault() => new();
}

public sealed class HnsSettings
{
    public bool Enabled { get; set; } = true;

    public bool AutoRestartAfterDefaultSwitch { get; set; } = true;

    public bool AutoRestartAfterAnyConnect { get; set; }
}

public sealed class UiSettings
{
    public string WindowTitle { get; set; } = "HyperTool";

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;
}