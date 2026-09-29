using System;
using System.IO;

namespace GregModIPAM;

/// <summary>
/// Crash-safe JSON persistence: writes go to path.tmp + atomic move, keeping
/// path.bak of the last good state. Readers fall back to the backup so a torn
/// write never silently resets mod data to empty.
/// </summary>
internal static class AtomicFile
{
    internal static void WriteAllText(string path, string content)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var tmp = path + ".tmp";
        var bak = path + ".bak";
        try
        {
            File.WriteAllText(tmp, content ?? "");
            try
            {
                if (File.Exists(path))
                {
                    File.Copy(path, bak, true);
                }
            }
            catch
            {
                // backup best-effort
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }

            File.Move(tmp, path);
        }
        catch
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch { }

            throw;
        }
    }

    internal static bool TryReadAllTextWithBackup(string path, out string content)
    {
        content = null;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                content = File.ReadAllText(path);
                return true;
            }
            catch
            {
                // fall through to backup
            }
        }

        var bak = (path ?? "") + ".bak";
        if (File.Exists(bak))
        {
            try
            {
                content = File.ReadAllText(bak);
                ModLogging.Warning($"gregMod.IPAM: {path} unreadable, recovered from backup.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"gregMod.IPAM: backup {bak} unreadable: {ex.Message}");
            }
        }

        return false;
    }
}
