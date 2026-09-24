using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace GregModIPAM
{
    // Tech dispatch from IPAM: per broken device (row button) or
    // bulk (all broken). Uses GameTechnicianDispatch (vanilla paths).
    public static partial class IPAMOverlay
    {
        private static string _techFeedback = "";
        private static float _techFeedbackUntil;

        private static bool ServerNeedsWork(Server server)
        {
            if (server == null) return false;
            try
            {
                if (server.isBroken) return true;
            }
            catch { }

            try
            {
                if (DeviceInventoryReflection.AppearsPastOrCriticalEol(server)) return true;
            }
            catch { }

            return false;
        }

        private static bool SwitchNeedsWork(NetworkSwitch sw)
        {
            if (sw == null) return false;
            try
            {
                if (DeviceInventoryReflection.AppearsPastOrCriticalEol(sw)) return true;
            }
            catch { }

            return false;
        }

        private static List<Server> CollectBrokenServers()
        {
            var result = new List<Server>();
            Server[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Server>(); } catch { }
            if (all == null) return result;
            foreach (var s in all)
            {
                if (s == null) continue;
                try
                {
                    var go = s.gameObject;
                    if (go == null || !go.scene.IsValid() || !go.scene.isLoaded) continue;
                }
                catch { continue; }

                try
                {
                    if (ServerNeedsWork(s)) result.Add(s);
                }
                catch { }
            }

            return result;
        }

        private static List<NetworkSwitch> CollectBrokenSwitches()
        {
            var result = new List<NetworkSwitch>();
            NetworkSwitch[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>(); } catch { }
            if (all == null) return result;
            foreach (var sw in all)
            {
                if (sw == null) continue;
                try
                {
                    var go = sw.gameObject;
                    if (go == null || !go.scene.IsValid() || !go.scene.isLoaded) continue;
                }
                catch { continue; }

                try
                {
                    if (SwitchNeedsWork(sw)) result.Add(sw);
                }
                catch { }
            }

            return result;
        }

        private static void TechFeedback(string message)
        {
            _techFeedback = message ?? "";
            try { _techFeedbackUntil = Time.unscaledTime + 6f; } catch { _techFeedbackUntil = 0f; }
            try { ShowIpamToast(message); } catch { }
        }

        private static bool DispatchTechnician(UnityEngine.Object device, string label)
        {
            if (device == null) return false;
            try
            {
                if (DeviceInventoryReflection.TrySendTechnician(device))
                {
                    TechFeedback($"Technician dispatched ({label}).");
                    return true;
                }
            }
            catch (Exception ex)
            {
                TechFeedback($"Dispatch failed: {ex.GetBaseException().Message}");
                return false;
            }

            TechFeedback("Dispatch failed: no game dispatch path worked.");
            return false;
        }

        // Row button left of power toggle (only when work needed).
        internal static void DrawServerTechButton(Rect rowRect, Server server)
        {
            if (server == null) return;
            bool needsWork = false;
            try { needsWork = ServerNeedsWork(server); } catch { return; }
            if (!needsWork) return;

            var btnW = 52f;
            var btnH = Mathf.Max(20f, rowRect.height - 6f);
            var rect = new Rect(rowRect.xMax - 64f - 8f - 6f - btnW, rowRect.y + (rowRect.height - btnH) * 0.5f, btnW, btnH);
            int key = 88000;
            try { key = 88000 + Math.Abs(server.GetInstanceID()) % 5000; } catch { }
            if (ImguiButtonOnce(rect, "Tech", key, _stMutedBtn))
            {
                string label = "";
                try { label = DeviceInventoryReflection.GetDisplayName(server) ?? ""; } catch { }
                DispatchTechnician(server, label);
            }
        }

        // Bulk panel below server table: status + "all broken".
        internal static void DrawTechnicianPanel(ref float y, float x0, float cardW)
        {
            int techCount = -1;
            int queued = -1;
            try
            {
                var tm = TechnicianManager.instance;
                if (tm != null)
                {
                    try
                    {
                        var list = tm.technicians;
                        if (list != null) techCount = list.Count;
                    }
                    catch { }

                    try { queued = tm.QueuedJobCount; } catch { }
                }
            }
            catch { }

            GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "Technicians", _stSectionTitle);
            y += SectionTitleH + 4f;

            var status = techCount < 0
                ? "Technician system not found in this scene."
                : $"Technicians: {techCount}   ·   queued jobs: {(queued < 0 ? "?" : queued.ToString())}";
            GUI.Label(new Rect(x0, y, cardW, 20f), status, _stMuted);
            y += 22f;

            if (ImguiButtonOnce(new Rect(x0, y, 220f, 24f), "Send to all broken devices", 88500, _stPrimaryBtn))
            {
                var servers = CollectBrokenServers();
                var switches = CollectBrokenSwitches();
                var sent = 0;
                foreach (var s in servers)
                {
                    try
                    {
                        if (DeviceInventoryReflection.TrySendTechnician(s)) sent++;
                    }
                    catch { }
                }

                foreach (var sw in switches)
                {
                    try
                    {
                        if (DeviceInventoryReflection.TrySendTechnician(sw)) sent++;
                    }
                    catch { }
                }

                TechFeedback(sent > 0
                    ? $"Technician dispatched to {sent} device(s)."
                    : "No broken devices found.");
                try { InvalidateDeviceCache(); } catch { }
            }

            y += 28f;

            if (!string.IsNullOrEmpty(_techFeedback))
            {
                bool expired = false;
                try { expired = Time.unscaledTime > _techFeedbackUntil && _techFeedbackUntil > 0f; } catch { }
                if (!expired)
                {
                    GUI.Label(new Rect(x0, y, cardW, 20f), _techFeedback, _stTableCell);
                    y += 22f;
                }
            }
        }

        internal static float TechnicianPanelHeight()
        {
            // Title + status + button + optional feedback.
            return SectionTitleH + 4f + 22f + 28f + 24f;
        }
    }
}
