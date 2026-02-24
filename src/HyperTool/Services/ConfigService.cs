using HyperTool.Models;
using Serilog;
using System.IO;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfigLoadResult LoadOrCreate(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                var defaultConfig = HyperToolConfig.CreateDefault();
                WriteConfig(configPath, defaultConfig);

                Log.Warning("Config file not found. Created default config at {ConfigPath}", configPath);
                return new ConfigLoadResult
                {
                    Config = defaultConfig,
                    ConfigPath = configPath,
                    IsGenerated = true,
                    Notice = "Konfiguration fehlte und wurde als Beispiel erzeugt. Bitte HyperTool.config.json prüfen."
                };
            }

            var raw = File.ReadAllText(configPath);
            var loaded = JsonSerializer.Deserialize<HyperToolConfig>(raw, SerializerOptions) ?? HyperToolConfig.CreateDefault();
            var (validated, wasUpdated, notice) = ValidateAndNormalize(loaded);

            if (wasUpdated)
            {
                WriteConfig(configPath, validated);
                Log.Warning("Config was normalized and rewritten at {ConfigPath}", configPath);
            }

            return new ConfigLoadResult
            {
                Config = validated,
                ConfigPath = configPath,
                HasValidationFixes = wasUpdated,
                Notice = notice
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load config. Recreating default config at {ConfigPath}", configPath);

            var fallbackConfig = HyperToolConfig.CreateDefault();
            WriteConfig(configPath, fallbackConfig);

            return new ConfigLoadResult
            {
                Config = fallbackConfig,
                ConfigPath = configPath,
                IsGenerated = true,
                Notice = "Konfiguration war ungültig und wurde auf ein Beispiel zurückgesetzt."
            };
        }
    }

    private static void WriteConfig(string configPath, HyperToolConfig config)
    {
        var directoryPath = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(configPath, json);
    }

    private static (HyperToolConfig Config, bool WasUpdated, string? Notice) ValidateAndNormalize(HyperToolConfig config)
    {
        var wasUpdated = false;
        var notices = new List<string>();

        if (config.Vms is null || config.Vms.Count == 0)
        {
            config.Vms = HyperToolConfig.CreateDefault().Vms;
            wasUpdated = true;
            notices.Add("VM-Liste war leer und wurde mit Beispieldaten ergänzt.");
        }

        foreach (var vm in config.Vms)
        {
            if (string.IsNullOrWhiteSpace(vm.Label))
            {
                vm.Label = vm.Name;
                wasUpdated = true;
            }
        }

        var vmExists = config.Vms.Any(vm => vm.Name.Equals(config.DefaultVmName, StringComparison.OrdinalIgnoreCase));
        if (!vmExists)
        {
            config.DefaultVmName = config.Vms[0].Name;
            wasUpdated = true;
            notices.Add("DefaultVmName war ungültig und wurde auf die erste VM gesetzt.");
        }

        if (string.IsNullOrWhiteSpace(config.DefaultSwitchName))
        {
            config.DefaultSwitchName = "Default Switch";
            wasUpdated = true;
            notices.Add("DefaultSwitchName war leer und wurde auf 'Default Switch' gesetzt.");
        }

        if (string.IsNullOrWhiteSpace(config.VmConnectComputerName))
        {
            config.VmConnectComputerName = "localhost";
            wasUpdated = true;
        }

        config.Hns ??= new HnsSettings();
        config.Ui ??= new UiSettings();

        if (string.IsNullOrWhiteSpace(config.Ui.WindowTitle))
        {
            config.Ui.WindowTitle = "HyperTool";
            wasUpdated = true;
        }

        var notice = notices.Count == 0 ? null : string.Join(" ", notices);
        return (config, wasUpdated, notice);
    }
}