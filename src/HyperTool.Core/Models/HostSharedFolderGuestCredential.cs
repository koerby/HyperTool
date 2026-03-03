namespace HyperTool.Models;

public sealed class HostSharedFolderGuestCredential
{
    public bool Available { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public string GroupPrincipal { get; set; } = string.Empty;

    public string HostName { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}
