using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

// Prefix/VLAN form text entry without GUI.TextField — IL2CPP games strip TextEditor APIs used by that control.

public static partial class IPAMOverlay
{
    private enum IpamTextFieldKind
    {
        Cidr,
        VlanIdDigits,
        Name,
    }

    private const float IpamBackspaceRepeatInitialDelay = 0.48f;
    private const float IpamBackspaceRepeatSlowInterval = 0.075f;
    private const float IpamBackspaceRepeatFastInterval = 0.035f;
    private static float _ipamFormBackspaceHeldSince = -1f;
    private static float _ipamFormLastBackspaceRepeatTime;

    public static void TickIpamFormInputSystem()
    {
        if (!IsVisible || !LicenseManager.IsIPAMUnlocked)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
            _ipamFormRackMountStartUSelectAll = false;
            return;
        }

        var kb = Keyboard.current;
        if (kb != null && _ipamChildPrefixWizardOpen && IpamEscapePressedThisFrame)
        {
            CloseIpamChildPrefixWizard();
            return;
        }

        if (_ipamFormFieldFocus != IpamFormFocusRackMountStartU)
        {
            _ipamFormRackMountStartUSelectAll = false;
        }

        if (_ipamFormFieldFocus == IpamFormFocusNone)
        {
            return;
        }

        var wizardChildPrefixFocus =
            _ipamChildPrefixWizardOpen
            && _navSection == NavSection.Ipam
            && _ipamSub == IpamSubSection.Prefixes
            && (_ipamFormFieldFocus == IpamFormFocusWizardChildCidr
                || _ipamFormFieldFocus == IpamFormFocusWizardChildName
                || _ipamFormFieldFocus == IpamFormFocusWizardChildTenant);

        var inlinePrefixSearchFocus =
            ShouldDrawServerEditPopup()
            && !_serverEditPopupDismissed
            && _inlineAssignCustomer != null
            && _ipamFormFieldFocus == IpamFormFocusInlinePrefixSearch;
        var racksTextFocus =
            _navSection == NavSection.Racks
            && _ipamFormFieldFocus >= IpamFormFocusRackNewName
            && _ipamFormFieldFocus <= IpamFormFocusRackMountSwitchSearch;
        if (!wizardChildPrefixFocus
            && !inlinePrefixSearchFocus
            && !racksTextFocus
            && (_navSection != NavSection.Ipam || (_ipamSub != IpamSubSection.Prefixes && _ipamSub != IpamSubSection.Vlans)))
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
            return;
        }

        if (_ipamFormFieldFocus == IpamFormFocusInlinePrefixSearch
            && (!ShouldDrawServerEditPopup() || _serverEditPopupDismissed || _inlineAssignCustomer == null))
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
            return;
        }

        if (_ipamFormFieldFocus is IpamFormFocusWizardChildCidr or IpamFormFocusWizardChildName or IpamFormFocusWizardChildTenant
            && (!_ipamChildPrefixWizardOpen || _navSection != NavSection.Ipam || _ipamSub != IpamSubSection.Prefixes))
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
            return;
        }

        if (kb == null)
        {
            return;
        }

        if (IpamEscapePressedThisFrame || kb.tabKey.wasPressedThisFrame)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
            _ipamFormRackMountStartUSelectAll = false;
            return;
        }

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
            _ipamFormRackMountStartUSelectAll = false;
            return;
        }

        var kind = GetIpamFormFocusKind();
        var maxLen = GetIpamFormFocusMaxLen();
        if (maxLen <= 0)
        {
            return;
        }

        if (kb.backspaceKey.wasReleasedThisFrame)
        {
            _ipamFormBackspaceHeldSince = -1f;
        }
        else if (kb.backspaceKey.wasPressedThisFrame)
        {
            IpamFormBackspaceFocused();
            _ipamFormBackspaceHeldSince = Time.realtimeSinceStartup;
            _ipamFormLastBackspaceRepeatTime = Time.realtimeSinceStartup;
            return;
        }
        else if (_ipamFormBackspaceHeldSince >= 0f && kb.backspaceKey.isPressed)
        {
            var buf = GetIpamFormFocusBuffer();
            if (string.IsNullOrEmpty(buf))
            {
                _ipamFormBackspaceHeldSince = -1f;
            }
            else
            {
                var held = Time.realtimeSinceStartup - _ipamFormBackspaceHeldSince;
                if (held >= IpamBackspaceRepeatInitialDelay)
                {
                    var interval = held >= 1.15f ? IpamBackspaceRepeatFastInterval : IpamBackspaceRepeatSlowInterval;
                    if (Time.realtimeSinceStartup - _ipamFormLastBackspaceRepeatTime >= interval)
                    {
                        _ipamFormLastBackspaceRepeatTime = Time.realtimeSinceStartup;
                        IpamFormBackspaceFocused();
                    }
                }
            }
        }

        if (kind == IpamTextFieldKind.VlanIdDigits)
        {
            for (var d = 0; d <= 9; d++)
            {
                if (!kb[IopsDigitKeys[d]].wasPressedThisFrame && !kb[IopsNumpadKeys[d]].wasPressedThisFrame)
                {
                    continue;
                }

                IpamFormTryAppendChar((char)('0' + d), maxLen, kind);
                return;
            }

            return;
        }

        if (kind == IpamTextFieldKind.Cidr)
        {
            for (var d = 0; d <= 9; d++)
            {
                if (!kb[IopsDigitKeys[d]].wasPressedThisFrame && !kb[IopsNumpadKeys[d]].wasPressedThisFrame)
                {
                    continue;
                }

                IpamFormTryAppendChar((char)('0' + d), maxLen, kind);
                return;
            }

            if (kb.periodKey.wasPressedThisFrame)
            {
                IpamFormTryAppendChar('.', maxLen, kind);
                return;
            }

            if (kb.slashKey.wasPressedThisFrame)
            {
                IpamFormTryAppendChar('/', maxLen, kind);
                return;
            }

            return;
        }

        // Name (prefix or VLAN)
        for (var d = 0; d <= 9; d++)
        {
            if (!kb[IopsDigitKeys[d]].wasPressedThisFrame && !kb[IopsNumpadKeys[d]].wasPressedThisFrame)
            {
                continue;
            }

            IpamFormTryAppendChar((char)('0' + d), maxLen, kind);
            return;
        }

        if (kb.spaceKey.wasPressedThisFrame)
        {
            IpamFormTryAppendChar(' ', maxLen, kind);
            return;
        }

        if (kb.periodKey.wasPressedThisFrame)
        {
            IpamFormTryAppendChar('.', maxLen, kind);
            return;
        }

        if (kb.minusKey.wasPressedThisFrame)
        {
            IpamFormTryAppendChar(kb.shiftKey.isPressed ? '_' : '-', maxLen, kind);
            return;
        }

        for (var i = 0; i < 26; i++)
        {
            var key = (Key)((int)Key.A + i);
            if (!kb[key].wasPressedThisFrame)
            {
                continue;
            }

            var ch = (char)('a' + i);
            if (kb.shiftKey.isPressed)
            {
                ch = char.ToUpperInvariant(ch);
            }

            IpamFormTryAppendChar(ch, maxLen, kind);
            return;
        }
    }

    private static IpamTextFieldKind GetIpamFormFocusKind()
    {
        return _ipamFormFieldFocus switch
        {
            IpamFormFocusPrefixCidr => IpamTextFieldKind.Cidr,
            IpamFormFocusPrefixName => IpamTextFieldKind.Name,
            IpamFormFocusVlanId => IpamTextFieldKind.VlanIdDigits,
            IpamFormFocusVlanName => IpamTextFieldKind.Name,
            IpamFormFocusInlinePrefixSearch => IpamTextFieldKind.Name,
            IpamFormFocusWizardChildCidr => IpamTextFieldKind.Cidr,
            IpamFormFocusWizardChildName => IpamTextFieldKind.Name,
            IpamFormFocusWizardChildTenant => IpamTextFieldKind.Name,
            IpamFormFocusRackMountStartU => IpamTextFieldKind.VlanIdDigits,
            IpamFormFocusRackMountServerSearch => IpamTextFieldKind.Name,
            IpamFormFocusRackMountSwitchSearch => IpamTextFieldKind.Name,
            _ => IpamTextFieldKind.Name,
        };
    }

    private static int GetIpamFormFocusMaxLen()
    {
        return _ipamFormFieldFocus switch
        {
            IpamFormFocusPrefixCidr => 64,
            IpamFormFocusPrefixName => 128,
            IpamFormFocusVlanId => 4,
            IpamFormFocusVlanName => 128,
            IpamFormFocusInlinePrefixSearch => 128,
            IpamFormFocusWizardChildCidr => 64,
            IpamFormFocusWizardChildName => 128,
            IpamFormFocusWizardChildTenant => 128,
            IpamFormFocusRackNewName => 96,
            IpamFormFocusRackRename => 96,
            IpamFormFocusRackMountStartU => 2,
            IpamFormFocusRackPatchLabel => 96,
            IpamFormFocusRackMountServerSearch => 96,
            IpamFormFocusRackMountSwitchSearch => 96,
            _ => 0,
        };
    }

    private static string GetIpamFormFocusBuffer()
    {
        return _ipamFormFieldFocus switch
        {
            IpamFormFocusPrefixCidr => _ipamPrefixFormCidr ?? "",
            IpamFormFocusPrefixName => _ipamPrefixFormName ?? "",
            IpamFormFocusVlanId => _ipamVlanFormId ?? "",
            IpamFormFocusVlanName => _ipamVlanFormName ?? "",
            IpamFormFocusInlinePrefixSearch => _inlineIpamPrefixSearchBuf ?? "",
            IpamFormFocusWizardChildCidr => _ipamChildPrefixWizardCidrBuf ?? "",
            IpamFormFocusWizardChildName => _ipamChildPrefixWizardNameBuf ?? "",
            IpamFormFocusWizardChildTenant => _ipamChildPrefixWizardTenantBuf ?? "",
            IpamFormFocusRackNewName => _rackFormNewName ?? "",
            IpamFormFocusRackRename => _rackRenameDraft ?? "",
            IpamFormFocusRackMountStartU => _rackMountStartU ?? "",
            IpamFormFocusRackPatchLabel => _rackPatchLabelDraft ?? "",
            IpamFormFocusRackMountServerSearch => _rackMountServerSearchBuf ?? "",
            IpamFormFocusRackMountSwitchSearch => _rackMountSwitchSearchBuf ?? "",
            _ => "",
        };
    }

    private static void SetIpamFormFocusBuffer(string s)
    {
        switch (_ipamFormFieldFocus)
        {
            case IpamFormFocusPrefixCidr:
                _ipamPrefixFormCidr = s;
                break;
            case IpamFormFocusPrefixName:
                _ipamPrefixFormName = s;
                break;
            case IpamFormFocusVlanId:
                _ipamVlanFormId = s;
                break;
            case IpamFormFocusVlanName:
                _ipamVlanFormName = s;
                break;
            case IpamFormFocusInlinePrefixSearch:
                _inlineIpamPrefixSearchBuf = s;
                break;
            case IpamFormFocusWizardChildCidr:
                _ipamChildPrefixWizardCidrBuf = s;
                break;
            case IpamFormFocusWizardChildName:
                _ipamChildPrefixWizardNameBuf = s;
                break;
            case IpamFormFocusWizardChildTenant:
                _ipamChildPrefixWizardTenantBuf = s;
                break;
            case IpamFormFocusRackNewName:
                _rackFormNewName = s;
                break;
            case IpamFormFocusRackRename:
                _rackRenameDraft = s;
                break;
            case IpamFormFocusRackMountStartU:
                _rackMountStartU = s;
                break;
            case IpamFormFocusRackPatchLabel:
                _rackPatchLabelDraft = s;
                break;
            case IpamFormFocusRackMountServerSearch:
                _rackMountServerSearchBuf = s;
                break;
            case IpamFormFocusRackMountSwitchSearch:
                _rackMountSwitchSearchBuf = s;
                break;
        }
    }

    private static void IpamFormBackspaceFocused()
    {
        if (_ipamFormFieldFocus == IpamFormFocusRackMountStartU && _ipamFormRackMountStartUSelectAll)
        {
            _ipamFormRackMountStartUSelectAll = false;
            SetIpamFormFocusBuffer("");
            return;
        }

        var v = GetIpamFormFocusBuffer();
        if (v.Length > 0)
        {
            SetIpamFormFocusBuffer(v.Substring(0, v.Length - 1));
        }
    }

    private static void IpamFormTryAppendChar(char c, int maxLen, IpamTextFieldKind kind)
    {
        if (!IpamFormCharAllowed(c, kind))
        {
            return;
        }

        if (_ipamFormFieldFocus == IpamFormFocusRackMountStartU && _ipamFormRackMountStartUSelectAll)
        {
            _ipamFormRackMountStartUSelectAll = false;
            SetIpamFormFocusBuffer(char.ToString(c));
            return;
        }

        var v = GetIpamFormFocusBuffer();
        if (v.Length >= maxLen)
        {
            return;
        }

        SetIpamFormFocusBuffer(v + c);
    }

    private static bool IpamFormCharAllowed(char c, IpamTextFieldKind kind)
    {
        return kind switch
        {
            IpamTextFieldKind.Cidr => char.IsDigit(c) || c == '.' || c == '/',
            IpamTextFieldKind.VlanIdDigits => char.IsDigit(c),
            IpamTextFieldKind.Name => c >= ' ' && c <= '~' && c != '"' && c != '\'',
            _ => false,
        };
    }

    private static void DrawIpamFormTextField(Rect r, int focusSlot, int maxLen, IpamTextFieldKind kind)
    {
        var id = GUIUtility.GetControlID(0x5C1000 + focusSlot, FocusType.Keyboard, r);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            _ipamFormFieldFocus = focusSlot;
            _activeOctetSlot = -1;
            GUIUtility.keyboardControl = id;
            _ipamFormRackMountStartUSelectAll = focusSlot == IpamFormFocusRackMountStartU;
            e.Use();
        }

        if (_ipamFormFieldFocus == focusSlot && e.type == EventType.KeyDown && Keyboard.current == null)
        {
            if (TryIpamFormTextFieldImguiKeyDown(e, maxLen, kind))
            {
                e.Use();
            }
        }

        if (e.type != EventType.Repaint)
        {
            return;
        }

        var v = GetIpamFormFocusBufferForSlot(focusSlot);
        var focused = _ipamFormFieldFocus == focusSlot;
        var bg = focused ? new Color(0.08f, 0.1f, 0.14f, 1f) : new Color(0.06f, 0.07f, 0.09f, 1f);
        DrawTintedRect(r, bg);
        var pad = 4f;
        var disp = v ?? "";
        var selectAll =
            focused
            && focusSlot == IpamFormFocusRackMountStartU
            && _ipamFormRackMountStartUSelectAll
            && disp.Length > 0
            && _stTableCell != null;
        if (selectAll)
        {
            var tw = Mathf.Min(_stTableCell.CalcSize(new GUIContent(disp)).x + 3f, r.width - pad * 2f);
            DrawTintedRect(
                new Rect(r.x + pad, r.y + 3f, Mathf.Max(tw, 10f), r.height - 6f),
                new Color(0.26f, 0.44f, 0.72f, 0.42f));
        }

        var showCaret =
            focused
            && !selectAll
            && (Mathf.FloorToInt(Time.realtimeSinceStartup * 2f) % 2 == 0);
        if (showCaret)
        {
            disp += "|";
        }

        GUI.Label(new Rect(r.x + pad, r.y + 2f, r.width - pad * 2f, r.height - 4f), disp, _stTableCell);
    }

    private static string GetIpamFormFocusBufferForSlot(int focusSlot)
    {
        return focusSlot switch
        {
            IpamFormFocusPrefixCidr => _ipamPrefixFormCidr ?? "",
            IpamFormFocusPrefixName => _ipamPrefixFormName ?? "",
            IpamFormFocusVlanId => _ipamVlanFormId ?? "",
            IpamFormFocusVlanName => _ipamVlanFormName ?? "",
            IpamFormFocusInlinePrefixSearch => _inlineIpamPrefixSearchBuf ?? "",
            IpamFormFocusWizardChildCidr => _ipamChildPrefixWizardCidrBuf ?? "",
            IpamFormFocusWizardChildName => _ipamChildPrefixWizardNameBuf ?? "",
            IpamFormFocusWizardChildTenant => _ipamChildPrefixWizardTenantBuf ?? "",
            IpamFormFocusRackNewName => _rackFormNewName ?? "",
            IpamFormFocusRackRename => _rackRenameDraft ?? "",
            IpamFormFocusRackMountStartU => _rackMountStartU ?? "",
            IpamFormFocusRackPatchLabel => _rackPatchLabelDraft ?? "",
            IpamFormFocusRackMountServerSearch => _rackMountServerSearchBuf ?? "",
            IpamFormFocusRackMountSwitchSearch => _rackMountSwitchSearchBuf ?? "",
            _ => "",
        };
    }

    private static bool TryIpamFormTextFieldImguiKeyDown(Event e, int maxLen, IpamTextFieldKind kind)
    {
        if (_ipamFormFieldFocus == IpamFormFocusNone)
        {
            return false;
        }

        if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Tab)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormRackMountStartUSelectAll = false;
            return true;
        }

        if (e.keyCode == KeyCode.Backspace)
        {
            IpamFormBackspaceFocused();
            return true;
        }

        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormRackMountStartUSelectAll = false;
            return true;
        }

        var c = e.character;
        if (c == '\0' || char.IsControl(c))
        {
            return false;
        }

        if (!IpamFormCharAllowed(c, kind))
        {
            return false;
        }

        if (_ipamFormFieldFocus == IpamFormFocusRackMountStartU && _ipamFormRackMountStartUSelectAll)
        {
            _ipamFormRackMountStartUSelectAll = false;
            SetIpamFormFocusBuffer(char.ToString(c));
            return true;
        }

        var v = GetIpamFormFocusBuffer();
        if (v.Length >= maxLen)
        {
            return true;
        }

        SetIpamFormFocusBuffer(v + c);
        return true;
    }
}
