using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DHCPSwitches;

// User-defined racks (rack_data.json) + optional scene-detected layouts (AssetManagementDeviceLine).

public static partial class IPAMOverlay
{
    /// <summary>Front-view diagram height in px — fixed so it does not stretch with the IPAM window.</summary>
    private const float RackDiagramFixedHeight = 560f;

    private static string _racksTabSelectedUnifiedId = "";
    private static string _racksTabDrilledUnifiedId = "";
    private static string _racksLastUnifiedId = "";
    private static int _rackDiscoveredFrame = -1;
    private static List<RackLayoutHelper.RackInfo> _rackDiscoveredCache;

    private static string _rackFormNewName = "New rack";
    private static string _rackRenameDraft = "";
    private static string _rackMountStartU = "1";
    private static string _rackPatchLabelDraft = "Patch panel";
    private static string _rackMountServerSearchBuf = "";
    private static string _rackMountSwitchSearchBuf = "";
    /// <summary>Scene server index in <see cref="SortedServersBuffer"/>, or -1 if none selected.</summary>
    private static int _rackMountPickIdx = -1;

    /// <summary>Scene switch index in <see cref="SortedSwitchesBuffer"/>, or -1 if none selected.</summary>
    private static int _rackMountSwitchPickIdx = -1;

    private static Vector2 _rackMountServerListScroll;
    private static Vector2 _rackMountSwitchListScroll;

    /// <summary>Last drawn pick-list viewport (GUI coords); used to deselect when clicking outside the list.</summary>
    private static Rect _rackMountServerPickViewportLast;

    private static Rect _rackMountSwitchPickViewportLast;

    private static GUIStyle _rackPickRowLabelStyle;
    private static GUIStyle _rackDiagramUnitLabelStyle;
    /// <summary>0 server, 1 switch, 2 router, 3 patch panel.</summary>
    private static int _rackAddMountCategory;

    private static readonly Color RackDiagramSwitchFill = new(0.93f, 0.93f, 0.94f, 0.96f);
    private static readonly Color RackDiagramRouterFill = new(0.55f, 0.56f, 0.58f, 0.96f);
    private static readonly Color RackDiagramPatchFill = new(0.06f, 0.06f, 0.07f, 0.98f);

    private sealed class UnifiedRackEntry
    {
        public string UnifiedId;
        public bool IsPersistedEditable;
        public RackDefinition Persisted;
        public RackLayoutHelper.RackInfo SceneCopy;
    }

    private sealed class RackDiagramDevice
    {
        public string EntryId;
        public int StartU;
        public int HeightU;
        public string DisplayName;
        public string TypeLabel;
        public Color FillColor;
        public Color TextColor;
    }

    private static List<RackLayoutHelper.RackInfo> GetDiscoveredRackCache()
    {
        if (Time.frameCount != _rackDiscoveredFrame)
        {
            RackLayoutHelper.BuildSceneRackLayout(out _rackDiscoveredCache);
            _rackDiscoveredFrame = Time.frameCount;
        }

        return _rackDiscoveredCache ?? new List<RackLayoutHelper.RackInfo>();
    }

    private static List<UnifiedRackEntry> BuildUnifiedRackList()
    {
        var discovered = GetDiscoveredRackCache();
        var persisted = RackDataStore.GetRacks().OrderBy(static r => r.DisplayName ?? "", StringComparer.OrdinalIgnoreCase).ToList();
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in persisted)
        {
            if (!string.IsNullOrEmpty(p.DiscoveredSourceKey))
            {
                linked.Add(p.DiscoveredSourceKey);
            }
        }

        var list = new List<UnifiedRackEntry>();
        foreach (var p in persisted)
        {
            RackLayoutHelper.RackInfo scene = null;
            if (!string.IsNullOrEmpty(p.DiscoveredSourceKey))
            {
                scene = discovered.FirstOrDefault(d => string.Equals(d.Key, p.DiscoveredSourceKey, StringComparison.Ordinal));
            }

            list.Add(
                new UnifiedRackEntry
                {
                    UnifiedId = "p:" + p.Id,
                    IsPersistedEditable = true,
                    Persisted = p,
                    SceneCopy = scene,
                });
        }

        foreach (var d in discovered)
        {
            if (linked.Contains(d.Key))
            {
                continue;
            }

            list.Add(
                new UnifiedRackEntry
                {
                    UnifiedId = "d:" + d.Key,
                    IsPersistedEditable = false,
                    Persisted = null,
                    SceneCopy = d,
                });
        }

        return list;
    }

    private static string ResolveMountDisplayName(RackMountRecord m)
    {
        if (m == null)
        {
            return "—";
        }

        var dt = m.DeviceType ?? RackDeviceTypes.Server;
        if (string.Equals(dt, RackDeviceTypes.PatchPanel, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(m.PatchLabel) ? "Patch panel" : m.PatchLabel.Trim();
        }

        if (string.Equals(dt, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
        {
            EnsureSortedServers();
            var sid = m.SceneInstanceId != 0 ? m.SceneInstanceId : m.ServerInstanceId;
            var srv = SortedServersBuffer.FirstOrDefault(s =>
            {
                try
                {
                    return s != null && s.GetInstanceID() == sid;
                }
                catch
                {
                    return false;
                }
            });
            return srv != null
                ? DeviceInventoryReflection.GetDisplayName(srv)
                : $"Missing server (#{sid})";
        }

        EnsureSortedSwitches();
        var iid = m.SceneInstanceId;
        var sw = SortedSwitchesBuffer.FirstOrDefault(w =>
        {
            try
            {
                return w != null && w.GetInstanceID() == iid;
            }
            catch
            {
                return false;
            }
        });
        return sw != null
            ? DeviceInventoryReflection.GetDisplayName(sw)
            : $"Missing network device (#{iid})";
    }

    private static string ShortMountTypeLabel(string deviceType)
    {
        if (string.Equals(deviceType, RackDeviceTypes.Switch, StringComparison.OrdinalIgnoreCase))
        {
            return "Switch";
        }

        if (string.Equals(deviceType, RackDeviceTypes.Router, StringComparison.OrdinalIgnoreCase))
        {
            return "Router";
        }

        if (string.Equals(deviceType, RackDeviceTypes.PatchPanel, StringComparison.OrdinalIgnoreCase))
        {
            return "Patch";
        }

        return "Server";
    }

    private static Server TryResolveServerForMount(RackMountRecord m)
    {
        if (m == null)
        {
            return null;
        }

        EnsureSortedServers();
        var sid = m.SceneInstanceId != 0 ? m.SceneInstanceId : m.ServerInstanceId;
        return SortedServersBuffer.FirstOrDefault(s =>
        {
            try
            {
                return s != null && s.GetInstanceID() == sid;
            }
            catch
            {
                return false;
            }
        });
    }

    private static void ApplyDiagramColorsForMount(RackDiagramDevice d, RackMountRecord m)
    {
        var dt = m?.DeviceType ?? RackDeviceTypes.Server;
        if (string.Equals(dt, RackDeviceTypes.PatchPanel, StringComparison.OrdinalIgnoreCase))
        {
            d.FillColor = RackDiagramPatchFill;
            d.TextColor = DeviceInventoryReflection.ContrastingRackDiagramTextColor(RackDiagramPatchFill);
            return;
        }

        if (string.Equals(dt, RackDeviceTypes.Switch, StringComparison.OrdinalIgnoreCase))
        {
            d.FillColor = RackDiagramSwitchFill;
            d.TextColor = DeviceInventoryReflection.ContrastingRackDiagramTextColor(RackDiagramSwitchFill);
            return;
        }

        if (string.Equals(dt, RackDeviceTypes.Router, StringComparison.OrdinalIgnoreCase))
        {
            d.FillColor = RackDiagramRouterFill;
            d.TextColor = DeviceInventoryReflection.ContrastingRackDiagramTextColor(RackDiagramRouterFill);
            return;
        }

        var srv = TryResolveServerForMount(m);
        if (srv != null)
        {
            d.FillColor = DeviceInventoryReflection.GetServerRackDiagramBlockTint(srv);
        }
        else
        {
            d.FillColor = new Color(0.74f, 0.76f, 0.80f, 0.92f);
        }

        d.TextColor = DeviceInventoryReflection.ContrastingRackDiagramTextColor(d.FillColor);
    }

    private static bool RackMountSearchMatches(string displayName, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return (displayName ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static List<int> BuildFilteredServerIndices(string query)
    {
        EnsureSortedServers();
        var r = new List<int>();
        for (var i = 0; i < SortedServersBuffer.Count; i++)
        {
            var s = SortedServersBuffer[i];
            var dn = s != null ? DeviceInventoryReflection.GetDisplayName(s) : "";
            if (RackMountSearchMatches(dn, query))
            {
                r.Add(i);
            }
        }

        return r;
    }

    /// <param name="wantRouter">True = L3 routers only; false = L2 switches only (see <see cref="DeviceInventoryReflection.NetworkSwitchBehavesAsRouter"/>).</param>
    private static List<int> BuildFilteredNetworkSwitchIndices(string query, bool wantRouter)
    {
        EnsureSortedSwitches();
        var r = new List<int>();
        for (var i = 0; i < SortedSwitchesBuffer.Count; i++)
        {
            var sw = SortedSwitchesBuffer[i];
            if (sw == null)
            {
                continue;
            }

            if (DeviceInventoryReflection.NetworkSwitchBehavesAsRouter(sw) != wantRouter)
            {
                continue;
            }

            var dn = DeviceInventoryReflection.GetDisplayName(sw);
            if (RackMountSearchMatches(dn, query))
            {
                r.Add(i);
            }
        }

        return r;
    }

    private static readonly Color RackPickRowBg = new Color(0.09f, 0.11f, 0.14f, 1f);
    private static readonly Color RackPickRowBgAlt = new Color(0.07f, 0.09f, 0.12f, 1f);
    private static readonly Color RackPickRowSelected = new Color(0.42f, 0.46f, 0.52f, 1f);

    private static GUIStyle RackPickRowLabelStyle()
    {
        if (_rackPickRowLabelStyle == null && _stTableCell != null)
        {
            // No GUIStyle(GUIStyle) copy ctor in this Unity API surface — mirror table cell, centered.
            var s = new GUIStyle();
            s.font = _stTableCell.font;
            s.fontSize = _stTableCell.fontSize;
            s.fontStyle = _stTableCell.fontStyle;
            s.normal.textColor = _stTableCell.normal.textColor;
            s.hover.textColor = _stTableCell.hover.textColor;
            s.active.textColor = _stTableCell.active.textColor;
            s.wordWrap = _stTableCell.wordWrap;
            s.alignment = TextAnchor.MiddleCenter;
            s.clipping = TextClipping.Clip;
            _rackPickRowLabelStyle = s;
        }

        return _rackPickRowLabelStyle ?? _stTableCell;
    }

    /// <summary>
    /// Rows use tinted rects + labels only (no GUI.Button), so the selection highlight stays visible.
    /// Scrollbar strip is excluded from hit-testing.
    /// </summary>
    private static void HandleRackMountServerListMouse(Rect viewportOuter, float innerW, List<int> filtered)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0)
        {
            return;
        }

        var mp = e.mousePosition;
        if (!viewportOuter.Contains(mp))
        {
            return;
        }

        var mx = mp.x - viewportOuter.xMin;
        if (mx < 0f || mx > innerW)
        {
            return;
        }

        var myFromTop = mp.y - viewportOuter.yMin + _rackMountServerListScroll.y;
        var innerH = filtered.Count * TableRowH;
        if (myFromTop < 0f || myFromTop >= innerH)
        {
            _rackMountPickIdx = -1;
            e.Use();
            return;
        }

        var ri = Mathf.FloorToInt(myFromTop / TableRowH);
        if (ri >= 0 && ri < filtered.Count)
        {
            _rackMountPickIdx = filtered[ri];
            e.Use();
        }
    }

    private static void HandleRackMountSwitchListMouse(Rect viewportOuter, float innerW, List<int> filtered)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0)
        {
            return;
        }

        var mp = e.mousePosition;
        if (!viewportOuter.Contains(mp))
        {
            return;
        }

        var mx = mp.x - viewportOuter.xMin;
        if (mx < 0f || mx > innerW)
        {
            return;
        }

        var myFromTop = mp.y - viewportOuter.yMin + _rackMountSwitchListScroll.y;
        var innerH = filtered.Count * TableRowH;
        if (myFromTop < 0f || myFromTop >= innerH)
        {
            _rackMountSwitchPickIdx = -1;
            e.Use();
            return;
        }

        var ri = Mathf.FloorToInt(myFromTop / TableRowH);
        if (ri >= 0 && ri < filtered.Count)
        {
            _rackMountSwitchPickIdx = filtered[ri];
            e.Use();
        }
    }

    /// <summary>Clears pick when the user clicks outside the list (search, tabs, diagram, etc.).</summary>
    private static void TryDeselectRackPickIfClickedOutsideViewport(Rect viewportLast, ref int pickIdx)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0)
        {
            return;
        }

        if (viewportLast.width > 0.5f && viewportLast.height > 0.5f && viewportLast.Contains(e.mousePosition))
        {
            return;
        }

        pickIdx = -1;
    }

    private static void DrawRackServerPickScroll(Rect outer, List<int> filtered)
    {
        var innerH = filtered.Count * TableRowH;
        var viewH = Mathf.Min(160f, Mathf.Max(TableRowH + 4f, innerH));
        var innerW = outer.width - 18f;
        var innerRect = new Rect(0f, 0f, innerW, Mathf.Max(innerH, viewH));
        var scrollRect = new Rect(outer.x, outer.y, outer.width, viewH);
        _rackMountServerPickViewportLast = scrollRect;
        _rackMountServerListScroll = GUI.BeginScrollView(scrollRect, _rackMountServerListScroll, innerRect);
        var rowStyle = RackPickRowLabelStyle();
        var y = 0f;
        for (var i = 0; i < filtered.Count; i++)
        {
            var idx = filtered[i];
            var srv = SortedServersBuffer[idx];
            var label = srv != null ? DeviceInventoryReflection.GetDisplayName(srv) : "—";
            if (label.Length > 72)
            {
                label = label.Substring(0, 71) + "\u2026";
            }

            var row = new Rect(0f, y, innerW, TableRowH);
            var sel = idx == _rackMountPickIdx;
            if (Event.current.type == EventType.Repaint)
            {
                var bg = sel ? RackPickRowSelected : (i % 2 == 0 ? RackPickRowBg : RackPickRowBgAlt);
                DrawTintedRect(row, bg);
            }

            if (rowStyle != null)
            {
                GUI.Label(row, label, rowStyle);
            }
            else
            {
                GUI.Label(row, label, _stMuted);
            }

            y += TableRowH;
        }

        GUI.EndScrollView();
        HandleRackMountServerListMouse(scrollRect, innerW, filtered);
    }

    private static void DrawRackSwitchPickScroll(Rect outer, List<int> filtered)
    {
        var innerH = filtered.Count * TableRowH;
        var viewH = Mathf.Min(160f, Mathf.Max(TableRowH + 4f, innerH));
        var innerW = outer.width - 18f;
        var innerRect = new Rect(0f, 0f, innerW, Mathf.Max(innerH, viewH));
        var scrollRect = new Rect(outer.x, outer.y, outer.width, viewH);
        _rackMountSwitchPickViewportLast = scrollRect;
        _rackMountSwitchListScroll = GUI.BeginScrollView(scrollRect, _rackMountSwitchListScroll, innerRect);
        var rowStyle = RackPickRowLabelStyle();
        var y = 0f;
        for (var i = 0; i < filtered.Count; i++)
        {
            var idx = filtered[i];
            var sw = SortedSwitchesBuffer[idx];
            var label = sw != null ? DeviceInventoryReflection.GetDisplayName(sw) : "—";
            if (label.Length > 72)
            {
                label = label.Substring(0, 71) + "\u2026";
            }

            var row = new Rect(0f, y, innerW, TableRowH);
            var sel = idx == _rackMountSwitchPickIdx;
            if (Event.current.type == EventType.Repaint)
            {
                var bg = sel ? RackPickRowSelected : (i % 2 == 0 ? RackPickRowBg : RackPickRowBgAlt);
                DrawTintedRect(row, bg);
            }

            if (rowStyle != null)
            {
                GUI.Label(row, label, rowStyle);
            }
            else
            {
                GUI.Label(row, label, _stMuted);
            }

            y += TableRowH;
        }

        GUI.EndScrollView();
        HandleRackMountSwitchListMouse(scrollRect, innerW, filtered);
    }

    private static List<RackDiagramDevice> BuildDiagramDevices(UnifiedRackEntry entry, out int totalU)
    {
        totalU = RackDataStore.RackStandardHeightU;
        var list = new List<RackDiagramDevice>();
        if (entry?.Persisted != null)
        {
            foreach (var m in entry.Persisted.Mounts.OrderBy(static x => x.StartU))
            {
                var rd = new RackDiagramDevice
                {
                    EntryId = m.EntryId ?? "",
                    StartU = m.StartU,
                    HeightU = Mathf.Max(1, m.HeightU),
                    DisplayName = ResolveMountDisplayName(m),
                    TypeLabel = ShortMountTypeLabel(m.DeviceType),
                };
                ApplyDiagramColorsForMount(rd, m);
                list.Add(rd);
            }

            return list;
        }

        if (entry?.SceneCopy != null)
        {
            foreach (var d in entry.SceneCopy.Devices)
            {
                var tint = d.Server != null
                    ? DeviceInventoryReflection.GetServerRackDiagramBlockTint(d.Server)
                    : new Color(0.74f, 0.76f, 0.80f, 0.92f);
                list.Add(
                    new RackDiagramDevice
                    {
                        EntryId = "",
                        StartU = d.StartU,
                        HeightU = Mathf.Max(1, d.HeightU),
                        DisplayName = d.DisplayName ?? "Device",
                        TypeLabel = "Server",
                        FillColor = tint,
                        TextColor = DeviceInventoryReflection.ContrastingRackDiagramTextColor(tint),
                    });
            }
        }

        return list;
    }

    private static void ComputeEffectiveStartsDiagram(IReadOnlyList<RackDiagramDevice> devices, out int[] eff, out bool anyGuess)
    {
        eff = new int[devices.Count];
        anyGuess = false;
        var next = 1;
        for (var i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            if (d.StartU > 0)
            {
                eff[i] = d.StartU;
            }
            else
            {
                eff[i] = next;
                next += Mathf.Max(1, d.HeightU);
                anyGuess = true;
            }
        }
    }

    private static UnifiedRackEntry FindDrilledEntry(List<UnifiedRackEntry> unified)
    {
        if (string.IsNullOrEmpty(_racksTabDrilledUnifiedId))
        {
            return null;
        }

        return unified.FirstOrDefault(u => string.Equals(u.UnifiedId, _racksTabDrilledUnifiedId, StringComparison.Ordinal));
    }

    private static float ComputeRacksContentHeight()
    {
        var innerW = Mathf.Max(520f, _lastInventoryCardWidth > 80f ? _lastInventoryCardWidth + 20f : 920f);
        var cardW = innerW - CardPad * 2f;
        var unified = BuildUnifiedRackList();
        var topBlock = SectionTitleH + 10f + 76f + SectionTitleH + 72f + 28f;
        if (unified.Count == 0)
        {
            return Mathf.Max(420f, CardPad * 2f + topBlock + 120f);
        }

        if (string.IsNullOrEmpty(_racksTabDrilledUnifiedId))
        {
            var listH = unified.Count * (TableRowH + 4f) + SectionTitleH + 48f;
            return Mathf.Max(420f, CardPad * 2f + topBlock + listH);
        }

        var drilled = FindDrilledEntry(unified);
        if (drilled == null)
        {
            var listH = unified.Count * (TableRowH + 4f) + SectionTitleH + 48f;
            return Mathf.Max(420f, CardPad * 2f + topBlock + listH);
        }

        var devices = BuildDiagramDevices(drilled, out _);
        var leftListH = Mathf.Min(unified.Count * (TableRowH + 4f) + 12f, 420f);
        var metaH = drilled.IsPersistedEditable ? 160f : 120f;
        var rowH = TableHeaderH + Mathf.Max(1, devices.Count) * TableRowH + (drilled.IsPersistedEditable ? 380f : 40f);
        var middleCol = metaH + rowH + 24f;
        var rightCol = SectionTitleH + RackDiagramFixedHeight + 56f;
        var body = Mathf.Max(leftListH + SectionTitleH + 20f, middleCol, rightCol);
        return Mathf.Max(480f, CardPad * 2f + topBlock + body);
    }

    private static void DrawRackListRows(float x0, ref float y, float rowW, IReadOnlyList<UnifiedRackEntry> unified, int selIdx)
    {
        GUI.Label(new Rect(x0, y, rowW, SectionTitleH), "Rack list", _stSectionTitle);
        y += SectionTitleH + 6f;

        var btnY = y;
        for (var i = 0; i < unified.Count; i++)
        {
            var rk = unified[i];
            var selRow = i == selIdx;
            var rowRect = new Rect(x0, btnY, rowW - 8f, TableRowH + 2f);

            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && rowRect.Contains(Event.current.mousePosition)
                && Event.current.clickCount >= 2)
            {
                _racksTabSelectedUnifiedId = rk.UnifiedId;
                _racksTabDrilledUnifiedId = rk.UnifiedId;
                Event.current.Use();
                RecomputeContentHeight();
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawTintedRect(
                    rowRect,
                    selRow ? new Color(0.12f, 0.28f, 0.38f, 0.85f) : new Color(0.06f, 0.08f, 0.1f, 0.45f));
            }

            var tag = rk.IsPersistedEditable ? "" : "[Scene] ";
            var count = rk.Persisted?.Mounts?.Count ?? rk.SceneCopy?.Devices?.Count ?? 0;
            var label = $"{tag}{GetUnifiedDisplayTitle(rk)}  ({count})";
            if (ImguiButtonOnce(rowRect, label, 9300 + i, _stMutedBtn))
            {
                _racksTabSelectedUnifiedId = rk.UnifiedId;
                RecomputeContentHeight();
            }

            btnY += TableRowH + 4f;
        }

        y = btnY + 8f;
    }

    private static void DrawRacksView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;
        _lastInventoryCardWidth = cardW;

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  Racks", _stBreadcrumb);
        y += SectionTitleH + 4f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 10f;

        GUI.Label(
            new Rect(x0, y, cardW, 76f),
            "All racks are standard 47 U cabinets. Create named racks and assign servers (3 U / 7 U), switches and routers (1 U), or patch panels (2 U). "
            + "Data is saved to rack_data.json. Scene-only rows appear when the game exposes asset lines — import to edit them.",
            _stHint);
        y += 80f;

        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "Add rack", _stSectionTitle);
        y += SectionTitleH + 4f;
        GUI.Label(new Rect(x0, y, 72f, 22f), "Name", _stMuted);
        DrawIpamFormTextField(
            new Rect(x0 + 76f, y, Mathf.Min(360f, cardW - 200f), 22f),
            IpamFormFocusRackNewName,
            96,
            IpamTextFieldKind.Name);
        if (ImguiButtonOnce(new Rect(x0 + cardW - 132f, y, 120f, 24f), "Create rack", 9220, _stPrimaryBtn))
        {
            if (RackDataStore.TryAddRack(_rackFormNewName, out var newId, out var errC))
            {
                _racksTabSelectedUnifiedId = "p:" + newId;
                ShowIpamToast("Created rack (47 U).");
                RecomputeContentHeight();
            }
            else if (!string.IsNullOrEmpty(errC))
            {
                ShowIpamToast(errC);
            }
        }

        y += 32f;

        var unified = BuildUnifiedRackList();
        if (!string.IsNullOrEmpty(_racksTabDrilledUnifiedId)
            && unified.All(u => !string.Equals(u.UnifiedId, _racksTabDrilledUnifiedId, StringComparison.Ordinal)))
        {
            _racksTabDrilledUnifiedId = "";
        }

        if (unified.Count == 0)
        {
            GUI.Label(
                new Rect(x0, y, cardW, 72f),
                "No racks yet — use Create rack above. Scene-only layouts appear here when the game creates contract rows.",
                _stMuted);
            return;
        }

        var selIdx = unified.FindIndex(u => string.Equals(u.UnifiedId, _racksTabSelectedUnifiedId, StringComparison.Ordinal));
        if (selIdx < 0)
        {
            selIdx = 0;
            _racksTabSelectedUnifiedId = unified[0].UnifiedId;
        }

        var drilledEntry = FindDrilledEntry(unified);
        if (drilledEntry != null)
        {
            var selDrilled = drilledEntry;
            if (!string.Equals(selDrilled.UnifiedId, _racksLastUnifiedId, StringComparison.Ordinal))
            {
                _racksLastUnifiedId = selDrilled.UnifiedId;
                if (selDrilled.Persisted != null)
                {
                    _rackRenameDraft = selDrilled.Persisted.DisplayName ?? "";
                }
                else if (selDrilled.SceneCopy != null)
                {
                    _rackRenameDraft = selDrilled.SceneCopy.DisplayName ?? "Rack";
                }
            }
        }
        else if (!string.Equals(unified[selIdx].UnifiedId, _racksLastUnifiedId, StringComparison.Ordinal))
        {
            var browseSel = unified[selIdx];
            _racksLastUnifiedId = browseSel.UnifiedId;
            if (browseSel.Persisted != null)
            {
                _rackRenameDraft = browseSel.Persisted.DisplayName ?? "";
            }
            else if (browseSel.SceneCopy != null)
            {
                _rackRenameDraft = browseSel.SceneCopy.DisplayName ?? "Rack";
            }
        }

        List<RackDiagramDevice> diagramDevices = new List<RackDiagramDevice>();
        var rackTotalU = RackDataStore.RackStandardHeightU;
        int[] eff = Array.Empty<int>();
        var anyGuess = false;
        if (!string.IsNullOrEmpty(_racksTabDrilledUnifiedId) && drilledEntry != null)
        {
            diagramDevices = BuildDiagramDevices(drilledEntry, out rackTotalU);
            ComputeEffectiveStartsDiagram(diagramDevices, out eff, out anyGuess);
        }

        if (string.IsNullOrEmpty(_racksTabDrilledUnifiedId))
        {
            GUI.Label(new Rect(x0, y, cardW, 22f), "Double-click a rack to open details, mounts, and front view.", _stMuted);
            y += 26f;
            DrawRackListRows(x0, ref y, cardW, unified, selIdx);
            return;
        }

        var gap = 12f;
        var leftListW = Mathf.Clamp(cardW * 0.20f, 168f, 240f);
        var rightDiagW = Mathf.Clamp(cardW * 0.42f, 320f, 540f);
        var midW = cardW - leftListW - rightDiagW - gap * 2f;
        if (midW < 200f)
        {
            leftListW = 168f;
            rightDiagW = Mathf.Clamp(cardW * 0.40f, 300f, 540f);
            midW = cardW - leftListW - rightDiagW - gap * 2f;
        }

        var yList = y;
        DrawRackListRows(x0, ref yList, leftListW, unified, selIdx);

        var mx0 = x0 + leftListW + gap;
        var my = y;
        if (ImguiButtonOnce(new Rect(mx0, my, 140f, 24f), "\u2190 All racks", 9290, _stMutedBtn))
        {
            _racksTabDrilledUnifiedId = "";
            RecomputeContentHeight();
        }

        my += 32f;

        if (drilledEntry == null)
        {
            return;
        }

        var selected = drilledEntry;
        var yMid = my;
        GUI.Label(new Rect(mx0, yMid, midW, SectionTitleH), GetUnifiedDisplayTitle(selected), _stSectionTitle);
        yMid += SectionTitleH + 4f;

        if (selected.IsPersistedEditable && selected.Persisted != null)
        {
            GUI.Label(new Rect(mx0, yMid, 52f, 22f), "Name", _stMuted);
            DrawIpamFormTextField(
                new Rect(mx0 + 56f, yMid, midW - 56f - 160f, 22f),
                IpamFormFocusRackRename,
                96,
                IpamTextFieldKind.Name);
            GUI.Label(new Rect(mx0 + midW - 152f, yMid, 148f, 22f), "47 U (standard)", _stMuted);
            yMid += 26f;
            if (ImguiButtonOnce(new Rect(mx0 + midW - 168f, yMid, 78f, 24f), "Apply", 9221, _stMutedBtn))
            {
                if (RackDataStore.TryUpdateRackName(selected.Persisted.Id, _rackRenameDraft, out var errA))
                {
                    ShowIpamToast("Rack updated.");
                    RecomputeContentHeight();
                }
                else if (!string.IsNullOrEmpty(errA))
                {
                    ShowIpamToast(errA);
                }
            }

            if (ImguiButtonOnce(new Rect(mx0 + midW - 84f, yMid, 78f, 24f), "Delete", 9223, _stMutedBtn))
            {
                if (RackDataStore.TryDeleteRack(selected.Persisted.Id))
                {
                    _racksTabSelectedUnifiedId = "";
                    _racksTabDrilledUnifiedId = "";
                    ShowIpamToast("Rack removed.");
                    RecomputeContentHeight();
                }
            }

            yMid += 30f;
        }
        else
        {
            GUI.Label(new Rect(mx0, yMid, midW, 22f), $"47 U  ·  Scene detection (read-only)", _stTableCell);
            yMid += 26f;
            GUI.Label(new Rect(mx0, yMid, midW, 44f), "Import copies servers into a rack you can edit and name.", _stMuted);
            yMid += 48f;
            if (selected.SceneCopy != null
                && ImguiButtonOnce(new Rect(mx0, yMid, Mathf.Min(midW, 220f), 28f), "Import into my racks…", 9225, _stPrimaryBtn))
            {
                var nm = string.IsNullOrWhiteSpace(_rackRenameDraft) ? selected.SceneCopy.DisplayName : _rackRenameDraft;
                if (RackDataStore.TryImportDiscoveredRack(selected.SceneCopy.Key, nm, selected.SceneCopy, out var nid, out var errI))
                {
                    _racksTabSelectedUnifiedId = "p:" + nid;
                    _racksTabDrilledUnifiedId = "p:" + nid;
                    ShowIpamToast("Imported rack — you can rename it and adjust mounts.");
                    RecomputeContentHeight();
                }
                else if (!string.IsNullOrEmpty(errI))
                {
                    ShowIpamToast(errI);
                }
            }

            if (selected.SceneCopy != null)
            {
                yMid += 32f;
            }
        }

        var colType = midW * 0.14f;
        var colPos = midW * 0.12f;
        var colSz = midW * 0.12f;
        var colDev = midW - colType - colPos - colSz - 40f;
        GUI.Label(new Rect(mx0, yMid, colType, TableHeaderH), "Type", _stTableHeaderText);
        GUI.Label(new Rect(mx0 + colType, yMid, colPos, TableHeaderH), "Pos", _stTableHeaderText);
        GUI.Label(new Rect(mx0 + colType + colPos, yMid, colSz, TableHeaderH), "Size", _stTableHeaderText);
        GUI.Label(new Rect(mx0 + colType + colPos + colSz, yMid, colDev, TableHeaderH), "Device", _stTableHeaderText);
        yMid += TableHeaderH;

        for (var r = 0; r < diagramDevices.Count; r++)
        {
            var d = diagramDevices[r];
            var alt = r % 2 == 1;
            var rr = new Rect(mx0, yMid, midW - (selected.IsPersistedEditable ? 36f : 4f), TableRowH);
            if (Event.current.type == EventType.Repaint)
            {
                DrawTintedRect(rr, alt ? new Color(0.06f, 0.08f, 0.1f, 0.5f) : new Color(0.04f, 0.05f, 0.06f, 0.35f));
            }

            var posTxt = d.StartU > 0 ? d.StartU.ToString() : $"~{eff[r]}";
            if (d.StartU <= 0 && anyGuess)
            {
                posTxt += " est.";
            }

            GUI.Label(new Rect(mx0 + 4f, yMid, colType - 8f, TableRowH), d.TypeLabel ?? "", _stTableCell);
            GUI.Label(new Rect(mx0 + colType, yMid, colPos - 4f, TableRowH), posTxt, _stTableCell);
            GUI.Label(new Rect(mx0 + colType + colPos, yMid, colSz, TableRowH), $"{d.HeightU} U", _stTableCell);
            GUI.Label(new Rect(mx0 + colType + colPos + colSz, yMid, colDev - 6f, TableRowH), d.DisplayName, _stTableCell);

            if (selected.IsPersistedEditable
                && selected.Persisted?.Mounts != null
                && !string.IsNullOrEmpty(d.EntryId))
            {
                var dedRm = 928000 + Mathf.Abs(d.EntryId.GetHashCode() % 90000);
                if (ImguiButtonOnce(new Rect(mx0 + midW - 32f, yMid + 2f, 28f, TableRowH - 4f), "×", dedRm, _stMutedBtn))
                {
                    if (RackDataStore.TryRemoveMount(selected.Persisted.Id, d.EntryId))
                    {
                        ShowIpamToast("Removed from rack.");
                        RecomputeContentHeight();
                    }
                }
            }

            yMid += TableRowH;
        }

        if (selected.IsPersistedEditable && selected.Persisted != null)
        {
            yMid += 8f;
            GUI.Label(new Rect(mx0, yMid, midW, SectionTitleH), "Add device", _stSectionTitle);
            yMid += SectionTitleH + 4f;

            _rackAddMountCategory = Mathf.Clamp(_rackAddMountCategory, 0, 3);
            var catY = yMid;
            var cw = 76f;
            if (ImguiButtonOnce(new Rect(mx0, catY, cw, 22f), "Server", 92310, _rackAddMountCategory == 0 ? _stPrimaryBtn : _stMutedBtn))
            {
                _rackAddMountCategory = 0;
                _rackMountSwitchPickIdx = -1;
            }

            if (ImguiButtonOnce(new Rect(mx0 + cw + 6f, catY, cw, 22f), "Switch", 92311, _rackAddMountCategory == 1 ? _stPrimaryBtn : _stMutedBtn))
            {
                _rackAddMountCategory = 1;
                _rackMountPickIdx = -1;
            }

            if (ImguiButtonOnce(new Rect(mx0 + (cw + 6f) * 2f, catY, cw, 22f), "Router", 92312, _rackAddMountCategory == 2 ? _stPrimaryBtn : _stMutedBtn))
            {
                _rackAddMountCategory = 2;
                _rackMountPickIdx = -1;
            }

            if (ImguiButtonOnce(new Rect(mx0 + (cw + 6f) * 3f, catY, cw + 10f, 22f), "Patch", 92313, _rackAddMountCategory == 3 ? _stPrimaryBtn : _stMutedBtn))
            {
                _rackAddMountCategory = 3;
                _rackMountPickIdx = -1;
                _rackMountSwitchPickIdx = -1;
            }

            yMid += 28f;

            if (_rackAddMountCategory == 0)
            {
                _rackMountServerPickViewportLast = default;
                EnsureSortedServers();
                var sv = SortedServersBuffer.Count;
                GUI.Label(new Rect(mx0, yMid, 72f, 22f), "Search", _stMuted);
                DrawIpamFormTextField(
                    new Rect(mx0 + 76f, yMid, midW - 76f, 22f),
                    IpamFormFocusRackMountServerSearch,
                    96,
                    IpamTextFieldKind.Name);
                yMid += 28f;

                var filteredSrv = BuildFilteredServerIndices(_rackMountServerSearchBuf ?? "");
                if (_rackMountPickIdx >= 0 && (sv <= 0 || _rackMountPickIdx >= sv || !filteredSrv.Contains(_rackMountPickIdx)))
                {
                    _rackMountPickIdx = -1;
                }

                if (sv <= 0)
                {
                    GUI.Label(new Rect(mx0, yMid, midW, 22f), "(no servers in scene)", _stMuted);
                    yMid += 26f;
                }
                else if (filteredSrv.Count == 0)
                {
                    GUI.Label(new Rect(mx0, yMid, midW, 22f), "(no matches — adjust search)", _stMuted);
                    yMid += 26f;
                }
                else
                {
                    DrawRackServerPickScroll(new Rect(mx0, yMid, midW, 162f), filteredSrv);
                    yMid += 162f;
                }

                if (sv > 0
                    && _rackMountPickIdx >= 0
                    && _rackMountPickIdx < SortedServersBuffer.Count
                    && SortedServersBuffer[_rackMountPickIdx] != null)
                {
                    var hintH = RackLayoutHelper.InferServerRackHeightU(SortedServersBuffer[_rackMountPickIdx]);
                    GUI.Label(
                        new Rect(mx0, yMid, midW, 22f),
                        $"Selected height: {hintH} U (3 U or 7 U server)",
                        _stMuted);
                    yMid += 26f;
                }
            }
            else if (_rackAddMountCategory is 1 or 2)
            {
                _rackMountSwitchPickViewportLast = default;
                EnsureSortedSwitches();
                var swN = SortedSwitchesBuffer.Count;
                GUI.Label(new Rect(mx0, yMid, 72f, 22f), "Search", _stMuted);
                DrawIpamFormTextField(
                    new Rect(mx0 + 76f, yMid, midW - 76f, 22f),
                    IpamFormFocusRackMountSwitchSearch,
                    96,
                    IpamTextFieldKind.Name);
                yMid += 28f;

                var filteredSw = BuildFilteredNetworkSwitchIndices(_rackMountSwitchSearchBuf ?? "", _rackAddMountCategory == 2);
                if (_rackMountSwitchPickIdx >= 0 && (swN <= 0 || _rackMountSwitchPickIdx >= swN || !filteredSw.Contains(_rackMountSwitchPickIdx)))
                {
                    _rackMountSwitchPickIdx = -1;
                }

                if (swN <= 0)
                {
                    GUI.Label(new Rect(mx0, yMid, midW, 22f), "(no switches/routers in scene)", _stMuted);
                    yMid += 26f;
                }
                else if (filteredSw.Count == 0)
                {
                    GUI.Label(new Rect(mx0, yMid, midW, 22f), "(no matches — adjust search)", _stMuted);
                    yMid += 26f;
                }
                else
                {
                    DrawRackSwitchPickScroll(new Rect(mx0, yMid, midW, 162f), filteredSw);
                    yMid += 162f;
                }

                GUI.Label(new Rect(mx0, yMid, midW, 22f), "Height: 1 U", _stMuted);
                yMid += 26f;
            }
            else
            {
                GUI.Label(new Rect(mx0, yMid, 72f, 22f), "Label", _stMuted);
                DrawIpamFormTextField(
                    new Rect(mx0 + 76f, yMid, Mathf.Min(320f, midW - 76f), 22f),
                    IpamFormFocusRackPatchLabel,
                    96,
                    IpamTextFieldKind.Name);
                yMid += 28f;
                GUI.Label(new Rect(mx0, yMid, midW, 22f), "Height: 2 U", _stMuted);
            }

            yMid += 28f;
            GUI.Label(new Rect(mx0, yMid, 72f, 22f), "Start U", _stMuted);
            DrawIpamFormTextField(
                new Rect(mx0 + 76f, yMid, 56f, 22f),
                IpamFormFocusRackMountStartU,
                2,
                IpamTextFieldKind.VlanIdDigits);
            yMid += 28f;
            if (ImguiButtonOnce(new Rect(mx0, yMid, 140f, 26f), "Add to rack", 9224, _stPrimaryBtn))
            {
                if (!int.TryParse((_rackMountStartU ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var su))
                {
                    ShowIpamToast("Invalid start U.");
                }
                else if (_rackAddMountCategory == 0)
                {
                    EnsureSortedServers();
                    var filtSrv = BuildFilteredServerIndices(_rackMountServerSearchBuf ?? "");
                    if (SortedServersBuffer.Count <= 0)
                    {
                        ShowIpamToast("No servers in scene.");
                    }
                    else if (filtSrv.Count == 0)
                    {
                        ShowIpamToast("No servers match your search.");
                    }
                    else if (!filtSrv.Contains(_rackMountPickIdx) || SortedServersBuffer[_rackMountPickIdx] == null)
                    {
                        ShowIpamToast("Select a server from the filtered list.");
                    }
                    else
                    {
                        var srv = SortedServersBuffer[_rackMountPickIdx];
                        int iid;
                        try
                        {
                            iid = srv.GetInstanceID();
                        }
                        catch
                        {
                            iid = 0;
                        }

                        var hu = RackLayoutHelper.InferServerRackHeightU(srv);
                        if (RackDataStore.TryAddRackMount(
                                selected.Persisted.Id,
                                RackDeviceTypes.Server,
                                iid,
                                null,
                                su,
                                hu,
                                out var errM))
                        {
                            ShowIpamToast("Device added.");
                            RecomputeContentHeight();
                        }
                        else if (!string.IsNullOrEmpty(errM))
                        {
                            ShowIpamToast(errM);
                        }
                    }
                }
                else if (_rackAddMountCategory is 1 or 2)
                {
                    EnsureSortedSwitches();
                    var filtSw = BuildFilteredNetworkSwitchIndices(_rackMountSwitchSearchBuf ?? "", _rackAddMountCategory == 2);
                    if (SortedSwitchesBuffer.Count <= 0)
                    {
                        ShowIpamToast("No switches/routers in scene.");
                    }
                    else if (filtSw.Count == 0)
                    {
                        ShowIpamToast("No devices match your search.");
                    }
                    else if (!filtSw.Contains(_rackMountSwitchPickIdx) || SortedSwitchesBuffer[_rackMountSwitchPickIdx] == null)
                    {
                        ShowIpamToast("Select a device from the filtered list.");
                    }
                    else
                    {
                        var sw = SortedSwitchesBuffer[_rackMountSwitchPickIdx];
                        int iid;
                        try
                        {
                            iid = sw.GetInstanceID();
                        }
                        catch
                        {
                            iid = 0;
                        }

                        var dtype = _rackAddMountCategory == 2 ? RackDeviceTypes.Router : RackDeviceTypes.Switch;
                        if (RackDataStore.TryAddRackMount(selected.Persisted.Id, dtype, iid, null, su, 1, out var errM))
                        {
                            ShowIpamToast("Device added.");
                            RecomputeContentHeight();
                        }
                        else if (!string.IsNullOrEmpty(errM))
                        {
                            ShowIpamToast(errM);
                        }
                    }
                }
                else
                {
                    if (RackDataStore.TryAddRackMount(
                            selected.Persisted.Id,
                            RackDeviceTypes.PatchPanel,
                            0,
                            _rackPatchLabelDraft,
                            su,
                            2,
                            out var errM))
                    {
                        ShowIpamToast("Patch panel added.");
                        RecomputeContentHeight();
                    }
                    else if (!string.IsNullOrEmpty(errM))
                    {
                        ShowIpamToast(errM);
                    }
                }
            }

            // Run after "Add to rack" so that button click cannot clear the pick before validation.
            if (_rackAddMountCategory == 0)
            {
                TryDeselectRackPickIfClickedOutsideViewport(_rackMountServerPickViewportLast, ref _rackMountPickIdx);
            }
            else if (_rackAddMountCategory is 1 or 2)
            {
                TryDeselectRackPickIfClickedOutsideViewport(_rackMountSwitchPickViewportLast, ref _rackMountSwitchPickIdx);
            }

            yMid += 34f;
        }

        var dx = mx0 + midW + gap;
        var yDiagCol = my;
        GUI.Label(new Rect(dx, yDiagCol, rightDiagW, SectionTitleH), "Front view", _stSectionTitle);
        yDiagCol += SectionTitleH + 6f;
        var ru = Mathf.Max(1, rackTotalU);
        var diagramH = RackDiagramFixedHeight;
        var unitLab = 56f;
        var rackBodyW = Mathf.Max(180f, rightDiagW - unitLab - 12f);
        DrawRackFrontDiagramDevices(new Rect(dx + unitLab + 4f, yDiagCol, rackBodyW, diagramH), ru, diagramDevices, eff);
        DrawRackUnitLabels(new Rect(dx, yDiagCol, unitLab, diagramH), ru);
        if (anyGuess)
        {
            GUI.Label(
                new Rect(dx, yDiagCol + diagramH + 6f, rightDiagW, 36f),
                "~ Position estimated where the game did not expose start U.",
                _stHint);
        }
    }

    private static string GetUnifiedDisplayTitle(UnifiedRackEntry e)
    {
        if (e?.Persisted != null)
        {
            return e.Persisted.DisplayName ?? "Rack";
        }

        return e?.SceneCopy?.DisplayName ?? "Rack";
    }

    private static void DrawRackFrontDiagramDevices(Rect rackBody, int totalU, IReadOnlyList<RackDiagramDevice> devices, int[] effStart)
    {
        if (Event.current.type == EventType.Repaint)
        {
            DrawTintedRect(rackBody, new Color(0.09f, 0.1f, 0.12f, 0.95f));
        }

        var tu = Mathf.Max(1, totalU);
        var cell = rackBody.height / tu;

        for (var u = 1; u < tu; u++)
        {
            var yLine = rackBody.yMax - u * cell;
            GUI.DrawTexture(new Rect(rackBody.x, yLine, rackBody.width, 1f), _texTableHeader);
        }

        GUI.DrawTexture(new Rect(rackBody.x, rackBody.y, rackBody.width, 2f), _texTableHeader);
        GUI.DrawTexture(new Rect(rackBody.x, rackBody.yMax - 1f, rackBody.width, 2f), _texTableHeader);
        GUI.DrawTexture(new Rect(rackBody.x, rackBody.y, 2f, rackBody.height), _texTableHeader);
        GUI.DrawTexture(new Rect(rackBody.xMax - 2f, rackBody.y, 2f, rackBody.height), _texTableHeader);

        var maxChars = Mathf.Clamp((int)(rackBody.width / 6.2f), 16, 80);

        for (var i = 0; i < devices.Count && i < effStart.Length; i++)
        {
            var d = devices[i];
            var su = effStart[i];
            var h = Mathf.Max(1, d.HeightU);
            var yTop = rackBody.yMax - (su + h - 1) * cell;
            var hPx = h * cell;
            var devRect = new Rect(rackBody.x + 3f, yTop, rackBody.width - 6f, hPx - 1f);
            var fill = d.FillColor.a > 0.01f ? d.FillColor : new Color(0.78f, 0.8f, 0.84f, 0.92f);
            var txtCol = d.TextColor.a > 0.01f ? d.TextColor : DeviceInventoryReflection.ContrastingRackDiagramTextColor(fill);
            if (Event.current.type == EventType.Repaint)
            {
                DrawTintedRect(devRect, fill);
            }

            var nm = d.DisplayName ?? "";
            if (nm.Length > maxChars)
            {
                nm = nm.Substring(0, maxChars - 1) + "…";
            }

            var oldCc = GUI.contentColor;
            GUI.contentColor = txtCol;
            GUI.Label(devRect, nm, _stTableCell);
            GUI.contentColor = oldCc;
        }
    }

    private static void DrawRackUnitLabels(Rect labelColumn, int totalU)
    {
        if (_rackDiagramUnitLabelStyle == null && _stMuted != null)
        {
            _rackDiagramUnitLabelStyle = new GUIStyle
            {
                font = _stMuted.font,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            _rackDiagramUnitLabelStyle.normal.textColor = _stMuted.normal.textColor;
        }

        var st = _rackDiagramUnitLabelStyle ?? _stMuted;
        var tu = Mathf.Max(1, totalU);
        var cell = labelColumn.height / tu;

        // Keep glyph height ≤ cell height so adjacent U labels do not overlap vertically.
        var fontPx = Mathf.Clamp(Mathf.RoundToInt(cell * 0.72f), 7, 11);
        if (_rackDiagramUnitLabelStyle != null)
        {
            _rackDiagramUnitLabelStyle.fontSize = fontPx;
        }

        var step = 1;
        if (cell < fontPx + 2f)
        {
            step = 2;
        }

        if (cell < 7f)
        {
            step = Mathf.Max(step, 5);
        }

        if (cell < 4f)
        {
            step = Mathf.Max(step, 10);
        }

        for (var u = 1; u <= tu; u++)
        {
            var show = u == 1 || u == tu || u % step == 0;
            if (!show)
            {
                continue;
            }

            var yb = labelColumn.yMax - u * cell;
            var cellRect = new Rect(labelColumn.x, yb + 0.5f, labelColumn.width - 4f, Mathf.Max(1f, cell - 1f));
            GUI.Label(cellRect, u.ToString(), st);
        }
    }
}
