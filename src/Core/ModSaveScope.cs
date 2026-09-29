using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GregModIPAM;

/// <summary>
/// Diagnostic session marker for mod UserData JSON (IPAM prefixes, racks, etc.).
/// Mod data is intentionally persistent across saves/sessions and is NEVER
/// auto-wiped: the previous scope hash (scene + device counts + money) changed
/// on normal progress (earn money, buy a server) and therefore deleted the
/// user's IPAM data on the next load of the SAME save. See issue: "log back in,
/// everything done is gone". Use <see cref="TryResetAllModData"/> for an
/// explicit user-triggered reset instead.
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
        // Do NOT invalidate already-loaded store data on scene change.
        // Only refresh the diagnostic marker and UI caches.
        _bindingChecked = false;
        try { IPAMOverlay.ResetUiResourcesForSessionChange(); } catch { }
    }

    internal static void TickCapture()
    {
        if (_captured)
        {
            return;
        }

        try
        {
            // Stable-ish diagnostic only: scene name. Device counts and money
            // are deliberately excluded — they change during normal play and
            // must never gate loading or trigger a wipe.
            string sceneName = "?";
            try { sceneName = SceneManager.GetActiveScene().name ?? "?"; }
            catch { }

            _currentScopeId = HashScope("scene:" + sceneName);
            _captured = true;
        }
        catch
        {
            // ignore — retry next tick
        }
    }

    /// <summary>
    /// Always returns true once a diagnostic scope is available. Never wipes.
    /// Kept signature-compatible: out value is always false.
    /// </summary>
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
            // Capture is scene-only now, so this is rare (very early boot).
            // Return true anyway so stores load from disk instead of queuing
            // writes into a deferred empty root that would later clobber the
            // real file on first save.
            _bindingChecked = true;
            return true;
        }

        _bindingChecked = true;
        try
        {
            var binding = LoadBinding();
            if (binding == null || !string.Equals(binding.ScopeId, _currentScopeId, StringComparison.Ordinal))
            {
                SaveBinding(_currentScopeId);
            }
        }
        catch
        {
            // diagnostics only — never block loading
        }

        return true;
    }

    /// <summary>
    /// Sanitized current save name (or null outside a loaded save). Backs
    /// per-save namespacing of mod JSON: each save diverges from the shared
    /// global files instead of overwriting them.
    /// </summary>
    internal static string CurrentSaveKey()
    {
        try
        {
            string raw = null;
            try { raw = SaveSystem.loadSaveName; } catch { }
            if (string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var inst = SaveData.instance;
                    if (inst != null) raw = inst.nameOfSave;
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw.Trim())
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            var key = sb.ToString().Trim();
            if (key.Length > 64) key = key.Substring(0, 64);
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch { return null; }
    }

    /// <summary>Write path for a mod file: per-save namespace when a save is loaded, else global.</summary>
    internal static string ScopedPath(string globalPath)
    {
        try
        {
            var key = CurrentSaveKey();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(globalPath)) return globalPath;
            var dir = Path.GetDirectoryName(globalPath);
            var file = Path.GetFileName(globalPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return globalPath;
            return Path.Combine(dir, "saves", key, file);
        }
        catch { return globalPath; }
    }

    /// <summary>
    /// Load path: namespaced file when present, otherwise the global legacy
    /// file (soft migration — globals are never moved or deleted).
    /// </summary>
    internal static string LoadPath(string globalPath)
    {
        try
        {
            var scoped = ScopedPath(globalPath);
            if (!string.Equals(scoped, globalPath, StringComparison.Ordinal)
                && (File.Exists(scoped) || File.Exists(scoped + ".bak")))
                return scoped;
        }
        catch { }
        return globalPath;
    }

    /// <summary>Explicit user-triggered reset (e.g. UI button). The only path that deletes mod UserData.</summary>
    internal static bool TryResetAllModData(out string error)
    {
        error = null;
        try
        {
            WipeModUserDataFiles();
            IpamDataStore.ResetForNewSaveSession();
            RackDataStore.ResetForNewSaveSession();
            NamingConventionStore.ResetForNewSaveSession();
            CablingDataStore.ResetForNewSaveSession();
            ModLogging.Msg("gregMod.IPAM: mod UserData was reset by user request.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static void WipeModUserDataFiles()
    {
        // Wipe both the global legacy files and the current save's namespaced
        // copies (plus their .bak files) — the only path that deletes mod data.
        foreach (var name in new[] { "ipam_data.json", "rack_data.json", "naming_data.json", "cabling_data.json" })
        {
            var global = GetModFilePath(name);
            TryDelete(global);
            TryDelete(global + ".bak");
            var scoped = ScopedPath(global);
            if (!string.Equals(scoped, global, StringComparison.Ordinal))
            {
                TryDelete(scoped);
                TryDelete(scoped + ".bak");
            }
        }
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
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file));
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
