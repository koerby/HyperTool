namespace HyperTool.Models;

public sealed class ConfigLoadResult
{
    public required HyperToolConfig Config { get; init; }

    public bool IsGenerated { get; init; }

    public bool HasValidationFixes { get; init; }

    public string? Notice { get; init; }

    public string ConfigPath { get; init; } = string.Empty;
}