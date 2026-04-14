using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>Loads/saves <see cref="RouterRuntimeConfig"/> and <see cref="SwitchRuntimeConfig"/> keyed by <see cref="DeviceStableId"/>.</summary>
internal static class DeviceConfigPersistence
{
    private const int FileVersion = 1;
    private const string SubDir = "DHCPSwitches";
    private const string FileName = "saved_device_configs.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Dictionary<string, RouterRuntimeConfig> RouterSeeds = new();
    private static readonly Dictionary<string, SwitchRuntimeConfig> SwitchSeeds = new();
    private static bool _loaded;

    internal static void LoadSeedsFromDisk()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        RouterSeeds.Clear();
        SwitchSeeds.Clear();

        var path = GetConfigPath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<PersistedFile>(json, JsonOptions);
            if (file?.Routers != null)
            {
                foreach (var kv in file.Routers)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                    {
                        RouterSeeds[kv.Key] = CloneRouter(kv.Value);
                    }
                }
            }

            if (file?.Switches != null)
            {
                foreach (var kv in file.Switches)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                    {
                        SwitchSeeds[kv.Key] = CloneSwitch(kv.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"Device config load failed ({path}): {ex.Message}");
        }
    }

    internal static RouterRuntimeConfig TryTakeRouterSeed(string key)
    {
        if (string.IsNullOrEmpty(key) || !RouterSeeds.TryGetValue(key, out var cfg))
        {
            return null;
        }

        RouterSeeds.Remove(key);
        return CloneRouter(cfg);
    }

    internal static SwitchRuntimeConfig TryTakeSwitchSeed(string key)
    {
        if (string.IsNullOrEmpty(key) || !SwitchSeeds.TryGetValue(key, out var cfg))
        {
            return null;
        }

        SwitchSeeds.Remove(key);
        return CloneSwitch(cfg);
    }

    internal static bool TrySaveAll(
        IReadOnlyDictionary<string, RouterRuntimeConfig> routers,
        IReadOnlyDictionary<string, SwitchRuntimeConfig> switches)
    {
        var path = GetConfigPath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var file = new PersistedFile
            {
                Version = FileVersion,
                Routers = new Dictionary<string, RouterRuntimeConfig>(),
                Switches = new Dictionary<string, SwitchRuntimeConfig>(),
            };

            foreach (var kv in routers)
            {
                file.Routers[kv.Key] = CloneRouter(kv.Value);
            }

            foreach (var kv in switches)
            {
                file.Switches[kv.Key] = CloneSwitch(kv.Value);
            }

            var json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(path, json);
            ModLogging.Msg($"Saved device configs to {path} ({file.Routers.Count} routers, {file.Switches.Count} switches).");
            return true;
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"Device config save failed ({path}): {ex.Message}");
            return false;
        }
    }

    private static string GetConfigPath()
    {
        try
        {
            var dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                var root = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(root))
                {
                    return Path.Combine(root, "UserData", SubDir, FileName);
                }
            }
        }
        catch
        {
            // fall through
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, SubDir, FileName);
    }

    private static RouterRuntimeConfig CloneRouter(RouterRuntimeConfig src)
    {
        if (src == null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(src, JsonOptions);
        return JsonSerializer.Deserialize<RouterRuntimeConfig>(json, JsonOptions);
    }

    private static SwitchRuntimeConfig CloneSwitch(SwitchRuntimeConfig src)
    {
        if (src == null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(src, JsonOptions);
        return JsonSerializer.Deserialize<SwitchRuntimeConfig>(json, JsonOptions);
    }

    private sealed class PersistedFile
    {
        public int Version { get; set; }
        public Dictionary<string, RouterRuntimeConfig> Routers { get; set; }
        public Dictionary<string, SwitchRuntimeConfig> Switches { get; set; }
    }
}
