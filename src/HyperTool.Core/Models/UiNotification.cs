namespace HyperTool.Models;

public sealed class UiNotification
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public string Message { get; init; } = string.Empty;

    public string Level { get; init; } = "Info";
}