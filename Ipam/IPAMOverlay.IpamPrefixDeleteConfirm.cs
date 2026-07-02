using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GregModIPAM;

// Confirmation modal before deleting a parent prefix (removes entire subtree).

public static partial class IPAMOverlay
{
    private static void CloseIpamPrefixDeleteConfirm()
    {
        _ipamPrefixDeleteConfirmOpen = false;
        _ipamPrefixDeleteConfirmId = null;
        _ipamPrefixDeleteConfirmChildLabels.Clear();
        _ipamPrefixDeleteConfirmHeadline = "";
    }

    private static void OpenIpamPrefixDeleteConfirm(
        string prefixId,
        IpamPrefixEntry root,
        IReadOnlyList<IpamPrefixEntry> descendants)
    {
        _ipamPrefixDeleteConfirmId = prefixId;
        _ipamPrefixDeleteConfirmChildLabels.Clear();
        foreach (var ch in descendants)
        {
            if (ch == null)
            {
                continue;
            }

            var cc = (ch.Cidr ?? "").Trim();
            var nm = (ch.Name ?? "").Trim();
            _ipamPrefixDeleteConfirmChildLabels.Add(string.IsNullOrEmpty(nm) ? cc : $"{cc} ({nm})");
        }

        var rc = (root?.Cidr ?? "").Trim();
        var rn = (root?.Name ?? "").Trim();
        var rootLabel = string.IsNullOrEmpty(rn) ? rc : $"{rc} ({rn})";
        var childN = descendants.Count;
        _ipamPrefixDeleteConfirmHeadline =
            $"Delete {rootLabel} and {childN} child prefix{(childN == 1 ? "" : "es")}?";

        const float ww = 520f;
        var bodyLines = 3 + Math.Min(_ipamPrefixDeleteConfirmChildLabels.Count, 8);
        if (_ipamPrefixDeleteConfirmChildLabels.Count > 8)
        {
            bodyLines++;
        }

        var wh = 118f + bodyLines * 18f;
        _ipamPrefixDeleteConfirmRect = new Rect(
            Mathf.Max(8f, (Screen.width - ww) * 0.5f),
            Mathf.Max(8f, (Screen.height - wh) * 0.5f),
            ww,
            wh);
        _ipamPrefixDeleteConfirmOpen = true;
    }

    private static void RequestIpamPrefixDelete(string prefixId)
    {
        _ipamPrefixFormError = "";
        if (string.IsNullOrEmpty(prefixId) || !Guid.TryParse(prefixId, out var delId))
        {
            _ipamPrefixFormError = "Select a prefix to delete (subtree is removed).";
            return;
        }

        if (!IpamDataStore.TryDescribePrefixSubtree(prefixId, out var root, out var total, out var children))
        {
            _ipamPrefixFormError = "Prefix not found.";
            return;
        }

        if (total <= 1)
        {
            if (!IpamDataStore.TryDeletePrefix(delId, out var err))
            {
                _ipamPrefixFormError = err ?? "Delete failed.";
            }
            else
            {
                _ipamSelectedPrefixId = null;
                IpamPruneDrillAfterPrefixMutation();
                RecomputeContentHeight();
            }

            return;
        }

        OpenIpamPrefixDeleteConfirm(prefixId, root, children);
    }

    private static void ConfirmIpamPrefixDelete()
    {
        if (string.IsNullOrEmpty(_ipamPrefixDeleteConfirmId)
            || !Guid.TryParse(_ipamPrefixDeleteConfirmId, out var delId))
        {
            CloseIpamPrefixDeleteConfirm();
            return;
        }

        if (!IpamDataStore.TryDeletePrefix(delId, out var err))
        {
            _ipamPrefixFormError = err ?? "Delete failed.";
        }
        else
        {
            _ipamSelectedPrefixId = null;
            IpamPruneDrillAfterPrefixMutation();
            RecomputeContentHeight();
        }

        CloseIpamPrefixDeleteConfirm();
    }

    private static void DrawIpamPrefixDeleteConfirmWindow(int windowId)
    {
        _ = windowId;
        if (IpamEscapePressedThisFrame)
        {
            CloseIpamPrefixDeleteConfirm();
            return;
        }

        if (Event.current.type == EventType.Repaint)
        {
            var fillW = _ipamPrefixDeleteConfirmRect.width;
            var fillH = Mathf.Min(2000f, _ipamPrefixDeleteConfirmRect.height + 48f);
            var oldGc = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, fillW, fillH), _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
            GUI.color = oldGc;
        }

        var px = 14f;
        var py = 28f;
        var iw = _ipamPrefixDeleteConfirmRect.width - 28f;
        GUI.Label(new Rect(px, py, iw, 24f), "Delete parent prefix?", _stSectionTitle);
        py += 28f;
        GUI.Label(new Rect(px, py, iw, 22f), _ipamPrefixDeleteConfirmHeadline, _stError);
        py += 26f;
        GUI.Label(
            new Rect(px, py, iw, 36f),
            "This removes every subnet underneath it from IPAM. This cannot be undone.",
            _stHint);
        py += 38f;

        if (_ipamPrefixDeleteConfirmChildLabels.Count > 0)
        {
            GUI.Label(new Rect(px, py, iw, 18f), "Child prefixes that will be removed:", _stFormLabel);
            py += 20f;

            var show = Math.Min(_ipamPrefixDeleteConfirmChildLabels.Count, 8);
            for (var i = 0; i < show; i++)
            {
                GUI.Label(new Rect(px + 8f, py, iw - 8f, 18f), "•  " + _ipamPrefixDeleteConfirmChildLabels[i], _stTableCell);
                py += 18f;
            }

            if (_ipamPrefixDeleteConfirmChildLabels.Count > show)
            {
                var more = _ipamPrefixDeleteConfirmChildLabels.Count - show;
                GUI.Label(
                    new Rect(px + 8f, py, iw - 8f, 18f),
                    $"… and {more.ToString(CultureInfo.InvariantCulture)} more",
                    _stMuted);
                py += 18f;
            }
        }

        var btnY = _ipamPrefixDeleteConfirmRect.height - 40f;
        if (ImguiButtonOnce(new Rect(px, btnY, 140f, 30f), "Delete all", 9150, _stPrimaryBtn))
        {
            ConfirmIpamPrefixDelete();
        }

        if (ImguiButtonOnce(new Rect(px + 150f, btnY, 120f, 30f), "Cancel", 9151, _stMutedBtn))
        {
            CloseIpamPrefixDeleteConfirm();
        }

        if (ImguiButtonOnce(new Rect(_ipamPrefixDeleteConfirmRect.width - 84f, 6f, 72f, 22f), "Close", 9152, _stMutedBtn))
        {
            CloseIpamPrefixDeleteConfirm();
        }
    }
}
