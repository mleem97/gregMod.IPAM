using UnityEngine;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

// Standalone IOPS calculator window (top-level GUI.Window), IMGUI KeyDown pump when no Input System keyboard, debug mouse line.

public static partial class IPAMOverlay
{
    private static int IopsCalcKeyDigest(Event e)
    {
        unchecked
        {
            if (e.keyCode == KeyCode.Escape)
            {
                return (int)0x1A000001;
            }

            if (e.keyCode == KeyCode.Backspace)
            {
                return (int)0x1A000002;
            }

            if (e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
            {
                return (int)0x1A001000 ^ (int)(e.keyCode - KeyCode.Alpha0);
            }

            if (e.keyCode >= KeyCode.Keypad0 && e.keyCode <= KeyCode.Keypad9)
            {
                return (int)0x1A002000 ^ (int)(e.keyCode - KeyCode.Keypad0);
            }

            if (e.keyCode == KeyCode.None && e.character >= '0' && e.character <= '9')
            {
                return (int)0x1A003000 ^ e.character;
            }

            return (int)0x1A004000 ^ (int)e.keyCode ^ (e.character * 397);
        }
    }

    /// <summary>
    /// Same physical key often produces two KeyDown events (e.g. Alpha1 vs character '1'). Register alternates so the second is ignored.
    /// </summary>
    private static void RegisterIopsDuplicateKeyDigests(Event e)
    {
        unchecked
        {
            if (e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
            {
                var d = (int)(e.keyCode - KeyCode.Alpha0);
                _iopsCalcKeyDigests.Add((int)0x1A003000 ^ ('0' + d));
            }
            else if (e.keyCode >= KeyCode.Keypad0 && e.keyCode <= KeyCode.Keypad9)
            {
                var d = (int)(e.keyCode - KeyCode.Keypad0);
                _iopsCalcKeyDigests.Add((int)0x1A003000 ^ ('0' + d));
            }
            else if (e.keyCode == KeyCode.None && e.character >= '0' && e.character <= '9')
            {
                var d = (int)(e.character - '0');
                _iopsCalcKeyDigests.Add((int)0x1A001000 ^ d);
                _iopsCalcKeyDigests.Add((int)0x1A002000 ^ d);
            }
        }
    }

    private static void PumpIopsCalculatorKeyboard()
    {
        // When the Input System keyboard exists, <see cref="TickIopsCalculatorInputSystem"/> owns digits/Esc/Backspace.
        // IMGUI KeyDown for the same physical key would append twice (e.g. "00" for one press of 0).
        if (Keyboard.current != null)
        {
            return;
        }

        var e = Event.current;
        if (e.type != EventType.KeyDown)
        {
            return;
        }

        var f = Time.frameCount;
        if (f != _iopsCalcKeyDedupeFrame)
        {
            _iopsCalcKeyDedupeFrame = f;
            _iopsCalcKeyDigests.Clear();
        }

        var digest = IopsCalcKeyDigest(e);
        if (_iopsCalcKeyDigests.Contains(digest))
        {
            e.Use();
            return;
        }

        _iopsCalcKeyDigests.Add(digest);
        RegisterIopsDuplicateKeyDigests(e);

        if (e.keyCode == KeyCode.Escape)
        {
            CloseIopsCalculatorModal("Escape");
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Backspace)
        {
            if (_iopsCalculatorDigits.Length > 0)
            {
                _iopsCalculatorDigits = _iopsCalculatorDigits.Substring(0, _iopsCalculatorDigits.Length - 1);
            }

            e.Use();
            return;
        }

        char? digit = null;
        if (e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
        {
            digit = (char)('0' + (e.keyCode - KeyCode.Alpha0));
        }
        else if (e.keyCode >= KeyCode.Keypad0 && e.keyCode <= KeyCode.Keypad9)
        {
            digit = (char)('0' + (e.keyCode - KeyCode.Keypad0));
        }
        else if (e.keyCode == KeyCode.None && e.character >= '0' && e.character <= '9')
        {
            digit = e.character;
        }

        if (digit != null && _iopsCalculatorDigits.Length < 14)
        {
            if (_iopsCalculatorDigits.Length == 0 && digit.Value == '0')
            {
                e.Use();
                return;
            }

            _iopsCalculatorDigits += digit.Value;
            e.Use();
        }
    }
    private static void CloseIopsCalculatorModal(string reason = null)
    {
        var wasOpen = _iopsCalculatorOpen;
        _iopsCalculatorOpen = false;
        if (wasOpen)
        {
            // Resync list + EOL on next tick (avoids stale table/EOL until some unrelated event invalidated cache).
            _nextListRefreshTime = 0f;
            _nextEolSnapshotRefreshTime = 0f;
            if (ModDebugLog.IsIpamFileLogEnabled && !string.IsNullOrEmpty(reason))
            {
                IpamDebugLog.IopsModalClosed(reason);
            }
        }
    }

    private static void OpenIopsCalculator()
    {
        _iopsCalculatorOpen = true;
        const float ww = 460f;
        const float wh = 280f;
        _iopsStandaloneWindowRect = new Rect(
            Mathf.Max(8f, (Screen.width - ww) * 0.5f),
            Mathf.Max(8f, (Screen.height - wh) * 0.5f),
            ww,
            wh);
    }

    /// <summary>One MouseDown line per frame when IPAM debug file is enabled (compare with [IOPS probe] from Update).</summary>
    private static void PumpIpamDebugOnGuiMouse()
    {
        if (!ModDebugLog.IsIpamFileLogEnabled)
        {
            return;
        }

        var e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0)
        {
            return;
        }

        if (_ipamDebugLastMouseDownFrame == Time.frameCount)
        {
            return;
        }

        _ipamDebugLastMouseDownFrame = Time.frameCount;
        IpamDebugLog.OnGuiMouseDown(_windowRect, e.mousePosition);
    }
    private static void DrawIopsStandaloneWindow(int id)
    {
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));

        // Solid shell (same pixels as IPAM) — avoids washed-out / tinted chrome from GUI.color or 9-slice gaps.
        if (Event.current.type == EventType.Repaint)
        {
            var fillW = _iopsStandaloneWindowRect.width;
            var oldGc = GUI.color;
            GUI.color = Color.white;
            var fillH = Mathf.Min(2000f, _iopsStandaloneWindowRect.height + 48f);
            GUI.DrawTexture(new Rect(0f, 0f, fillW, fillH), _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
            GUI.color = oldGc;
        }

        const float pad = 12f;
        const float resultPad = 10f;
        var w = _iopsStandaloneWindowRect.width;
        var innerW = w - pad * 2f;
        var x = pad;
        var y = 6f;

        GUI.Label(
            new Rect(x, y, innerW, 38f),
            "Simple sizing: servers needed = required IOPS ÷ per-server IOPS (rounded up). Mod constants only — not read from the game.",
            _stMuted);
        y += 40f;

        GUI.Label(new Rect(x, y, 160f, 22f), "Required IOPS", _stFormLabel);
        y += 24f;
        var fieldRect = new Rect(x, y, innerW, 28f);
        GUI.Box(fieldRect, GUIContent.none, _stMutedBtn);
        var disp = string.IsNullOrEmpty(_iopsCalculatorDigits) ? "(type digits — keyboard)" : _iopsCalculatorDigits + "_";
        GUI.Label(new Rect(fieldRect.x + 10f, fieldRect.y + 5f, fieldRect.width - 16f, 20f), disp, _stTableCell);
        y += 34f;

        GUI.Label(new Rect(x, y, innerW, 18f), "Server type (IOPS per server)", _stFormLabel);
        y += 22f;
        var half = (innerW - 8f) * 0.5f;
        var twoU = _iopsCalculatorServerKind == 0;
        var fourU = _iopsCalculatorServerKind == 1;
        if (GUI.Button(new Rect(x, y, half, 30f), $"2U  ({IopsPer2UServer:N0} IOPS)", twoU ? _stPrimaryBtn : _stMutedBtn))
        {
            _iopsCalculatorServerKind = 0;
        }

        if (GUI.Button(new Rect(x + half + 8f, y, half, 30f), $"4U  ({IopsPer4UServer:N0} IOPS)", fourU ? _stPrimaryBtn : _stMutedBtn))
        {
            _iopsCalculatorServerKind = 1;
        }

        y += 38f;

        var perServerKind = _iopsCalculatorServerKind == 0 ? IopsPer2UServer : IopsPer4UServer;
        string resultLine1;
        GUIStyle resultStyle1;
        if (string.IsNullOrEmpty(_iopsCalculatorDigits)
            || !ulong.TryParse(_iopsCalculatorDigits, out var reqIops)
            || reqIops == 0)
        {
            resultLine1 = "Enter a positive IOPS requirement to see server count.";
            resultStyle1 = _stIopsResultPlaceholder;
        }
        else
        {
            var need = (long)((reqIops + (ulong)perServerKind - 1UL) / (ulong)perServerKind);
            resultLine1 = $"Servers needed: {need}";
            resultStyle1 = _stIopsResult;
        }

        var textW = innerW - resultPad * 2f;
        var rh1 = resultStyle1.CalcHeight(new GUIContent(resultLine1), textW);

        var cardH = resultPad * 2f + rh1;
        var cardRect = new Rect(x, y, innerW, cardH);
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(cardRect, _texCard, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
        }

        GUI.Label(new Rect(x + resultPad, y + resultPad, textW, rh1), resultLine1, resultStyle1);

        y += cardH;

        y += 10f;
        const float footerRowH = 28f;
        const float bottomPad = 8f;
        if (GUI.Button(new Rect(x + innerW - 100f, y, 100f, footerRowH), "Close", _stMutedBtn))
        {
            CloseIopsCalculatorModal("Close button");
        }

        GUI.Label(new Rect(x, y + 2f, innerW - 108f, footerRowH), "Esc closes · Backspace erases digits", _stMuted);

        // Fit window to content (fixed 400px left a large empty band under the footer).
        var clientBottom = y + footerRowH + bottomPad;
        // Total window height = client area (this callback) + title bar; border.top alone is often 9-slice, not full chrome.
        const float titleBarApprox = 24f;
        var desiredTotalH = clientBottom + titleBarApprox;
        if (Mathf.Abs(_iopsStandaloneWindowRect.height - desiredTotalH) > 0.5f)
        {
            _iopsStandaloneWindowRect.height = desiredTotalH;
        }
    }
}
