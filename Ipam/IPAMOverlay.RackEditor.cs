using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GregModIPAM;

/// <summary>
/// Partial class extending IPAMOverlay with Rack Editor features:
/// - Auto-cabling (simple/redundant)
/// - Server power on/off
/// - Cabling visualization in rack diagram
/// </summary>
public static partial class IPAMOverlay
{
    // ── Auto-Cabling State ──
    private static bool _autoCablingModalOpen;
    private static CablingMode _autoCablingMode = CablingMode.Simple;
    private static AutoCablingEngine.CablingResult _autoCablingPreview;
    private static bool _autoCablingCreateGameCables;

    // ── Colors ──
    private static readonly Color PowerOnColor = new(0.18f, 0.78f, 0.38f, 0.95f);
    private static readonly Color PowerOffColor = new(0.78f, 0.22f, 0.18f, 0.95f);
    private static readonly Color CableLineColor = new(0.30f, 0.85f, 0.65f, 0.80f);
    private static readonly Color CableLineColorB = new(0.30f, 0.65f, 0.90f, 0.80f);

    // ──────────────────────────────────────────────
    //  Auto-Cabling Modal
    // ──────────────────────────────────────────────

    private static void DrawAutoCablingModal(float x0, float y, float cardW, string rackId)
    {
        if (!_autoCablingModalOpen) return;

        var modalW = Mathf.Min(480f, cardW - 40f);
        var modalH = 320f;
        var modalX = x0 + (cardW - modalW) * 0.5f;
        var modalY = y + 20f;

        // Backdrop
        DrawTintedRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

        var modalRect = new Rect(modalX, modalY, modalW, modalH);
        DrawTintedRect(modalRect, new Color(0.10f, 0.12f, 0.15f, 0.98f));

        // Border
        var borderC = new Color(0.35f, 0.55f, 0.75f, 0.9f);
        GUI.DrawTexture(new Rect(modalX, modalY, modalW, 2f), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderC, 0f, 0f);
        GUI.DrawTexture(new Rect(modalX, modalY + modalH - 2f, modalW, 2f), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderC, 0f, 0f);
        GUI.DrawTexture(new Rect(modalX, modalY, 2f, modalH), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderC, 0f, 0f);
        GUI.DrawTexture(new Rect(modalX + modalW - 2f, modalY, 2f, modalH), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderC, 0f, 0f);

        var mx = modalX + 16f;
        var my = modalY + 16f;
        var iw = modalW - 32f;

        GUI.Label(new Rect(mx, my, iw, 24f), "Auto-Verkabelung", _stSectionTitle);
        my += 30f;

        // Mode selection
        GUI.Label(new Rect(mx, my, 60f, 22f), "Modus:", _stMuted);
        if (ImguiButtonOnce(new Rect(mx + 64f, my, 100f, 24f), "Einfach", 95001,
            _autoCablingMode == CablingMode.Simple ? _stPrimaryBtn : _stMutedBtn))
        {
            _autoCablingMode = CablingMode.Simple;
            _autoCablingPreview = null;
        }

        if (ImguiButtonOnce(new Rect(mx + 170f, my, 100f, 24f), "Redundant", 95002,
            _autoCablingMode == CablingMode.Redundant ? _stPrimaryBtn : _stMutedBtn))
        {
            _autoCablingMode = CablingMode.Redundant;
            _autoCablingPreview = null;
        }

        my += 32f;

        // Preview / Plan
        if (_autoCablingPreview == null)
        {
            if (ImguiButtonOnce(new Rect(mx, my, 160f, 28f), "Vorschau berechnen", 95003, _stPrimaryBtn))
            {
                _autoCablingPreview = AutoCablingEngine.PlanCabling(rackId, _autoCablingMode);
            }
            my += 36f;
        }
        else
        {
            if (!_autoCablingPreview.Success)
            {
                GUI.Label(new Rect(mx, my, iw, 44f), _autoCablingPreview.ErrorMessage, _stMuted);
                my += 50f;
                if (ImguiButtonOnce(new Rect(mx, my, 120f, 24f), "Erneut", 95004, _stMutedBtn))
                {
                    _autoCablingPreview = null;
                }
            }
            else
            {
                GUI.Label(new Rect(mx, my, iw, 22f),
                    $"{_autoCablingPreview.ServerCount} Server, {_autoCablingPreview.NetworkDeviceCount} Netzwerkgeräte, {_autoCablingPreview.Connections.Count} Kabel",
                    _stTableCell);
                my += 26f;

                // Connection preview table
                var previewH = Mathf.Min(120f, _autoCablingPreview.Connections.Count * 18f + 4f);
                var previewRect = new Rect(mx, my, iw, previewH);
                DrawTintedRect(previewRect, new Color(0.06f, 0.08f, 0.10f, 0.6f));

                var scrollInner = new Rect(0f, 0f, iw - 18f, _autoCablingPreview.Connections.Count * 18f);
                _autoCablingScroll = GUI.BeginScrollView(previewRect, _autoCablingScroll, scrollInner);
                for (var i = 0; i < _autoCablingPreview.Connections.Count; i++)
                {
                    var c = _autoCablingPreview.Connections[i];
                    var srcName = FindMountDisplayName(rackId, c.SourceEntryId);
                    var tgtName = FindMountDisplayName(rackId, c.TargetEntryId);
                    GUI.Label(new Rect(4f, i * 18f, scrollInner.width, 18f),
                        $"{srcName}.{c.SourcePort} → {tgtName}.{c.TargetPort}", _stMuted);
                }
                GUI.EndScrollView();
                my += previewH + 8f;

                // Game cable option
                _autoCablingCreateGameCables = GUI.Toggle(new Rect(mx, my, iw, 22f),
                    _autoCablingCreateGameCables, " Game-Kabel erstellen (experimentell)");
                my += 28f;

                // Execute button
                if (ImguiButtonOnce(new Rect(mx, my, 180f, 28f), "Verkabelung erstellen", 95005, _stPrimaryBtn))
                {
                    AutoCablingEngine.ExecuteCabling(rackId, _autoCablingPreview, _autoCablingCreateGameCables);
                    _autoCablingModalOpen = false;
                    _autoCablingPreview = null;
                    ShowIpamToast("Auto-Verkabelung abgeschlossen.");
                    RecomputeContentHeight();
                }
            }
        }

        my += 36f;

        // Close button
        if (ImguiButtonOnce(new Rect(mx + modalW - 120f, modalY + modalH - 42f, 90f, 28f), "Schließen", 95006, _stMutedBtn))
        {
            _autoCablingModalOpen = false;
            _autoCablingPreview = null;
        }
    }

    private static Vector2 _autoCablingScroll;

    // ──────────────────────────────────────────────
    //  Power Panel (drawn below rack diagram)
    // ──────────────────────────────────────────────

    private static void DrawPowerPanel(float x0, ref float y, float cardW, string rackId,
        List<RackDiagramDevice> devices, UnifiedRackEntry entry)
    {
        if (entry?.Persisted == null) return;

        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "Power Management", _stSectionTitle);
        y += SectionTitleH + 4f;

        // Bulk buttons
        var serverMounts = entry.Persisted.Mounts
            .Where(m => string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var poweredOn = CablingDataStore.CountPoweredOn(rackId);
        var total = serverMounts.Count;

        GUI.Label(new Rect(x0, y, 200f, 22f), $"{poweredOn}/{total} Server eingeschaltet", _stTableCell);

        if (ImguiButtonOnce(new Rect(x0 + 210f, y, 100f, 22f), "Alle Ein", 96001, _stPrimaryBtn))
        {
            var cnt = ServerPowerController.PowerOnAll(rackId);
            ShowIpamToast($"{cnt} Server eingeschaltet.");
        }

        if (ImguiButtonOnce(new Rect(x0 + 316f, y, 100f, 22f), "Alle Aus", 96002, _stMutedBtn))
        {
            var cnt = ServerPowerController.PowerOffAll(rackId);
            ShowIpamToast($"{cnt} Server ausgeschaltet.");
        }

        y += 28f;

        // Per-device power table
        var colPWPos = 50f;
        var colPWName = cardW - colPWPos - 140f;
        var colPWStatus = 70f;
        var colPWBtn = 60f;

        GUI.Label(new Rect(x0, y, colPWPos, TableHeaderH), "U", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPWPos, y, colPWName, TableHeaderH), "Gerät", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPWPos + colPWName, y, colPWStatus, TableHeaderH), "Status", _stTableHeaderText);
        y += TableHeaderH;

        for (var i = 0; i < serverMounts.Count; i++)
        {
            var m = serverMounts[i];
            var isOn = CablingDataStore.IsPoweredOn(rackId, m.EntryId);
            var alt = i % 2 == 1;
            var row = new Rect(x0, y, cardW - 4f, TableRowH);

            if (Event.current.type == EventType.Repaint)
            {
                DrawTintedRect(row, alt ? new Color(0.06f, 0.08f, 0.10f, 0.5f) : new Color(0.04f, 0.05f, 0.06f, 0.35f));
            }

            GUI.Label(new Rect(x0 + 4f, y, colPWPos - 8f, TableRowH), m.StartU.ToString(), _stTableCell);
            GUI.Label(new Rect(x0 + colPWPos, y, colPWName - 4f, TableRowH), m.DisplayName ?? m.DeviceType, _stTableCell);

            // Power LED + label
            var ledX = x0 + colPWPos + colPWName + 4f;
            var ledY = y + (TableRowH - 10f) * 0.5f;
            DrawTintedRect(new Rect(ledX, ledY, 10f, 10f), isOn ? PowerOnColor : PowerOffColor);
            GUI.Label(new Rect(ledX + 14f, y, colPWStatus - 18f, TableRowH), isOn ? "ON" : "OFF", _stTableCell);

            // Toggle button
            var btnId = 96100 + Mathf.Abs(m.EntryId.GetHashCode() % 9000);
            var btnLabel = isOn ? "OFF" : "ON";
            if (ImguiButtonOnce(new Rect(x0 + colPWPos + colPWName + colPWStatus, y + 2f, colPWBtn, TableRowH - 4f),
                btnLabel, btnId, _stMutedBtn))
            {
                ServerPowerController.TryTogglePower(rackId, m);
                ShowIpamToast($"{m.DisplayName}: {(isOn ? "ausgeschaltet" : "eingeschaltet")}");
            }

            y += TableRowH;
        }
    }

    // ──────────────────────────────────────────────
    //  Cabling info in device table
    // ──────────────────────────────────────────────

    private static void DrawCablingInfoColumn(float x, float y, float width, string rackId, string entryId)
    {
        var connections = CablingDataStore.GetConnectionsForDevice(rackId, entryId);
        if (connections.Count == 0)
        {
            GUI.Label(new Rect(x, y, width, TableRowH), "—", _stMuted);
            return;
        }

        var label = $"{connections.Count} Kabel";
        GUI.Label(new Rect(x, y, width, TableRowH), label, _stTableCell);
    }

    // ──────────────────────────────────────────────
    //  Cabling lines in rack front diagram
    // ──────────────────────────────────────────────

    private static void DrawCablingLinesInDiagram(Rect rackBody, int totalU,
        List<RackDiagramDevice> devices, int[] effStart, string rackId)
    {
        var connections = CablingDataStore.GetConnections(rackId);
        if (connections.Count == 0) return;

        var tu = Mathf.Max(1, totalU);
        var cell = rackBody.height / tu;

        // Draw a small cable indicator dot on the right edge of each connected device
        var drawn = new HashSet<string>();
        foreach (var conn in connections)
        {
            // Source indicator
            if (!drawn.Contains(conn.SourceEntryId))
            {
                drawn.Add(conn.SourceEntryId);
                var srcIdx = devices.FindIndex(d => string.Equals(d.EntryId, conn.SourceEntryId, StringComparison.Ordinal));
                if (srcIdx >= 0)
                {
                    var srcDev = devices[srcIdx];
                    var srcU = effStart[srcIdx];
                    var srcH = Mathf.Max(1, srcDev.HeightU);
                    var srcY = rackBody.yMax - (srcU + srcH - 1) * cell + (srcH * cell * 0.5f) - 3f;
                    DrawTintedRect(new Rect(rackBody.xMax - 8f, srcY, 6f, 6f), CableLineColor);
                }
            }

            // Target indicator
            if (!drawn.Contains(conn.TargetEntryId))
            {
                drawn.Add(conn.TargetEntryId);
                var tgtIdx = devices.FindIndex(d => string.Equals(d.EntryId, conn.TargetEntryId, StringComparison.Ordinal));
                if (tgtIdx >= 0)
                {
                    var tgtDev = devices[tgtIdx];
                    var tgtU = effStart[tgtIdx];
                    var tgtH = Mathf.Max(1, tgtDev.HeightU);
                    var tgtY = rackBody.yMax - (tgtU + tgtH - 1) * cell + (tgtH * cell * 0.5f) - 3f;
                    DrawTintedRect(new Rect(rackBody.xMax - 8f, tgtY, 6f, 6f), CableLineColor);
                }
            }
        }
    }

    // ── Helper ──

    private static string FindMountDisplayName(string rackId, string entryId)
    {
        var rack = RackDataStore.FindById(rackId);
        if (rack?.Mounts == null) return "?";
        var mount = rack.Mounts.FirstOrDefault(m =>
            string.Equals(m.EntryId, entryId, StringComparison.Ordinal));
        return mount?.DisplayName ?? mount?.DeviceType ?? "?";
    }
}
