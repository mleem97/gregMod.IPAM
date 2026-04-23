using System;
using UnityEngine;

namespace DHCPSwitches;

// IMGUI primitives for IPAM: 1x1 textures, GUIStyle construction, deduped buttons, octet/IOPS toolbar controls.
// Does not own: device list ticks (Lifecycle), table sort/EOL (InventoryTable), main window layout (WindowUi).

public static partial class IPAMOverlay
{
    private static RectOffset Ro(int l, int r, int t, int b)
    {
        var o = new RectOffset();
        o.left = l;
        o.right = r;
        o.top = t;
        o.bottom = b;
        return o;
    }

    /// <summary>
    /// <see cref="GUI.Window"/> may run the window function several times per mouse release (layout/repaint).
    /// <see cref="GUI.Button"/> can then return true multiple times in one frame — dedupe per control key (see hub fields <c>_imguiButtonDedupe*</c>).
    /// </summary>
    private static bool ImguiButtonOnce(Rect r, string text, int dedupeKey, GUIStyle style = null)
    {
        var pressed = style != null ? GUI.Button(r, text, style) : GUI.Button(r, text);
        if (!pressed)
        {
            return false;
        }

        var f = Time.frameCount;
        if (f == _imguiButtonDedupeFrame && dedupeKey == _imguiButtonDedupeKey)
        {
            return false;
        }

        _imguiButtonDedupeFrame = f;
        _imguiButtonDedupeKey = dedupeKey;
        return true;
    }

    private static bool ImguiButtonOnce(Rect r, GUIContent content, int dedupeKey, GUIStyle style = null)
    {
        var pressed = style != null ? GUI.Button(r, content, style) : GUI.Button(r, content);
        if (!pressed)
        {
            return false;
        }

        var f = Time.frameCount;
        if (f == _imguiButtonDedupeFrame && dedupeKey == _imguiButtonDedupeKey)
        {
            return false;
        }

        _imguiButtonDedupeFrame = f;
        _imguiButtonDedupeKey = dedupeKey;
        return true;
    }

    /// <summary>
    /// Single-step +/- for octets. Does not use <see cref="GUI.Button"/> — inside <see cref="GUI.Window"/> that helper
    /// can report the same release multiple times per click; <see cref="Event.GetTypeForControl"/> fires once per control.
    /// </summary>
    private static bool OctetStepButton(Rect r, string label, int controlHint)
    {
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, r);
        var e = Event.current;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != id)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                e.Use();
                if (r.Contains(e.mousePosition))
                {
                    return true;
                }

                break;
            case EventType.Repaint:
                if (_stMutedBtn != null)
                {
                    _stMutedBtn.Draw(r, new GUIContent(label), id);
                }
                else
                {
                    GUI.skin.button.Draw(r, new GUIContent(label), id);
                }

                break;
        }

        return false;
    }

    /// <summary>
    /// IOPS toolbar control: <see cref="GUI.Button"/> is unreliable inside <see cref="GUI.Window"/> on some IL2CPP builds;
    /// use explicit MouseDown/MouseUp like <see cref="OctetStepButton"/>.
    /// </summary>
    private static bool IopsCalcToolbarButton(Rect r, string label)
    {
        const int controlHint = 0x49A0_0000;
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, r);
        var e = Event.current;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != id)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                e.Use();
                if (r.Contains(e.mousePosition))
                {
                    return true;
                }

                break;
            case EventType.Repaint:
                if (_stMutedBtn != null)
                {
                    _stMutedBtn.Draw(r, new GUIContent(label), id);
                }
                else
                {
                    GUI.skin.button.Draw(r, new GUIContent(label), id);
                }

                break;
        }

        return false;
    }
    private static void EnsureTextures()
    {
        if (_texturesReady)
        {
            return;
        }

        _texBackdrop = MakeTexture(10, 12, 16, 255);
        _texSidebar = MakeTexture(24, 30, 40, 255);
        _texToolbar = MakeTexture(28, 34, 44, 255);
        _texPageBg = MakeTexture(20, 24, 32, 255);
        _texCard = MakeTexture(30, 36, 46, 255);
        _texTableHeader = MakeTexture(40, 48, 60, 255);
        _texRowA = MakeTexture(34, 40, 52, 255);
        _texRowB = MakeTexture(38, 45, 58, 255);
        _texRowHover = MakeTexture(52, 62, 78, 255);
        _texNavActive = MakeTexture(0, 122, 111, 255);
        _texPrimaryBtn = MakeTexture(0, 133, 120, 255);
        _texPrimaryBtnHover = MakeTexture(0, 152, 136, 255);
        _texMutedBtn = MakeTexture(48, 55, 68, 255);
        _texMutedBtnHover = MakeTexture(58, 66, 82, 255);
        _texNavBtnHover = MakeTexture(38, 46, 60, 255);
        _texModalDim = MakeTexture(0, 0, 0, 140);
        _texWhite = MakeTexture(255, 255, 255, 255);
        _texturesReady = true;
    }

    private static void EnsureStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _stModalBlocker = new GUIStyle();
        _stModalBlocker.normal.background = _texModalDim;
        _stModalBlocker.border = Ro(0, 0, 0, 0);

        var lf = GUI.skin.label.font;
        var bf = GUI.skin.button.font;

        _stWindowTitle = new GUIStyle();
        _stWindowTitle.font = lf;
        _stWindowTitle.fontSize = 13;
        _stWindowTitle.fontStyle = FontStyle.Bold;
        _stWindowTitle.alignment = TextAnchor.MiddleLeft;
        _stWindowTitle.padding = Ro(10, 8, 0, 0);
        _stWindowTitle.normal.textColor = new Color32(248, 250, 252, 255);

        _stToolbarTitle = new GUIStyle();
        _stToolbarTitle.font = lf;
        _stToolbarTitle.fontSize = 15;
        _stToolbarTitle.fontStyle = FontStyle.Bold;
        _stToolbarTitle.alignment = TextAnchor.MiddleLeft;
        _stToolbarTitle.normal.textColor = new Color32(236, 240, 247, 255);

        _stToolbarSub = new GUIStyle();
        _stToolbarSub.font = lf;
        _stToolbarSub.fontSize = 11;
        _stToolbarSub.alignment = TextAnchor.MiddleLeft;
        _stToolbarSub.normal.textColor = new Color32(154, 164, 178, 255);

        _stBadgeOn = new GUIStyle();
        _stBadgeOn.font = lf;
        _stBadgeOn.fontSize = 9;
        _stBadgeOn.fontStyle = FontStyle.Bold;
        _stBadgeOn.alignment = TextAnchor.MiddleCenter;
        _stBadgeOn.normal.textColor = new Color32(110, 231, 210, 255);
        _stBadgeOn.normal.background = MakeTexture(12, 56, 52, 255);
        _stBadgeOn.border = Ro(4, 4, 4, 4);

        _stBadgeOff = new GUIStyle();
        _stBadgeOff.font = lf;
        _stBadgeOff.fontSize = 9;
        _stBadgeOff.fontStyle = FontStyle.Bold;
        _stBadgeOff.alignment = TextAnchor.MiddleCenter;
        _stBadgeOff.normal.textColor = new Color32(140, 148, 160, 255);
        _stBadgeOff.normal.background = MakeTexture(45, 50, 60, 255);
        _stBadgeOff.border = Ro(4, 4, 4, 4);

        _stNavItemActive = new GUIStyle();
        _stNavItemActive.font = lf;
        _stNavItemActive.fontSize = 12;
        _stNavItemActive.alignment = TextAnchor.MiddleLeft;
        _stNavItemActive.padding = Ro(16, 8, 0, 0);
        _stNavItemActive.normal.textColor = Color.white;

        _stNavHint = new GUIStyle();
        _stNavHint.font = lf;
        _stNavHint.fontSize = 10;
        _stNavHint.alignment = TextAnchor.UpperLeft;
        _stNavHint.wordWrap = true;
        _stNavHint.padding = Ro(14, 10, 8, 4);
        _stNavHint.normal.textColor = new Color32(148, 163, 184, 255);

        _stBreadcrumb = new GUIStyle();
        _stBreadcrumb.font = lf;
        _stBreadcrumb.fontSize = 11;
        _stBreadcrumb.alignment = TextAnchor.MiddleLeft;
        _stBreadcrumb.normal.textColor = new Color32(140, 152, 168, 255);

        _stSectionTitle = new GUIStyle();
        _stSectionTitle.font = lf;
        _stSectionTitle.fontSize = 12;
        _stSectionTitle.fontStyle = FontStyle.Bold;
        _stSectionTitle.alignment = TextAnchor.MiddleLeft;
        _stSectionTitle.normal.textColor = new Color32(226, 232, 240, 255);

        _stTableHeaderText = new GUIStyle();
        _stTableHeaderText.font = lf;
        _stTableHeaderText.fontSize = 10;
        _stTableHeaderText.fontStyle = FontStyle.Bold;
        _stTableHeaderText.alignment = TextAnchor.MiddleLeft;
        _stTableHeaderText.padding = Ro(12, 8, 0, 0);
        _stTableHeaderText.normal.textColor = new Color32(176, 186, 200, 255);

        _stHeaderSortBtn = new GUIStyle();
        _stHeaderSortBtn.font = lf;
        _stHeaderSortBtn.fontSize = 10;
        _stHeaderSortBtn.fontStyle = FontStyle.Bold;
        _stHeaderSortBtn.alignment = TextAnchor.MiddleLeft;
        _stHeaderSortBtn.padding = Ro(10, 6, 0, 0);
        _stHeaderSortBtn.normal.textColor = new Color32(176, 186, 200, 255);
        _stHeaderSortBtn.hover.textColor = new Color32(220, 228, 240, 255);
        _stHeaderSortBtn.active.textColor = Color.white;
        _stHeaderSortBtn.hover.background = MakeTexture(52, 60, 74, 220);
        _stHeaderSortBtn.border = Ro(2, 2, 2, 2);

        _stTableCell = new GUIStyle();
        _stTableCell.font = lf;
        _stTableCell.fontSize = 12;
        _stTableCell.alignment = TextAnchor.MiddleLeft;
        _stTableCell.padding = Ro(12, 8, 0, 0);
        _stTableCell.clipping = TextClipping.Clip;
        _stTableCell.normal.textColor = new Color32(220, 226, 235, 255);

        _stNavBtn = new GUIStyle();
        _stNavBtn.font = lf;
        _stNavBtn.fontSize = 12;
        _stNavBtn.alignment = TextAnchor.MiddleLeft;
        _stNavBtn.padding = Ro(16, 8, 0, 0);
        _stNavBtn.normal.background = _texSidebar;
        _stNavBtn.hover.background = _texNavBtnHover;
        _stNavBtn.active.background = _texNavBtnHover;
        _stNavBtn.normal.textColor = new Color32(203, 213, 225, 255);
        _stNavBtn.hover.textColor = new Color32(240, 244, 250, 255);
        _stNavBtn.active.textColor = Color.white;
        _stNavBtn.border = Ro(0, 0, 0, 0);

        _stMuted = new GUIStyle();
        _stMuted.font = lf;
        _stMuted.fontSize = 11;
        _stMuted.alignment = TextAnchor.MiddleLeft;
        _stMuted.normal.textColor = new Color32(154, 164, 178, 255);

        _stMutedCenter = new GUIStyle();
        _stMutedCenter.font = lf;
        _stMutedCenter.fontSize = 11;
        _stMutedCenter.alignment = TextAnchor.MiddleCenter;
        _stMutedCenter.normal.textColor = new Color32(154, 164, 178, 255);

        _stHint = new GUIStyle();
        _stHint.font = lf;
        _stHint.fontSize = 10;
        _stHint.alignment = TextAnchor.UpperLeft;
        _stHint.wordWrap = true;
        _stHint.normal.textColor = new Color32(130, 170, 255, 255);

        _stError = new GUIStyle();
        _stError.font = lf;
        _stError.fontSize = 10;
        _stError.alignment = TextAnchor.UpperLeft;
        _stError.wordWrap = true;
        _stError.normal.textColor = new Color32(255, 130, 120, 255);

        _stFormLabel = new GUIStyle();
        _stFormLabel.font = lf;
        _stFormLabel.fontSize = 11;
        _stFormLabel.fontStyle = FontStyle.Bold;
        _stFormLabel.alignment = TextAnchor.MiddleLeft;
        _stFormLabel.normal.textColor = new Color32(200, 208, 218, 255);

        _stOctetVal = new GUIStyle();
        _stOctetVal.font = lf;
        _stOctetVal.fontSize = 12;
        _stOctetVal.fontStyle = FontStyle.Bold;
        _stOctetVal.alignment = TextAnchor.MiddleCenter;
        _stOctetVal.normal.textColor = new Color32(240, 242, 248, 255);

        _stPrimaryBtn = new GUIStyle();
        _stPrimaryBtn.font = bf;
        _stPrimaryBtn.fontSize = 11;
        _stPrimaryBtn.fontStyle = FontStyle.Bold;
        _stPrimaryBtn.alignment = TextAnchor.MiddleCenter;
        _stPrimaryBtn.padding = Ro(12, 12, 6, 6);
        _stPrimaryBtn.normal.background = _texPrimaryBtn;
        _stPrimaryBtn.hover.background = _texPrimaryBtnHover;
        _stPrimaryBtn.active.background = MakeTexture(0, 104, 94, 255);
        _stPrimaryBtn.normal.textColor = Color.white;
        _stPrimaryBtn.hover.textColor = Color.white;
        _stPrimaryBtn.active.textColor = Color.white;
        _stPrimaryBtn.border = Ro(3, 3, 3, 3);

        _stMutedBtn = new GUIStyle();
        _stMutedBtn.font = bf;
        _stMutedBtn.fontSize = 11;
        _stMutedBtn.alignment = TextAnchor.MiddleCenter;
        _stMutedBtn.padding = Ro(10, 10, 5, 5);
        _stMutedBtn.normal.background = _texMutedBtn;
        _stMutedBtn.hover.background = _texMutedBtnHover;
        _stMutedBtn.active.background = _texMutedBtnHover;
        _stMutedBtn.normal.textColor = new Color32(230, 234, 240, 255);
        _stMutedBtn.hover.textColor = Color.white;
        _stMutedBtn.active.textColor = Color.white;
        _stMutedBtn.border = Ro(3, 3, 3, 3);

        _stIopsResult = new GUIStyle();
        _stIopsResult.font = lf;
        _stIopsResult.fontSize = 12;
        _stIopsResult.fontStyle = FontStyle.Normal;
        _stIopsResult.alignment = TextAnchor.UpperLeft;
        _stIopsResult.wordWrap = true;
        _stIopsResult.padding = Ro(0, 0, 0, 0);
        _stIopsResult.clipping = TextClipping.Clip;
        _stIopsResult.normal.textColor = new Color32(220, 226, 235, 255);

        _stIopsResultCounts = new GUIStyle();
        _stIopsResultCounts.font = lf;
        _stIopsResultCounts.fontSize = 20;
        _stIopsResultCounts.fontStyle = FontStyle.Bold;
        _stIopsResultCounts.alignment = TextAnchor.UpperLeft;
        _stIopsResultCounts.wordWrap = true;
        _stIopsResultCounts.padding = Ro(0, 0, 0, 0);
        _stIopsResultCounts.clipping = TextClipping.Clip;
        _stIopsResultCounts.normal.textColor = new Color32(220, 226, 235, 255);

        _stDashboardHeroValue = new GUIStyle();
        _stDashboardHeroValue.font = lf;
        _stDashboardHeroValue.fontSize = 28;
        _stDashboardHeroValue.fontStyle = FontStyle.Bold;
        _stDashboardHeroValue.alignment = TextAnchor.UpperLeft;
        _stDashboardHeroValue.wordWrap = false;
        _stDashboardHeroValue.padding = Ro(0, 0, 0, 0);
        _stDashboardHeroValue.clipping = TextClipping.Clip;
        _stDashboardHeroValue.normal.textColor = new Color32(248, 250, 252, 255);

        _stIopsResultPlaceholder = new GUIStyle();
        _stIopsResultPlaceholder.font = lf;
        _stIopsResultPlaceholder.fontSize = 11;
        _stIopsResultPlaceholder.fontStyle = FontStyle.Normal;
        _stIopsResultPlaceholder.alignment = TextAnchor.UpperLeft;
        _stIopsResultPlaceholder.wordWrap = true;
        _stIopsResultPlaceholder.padding = Ro(0, 0, 0, 0);
        _stIopsResultPlaceholder.clipping = TextClipping.Clip;
        _stIopsResultPlaceholder.normal.textColor = new Color32(154, 164, 178, 255);

        _stylesReady = true;
    }

    private static Texture2D MakeTexture(byte r, byte g, byte b, byte a)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        tex.SetPixel(0, 0, new Color32(r, g, b, a));
        tex.Apply();
        return tex;
    }

    private static float ToolbarTextButtonWidth(GUIStyle st, string text, float minW = 56f)
    {
        return Mathf.Max(minW, st.CalcSize(new GUIContent(text)).x + 14f);
    }

    private static float ComputeToolbarInventoryMinWidth()
    {
        if (!_stylesReady || _stMutedBtn == null)
        {
            return 420f;
        }

        const float g = 8f;
        const float tr = 14f;
        float W(GUIStyle st, string t) => ToolbarTextButtonWidth(st, t);
        var sum = tr;
        sum += g + W(_stMutedBtn, "Fit columns");
        sum += g + W(_stMutedBtn, "IOPS calc");
        return sum;
    }

    private static void ShowIpamToast(string message)
    {
        _ipamToast = message ?? "";
        _ipamToastUntil = Time.realtimeSinceStartup + 6f;
    }
}
