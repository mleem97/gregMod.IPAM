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

    private const int RackFloorRowCount = 16;
    private const int RackFloorColCount = 32;
    /// <summary>Group sizes reading right → left (column 1 starts on the right).</summary>
    private static readonly int[] RackFloorGroupsFromRight = { 5, 5, 4, 5, 4, 5, 4 };
    /// <summary>Same groups laid out left → right on screen (mirrored).</summary>
    private static readonly int[] RackFloorGroupsOnScreen = { 4, 5, 4, 5, 4, 5, 5 };
    private static readonly int[] RackFloorScreenColumnOrder = BuildRackFloorScreenColumnOrder();
    private const float RackFloorGroupAisleW = 8f;
    private const float RackFloorRowAisleH = 10f;
    private const float RackFloorAxisLabelW = 24f;
    private const float RackFloorAxisLabelH = 18f;
    private static readonly Color RackFloorCellEmpty = new(0.44f, 0.46f, 0.49f, 0.90f);
    private static readonly Color RackFloorCellOccupied = new(0.22f, 0.62f, 0.34f, 0.92f);
    private static readonly Color RackMountDragPreview = new(0.28f, 0.88f, 0.52f, 0.62f);
    private static readonly Color RackMountDragGhost = new(0.22f, 0.78f, 0.48f, 0.92f);
    private static readonly Color RackPatchDragRowFill = new(0.16f, 0.38f, 0.46f, 0.96f);
    private static readonly Color RackPatchDragRowBorder = new(0.35f, 0.82f, 0.78f, 0.95f);

    private static GUIStyle _rackGridLabelStyle;
    private static GUIStyle _rackGridAxisLabelStyle;
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
    private static bool _rackMountDragActive;
    private static int _rackMountDragHeightU = 1;
    private static int _rackMountDragHoverStartU = -1;
    private static Rect _rackMountDropBodyLast;
    private static string _rackMountDropRackId = "";
    private static int _rackMountDropRackTotalU = 47;
    private static string _rackMountDragLabel = "";
    private static Vector2 _rackMountDragStartPos;
    private const int RackMountDragControlBase = unchecked((int)0x524D_0000);

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

    private static string FormatRackGridLabel(int rowIndex, int column)
    {
        return ((char)('A' + rowIndex)).ToString(CultureInfo.InvariantCulture)
               + column.ToString(CultureInfo.InvariantCulture);
    }

    private static UnifiedRackEntry FindUnifiedAtGrid(int rowIndex, int column, IReadOnlyList<UnifiedRackEntry> unified)
    {
        if (unified == null || rowIndex < 0 || rowIndex >= RackFloorRowCount || column < 1 || column > RackFloorColCount)
        {
            return null;
        }

        var rowLetter = ((char)('A' + rowIndex)).ToString();
        var label = FormatRackGridLabel(rowIndex, column);
        foreach (var u in unified)
        {
            if (u?.Persisted != null)
            {
                if (u.Persisted.GridColumn == column
                    && string.Equals(u.Persisted.GridRow, rowLetter, StringComparison.OrdinalIgnoreCase))
                {
                    return u;
                }

                if (string.Equals((u.Persisted.DisplayName ?? "").Trim(), label, StringComparison.OrdinalIgnoreCase))
                {
                    return u;
                }
            }

            if (u?.SceneCopy != null
                && string.Equals((u.SceneCopy.DisplayName ?? "").Trim(), label, StringComparison.OrdinalIgnoreCase))
            {
                return u;
            }
        }

        return null;
    }

    private static int[] BuildRackFloorScreenColumnOrder()
    {
        var order = new int[RackFloorColCount];
        var visualIdx = RackFloorColCount - 1;
        var logicalCol = 1;
        foreach (var gsize in RackFloorGroupsFromRight)
        {
            for (var i = 0; i < gsize; i++)
            {
                order[visualIdx--] = logicalCol++;
            }
        }

        return order;
    }

    private static void ComputeRackFloorGridMetrics(
        float availableW,
        out float cellSize,
        out float gridW,
        out float gridH,
        out float totalW,
        out float totalH)
    {
        var aisleTotal = (RackFloorGroupsOnScreen.Length - 1) * RackFloorGroupAisleW;
        var usableW = Mathf.Max(120f, availableW - RackFloorAxisLabelW * 2f);
        cellSize = Mathf.Max(12f, (usableW - aisleTotal) / RackFloorColCount);
        gridW = RackFloorColCount * cellSize + aisleTotal;
        gridH = RackFloorRowCount * cellSize + (RackFloorRowCount - 1) * RackFloorRowAisleH;
        totalW = gridW + RackFloorAxisLabelW * 2f;
        totalH = gridH + RackFloorAxisLabelH * 2f;
    }

    private static float RackFloorVisualIndexToX(float gridX0, int visualIndex, float cellSize)
    {
        var x = gridX0;
        var vi = 0;
        foreach (var gsize in RackFloorGroupsOnScreen)
        {
            for (var i = 0; i < gsize; i++)
            {
                if (vi == visualIndex)
                {
                    return x;
                }

                x += cellSize;
                vi++;
            }

            if (vi < RackFloorColCount)
            {
                x += RackFloorGroupAisleW;
            }
        }

        return x;
    }

    private static float RackFloorRowToY(float gridY0, int rowIndex, float cellSize)
    {
        return gridY0 + rowIndex * (cellSize + RackFloorRowAisleH);
    }

    private static GUIStyle GetRackGridAxisLabelStyle(float cellSize)
    {
        if (_rackGridAxisLabelStyle == null && _stMutedCenter != null)
        {
            var s = new GUIStyle();
            s.font = _stMutedCenter.font;
            s.fontSize = _stMutedCenter.fontSize;
            s.fontStyle = FontStyle.Bold;
            s.normal.textColor = new Color32(176, 186, 200, 255);
            s.alignment = TextAnchor.MiddleCenter;
            s.clipping = TextClipping.Clip;
            s.wordWrap = false;
            _rackGridAxisLabelStyle = s;
        }

        if (_rackGridAxisLabelStyle != null)
        {
            _rackGridAxisLabelStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(cellSize, RackFloorAxisLabelH) * 0.42f), 8, 11);
        }

        return _rackGridAxisLabelStyle ?? _stMutedCenter;
    }

    private static GUIStyle GetRackGridLabelStyle(float cellSize)
    {
        if (_rackGridLabelStyle == null && _stMutedCenter != null)
        {
            var s = new GUIStyle();
            s.font = _stMutedCenter.font;
            s.fontSize = _stMutedCenter.fontSize;
            s.fontStyle = _stMutedCenter.fontStyle;
            s.normal.textColor = new Color32(248, 244, 236, 255);
            s.alignment = TextAnchor.MiddleCenter;
            s.clipping = TextClipping.Clip;
            s.wordWrap = false;
            _rackGridLabelStyle = s;
        }

        if (_rackGridLabelStyle != null)
        {
            _rackGridLabelStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(cellSize * 0.38f), 7, 12);
        }

        return _rackGridLabelStyle ?? _stMutedCenter;
    }

    private static void DrawRackFloorGrid(
        float x0,
        ref float y,
        float availableW,
        IReadOnlyList<UnifiedRackEntry> unified,
        bool allowOpenOnClick)
    {
        ComputeRackFloorGridMetrics(availableW, out var cellSize, out var gridW, out var gridH, out var totalW, out var totalH);
        GUI.Label(new Rect(x0, y, totalW, SectionTitleH), "Datacenter floor (A–P × 1–32)", _stSectionTitle);
        y += SectionTitleH + 4f;
        GUI.Label(
            new Rect(x0, y, totalW, 36f),
            "Column 1 is on the right. Rack groups (right → left): 5 · 5 · 4 · 5 · 4 · 5 · 4 with aisles between. Click a slot to open.",
            _stHint);
        y += 38f;

        var outerRect = new Rect(x0, y, totalW, totalH);
        var gridX0 = RackFloorAxisLabelW;
        var gridY0 = RackFloorAxisLabelH;
        var labelSt = GetRackGridLabelStyle(cellSize);
        var axisSt = GetRackGridAxisLabelStyle(cellSize);

        GUI.BeginGroup(outerRect);

        if (Event.current.type == EventType.Repaint)
        {
            for (var visualIndex = 0; visualIndex < RackFloorColCount; visualIndex++)
            {
                var col = RackFloorScreenColumnOrder[visualIndex];
                var cx = RackFloorVisualIndexToX(gridX0, visualIndex, cellSize);
                var topRect = new Rect(cx, 0f, cellSize, RackFloorAxisLabelH - 2f);
                var bottomRect = new Rect(cx, gridY0 + gridH + 2f, cellSize, RackFloorAxisLabelH - 2f);
                DrawAxisLabel(topRect, col.ToString(CultureInfo.InvariantCulture), axisSt);
                DrawAxisLabel(bottomRect, col.ToString(CultureInfo.InvariantCulture), axisSt);
            }
        }

        for (var row = 0; row < RackFloorRowCount; row++)
        {
            var rowLetter = ((char)('A' + row)).ToString(CultureInfo.InvariantCulture);
            var cy = RackFloorRowToY(gridY0, row, cellSize);
            var leftAxisRect = new Rect(0f, cy, RackFloorAxisLabelW - 2f, cellSize);
            var rightAxisRect = new Rect(gridX0 + gridW + 2f, cy, RackFloorAxisLabelW - 2f, cellSize);

            if (Event.current.type == EventType.Repaint)
            {
                DrawAxisLabel(leftAxisRect, rowLetter, axisSt);
                DrawAxisLabel(rightAxisRect, rowLetter, axisSt);
            }

            for (var visualIndex = 0; visualIndex < RackFloorColCount; visualIndex++)
            {
                var col = RackFloorScreenColumnOrder[visualIndex];
                var cx = RackFloorVisualIndexToX(gridX0, visualIndex, cellSize);
                var cellRect = new Rect(cx, cy, cellSize, cellSize);
                var entry = FindUnifiedAtGrid(row, col, unified);
                var selected = entry != null
                               && string.Equals(entry.UnifiedId, _racksTabSelectedUnifiedId, StringComparison.Ordinal);
                var drilled = entry != null
                              && string.Equals(entry.UnifiedId, _racksTabDrilledUnifiedId, StringComparison.Ordinal);
                var hasMounts = entry != null
                                && ((entry.Persisted?.Mounts?.Count ?? 0) > 0
                                    || (entry.SceneCopy?.Devices?.Count ?? 0) > 0);

                var controlHint = unchecked(0x5241_0000 + row * RackFloorColCount + visualIndex);
                if (allowOpenOnClick && ImguiListRowClick(cellRect, controlHint))
                {
                    if (entry != null)
                    {
                        _racksTabSelectedUnifiedId = entry.UnifiedId;
                        _racksTabDrilledUnifiedId = entry.UnifiedId;
                    }
                    else if (RackDataStore.TryEnsureRackAtGrid(row, col, out var newId, out var errOpen))
                    {
                        _racksTabSelectedUnifiedId = "p:" + newId;
                        _racksTabDrilledUnifiedId = "p:" + newId;
                    }
                    else if (!string.IsNullOrEmpty(errOpen))
                    {
                        ShowIpamToast(errOpen);
                    }

                    RecomputeContentHeight();
                }

                if (Event.current.type != EventType.Repaint)
                {
                    continue;
                }

                var occupied = entry != null && hasMounts;
                var fill = occupied ? RackFloorCellOccupied : RackFloorCellEmpty;

                DrawTintedRect(cellRect, fill);
                if (selected || drilled)
                {
                    DrawTintedRect(cellRect, new Color(0.12f, 0.28f, 0.38f, drilled ? 0.55f : 0.35f));
                }

                GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, cellRect.width, 1f), _texTableHeader);
                GUI.DrawTexture(new Rect(cellRect.x, cellRect.yMax - 1f, cellRect.width, 1f), _texTableHeader);
                GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, 1f, cellRect.height), _texTableHeader);
                GUI.DrawTexture(new Rect(cellRect.xMax - 1f, cellRect.y, 1f, cellRect.height), _texTableHeader);

                var slotLabel = FormatRackGridLabel(row, col);
                if (labelSt != null)
                {
                    labelSt.Draw(cellRect, new GUIContent(slotLabel), false, false, false, false);
                }
            }
        }

        GUI.EndGroup();

        y += totalH + 8f;
    }

    private static void DrawAxisLabel(Rect r, string text, GUIStyle st)
    {
        if (st != null)
        {
            st.Draw(r, new GUIContent(text), false, false, false, false);
        }
        else
        {
            GUI.Label(r, text, _stMutedCenter);
        }
    }

    private static bool CanStartRackMountDrag()
    {
        switch (_rackAddMountCategory)
        {
            case 0:
                EnsureSortedServers();
                return _rackMountPickIdx >= 0
                       && _rackMountPickIdx < SortedServersBuffer.Count
                       && SortedServersBuffer[_rackMountPickIdx] != null;
            case 1:
            case 2:
                EnsureSortedSwitches();
                return _rackMountSwitchPickIdx >= 0
                       && _rackMountSwitchPickIdx < SortedSwitchesBuffer.Count
                       && SortedSwitchesBuffer[_rackMountSwitchPickIdx] != null;
            case 3:
                return true;
            default:
                return false;
        }
    }

    private static int GetPendingMountHeightU()
    {
        switch (_rackAddMountCategory)
        {
            case 0:
                EnsureSortedServers();
                if (_rackMountPickIdx >= 0
                    && _rackMountPickIdx < SortedServersBuffer.Count
                    && SortedServersBuffer[_rackMountPickIdx] != null)
                {
                    return RackLayoutHelper.InferServerRackHeightU(SortedServersBuffer[_rackMountPickIdx]);
                }

                return 3;
            case 1:
            case 2:
                return 1;
            case 3:
                return 2;
            default:
                return 1;
        }
    }

    private static string GetPendingMountPreviewLabel()
    {
        switch (_rackAddMountCategory)
        {
            case 0:
                EnsureSortedServers();
                if (_rackMountPickIdx >= 0
                    && _rackMountPickIdx < SortedServersBuffer.Count
                    && SortedServersBuffer[_rackMountPickIdx] != null)
                {
                    return DeviceInventoryReflection.GetDisplayName(SortedServersBuffer[_rackMountPickIdx]);
                }

                return "Server";
            case 1:
            case 2:
                EnsureSortedSwitches();
                if (_rackMountSwitchPickIdx >= 0
                    && _rackMountSwitchPickIdx < SortedSwitchesBuffer.Count
                    && SortedSwitchesBuffer[_rackMountSwitchPickIdx] != null)
                {
                    return DeviceInventoryReflection.GetDisplayName(SortedSwitchesBuffer[_rackMountSwitchPickIdx]);
                }

                return _rackAddMountCategory == 2 ? "Router" : "Switch";
            case 3:
                return string.IsNullOrWhiteSpace(_rackPatchLabelDraft) ? "Patch panel" : _rackPatchLabelDraft.Trim();
            default:
                return "Device";
        }
    }

    private static int RackDiagramMouseToStartU(Rect rackBody, int totalU, int heightU, float mouseY)
    {
        var tu = Mathf.Max(1, totalU);
        var h = Mathf.Max(1, heightU);
        var cell = rackBody.height / tu;
        if (cell <= 0.001f)
        {
            return 1;
        }

        var uFloat = (rackBody.yMax - mouseY) / cell;
        var startU = Mathf.FloorToInt(uFloat) + 1;
        return Mathf.Clamp(startU, 1, Mathf.Max(1, tu - h + 1));
    }

    private static void UpdateRackMountDragHover(int rackTotalU)
    {
        var mouse = Event.current.mousePosition;
        if (_rackMountDropBodyLast.width <= 0f || !_rackMountDropBodyLast.Contains(mouse))
        {
            _rackMountDragHoverStartU = -1;
            return;
        }

        _rackMountDragHoverStartU = RackDiagramMouseToStartU(
            _rackMountDropBodyLast,
            rackTotalU,
            _rackMountDragHeightU,
            mouse.y);
        _rackMountStartU = _rackMountDragHoverStartU.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryAddPendingMountToRack(string rackId, int startU, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(rackId))
        {
            error = "Rack not found.";
            return false;
        }

        switch (_rackAddMountCategory)
        {
            case 0:
                EnsureSortedServers();
                var filtSrv = BuildFilteredServerIndices(_rackMountServerSearchBuf ?? "");
                if (SortedServersBuffer.Count <= 0)
                {
                    error = "No servers in scene.";
                    return false;
                }

                if (!filtSrv.Contains(_rackMountPickIdx) || SortedServersBuffer[_rackMountPickIdx] == null)
                {
                    error = "Select a server from the filtered list.";
                    return false;
                }

                var srv = SortedServersBuffer[_rackMountPickIdx];
                int srvIid;
                try
                {
                    srvIid = srv.GetInstanceID();
                }
                catch
                {
                    srvIid = 0;
                }

                var hu = RackLayoutHelper.InferServerRackHeightU(srv);
                return RackDataStore.TryAddRackMount(rackId, RackDeviceTypes.Server, srvIid, null, startU, hu, out error);
            case 1:
            case 2:
                EnsureSortedSwitches();
                var filtSw = BuildFilteredNetworkSwitchIndices(_rackMountSwitchSearchBuf ?? "", _rackAddMountCategory == 2);
                if (SortedSwitchesBuffer.Count <= 0)
                {
                    error = "No switches/routers in scene.";
                    return false;
                }

                if (!filtSw.Contains(_rackMountSwitchPickIdx) || SortedSwitchesBuffer[_rackMountSwitchPickIdx] == null)
                {
                    error = "Select a device from the filtered list.";
                    return false;
                }

                var sw = SortedSwitchesBuffer[_rackMountSwitchPickIdx];
                int swIid;
                try
                {
                    swIid = sw.GetInstanceID();
                }
                catch
                {
                    swIid = 0;
                }

                var dtype = _rackAddMountCategory == 2 ? RackDeviceTypes.Router : RackDeviceTypes.Switch;
                return RackDataStore.TryAddRackMount(rackId, dtype, swIid, null, startU, 1, out error);
            case 3:
                return RackDataStore.TryAddRackMount(
                    rackId,
                    RackDeviceTypes.PatchPanel,
                    0,
                    _rackPatchLabelDraft,
                    startU,
                    2,
                    out error);
            default:
                error = "Unknown device type.";
                return false;
        }
    }

    private static void TryFinishRackMountDrag(string rackId)
    {
        string errDrop = null;
        var mouse = Event.current.mousePosition;
        if (_rackMountDragActive
            && _rackMountDropBodyLast.width > 0f
            && _rackMountDropBodyLast.Contains(mouse))
        {
            UpdateRackMountDragHover(_rackMountDropRackTotalU);
            if (_rackMountDragHoverStartU > 0)
            {
                if (TryAddPendingMountToRack(rackId, _rackMountDragHoverStartU, out errDrop))
                {
                    ShowIpamToast("Device added.");
                    RecomputeContentHeight();
                }
                else if (!string.IsNullOrEmpty(errDrop))
                {
                    ShowIpamToast(errDrop);
                }
                else
                {
                    ShowIpamToast("Could not add device to rack.");
                }
            }
        }

        _rackMountDragActive = false;
        _rackMountDragHoverStartU = -1;
        _rackMountDragLabel = "";
    }

    private static void RackMountPickRowSelect(Rect row, Action onSelect)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !row.Contains(e.mousePosition))
        {
            return;
        }

        onSelect?.Invoke();
        e.Use();
    }

    private static void RackMountPickRowDrag(
        Rect row,
        int controlHint,
        string rackId,
        int rackTotalU,
        string label,
        int heightU,
        Action onSelect)
    {
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, row);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (GUI.enabled && e.button == 0 && row.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    _rackMountDragActive = false;
                    _rackMountDragStartPos = e.mousePosition;
                    _rackMountDragLabel = label ?? "";
                    _rackMountDragHeightU = Mathf.Max(1, heightU);
                    onSelect?.Invoke();
                    e.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id)
                {
                    break;
                }

                if ((e.mousePosition - _rackMountDragStartPos).sqrMagnitude > 36f)
                {
                    _rackMountDragActive = true;
                }

                if (_rackMountDragActive)
                {
                    UpdateRackMountDragHover(rackTotalU);
                }

                e.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != id)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                TryFinishRackMountDrag(rackId);
                e.Use();
                break;
        }
    }

    private static void DrawRackMountDragGhost()
    {
        if (!_rackMountDragActive || Event.current.type != EventType.Repaint)
        {
            return;
        }

        var ghostW = 220f;
        var mp = Event.current.mousePosition;
        var ghost = new Rect(mp.x - ghostW * 0.5f, mp.y - 14f, ghostW, 28f);
        DrawTintedRect(ghost, RackMountDragGhost);
        var oldCc = GUI.contentColor;
        GUI.contentColor = Color.white;
        GUI.Label(ghost, _rackMountDragLabel, _stTableCell);
        GUI.contentColor = oldCc;
    }

    private static void DrawRackFrontDiagramDropTarget(Rect rackBody, string rackId, int rackTotalU)
    {
        if (!_rackMountDragActive)
        {
            return;
        }

        var id = GUIUtility.GetControlID(RackMountDragControlBase + 100, FocusType.Passive, rackBody);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDrag:
                UpdateRackMountDragHover(rackTotalU);
                e.Use();
                break;
            case EventType.MouseUp:
                if (e.button == 0)
                {
                    TryFinishRackMountDrag(rackId);
                    e.Use();
                }

                break;
        }
    }

    private static void DrawRackMountDragRow(Rect r, string rackId, int rackTotalU)
    {
        var label = GetPendingMountPreviewLabel();
        var heightU = GetPendingMountHeightU();
        if (Event.current.type == EventType.Repaint)
        {
            DrawTintedRect(r, RackPatchDragRowFill);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), _texTableHeader, ScaleMode.StretchToFill, false, 0f, RackPatchDragRowBorder, 0f, 0f);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), _texTableHeader, ScaleMode.StretchToFill, false, 0f, RackPatchDragRowBorder, 0f, 0f);
            GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), _texTableHeader, ScaleMode.StretchToFill, false, 0f, RackPatchDragRowBorder, 0f, 0f);
            GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), _texTableHeader, ScaleMode.StretchToFill, false, 0f, RackPatchDragRowBorder, 0f, 0f);
        }

        var oldCc = GUI.contentColor;
        GUI.contentColor = new Color(0.94f, 0.98f, 1f, 1f);
        GUI.Label(new Rect(r.x + 8f, r.y, r.width - 16f, r.height), $"{label}  ·  drag into front view →", _stTableCell);
        GUI.contentColor = oldCc;
        RackMountPickRowDrag(r, RackMountDragControlBase + 99, rackId, rackTotalU, label, heightU, null);
    }

    private static void DrawRackMountDropPreview(Rect rackBody, int totalU, int heightU, int startU)
    {
        if (!_rackMountDragActive || startU <= 0 || Event.current.type != EventType.Repaint)
        {
            return;
        }

        var tu = Mathf.Max(1, totalU);
        var h = Mathf.Max(1, heightU);
        var cell = rackBody.height / tu;
        var yTop = rackBody.yMax - (startU + h - 1) * cell;
        var preview = new Rect(rackBody.x + 3f, yTop, rackBody.width - 6f, h * cell - 1f);
        DrawTintedRect(preview, RackMountDragPreview);
        var oldCc = GUI.contentColor;
        GUI.contentColor = Color.white;
        GUI.Label(preview, $"U{startU}", _stTableCell);
        GUI.contentColor = oldCc;
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

        return DeviceInventoryReflection.InventorySearchQueryMatches(query, displayName);
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
    private static void TryDeselectRackPickIfClickedOutsideViewport(Rect viewportLast, ref int pickIdx)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0)
        {
            return;
        }

        // Pick-row drag sets hotControl on the same MouseDown — do not clear selection before drop.
        if (GUIUtility.hotControl != 0)
        {
            return;
        }

        var contentMouse = e.mousePosition;
        if (viewportLast.width > 0.5f && viewportLast.height > 0.5f && viewportLast.Contains(contentMouse))
        {
            return;
        }

        pickIdx = -1;
    }

    private static void DrawRackServerPickScroll(Rect outer, List<int> filtered, string rackId, int rackTotalU)
    {
        var innerH = filtered.Count * TableRowH;
        var viewH = Mathf.Min(160f, Mathf.Max(TableRowH + 4f, innerH));
        var innerW = outer.width - 18f;
        var innerRect = new Rect(0f, 0f, innerW, Mathf.Max(innerH, viewH));
        var scrollRect = new Rect(outer.x, outer.y, outer.width, viewH);
        _rackMountServerPickViewportLast = scrollRect;
        _rackMountServerListScroll = SafeBeginScrollView(scrollRect, _rackMountServerListScroll, innerRect);
        try
        {
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

                RackMountPickRowSelect(row, () => { _rackMountPickIdx = idx; });

                y += TableRowH;
            }
        }
        finally
        {
            SafeEndScrollView();
        }
    }

    private static void DrawRackSwitchPickScroll(Rect outer, List<int> filtered, string rackId, int rackTotalU)
    {
        var innerH = filtered.Count * TableRowH;
        var viewH = Mathf.Min(160f, Mathf.Max(TableRowH + 4f, innerH));
        var innerW = outer.width - 18f;
        var innerRect = new Rect(0f, 0f, innerW, Mathf.Max(innerH, viewH));
        var scrollRect = new Rect(outer.x, outer.y, outer.width, viewH);
        _rackMountSwitchPickViewportLast = scrollRect;
        _rackMountSwitchListScroll = SafeBeginScrollView(scrollRect, _rackMountSwitchListScroll, innerRect);
        try
        {
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

                RackMountPickRowSelect(row, () => { _rackMountSwitchPickIdx = idx; });

                y += TableRowH;
            }
        }
        finally
        {
            SafeEndScrollView();
        }
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
        var topBlock = SectionTitleH + 10f + 76f + 8f;
        ComputeRackFloorGridMetrics(cardW, out _, out _, out _, out _, out var gridTotalH);
        var gridBlock = SectionTitleH + 42f + gridTotalH + 16f;

        if (string.IsNullOrEmpty(_racksTabDrilledUnifiedId))
        {
            return Mathf.Max(420f, CardPad * 2f + topBlock + gridBlock);
        }

        var unified = BuildUnifiedRackList();
        var drilled = FindDrilledEntry(unified);
        if (drilled == null)
        {
            return Mathf.Max(420f, CardPad * 2f + topBlock + gridBlock);
        }

        var devices = BuildDiagramDevices(drilled, out _);
        var metaH = drilled.IsPersistedEditable ? 160f : 120f;
        var rowH = TableHeaderH + Mathf.Max(1, devices.Count) * TableRowH + (drilled.IsPersistedEditable ? 380f : 40f);
        var middleCol = metaH + rowH + 24f;
        var rightCol = SectionTitleH + RackDiagramFixedHeight + 56f;
        var body = Mathf.Max(middleCol, rightCol) + 36f;
        return Mathf.Max(480f, CardPad * 2f + topBlock + body);
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
            "Standard 47 U cabinets on a 16×32 floor grid (rows A–P, columns 1–32). Assign servers (3 U / 7 U), switches and routers (1 U), or patch panels (2 U). "
            + "Click a slot to open that rack. Scene-detected layouts appear when the game exposes asset lines.",
            _stHint);
        y += 80f;

        var unified = BuildUnifiedRackList();
        if (!string.IsNullOrEmpty(_racksTabDrilledUnifiedId)
            && unified.All(u => !string.Equals(u.UnifiedId, _racksTabDrilledUnifiedId, StringComparison.Ordinal)))
        {
            _racksTabDrilledUnifiedId = "";
        }

        var drilledEntry = FindDrilledEntry(unified);
        if (drilledEntry != null)
        {
            if (!string.Equals(drilledEntry.UnifiedId, _racksLastUnifiedId, StringComparison.Ordinal))
            {
                _racksLastUnifiedId = drilledEntry.UnifiedId;
                if (drilledEntry.Persisted != null)
                {
                    _rackRenameDraft = drilledEntry.Persisted.DisplayName ?? "";
                }
                else if (drilledEntry.SceneCopy != null)
                {
                    _rackRenameDraft = drilledEntry.SceneCopy.DisplayName ?? "Rack";
                }
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
            DrawRackFloorGrid(x0, ref y, cardW, unified, allowOpenOnClick: true);
            return;
        }

        var gap = 12f;
        var rightDiagW = Mathf.Clamp(cardW * 0.42f, 320f, 540f);
        var midW = cardW - rightDiagW - gap;
        if (midW < 240f)
        {
            rightDiagW = Mathf.Clamp(cardW * 0.40f, 300f, 540f);
            midW = cardW - rightDiagW - gap;
        }

        var mx0 = x0;
        var my = y;
        if (ImguiButtonOnce(new Rect(mx0, my, 140f, 24f), "\u2190 Floor plan", 9290, _stMutedBtn))
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
        var dx = mx0 + midW + gap;
        var yDiagCol = my;
        var ru = Mathf.Max(1, rackTotalU);
        var diagramH = RackDiagramFixedHeight;
        var unitLab = 56f;
        var rackBodyW = Mathf.Max(180f, rightDiagW - unitLab - 12f);
        var rackBodyRect = new Rect(dx + unitLab + 4f, yDiagCol + SectionTitleH + 6f, rackBodyW, diagramH);
        _rackMountDropBodyLast = rackBodyRect;
        _rackMountDropRackId = selected.IsPersistedEditable && selected.Persisted != null ? selected.Persisted.Id : "";
        _rackMountDropRackTotalU = ru;

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
                    DrawRackServerPickScroll(new Rect(mx0, yMid, midW, 162f), filteredSrv, selected.Persisted.Id, ru);
                    yMid += 162f;
                }

                if (sv > 0
                    && _rackMountPickIdx >= 0
                    && _rackMountPickIdx < SortedServersBuffer.Count
                    && SortedServersBuffer[_rackMountPickIdx] != null)
                {
                    var picked = SortedServersBuffer[_rackMountPickIdx];
                    var hintH = RackLayoutHelper.InferServerRackHeightU(picked);
                    var ff = DeviceInventoryReflection.GetServerFormFactorLabel(picked);
                    GUI.Label(
                        new Rect(mx0, yMid, midW, 22f),
                        $"Selected: {ff} ({hintH} U rack space) — drag row below into front view",
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
                    DrawRackSwitchPickScroll(new Rect(mx0, yMid, midW, 162f), filteredSw, selected.Persisted.Id, ru);
                    yMid += 162f;
                }

                GUI.Label(new Rect(mx0, yMid, midW, 22f), "Height: 1 U — select above, drag row below into front view", _stMuted);
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
                GUI.Label(new Rect(mx0, yMid, midW, 22f), "Height: 2 U — drag row below into front view", _stMuted);
            }

            yMid += 28f;
            if (CanStartRackMountDrag())
            {
                DrawRackMountDragRow(new Rect(mx0, yMid, midW, 34f), selected.Persisted.Id, ru);
                yMid += 40f;
            }

            if (_rackMountDragActive && _rackMountDragHoverStartU > 0)
            {
                GUI.Label(
                    new Rect(mx0, yMid, midW, 20f),
                    $"Drop at U {_rackMountDragHoverStartU} ({_rackMountDragHeightU} U tall)",
                    _stHint);
                yMid += 22f;
            }
            else if (!CanStartRackMountDrag() && _rackAddMountCategory != 3)
            {
                GUI.Label(
                    new Rect(mx0, yMid, midW, 20f),
                    "Select a device from the list above.",
                    _stHint);
                yMid += 22f;
            }
            else if (CanStartRackMountDrag())
            {
                GUI.Label(
                    new Rect(mx0, yMid, midW, 20f),
                    "Drag the row above into the front view.",
                    _stHint);
                yMid += 22f;
            }

            if (_rackAddMountCategory == 0)
            {
                TryDeselectRackPickIfClickedOutsideViewport(_rackMountServerPickViewportLast, ref _rackMountPickIdx);
            }
            else if (_rackAddMountCategory is 1 or 2)
            {
                TryDeselectRackPickIfClickedOutsideViewport(_rackMountSwitchPickViewportLast, ref _rackMountSwitchPickIdx);
            }
        }

        GUI.Label(new Rect(dx, yDiagCol, rightDiagW, SectionTitleH), "Front view", _stSectionTitle);
        yDiagCol += SectionTitleH + 6f;
        DrawRackFrontDiagramDevices(rackBodyRect, ru, diagramDevices, eff);
        DrawRackFrontDiagramDropTarget(rackBodyRect, selected.Persisted?.Id ?? _rackMountDropRackId, ru);
        DrawRackMountDropPreview(rackBodyRect, ru, _rackMountDragHeightU, _rackMountDragHoverStartU);
        DrawRackUnitLabels(new Rect(dx, yDiagCol, unitLab, diagramH), ru);
        DrawRackMountDragGhost();
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
            if (!string.IsNullOrEmpty(e.Persisted.GridRow)
                && e.Persisted.GridColumn >= 1
                && e.Persisted.GridColumn <= RackFloorColCount)
            {
                var rowIndex = char.ToUpperInvariant(e.Persisted.GridRow[0]) - 'A';
                if (rowIndex >= 0 && rowIndex < RackFloorRowCount)
                {
                    return FormatRackGridLabel(rowIndex, e.Persisted.GridColumn);
                }
            }

            return e.Persisted.DisplayName ?? "Rack";
        }

        return e?.SceneCopy?.DisplayName ?? "Rack";
    }

    private static void DrawRackFrontDiagramDevices(Rect rackBody, int totalU, IReadOnlyList<RackDiagramDevice> devices, int[] effStart)
    {
        if (Event.current.type == EventType.Repaint)
        {
            DrawTintedRect(rackBody, new Color(0.12f, 0.14f, 0.17f, 0.98f));
        }

        var tu = Mathf.Max(1, totalU);
        var cell = rackBody.height / tu;
        var lineColor = new Color(0.52f, 0.56f, 0.62f, 0.85f);

        for (var u = 1; u < tu; u++)
        {
            var yLine = rackBody.yMax - u * cell;
            GUI.DrawTexture(new Rect(rackBody.x, yLine, rackBody.width, 1f), _texTableHeader, ScaleMode.StretchToFill, false, 0f, lineColor, 0f, 0f);
        }

        var borderColor = new Color(0.58f, 0.64f, 0.72f, 0.95f);
        GUI.DrawTexture(new Rect(rackBody.x, rackBody.y, rackBody.width, 2f), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderColor, 0f, 0f);
        GUI.DrawTexture(new Rect(rackBody.x, rackBody.yMax - 1f, rackBody.width, 2f), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderColor, 0f, 0f);
        GUI.DrawTexture(new Rect(rackBody.x, rackBody.y, 2f, rackBody.height), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderColor, 0f, 0f);
        GUI.DrawTexture(new Rect(rackBody.xMax - 2f, rackBody.y, 2f, rackBody.height), _texTableHeader, ScaleMode.StretchToFill, false, 0f, borderColor, 0f, 0f);

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
