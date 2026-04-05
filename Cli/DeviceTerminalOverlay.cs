using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DHCPSwitches;

/// <summary>Separate draggable IMGUI window with a Cisco-style line editor (no TextField).</summary>
public static class DeviceTerminalOverlay
{
    private static bool _visible;
    private static Rect _windowRect = new(1040f, 80f, 640f, 420f);
    private static NetworkSwitch _target;
    private static CiscoLikeCliSession _session;
    private static readonly List<string> Scrollback = new();
    private static Vector2 _scroll;
    private static string _line = "";
    private const int MaxScrollLines = 200;

    private static readonly List<string> CommandHistory = new();
    private const int MaxCommandHistory = 100;
    private static int _historyNavIndex = -1;
    private static string _historySavedDraft = "";

    private static readonly StringBuilder ExecuteBuffer = new();

    private static Texture2D _texOpaqueBlack;
    private static GUIStyle _stCliWindowBg;
    private static GUIStyle _stCliText;
    private static bool _stylesReady;

    /// <summary>IMGUI often delivers two <see cref="EventType.KeyDown"/> events per physical key. They may use <c>character</c> with <c>KeyCode.None</c> vs <c>KeyCode.A</c> with <c>character == 0</c> — dedupe by the <b>logical glyph</b> (or command key) per frame.</summary>
    private static int _physicalKeyFrame = -1;
    private static readonly HashSet<int> PhysicalKeyDigests = new();

    private static bool _savedSendNavigationEvents = true;
    private static bool _navigationEventsOverridden;

    public static bool IsVisible => _visible;

    public static void OpenFor(NetworkSwitch sw)
    {
        if (sw == null)
        {
            return;
        }

        _target = sw;
        _session = new CiscoLikeCliSession(sw);
        _visible = true;
        Scrollback.Clear();
        _line = "";
        ApplyEventSystemCliFocus();
        AppendLine($"{_session.Prompt} (Tab completes; '?' for help; ↑/↓ history; Ctrl+C stops ping -t; unique abbreviations OK, e.g. sh → show)");
    }

    public static void Close()
    {
        DHCPSwitchesBehaviour.CancelPingVisuals();
        RestoreEventSystemAfterCli();
        _visible = false;
        _target = null;
        _session = null;
        _line = "";
        IPAMOverlay.ScheduleImguiInputRecovery();
        UiRaycastBlocker.SetBlocking(IPAMOverlay.IsVisible);
        GameInputSuppression.SetSuppressed(false);
        IpamMenuOcclusion.Tick(IPAMOverlay.IsVisible);
    }

    private static void ApplyEventSystemCliFocus()
    {
        var es = EventSystem.current;
        if (es == null)
        {
            return;
        }

        if (!_navigationEventsOverridden)
        {
            _savedSendNavigationEvents = es.sendNavigationEvents;
            _navigationEventsOverridden = true;
        }

        es.sendNavigationEvents = false;
    }

    private static void RestoreEventSystemAfterCli()
    {
        if (!_navigationEventsOverridden)
        {
            return;
        }

        var es = EventSystem.current;
        if (es != null)
        {
            es.sendNavigationEvents = _savedSendNavigationEvents;
        }

        _navigationEventsOverridden = false;
    }

    /// <summary>Call from <see cref="DHCPSwitchesBehaviour.Update"/> while the CLI is open: clears legacy input axes so <c>Input.GetKey</c> does not see keys.</summary>
    public static void TickCliInput()
    {
        if (!_visible)
        {
            return;
        }

        PumpInputSystemEscapeSinkForCli();
        LegacyInputAxes.TryReset();
    }

    /// <summary>Call from <see cref="DHCPSwitchesMod.OnUpdate"/> so Escape is cleared before other game scripts' Update when possible.</summary>
    internal static void PumpInputSystemEscapeSinkForCli()
    {
        if (!_visible)
        {
            return;
        }

        TryClearEscapeOnInputSystemKeyboard();
    }

    /// <summary>Gameplay/menus read Escape from the Input System <see cref="Keyboard"/>; clearing pressed state stops menus from closing while IMGUI can still see <see cref="EventType.KeyDown"/>.</summary>
    private static void TryClearEscapeOnInputSystemKeyboard()
    {
        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        var esc = kb.escapeKey;
        if (esc == null || !esc.isPressed)
        {
            return;
        }

        try
        {
            InputState.Change(esc, 0f);
        }
        catch (System.Exception ex)
        {
            ModLogging.Warning($"CLI: could not clear Escape on Input System keyboard: {ex.Message}");
        }
    }

    private static void SubmitCurrentLine()
    {
        if (_session == null || !TargetAlive())
        {
            Close();
            return;
        }

        var cmd = _line.TrimEnd();
        var trimmedForHistory = cmd.Trim();
        if (trimmedForHistory.Length > 0)
        {
            if (CommandHistory.Count == 0 || CommandHistory[^1] != trimmedForHistory)
            {
                CommandHistory.Add(trimmedForHistory);
                while (CommandHistory.Count > MaxCommandHistory)
                {
                    CommandHistory.RemoveAt(0);
                }
            }
        }

        _historyNavIndex = -1;
        _historySavedDraft = "";
        _line = "";
        AppendLine($"{_session.Prompt} {cmd}");
        ExecuteBuffer.Clear();
        _session.Execute(cmd, ExecuteBuffer);
        if (ExecuteBuffer.Length > 0)
        {
            AppendLine(ExecuteBuffer.ToString().TrimEnd('\n', '\r'));
        }
    }

    private static void HistoryNavigateUp()
    {
        if (CommandHistory.Count == 0)
        {
            return;
        }

        if (_historyNavIndex < 0)
        {
            _historySavedDraft = _line;
            _historyNavIndex = CommandHistory.Count - 1;
        }
        else if (_historyNavIndex > 0)
        {
            _historyNavIndex--;
        }

        _line = CommandHistory[_historyNavIndex];
    }

    private static void HistoryNavigateDown()
    {
        if (_historyNavIndex < 0)
        {
            return;
        }

        if (_historyNavIndex < CommandHistory.Count - 1)
        {
            _historyNavIndex++;
            _line = CommandHistory[_historyNavIndex];
        }
        else
        {
            _historyNavIndex = -1;
            _line = _historySavedDraft ?? "";
        }
    }

    /// <summary>What printable character this KeyDown would insert (IMGUI <c>character</c> first, else US-layout <c>keyCode</c>).</summary>
    private static bool TryPeekGlyphFromKeyEvent(Event e, out char ch)
    {
        ch = default;
        if (e.type != EventType.KeyDown)
        {
            return false;
        }

        if (e.character != 0 && e.character != '\n' && e.character != '\r' && !char.IsControl(e.character))
        {
            ch = e.character;
            return true;
        }

        var sh = e.shift;
        var k = e.keyCode;
        char? add = null;

        if (k >= KeyCode.A && k <= KeyCode.Z)
        {
            var c = (char)('a' + (k - KeyCode.A));
            add = sh ? char.ToUpperInvariant(c) : c;
        }
        else if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9)
        {
            var i = k - KeyCode.Alpha0;
            const string shifted = ")!@#$%^&*(";
            add = sh ? shifted[i] : (char)('0' + i);
        }
        else
        {
            add = k switch
            {
                KeyCode.Space => ' ',
                KeyCode.Minus => sh ? '_' : '-',
                KeyCode.Equals => sh ? '+' : '=',
                KeyCode.LeftBracket => sh ? '{' : '[',
                KeyCode.RightBracket => sh ? '}' : ']',
                KeyCode.Backslash => sh ? '|' : '\\',
                KeyCode.Semicolon => sh ? ':' : ';',
                KeyCode.Quote => sh ? '"' : '\'',
                KeyCode.Comma => sh ? '<' : ',',
                KeyCode.Period => sh ? '>' : '.',
                KeyCode.Slash => sh ? '?' : '/',
                KeyCode.BackQuote => sh ? '~' : '`',
                KeyCode.KeypadDivide => '/',
                KeyCode.KeypadMultiply => '*',
                KeyCode.KeypadMinus => '-',
                KeyCode.KeypadPlus => '+',
                KeyCode.KeypadPeriod => '.',
                KeyCode.KeypadEquals => '=',
                _ => null,
            };

            if (add == null && k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9)
            {
                add = (char)('0' + (k - KeyCode.Keypad0));
            }
        }

        if (add == null)
        {
            return false;
        }

        ch = add.Value;
        return true;
    }

    private static bool TryAppendFromKeyEvent(Event e, ref string line)
    {
        if (!TryPeekGlyphFromKeyEvent(e, out var ch))
        {
            return false;
        }

        if (line.Length < 512)
        {
            line += ch;
        }

        return true;
    }

    private static int CliKeyDownDigest(Event e)
    {
        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            unchecked
            {
                return (int)0xC10E0000 ^ (int)e.keyCode;
            }
        }

        if (e.keyCode == KeyCode.Backspace)
        {
            return unchecked((int)0xC10E0001);
        }

        if (e.keyCode == KeyCode.Escape)
        {
            return unchecked((int)0xC10E0002);
        }

        if (e.keyCode == KeyCode.Tab)
        {
            return unchecked((int)0xC10E0003);
        }

        if (e.control && e.keyCode == KeyCode.C)
        {
            return unchecked((int)0xC10E0006);
        }

        if (e.keyCode == KeyCode.UpArrow)
        {
            return unchecked((int)0xC10E0004);
        }

        if (e.keyCode == KeyCode.DownArrow)
        {
            return unchecked((int)0xC10E0005);
        }

        if (TryPeekGlyphFromKeyEvent(e, out var ch))
        {
            unchecked
            {
                return ((int)0xC10E1000 ^ ch) * 397;
            }
        }

        unchecked
        {
            var h = (int)0xC10E2000;
            h = (h * 397) ^ (int)e.keyCode;
            h = (h * 397) ^ (e.shift ? 1 : 0);
            h = (h * 397) ^ (e.control ? 1 : 0);
            h = (h * 397) ^ (e.alt ? 1 : 0);
            h = (h * 397) ^ (e.command ? 1 : 0);
            return h;
        }
    }

    private static void AppendLine(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return;
        }

        var normalized = s.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var part in normalized.Split('\n'))
        {
            var t = part.TrimEnd('\r');
            if (t.Length > 0)
            {
                Scrollback.Add(t);
            }
        }

        while (Scrollback.Count > MaxScrollLines)
        {
            Scrollback.RemoveAt(0);
        }

        _scroll.y = float.MaxValue;
    }

    private static void EnsureStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _texOpaqueBlack = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        _texOpaqueBlack.SetPixel(0, 0, new Color32(0, 0, 0, 255));
        _texOpaqueBlack.Apply();

        _stCliWindowBg = new GUIStyle();
        _stCliWindowBg.normal.background = _texOpaqueBlack;

        _stCliText = new GUIStyle();
        if (GUI.skin != null && GUI.skin.label != null)
        {
            _stCliText.font = GUI.skin.label.font;
        }

        _stCliText.fontSize = 12;
        _stCliText.normal.textColor = new Color32(200, 255, 200, 255);
        // Rich text / word wrap can clip the first glyph on some Unity + IMGUI scroll combinations (Il2CPP).
        _stCliText.richText = false;
        _stCliText.wordWrap = false;
        _stCliText.clipping = TextClipping.Overflow;

        _stylesReady = true;
    }

    private static bool TargetAlive()
    {
        return _target != null;
    }

    public static void Draw()
    {
        if (!_visible)
        {
            return;
        }

        if (_session == null || !TargetAlive())
        {
            Close();
            return;
        }

        EnsureStyles();

        var f = Time.frameCount;
        if (f != _physicalKeyFrame)
        {
            _physicalKeyFrame = f;
            PhysicalKeyDigests.Clear();
        }

        var oldDepth = GUI.depth;
        GUI.depth = 31999;
        GUI.FocusWindow(9002);
        var title = $"CLI · {_target.name}";
        _windowRect = GUI.Window(9002, _windowRect, (GUI.WindowFunction)DrawWindow, title, _stCliWindowBg);
        GUI.depth = oldDepth;
    }

    private static void DrawWindow(int id)
    {
        if (_session == null || !TargetAlive())
        {
            Close();
            return;
        }

        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            var digest = CliKeyDownDigest(e);
            if (PhysicalKeyDigests.Contains(digest))
            {
                e.Use();
            }
            else
            {
                PhysicalKeyDigests.Add(digest);
                if (e.control && e.keyCode == KeyCode.C)
                {
                    DHCPSwitchesBehaviour.CancelPingVisuals();
                    AppendLine("^C");
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    SubmitCurrentLine();
                }
                else if (e.keyCode == KeyCode.Backspace && _line.Length > 0)
                {
                    if (_historyNavIndex >= 0)
                    {
                        _historyNavIndex = -1;
                        _historySavedDraft = "";
                    }

                    _line = _line.Substring(0, _line.Length - 1);
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Close();
                }
                else if (e.keyCode == KeyCode.Tab)
                {
                    if (_historyNavIndex >= 0)
                    {
                        _historyNavIndex = -1;
                        _historySavedDraft = "";
                    }

                    _session.TryTabComplete(ref _line);
                }
                else if (e.keyCode == KeyCode.UpArrow)
                {
                    HistoryNavigateUp();
                }
                else if (e.keyCode == KeyCode.DownArrow)
                {
                    HistoryNavigateDown();
                }
                else
                {
                    _historyNavIndex = -1;
                    _historySavedDraft = "";
                    TryAppendFromKeyEvent(e, ref _line);
                }

                e.Use();
            }
        }

        var innerBg = new Rect(1, 18, _windowRect.width - 2, _windowRect.height - 20);
        GUI.DrawTexture(innerBg, _texOpaqueBlack, ScaleMode.StretchToFill);

        var body = new Rect(8, 22, _windowRect.width - 16, _windowRect.height - 30);
        _scroll = GUI.BeginScrollView(new Rect(body.x, body.y, body.width, body.height - 28), _scroll, new Rect(0, 0, body.width - 24, Mathf.Max(body.height - 28, Scrollback.Count * 18f + 8f)));
        var y = 0f;
        foreach (var ln in Scrollback)
        {
            GUI.Label(new Rect(4, y, body.width - 32, 18f), ln, _stCliText);
            y += 18f;
        }

        GUI.EndScrollView();

        var prompt = _session.Prompt + " " + _line + "_";
        GUI.Label(new Rect(body.x + 4, body.y + body.height - 24, body.width - 8, 22), prompt, _stCliText);

        if (GUI.Button(new Rect(_windowRect.width - 84, 4, 76, 18), "Close"))
        {
            Close();
        }

        GUI.DragWindow(new Rect(0, 0, _windowRect.width - 90, 20));
    }
}
