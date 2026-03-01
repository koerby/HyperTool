using HyperTool.Models;

namespace HyperTool.Services;

public interface IConfigService
{
    ConfigLoadResult LoadOrCreate(string configPath);

    bool TrySave(string configPath, HyperToolConfig config, out string? errorMessage);
}