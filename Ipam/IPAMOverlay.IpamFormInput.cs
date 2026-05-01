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
            return;
        }

        var kb = Keyboard.current;
        if (kb != null && !string.IsNullOrEmpty(_ipamPrefixEditId) && IpamEscapePressedThisFrame)
        {
            IpamClosePrefixEditPanel();
            return;
        }

        if (_ipamFormFieldFocus == IpamFormFocusNone)
        {
            return;
        }

        if (_navSection != NavSection.Ipam || (_ipamSub != IpamSubSection.Prefixes && _ipamSub != IpamSubSection.Vlans))
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
            return;
        }

        if (_ipamFormFieldFocus is IpamFormFocusEditPrefixName or IpamFormFocusEditPrefixTenant
            && (_ipamSub != IpamSubSection.Prefixes || string.IsNullOrEmpty(_ipamPrefixEditId)))
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
            return;
        }

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamFormBackspaceHeldSince = -1f;
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
            IpamFormFocusEditPrefixName => IpamTextFieldKind.Name,
            IpamFormFocusEditPrefixTenant => IpamTextFieldKind.Name,
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
            IpamFormFocusEditPrefixName => 128,
            IpamFormFocusEditPrefixTenant => 128,
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
            IpamFormFocusEditPrefixName => _ipamPrefixEditNameBuf ?? "",
            IpamFormFocusEditPrefixTenant => _ipamPrefixEditTenantBuf ?? "",
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
            case IpamFormFocusEditPrefixName:
                _ipamPrefixEditNameBuf = s;
                break;
            case IpamFormFocusEditPrefixTenant:
                _ipamPrefixEditTenantBuf = s;
                break;
        }
    }

    private static void IpamFormBackspaceFocused()
    {
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
        if (focused && (Mathf.FloorToInt(Time.realtimeSinceStartup * 2f) % 2 == 0))
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
            IpamFormFocusEditPrefixName => _ipamPrefixEditNameBuf ?? "",
            IpamFormFocusEditPrefixTenant => _ipamPrefixEditTenantBuf ?? "",
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
            if (!string.IsNullOrEmpty(_ipamPrefixEditId)
                && (_ipamFormFieldFocus == IpamFormFocusEditPrefixName || _ipamFormFieldFocus == IpamFormFocusEditPrefixTenant))
            {
                IpamClosePrefixEditPanel();
            }
            else
            {
                _ipamFormFieldFocus = IpamFormFocusNone;
            }

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

        var v = GetIpamFormFocusBuffer();
        if (v.Length >= maxLen)
        {
            return true;
        }

        SetIpamFormFocusBuffer(v + c);
        return true;
    }
}
