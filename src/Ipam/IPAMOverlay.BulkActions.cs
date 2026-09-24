using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace GregModIPAM
{
    // Checkbox multi-select + bulk actions for server lists
    // (devices, IPs, customer servers). Sorting still via
    // clickable column headers (▲▼) of each table.
    public static partial class IPAMOverlay
    {
        // ── Checkbox ─────────────────────────────────────────────────────────

        internal static Rect GetServerCheckboxRect(Rect rowRect, float rightReserve)
        {
            const float boxW = 22f;
            var boxH = Mathf.Max(20f, rowRect.height - 6f);
            return new Rect(
                rowRect.xMax - rightReserve - 6f - boxW,
                rowRect.y + (rowRect.height - boxH) * 0.5f,
                boxW,
                boxH);
        }

        internal static Rect UnionRects(Rect a, Rect b)
        {
            var xMin = Mathf.Min(a.xMin, b.xMin);
            var yMin = Mathf.Min(a.yMin, b.yMin);
            var xMax = Mathf.Max(a.xMax, b.xMax);
            var yMax = Mathf.Max(a.yMax, b.yMax);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        internal static void DrawServerRowCheckbox(Rect rowRect, Server server, float rightReserve)
        {
            var box = GetServerCheckboxRect(rowRect, rightReserve);
            if (server == null) return;

            int id = 0;
            try { id = server.GetInstanceID(); } catch { return; }

            bool selected = false;
            try { selected = _selectedServerInstanceIds.Contains(id); } catch { }

            var next = ImguiToggleOnce(box, selected, 87000 + Math.Abs(id) % 5000, GUIContent.none);
            if (next == selected) return;
            try
            {
                if (next) _selectedServerInstanceIds.Add(id);
                else _selectedServerInstanceIds.Remove(id);
                _serverRangeAnchorInstanceId = id;
                UpdateAnchorServerForDetail();
                DHCPManager.ClearLastSetIpError();
            }
            catch { }
        }

        // ── Bulk-Bar ─────────────────────────────────────────────────────────

        internal static float ServerBulkBarHeight()
        {
            try
            {
                return _selectedServerInstanceIds.Count > 0 ? 30f : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        internal static void DrawServerBulkBar(ref float y, float x0, float width)
        {
            int count = 0;
            try { count = _selectedServerInstanceIds.Count; } catch { }
            if (count <= 0) return;

            GUI.Label(new Rect(x0, y + 3f, 150f, 22f), $"{count} selected:", _stTableCell);

            var bx = x0 + 154f;
            if (ImguiButtonOnce(new Rect(bx, y, 110f, 24f), "Assign DHCP", 87100, _stPrimaryBtn))
            {
                BulkDhcpAssignSelected();
            }

            bx += 116f;
            if (ImguiButtonOnce(new Rect(bx, y, 110f, 24f), "Technician", 87101, _stMutedBtn))
            {
                BulkTechnicianSelected();
            }

            bx += 116f;
            if (ImguiButtonOnce(new Rect(bx, y, 110f, 24f), "Deselect", 87102, _stMutedBtn))
            {
                try
                {
                    _selectedServerInstanceIds.Clear();
                    _serverRangeAnchorInstanceId = -1;
                    UpdateAnchorServerForDetail();
                    RecomputeContentHeight();
                }
                catch { }
            }

            y += 30f;
        }

        private static List<Server> SelectedServersAlive()
        {
            var result = new List<Server>();
            HashSet<int> ids = null;
            try { ids = new HashSet<int>(_selectedServerInstanceIds); } catch { }
            if (ids == null || ids.Count == 0) return result;
            Server[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Server>(); } catch { }
            if (all == null) return result;
            foreach (var s in all)
            {
                if (s == null) continue;
                try
                {
                    if (ids.Contains(s.GetInstanceID())) result.Add(s);
                }
                catch { }
            }

            return result;
        }

        private static void BulkDhcpAssignSelected()
        {
            var servers = SelectedServersAlive();
            if (servers.Count == 0) return;
            Server[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Server>(); } catch { }
            var ok = 0;
            foreach (var server in servers)
            {
                try
                {
                    var ip = DHCPManager.GetNextFreeIpForServer(server, all);
                    if (string.IsNullOrEmpty(ip)) continue;
                    if (DHCPManager.SetServerIP(server, ip, skipUsableListCheck: true)) ok++;
                }
                catch (Exception ex)
                {
                    ModLogging.Warning($"Bulk DHCP failed: {ex.GetBaseException().Message}");
                }
            }

            try { ShowIpamToast($"DHCP: {ok}/{servers.Count} assigned."); } catch { }
            try { InvalidateDeviceCache(); } catch { }
            try { RecomputeContentHeight(); } catch { }
        }

        private static void BulkTechnicianSelected()
        {
            var servers = SelectedServersAlive();
            if (servers.Count == 0) return;
            var ok = 0;
            foreach (var server in servers)
            {
                try
                {
                    if (DeviceInventoryReflection.TrySendTechnician(server)) ok++;
                }
                catch { }
            }

            try { ShowIpamToast($"Technician to {ok}/{servers.Count} devices."); } catch { }
        }
    }
}
