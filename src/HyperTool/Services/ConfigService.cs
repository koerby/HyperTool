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

                var couldWriteDefault = TryWriteConfig(configPath, defaultConfig);

                Log.Warning("Config file not found. Created default config at {ConfigPath}", configPath);
                return new ConfigLoadResult
                {
                    Config = defaultConfig,
                    ConfigPath = configPath,
                    IsGenerated = true,
                    Notice = couldWriteDefault
                        ? "Konfiguration fehlte und wurde als Beispiel erzeugt. Bitte HyperTool.config.json prüfen."
                        : "Konfiguration fehlte und konnte wegen fehlender Schreibrechte nicht gespeichert werden. HyperTool läuft mit In-Memory-Defaults."
                };
            }

            var raw = File.ReadAllText(configPath);
            var loaded = JsonSerializer.Deserialize<HyperToolConfig>(raw, SerializerOptions) ?? HyperToolConfig.CreateDefault();
            var (validated, wasUpdated, notice) = ValidateAndNormalize(loaded);

            if (wasUpdated)
            {
                var couldWriteValidated = TryWriteConfig(configPath, validated);
                if (couldWriteValidated)
                {
                    Log.Warning("Config was normalized and rewritten at {ConfigPath}", configPath);
                }
                else
                {
                    notice = string.IsNullOrWhiteSpace(notice)
                        ? "Konfiguration wurde korrigiert, konnte aber nicht gespeichert werden (fehlende Schreibrechte)."
                        : $"{notice} Konfiguration konnte nicht gespeichert werden (fehlende Schreibrechte).";
                }
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
            var couldWriteFallback = TryWriteConfig(configPath, fallbackConfig);

            return new ConfigLoadResult
            {
                Config = fallbackConfig,
                ConfigPath = configPath,
                IsGenerated = true,
                Notice = couldWriteFallback
                    ? "Konfiguration war ungültig und wurde auf ein Beispiel zurückgesetzt."
                    : "Konfiguration war ungültig und konnte wegen fehlender Schreibrechte nicht gespeichert werden. HyperTool läuft mit In-Memory-Defaults."
            };
        }
    }

    public bool TrySave(string configPath, HyperToolConfig config, out string? errorMessage)
    {
        try
        {
            var (validated, _, _) = ValidateAndNormalize(config);
            var success = TryWriteConfig(configPath, validated);

            if (success)
            {
                errorMessage = null;
                Log.Information("Config saved to {ConfigPath}", configPath);
                return true;
            }

            errorMessage = "Konfiguration konnte nicht gespeichert werden (Schreibrechte prüfen).";
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config save failed for {ConfigPath}", configPath);
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool TryWriteConfig(string configPath, HyperToolConfig config)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var json = JsonSerializer.Serialize(config, SerializerOptions);
            File.WriteAllText(configPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config write failed for {ConfigPath}", configPath);
            return false;
        }
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
        else
        {
            var normalizedVms = config.Vms
                .Where(vm => vm is not null)
                .Select(vm => new VmDefinition
                {
                    Name = vm.Name?.Trim() ?? string.Empty,
                    Label = vm.Label?.Trim() ?? string.Empty
                })
                .Where(vm => !string.IsNullOrWhiteSpace(vm.Name))
                .ToList();

            if (normalizedVms.Count != config.Vms.Count)
            {
                wasUpdated = true;
            }

            config.Vms = normalizedVms;
        }

        if (config.Vms.Count == 0)
        {
            config.Vms = HyperToolConfig.CreateDefault().Vms;
            wasUpdated = true;
            notices.Add("Ungültige VM-Einträge wurden entfernt; Beispiele wurden ergänzt.");
        }

        foreach (var vm in config.Vms)
        {
            vm.Name = vm.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(vm.Label))
            {
                vm.Label = vm.Name;
                wasUpdated = true;
            }
        }

        config.DefaultVmName = config.DefaultVmName?.Trim() ?? string.Empty;

        var vmExists = config.Vms.Any(vm => string.Equals(vm.Name, config.DefaultVmName, StringComparison.OrdinalIgnoreCase));
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
        config.Update ??= new UpdateSettings();

        if (string.IsNullOrWhiteSpace(config.Ui.WindowTitle))
        {
            config.Ui.WindowTitle = "HyperTool";
            wasUpdated = true;
        }

        if (string.IsNullOrWhiteSpace(config.Update.GitHubOwner))
        {
            config.Update.GitHubOwner = "koerby";
            wasUpdated = true;
        }

        if (string.IsNullOrWhiteSpace(config.Update.GitHubRepo))
        {
            config.Update.GitHubRepo = "hyperVswitcher";
            wasUpdated = true;
        }

        var notice = notices.Count == 0 ? null : string.Join(" ", notices);
        return (config, wasUpdated, notice);
    }
}