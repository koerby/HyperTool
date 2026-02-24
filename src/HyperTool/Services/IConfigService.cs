using HyperTool.Models;

namespace HyperTool.Services;

public interface IConfigService
{
    ConfigLoadResult LoadOrCreate(string configPath);
}