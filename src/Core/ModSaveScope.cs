using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GregModIPAM;

/// <summary>
/// Binds mod UserData JSON (IPAM prefixes, racks, etc.) to a single playthrough so a new game does not
/// inherit data from a deleted/old save. Scope is captured once after a gameplay scene loads.
/// </summary>
internal static class ModSaveScope
{
    private const string SubDir = "gregMod.IPAM";
    private const string BindingFileName = "save_binding.json";

    private static string _currentScopeId;
    private static bool _captured;
    private static bool _bindingChecked;

    internal static bool HasScope => _captured;

    internal static string CurrentScopeId => _currentScopeId ?? "";

    internal static void NotifySceneLoaded()
    {
        _captured = false;
        _bindingChecked = false;
        _currentScopeId = null;
        IPAMOverlay.ResetUiResourcesForSessionChange();
    }

    internal static void TickCapture()
    {
        if (_captured)
        {
            return;
        }

        try
        {
            if (MainGameManager.instance == null)
            {
                return;
            }

            var sb = new StringBuilder(128);
            sb.Append(SceneManager.GetActiveScene().name ?? "?");

            var servers = UnityEngine.Object.FindObjectsOfType<Server>();
            sb.Append("|srv:").Append(servers != null ? servers.Length : 0);

            var switches = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
            sb.Append("|sw:").Append(switches != null ? switches.Length : 0);

            try
            {
                var pm = PlayerManager.instance;
                var pc = pm != null ? pm.playerClass : null;
                if (pc != null)
                {
                    sb.Append("|m:").Append(pc.money);
                }
            }
            catch
            {
                // ignore
            }

            _currentScopeId = HashScope(sb.ToString());
            _captured = true;
        }
        catch
        {
            // ignore — stay uncaptured until gameplay is ready
        }
    }

    /// <summary>Returns true when mod JSON should be wiped before loading (new game / different save).</summary>
    internal static bool EnsureBindingChecked(out bool resetModUserData)
    {
        resetModUserData = false;
        if (_bindingChecked)
        {
            return _captured;
        }

        TickCapture();
        if (!_captured)
        {
            return false;
        }

        _bindingChecked = true;
        var binding = LoadBinding();
        if (binding != null
            && !string.IsNullOrEmpty(binding.ScopeId)
            && !string.Equals(binding.ScopeId, _currentScopeId, StringComparison.Ordinal))
        {
            resetModUserData = true;
            ModLogging.Msg(
                "gregMod.IPAM: detected a new game/save — resetting mod UserData (IPAM prefixes, racks, etc.).");
            WipeModUserDataFiles();
            IpamDataStore.ResetForNewSaveSession();
            RackDataStore.ResetForNewSaveSession();
            NamingConventionStore.ResetForNewSaveSession();
            CablingDataStore.ResetForNewSaveSession();
        }

        SaveBinding(_currentScopeId);
        return true;
    }

    internal static void WipeModUserDataFiles()
    {
        TryDelete(GetModFilePath("ipam_data.json"));
        TryDelete(GetModFilePath("rack_data.json"));
        TryDelete(GetModFilePath("naming_data.json"));
        TryDelete(GetModFilePath("cabling_data.json"));
    }

    private static void TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"gregMod.IPAM: could not delete {path}: {ex.Message}");
        }
    }

    private static string GetModFilePath(string fileName)
    {
        try
        {
            var dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                var rootDir = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(rootDir))
                {
                    return Path.Combine(rootDir, "UserData", SubDir, fileName);
                }
            }
        }
        catch
        {
            // fall through
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, SubDir, fileName);
    }

    private static string HashScope(string raw)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw ?? ""));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    private static string GetBindingPath()
    {
        try
        {
            var dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                var rootDir = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(rootDir))
                {
                    return Path.Combine(rootDir, "UserData", SubDir, BindingFileName);
                }
            }
        }
        catch
        {
            // fall through
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, SubDir, BindingFileName);
    }

    private static SaveBindingFile LoadBinding()
    {
        var path = GetBindingPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SaveBindingFile>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static void SaveBinding(string scopeId)
    {
        if (string.IsNullOrEmpty(scopeId))
        {
            return;
        }

        var path = GetBindingPath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var file = new SaveBindingFile { ScopeId = scopeId };
            File.WriteAllText(path, JsonSerializer.Serialize(file));
        }
        catch (Exception ex)
        {
            ModLogging.Warning("gregMod.IPAM: save_binding.json write failed: " + ex.Message);
        }
    }

    private sealed class SaveBindingFile
    {
        public string ScopeId { get; set; }
    }
}
