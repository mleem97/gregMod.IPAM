using System;
using System.Linq;
using UnityEngine;

namespace DHCPSwitches;

// Modal window (9006): create a child prefix from a free gap or edit an existing prefix (CIDR, name, tenant).

public static partial class IPAMOverlay
{
    private static void CloseIpamChildPrefixWizard()
    {
        _ipamChildPrefixWizardOpen = false;
        _ipamChildPrefixWizardParentFolderId = null;
        _ipamChildPrefixWizardEditEntryId = null;
        _ipamChildPrefixWizardCidrBuf = "";
        _ipamChildPrefixWizardNameBuf = "";
        _ipamChildPrefixWizardTenantBuf = "";
        _ipamChildPrefixWizardError = "";
        if (_ipamFormFieldFocus == IpamFormFocusWizardChildCidr
            || _ipamFormFieldFocus == IpamFormFocusWizardChildName
            || _ipamFormFieldFocus == IpamFormFocusWizardChildTenant)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
        }

        _ipamFormBackspaceHeldSince = -1f;
    }

    private static void OpenIpamChildPrefixWizardCreate(string parentFolderEntryId, string suggestedCidr)
    {
        _ipamChildPrefixWizardParentFolderId = parentFolderEntryId ?? "";
        _ipamChildPrefixWizardEditEntryId = null;
        _ipamChildPrefixWizardCidrBuf = (suggestedCidr ?? "").Trim();
        _ipamChildPrefixWizardNameBuf = "";
        _ipamChildPrefixWizardTenantBuf = "";
        _ipamChildPrefixWizardError = "";
        _ipamFormFieldFocus = IpamFormFocusNone;
        _ipamChildPrefixWizardOpen = true;
    }

    private static void OpenIpamChildPrefixWizardEdit(string entryId)
    {
        _ipamChildPrefixWizardParentFolderId = null;
        _ipamChildPrefixWizardEditEntryId = entryId;
        _ipamChildPrefixWizardError = "";
        var prefixes = IpamDataStore.GetPrefixes();
        var e = prefixes.FirstOrDefault(p => string.Equals(p.Id, entryId, StringComparison.Ordinal));
        if (e == null)
        {
            CloseIpamChildPrefixWizard();
            return;
        }

        _ipamChildPrefixWizardCidrBuf = (e.Cidr ?? "").Trim();
        _ipamChildPrefixWizardNameBuf = e.Name ?? "";
        _ipamChildPrefixWizardTenantBuf = e.Tenant ?? "";
        _ipamFormFieldFocus = IpamFormFocusNone;
        _ipamChildPrefixWizardOpen = true;
    }

    private static void ApplyIpamChildPrefixWizard()
    {
        _ipamChildPrefixWizardError = "";
        if (string.IsNullOrEmpty(_ipamChildPrefixWizardEditEntryId))
        {
            if (string.IsNullOrEmpty(_ipamChildPrefixWizardParentFolderId)
                || !Guid.TryParse(_ipamChildPrefixWizardParentFolderId, out var pg))
            {
                _ipamChildPrefixWizardError = "Invalid parent folder.";
                return;
            }

            if (!IpamDataStore.TryAddPrefix(
                    _ipamChildPrefixWizardCidrBuf,
                    _ipamChildPrefixWizardNameBuf,
                    _ipamChildPrefixWizardTenantBuf,
                    IpamPrefixParentMode.ExplicitParent,
                    pg,
                    out var err))
            {
                _ipamChildPrefixWizardError = err ?? "Could not add prefix.";
                return;
            }
        }
        else if (!IpamDataStore.TryUpdatePrefix(
                     _ipamChildPrefixWizardEditEntryId,
                     _ipamChildPrefixWizardCidrBuf,
                     _ipamChildPrefixWizardNameBuf,
                     _ipamChildPrefixWizardTenantBuf,
                     out var err2))
        {
            _ipamChildPrefixWizardError = err2 ?? "Could not update prefix.";
            return;
        }

        CloseIpamChildPrefixWizard();
        IpamPruneDrillAfterPrefixMutation();
        RecomputeContentHeight();
    }

    private static void DrawIpamChildPrefixWizardWindow(int windowId)
    {
        _ = windowId;
        var w = _ipamChildPrefixWizardRect.width;
        var dragW = Mathf.Max(40f, w - 92f);
        GUI.DragWindow(new Rect(0f, 0f, dragW, 28f));

        if (Event.current.type == EventType.Repaint)
        {
            var fillW = _ipamChildPrefixWizardRect.width;
            var fillH = Mathf.Min(2000f, _ipamChildPrefixWizardRect.height + 48f);
            var oldGc = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, fillW, fillH), _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
            GUI.color = oldGc;
        }

        var px = 12f;
        var py = 28f;
        var iw = w - 24f;
        var createMode = string.IsNullOrEmpty(_ipamChildPrefixWizardEditEntryId);
        GUI.Label(
            new Rect(px, py, iw, 22f),
            createMode ? "Create child prefix" : "Edit prefix",
            _stSectionTitle);
        py += 26f;

        GUI.Label(new Rect(px, py, 44f, 22f), "CIDR", _stFormLabel);
        DrawIpamFormTextField(new Rect(px + 48f, py, Mathf.Min(iw - 52f, 400f), 22f), IpamFormFocusWizardChildCidr, 64, IpamTextFieldKind.Cidr);
        py += 28f;

        GUI.Label(new Rect(px, py, 44f, 22f), "Name", _stFormLabel);
        DrawIpamFormTextField(new Rect(px + 48f, py, Mathf.Min(iw - 52f, 400f), 22f), IpamFormFocusWizardChildName, 128, IpamTextFieldKind.Name);
        py += 28f;

        GUI.Label(new Rect(px, py, 52f, 22f), "Tenant", _stFormLabel);
        DrawIpamFormTextField(new Rect(px + 56f, py, Mathf.Min(iw - 60f, 400f), 22f), IpamFormFocusWizardChildTenant, 128, IpamTextFieldKind.Name);
        py += 30f;

        if (!string.IsNullOrEmpty(_ipamChildPrefixWizardError))
        {
            GUI.Label(new Rect(px, py, iw, 40f), _ipamChildPrefixWizardError, _stError);
            py += 44f;
        }

        if (createMode)
        {
            GUI.Label(
                new Rect(px, py, iw, 36f),
                "CIDR must be a strict subnet of the parent folder. Adjust prefix length to resize (e.g. /24, /25).",
                _stHint);
            py += 40f;
        }
        else
        {
            GUI.Label(
                new Rect(px, py, iw, 36f),
                "Changing CIDR must keep all child prefixes inside the new range and must not overlap siblings.",
                _stHint);
            py += 40f;
        }

        var btnY = _ipamChildPrefixWizardRect.height - 40f;
        if (ImguiButtonOnce(new Rect(px, btnY, 120f, 30f), "Save", 9140, _stPrimaryBtn))
        {
            ApplyIpamChildPrefixWizard();
        }

        if (ImguiButtonOnce(new Rect(px + 130f, btnY, 120f, 30f), "Cancel", 9141, _stMutedBtn))
        {
            CloseIpamChildPrefixWizard();
        }

        if (ImguiButtonOnce(new Rect(w - 84f, 6f, 72f, 22f), "Close", 9142, _stMutedBtn))
        {
            CloseIpamChildPrefixWizard();
        }
    }
}
