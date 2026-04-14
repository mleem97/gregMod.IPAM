using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DHCPSwitches;

/// <summary>
/// Adds a dedicated "DHCP" row for the vanilla <see cref="SetIP"/> keypad as a separate uGUI block parented to the same
/// root <see cref="Canvas"/> as SetIP — visually aligned under the keypad (matching width and grey/cyan styling) without
/// injecting into the game's layout or resizing their rects.
/// </summary>
internal static class SetIpKeypadDhcpButton
{
    private const float DhcpRowHeight = 44f;
    private const float DhcpButtonWidth = 112f;
    private const float DhcpEdgePad = 10f;

    private static readonly Vector3[] CornerScratch = new Vector3[4];

    /// <summary>Unity <see cref="Image"/> often draws nothing when <see cref="Image.sprite"/> is null (built player).</summary>
    private static Sprite _modUiWhiteSprite;

    private static GameObject _dhcpButtonGo;
    private static int _boundServerInstanceId;
    private static int _spawnNotBeforeFrame = -1;

    private static string _lastDhcpVerboseState = "";

    private static bool _loggedSetIpPick;
    private static bool _loggedResolveTotallyEmpty;

    /// <summary>
    /// Resolves <see cref="SetIP"/> for the physical keypad: <see cref="MainGameManager.setIP"/> first, then any instance
    /// in loaded scenes (including inactive). Ranks by <see cref="GameObject.activeInHierarchy"/>, then <see cref="SetIP.isActive"/>, then bound server.
    /// </summary>
    internal static SetIP ResolveSetIPForTick()
    {
        SetIP best = null;
        var bestRank = int.MinValue;

        void Consider(SetIP candidate)
        {
            if (candidate == null)
            {
                return;
            }

            var r = RankSetIpCandidate(candidate);
            if (r > bestRank)
            {
                bestRank = r;
                best = candidate;
            }
        }

        Consider(MainGameManager.instance?.setIP);

        var allSetIp = UnityEngine.Object.FindObjectsOfType<SetIP>(true);
        if (allSetIp != null)
        {
            for (var i = 0; i < allSetIp.Length; i++)
            {
                Consider(allSetIp[i]);
            }
        }

        if (ModDebugLog.IsSetIpKeypadDhcpLogEnabled && best != null && !_loggedSetIpPick)
        {
            _loggedSetIpPick = true;
            LogDhcp(
                $"resolve pick: {best.name} rank={bestRank} hierarchy={best.gameObject.activeInHierarchy} " +
                $"isActive={best.isActive} server={(best.server != null ? best.server.GetInstanceID().ToString() : "null")}");
        }

        if (best == null && !_loggedResolveTotallyEmpty)
        {
            var srvArr = UnityEngine.Object.FindObjectsOfType<Server>();
            var srvCount = srvArr != null ? srvArr.Length : 0;
            if (srvCount > 0)
            {
                _loggedResolveTotallyEmpty = true;
                ModDebugLog.Bootstrap();
                ModDebugLog.WriteLine(
                    "setip-dhcp: no SetIP resolved while servers exist — keypad type may differ from SetIP in this build.");
            }
        }

        return best;
    }

    private static int RankSetIpCandidate(SetIP s)
    {
        if (s == null)
        {
            return int.MinValue;
        }

        var r = 0;
        if (s.gameObject.activeInHierarchy)
        {
            r += 1000;
        }

        if (s.isActive)
        {
            r += 100;
        }

        if (s.server != null)
        {
            r += 10;
        }

        return r;
    }

    internal static void Tick(SetIP setIp)
    {
        if (setIp == null)
        {
            _lastDhcpVerboseState = "";
            ResetSpawnState();
            DestroyDhcpButton("setIP null");
            return;
        }

        if (!LicenseManager.IsDHCPUnlocked)
        {
            ResetSpawnState();
            DestroyDhcpButton("DHCP locked (title bar or Ctrl+D)");
            return;
        }

        if (IPAMOverlay.IsVisible)
        {
            ResetSpawnState();
            DestroyDhcpButton("IPAM overlay open");
            return;
        }

        // Do not require SetIP.isActive — some builds show the keypad without setting that flag.
        if (!setIp.gameObject.activeInHierarchy || setIp.server == null)
        {
            ResetSpawnState();
            DestroyDhcpButton(!setIp.gameObject.activeInHierarchy ? "SetIP GameObject not in active hierarchy" : "setIP.server null");
            return;
        }

        var sid = setIp.server.GetInstanceID();
        if (_dhcpButtonGo != null && _boundServerInstanceId != sid)
        {
            DestroyDhcpButton("bound server instance changed");
        }

        if (ModDebugLog.IsSetIpKeypadDhcpLogEnabled)
        {
            var st =
                $"active={setIp.isActive},srv={sid},dhcpUnlock={LicenseManager.IsDHCPUnlocked},ipam={IPAMOverlay.IsVisible},spawnWait={Time.frameCount < _spawnNotBeforeFrame}";
            if (st != _lastDhcpVerboseState)
            {
                _lastDhcpVerboseState = st;
                LogDhcp($"tick {st}");
            }
        }

        if (_dhcpButtonGo != null)
        {
            SetButtonLabelText(_dhcpButtonGo, "DHCP");
            return;
        }

        if (_spawnNotBeforeFrame < 0)
        {
            _spawnNotBeforeFrame = Time.frameCount + 2;
        }

        if (Time.frameCount < _spawnNotBeforeFrame)
        {
            return;
        }

        TrySpawnFloatingDhcpPanel(setIp, sid);
    }

    private static void ResetSpawnState()
    {
        _spawnNotBeforeFrame = -1;
    }

    private static void ApplyModUiWhiteSprite(Image img)
    {
        if (img == null)
        {
            return;
        }

        if (_modUiWhiteSprite == null)
        {
            var tex = Texture2D.whiteTexture;
            _modUiWhiteSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        img.sprite = _modUiWhiteSprite;
        img.type = Image.Type.Simple;
    }

    private static Canvas TryResolveAnyCanvas(SetIP setIp)
    {
        var c = setIp.GetComponentInParent<Canvas>();
        if (c != null)
        {
            return c;
        }

        if (setIp.canvas != null)
        {
            c = setIp.canvas.GetComponent<Canvas>();
            if (c != null)
            {
                return c;
            }

            c = setIp.canvas.GetComponentInChildren<Canvas>(true);
            if (c != null)
            {
                return c;
            }
        }

        return setIp.transform.root.GetComponentInChildren<Canvas>(true);
    }

    /// <summary>
    /// DHCP row parented under SetIP's own <see cref="RectTransform"/> when possible (same scale/pivot as the keypad),
    /// else under the root canvas. Uses drawable sprites — Unity <see cref="Image"/> is often invisible with null sprite.
    /// </summary>
    private static bool TrySpawnFloatingDhcpPanel(SetIP setIp, int sid)
    {
        var setRt = setIp.GetComponent<RectTransform>();
        Vector2 localCenter;
        Vector2 sizeDelta;
        RectTransform parentRt;
        string layoutMode;

        if (setRt != null && TryComputeFloatingDhcpLayoutUnderSetIp(setIp, setRt, out localCenter, out sizeDelta))
        {
            parentRt = setRt;
            layoutMode = "under_SetIP_RectTransform";
        }
        else
        {
            var rootCanvas = TryResolveAnyCanvas(setIp);
            if (rootCanvas == null)
            {
                LogDhcp("floating: no Canvas found");
                return false;
            }

            var canvasRt = rootCanvas.transform as RectTransform;
            if (canvasRt == null || !TryComputeFloatingDhcpLayout(setIp, rootCanvas, out localCenter, out sizeDelta))
            {
                LogDhcp("floating: canvas layout failed");
                return false;
            }

            parentRt = canvasRt;
            layoutMode = $"canvas_{rootCanvas.renderMode}";
        }

        var root = new GameObject("DHCPSwitches_FloatingDhcp");
        var rootRt = root.AddComponent<RectTransform>();
        root.transform.SetParent(parentRt, false);
        rootRt.SetAsLastSibling();
        rootRt.localScale = Vector3.one;
        rootRt.localRotation = Quaternion.identity;

        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = sizeDelta;
        rootRt.anchoredPosition = localCenter;

        var bgGo = new GameObject("PanelBg");
        bgGo.transform.SetParent(root.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(2f, 2f);
        bgRt.offsetMax = new Vector2(-2f, -2f);
        var bgImg = bgGo.AddComponent<Image>();
        ApplyModUiWhiteSprite(bgImg);
        bgImg.color = new Color(0.22f, 0.22f, 0.24f, 0.97f);
        bgImg.raycastTarget = false;

        var lineGo = new GameObject("TopBorder");
        lineGo.transform.SetParent(root.transform, false);
        var lineRt = lineGo.AddComponent<RectTransform>();
        lineRt.anchorMin = new Vector2(0f, 1f);
        lineRt.anchorMax = new Vector2(1f, 1f);
        lineRt.pivot = new Vector2(0.5f, 1f);
        lineRt.offsetMin = new Vector2(1f, -3f);
        lineRt.offsetMax = new Vector2(-1f, 0f);
        var lineImg = lineGo.AddComponent<Image>();
        ApplyModUiWhiteSprite(lineImg);
        lineImg.color = new Color(0.2f, 0.72f, 0.82f, 1f);
        lineImg.raycastTarget = false;

        var btnGo = new GameObject("DhcpButton");
        btnGo.transform.SetParent(root.transform, false);
        var btnRt = btnGo.AddComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.5f);
        btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.pivot = new Vector2(0.5f, 0.5f);
        var bw = Mathf.Min(DhcpButtonWidth, sizeDelta.x - 12f);
        var bh = Mathf.Max(28f, sizeDelta.y - 14f);
        btnRt.sizeDelta = new Vector2(bw, bh);
        btnRt.anchoredPosition = new Vector2(0f, -2f);

        var btnImg = btnGo.AddComponent<Image>();
        ApplyModUiWhiteSprite(btnImg);
        btnImg.color = new Color(0.15f, 0.32f, 0.48f, 0.96f);
        btnImg.raycastTarget = true;
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener((UnityAction)(() => OnDhcpClicked()));

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(btnGo.transform, false);
        var trText = textGo.AddComponent<RectTransform>();
        trText.anchorMin = Vector2.zero;
        trText.anchorMax = Vector2.one;
        trText.offsetMin = Vector2.zero;
        trText.offsetMax = Vector2.zero;
        var ut = textGo.AddComponent<Text>();
        ut.text = "DHCP";
        ut.alignment = TextAnchor.MiddleCenter;
        ut.color = Color.white;
        ut.fontSize = 15;
        ut.resizeTextForBestFit = true;
        ut.resizeTextMinSize = 10;
        ut.resizeTextMaxSize = 18;
        ut.raycastTarget = false;
        TryAssignBuiltinFont(ut);

        _dhcpButtonGo = root;
        _boundServerInstanceId = sid;

        if (ModDebugLog.IsSetIpKeypadDhcpLogEnabled)
        {
            LogDhcp(
                $"spawn floating ok: parent={layoutMode} size={sizeDelta} pos={localCenter} " +
                $"path={BuildTransformPath(GetFloatingWidthSourceRect(setIp), setIp.transform)}");
        }

        return true;
    }

    /// <summary>Layout in <paramref name="parentRt"/> local space from keypad world corners — stable with CanvasScaler and world-space UI.</summary>
    private static bool TryComputeFloatingDhcpLayoutUnderSetIp(SetIP setIp, RectTransform parentRt, out Vector2 localCenter, out Vector2 sizeDelta)
    {
        localCenter = default;
        sizeDelta = default;

        var widthSource = GetFloatingWidthSourceRect(setIp);
        if (widthSource == null)
        {
            return false;
        }

        widthSource.GetWorldCorners(CornerScratch);
        var locBl = (Vector2)parentRt.InverseTransformPoint(CornerScratch[0]);
        var locBr = (Vector2)parentRt.InverseTransformPoint(CornerScratch[3]);
        var locTl = (Vector2)parentRt.InverseTransformPoint(CornerScratch[1]);

        var widthLocal = Mathf.Abs(locBr.x - locBl.x);
        if (widthLocal < 12f)
        {
            return false;
        }

        float rowHLocal;
        var z = FindButtonBySingleCharLabel(setIp, "0");
        var zrt = z != null ? z.GetComponent<RectTransform>() : null;
        if (zrt != null)
        {
            zrt.GetWorldCorners(CornerScratch);
            var zbl = (Vector2)parentRt.InverseTransformPoint(CornerScratch[0]);
            var ztl = (Vector2)parentRt.InverseTransformPoint(CornerScratch[1]);
            rowHLocal = Mathf.Max(20f, Mathf.Abs(zbl.y - ztl.y));
        }
        else
        {
            rowHLocal = Mathf.Max(28f, Mathf.Abs(locBl.y - locTl.y) * 0.22f);
        }

        var hLocal = Mathf.Max(rowHLocal * 1.06f, DhcpRowHeight);
        sizeDelta = new Vector2(Mathf.Max(96f, widthLocal), hLocal);

        var midBottomLocal = (locBl + locBr) * 0.5f;
        var down = locBl - locTl;
        if (down.sqrMagnitude < 1e-8f)
        {
            down = Vector2.down;
        }
        else
        {
            down.Normalize();
        }

        var gap = Mathf.Max(2f, hLocal * 0.05f);
        localCenter = midBottomLocal + down * (hLocal * 0.5f + gap);
        return true;
    }

    /// <summary>Horizontal span for the DHCP strip: keypad border, or grid parent when border is effectively fullscreen.</summary>
    private static RectTransform GetFloatingWidthSourceRect(SetIP setIp)
    {
        var setRt = setIp.GetComponent<RectTransform>();
        var borderRt = FindKeypadBorderRectForDhcp(setIp) ?? setRt;
        if (borderRt == null)
        {
            return null;
        }

        if (setRt != null && IsNearlyFullScreenRect(borderRt, setRt))
        {
            var grid = FindKeypadGridLayout(setIp);
            var gp = grid != null ? grid.transform.parent as RectTransform : null;
            if (gp != null)
            {
                return gp;
            }
        }

        return borderRt;
    }

    private static bool TryComputeFloatingDhcpLayout(SetIP setIp, Canvas rootCanvas, out Vector2 localCenter, out Vector2 sizeDelta)
    {
        localCenter = default;
        sizeDelta = default;

        var canvasRt = rootCanvas.transform as RectTransform;
        if (canvasRt == null)
        {
            return false;
        }

        var widthSource = GetFloatingWidthSourceRect(setIp);
        if (widthSource == null)
        {
            return false;
        }

        widthSource.GetWorldCorners(CornerScratch);
        var blW = CornerScratch[0];
        var brW = CornerScratch[3];
        var tlW = CornerScratch[1];

        var cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        if (rootCanvas.renderMode == RenderMode.ScreenSpaceCamera && cam == null)
        {
            cam = Camera.main;
        }

        var sbl = RectTransformUtility.WorldToScreenPoint(cam, blW);
        var sbr = RectTransformUtility.WorldToScreenPoint(cam, brW);
        var stl = RectTransformUtility.WorldToScreenPoint(cam, tlW);
        var midBottom = (blW + brW) * 0.5f;
        var smid = RectTransformUtility.WorldToScreenPoint(cam, midBottom);

        var scale = Mathf.Max(0.0001f, rootCanvas.scaleFactor);
        var wCanvas = Mathf.Abs(sbr.x - sbl.x) / scale;

        float rowHScreen;
        var z = FindButtonBySingleCharLabel(setIp, "0");
        var zrt = z != null ? z.GetComponent<RectTransform>() : null;
        if (zrt != null)
        {
            zrt.GetWorldCorners(CornerScratch);
            var sz0 = RectTransformUtility.WorldToScreenPoint(cam, CornerScratch[0]);
            var sz1 = RectTransformUtility.WorldToScreenPoint(cam, CornerScratch[1]);
            rowHScreen = Mathf.Max(28f, Mathf.Abs(sz0.y - sz1.y));
        }
        else
        {
            rowHScreen = Mathf.Max(36f, Mathf.Abs(sbl.y - stl.y) * 0.25f);
        }

        var hCanvas = Mathf.Max(rowHScreen * 1.08f, DhcpRowHeight);
        sizeDelta = new Vector2(Mathf.Max(96f, wCanvas), hCanvas);

        var downScreen = (sbl - stl);
        if (downScreen.sqrMagnitude < 1e-4f)
        {
            downScreen = Vector2.down;
        }
        else
        {
            downScreen.Normalize();
        }

        var gap = DhcpEdgePad * 0.5f;
        var panelCenterScreen = smid + downScreen * (hCanvas * 0.5f + gap);

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt,
            panelCenterScreen,
            cam,
            out localCenter);
    }

    private static void LogDhcp(string message)
    {
        if (!ModDebugLog.IsSetIpKeypadDhcpLogEnabled)
        {
            return;
        }

        ModDebugLog.Bootstrap();
        ModDebugLog.WriteLine($"setip-dhcp: {message}");
    }

    private static string BuildTransformPath(Transform t, Transform stop)
    {
        if (t == null)
        {
            return "";
        }

        var sb = new StringBuilder();
        for (var x = t; x != null && x != stop; x = x.parent)
        {
            if (sb.Length > 0)
            {
                sb.Insert(0, "/");
            }

            sb.Insert(0, x.name ?? "?");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Resolves a RectTransform that wraps only the keypad (not the full-screen SetIP shell). Uses LCA of Paste vs
    /// a bottom-row key when Unity's <see cref="GridLayoutGroup"/> is missing or not enumerated under Il2Cpp.
    /// </summary>
    private static RectTransform FindKeypadHostRect(SetIP setIp)
    {
        if (setIp?.gameObject == null)
        {
            return null;
        }

        var setRt = setIp.GetComponent<RectTransform>();
        var stop = setIp.transform;

        var paste = FindButtonByTransformName(setIp, "Paste");
        if (paste == null)
        {
            paste = FindButtonWithVisibleText(setIp, "Paste");
        }

        var bottomRef = FindKeypadBottomRowReferenceButton(setIp);

        if (paste != null && bottomRef != null)
        {
            var lca = FindLowestCommonAncestor(paste.transform, bottomRef.transform, stop);
            if (lca != null && lca is RectTransform lcaRt)
            {
                var glgInLca = lcaRt.GetComponentInChildren<GridLayoutGroup>(true);
                if (glgInLca != null)
                {
                    var border = FindBestKeypadBorderRect(setIp, glgInLca);
                    if (border != null && (setRt == null || !IsNearlyFullScreenRect(border, setRt)))
                    {
                        return border;
                    }
                }

                if (setRt == null || !IsNearlyFullScreenRect(lcaRt, setRt))
                {
                    return lcaRt;
                }
            }
        }

        var grid = FindKeypadGridLayout(setIp);
        if (grid != null)
        {
            var border = FindBestKeypadBorderRect(setIp, grid);
            if (border != null && (setRt == null || !IsNearlyFullScreenRect(border, setRt)))
            {
                return border;
            }
        }

        if (paste != null)
        {
            var host = FindLargestNonFullscreenAncestor(paste.transform, stop, setRt);
            if (host != null)
            {
                return host;
            }

            host = FindFirstNonFullscreenAncestorFrom(paste.transform, stop, setRt);
            if (host != null)
            {
                return host;
            }

            var p1 = paste.transform.parent as RectTransform;
            var p2 = p1 != null ? p1.parent as RectTransform : null;
            if (p2 != null)
            {
                return p2;
            }

            if (p1 != null)
            {
                return p1;
            }
        }

        foreach (var token in new[] { "Clear", "Copy", "7" })
        {
            var b = FindButtonByTransformName(setIp, token);
            if (b == null)
            {
                continue;
            }

            var p2 = b.transform.parent != null ? b.transform.parent.parent as RectTransform : null;
            if (p2 != null)
            {
                return p2;
            }

            if (b.transform.parent is RectTransform row)
            {
                return row;
            }
        }

        return null;
    }

    private static RectTransform FindKeypadBorderRectForDhcp(SetIP setIp)
    {
        var setRt = setIp.GetComponent<RectTransform>();

        bool IsUsableBorder(RectTransform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (setRt != null && (ReferenceEquals(candidate, setRt) || IsNearlyFullScreenRect(candidate, setRt)))
            {
                return false;
            }

            return true;
        }

        var grid = FindKeypadGridLayout(setIp);
        if (grid != null)
        {
            var b = FindBestKeypadBorderRect(setIp, grid);
            if (b != null)
            {
                return b;
            }
        }

        var host = FindKeypadHostRect(setIp);
        if (IsUsableBorder(host))
        {
            return host;
        }

        var paste = FindButtonByTransformName(setIp, "Paste") ?? FindButtonWithVisibleText(setIp, "Paste");
        if (paste != null)
        {
            for (var t = paste.transform.parent; t != null && t != setIp.transform; t = t.parent)
            {
                if (t is RectTransform rt && IsUsableBorder(rt))
                {
                    return rt;
                }
            }
        }

        return setRt;
    }

    /// <summary>First ancestor rect under SetIP that is not as large as the whole overlay (for when layout size is still 0).</summary>
    private static RectTransform FindFirstNonFullscreenAncestorFrom(Transform leaf, Transform stop, RectTransform setRt)
    {
        for (var t = leaf.parent; t != null && t != stop; t = t.parent)
        {
            if (t is not RectTransform rt)
            {
                continue;
            }

            if (setRt != null && IsNearlyFullScreenRect(rt, setRt))
            {
                continue;
            }

            return rt;
        }

        return null;
    }

    /// <summary>Bottom keypad row — transform names first (TMP text may not reflect on child components under Il2Cpp).</summary>
    private static Button FindKeypadBottomRowReferenceButton(SetIP setIp)
    {
        foreach (var token in new[] { "Cancel", "OK", "DEL", "Period", "Dot", "Key0", "Digit0", "Zero" })
        {
            var b = FindButtonByTransformName(setIp, token);
            if (b != null)
            {
                return b;
            }
        }

        var b2 = FindButtonBySingleCharLabel(setIp, "0");
        if (b2 != null)
        {
            return b2;
        }

        b2 = FindButtonBySingleCharLabel(setIp, ".");
        if (b2 != null)
        {
            return b2;
        }

        b2 = FindButtonWithVisibleText(setIp, "Cancel");
        if (b2 != null)
        {
            return b2;
        }

        return FindButtonWithVisibleText(setIp, "OK");
    }

    private static Button FindButtonBySingleCharLabel(SetIP setIp, string single)
    {
        foreach (var btn in setIp.gameObject.GetComponentsInChildren<Button>(true))
        {
            if (btn == null)
            {
                continue;
            }

            foreach (var comp in btn.GetComponentsInChildren<Component>(true))
            {
                if (comp == null)
                {
                    continue;
                }

                var t = comp.GetType();
                if (t == typeof(Transform) || t == typeof(RectTransform))
                {
                    continue;
                }

                var txt = TryReadTextProperty(comp);
                if (txt != null && string.Equals(txt.Trim(), single, StringComparison.Ordinal))
                {
                    return btn;
                }
            }
        }

        return null;
    }

    private static Transform FindLowestCommonAncestor(Transform a, Transform b, Transform stop)
    {
        if (a == null || b == null)
        {
            return null;
        }

        var da = GetDepthToStop(a, stop);
        var db = GetDepthToStop(b, stop);
        while (da > db && a != null && a != stop)
        {
            a = a.parent;
            da--;
        }

        while (db > da && b != null && b != stop)
        {
            b = b.parent;
            db--;
        }

        while (a != null && b != null && a != stop && b != stop)
        {
            if (a == b)
            {
                return a;
            }

            a = a.parent;
            b = b.parent;
        }

        return null;
    }

    private static int GetDepthToStop(Transform t, Transform stop)
    {
        var d = 0;
        for (; t != null && t != stop; t = t.parent)
        {
            d++;
        }

        return d;
    }

    /// <summary>True when this rect covers almost the entire SetIP root area (anchoring bottom-right would hit the screen edge).</summary>
    private static bool IsNearlyFullScreenRect(RectTransform candidate, RectTransform setIpRoot)
    {
        if (candidate == null || setIpRoot == null)
        {
            return false;
        }

        if (ReferenceEquals(candidate, setIpRoot))
        {
            return true;
        }

        var ca = Mathf.Abs(candidate.rect.width * candidate.rect.height);
        var sa = Mathf.Abs(setIpRoot.rect.width * setIpRoot.rect.height);
        if (sa < 10f)
        {
            return false;
        }

        return ca >= sa * 0.88f;
    }

    /// <summary>Largest ancestor of Paste that is still clearly smaller than the full SetIP panel (keypad column / inner chrome).</summary>
    private static RectTransform FindLargestNonFullscreenAncestor(Transform leaf, Transform stop, RectTransform setRt)
    {
        RectTransform best = null;
        var bestArea = -1f;
        for (var t = leaf.parent; t != null && t != stop; t = t.parent)
        {
            if (t is not RectTransform rt)
            {
                continue;
            }

            if (setRt != null && IsNearlyFullScreenRect(rt, setRt))
            {
                continue;
            }

            var area = Mathf.Abs(rt.rect.width * rt.rect.height);
            if (area < 1f)
            {
                area = 1f;
            }

            if (area > bestArea)
            {
                best = rt;
                bestArea = area;
            }
        }

        return best;
    }

    private static GridLayoutGroup FindKeypadGridLayout(SetIP setIp)
    {
        if (setIp?.gameObject == null)
        {
            return null;
        }

        GridLayoutGroup best = null;
        var bestChildren = -1;
        foreach (var g in setIp.gameObject.GetComponentsInChildren<GridLayoutGroup>(true))
        {
            if (g == null)
            {
                continue;
            }

            var n = g.transform.childCount;
            if (n > bestChildren)
            {
                best = g;
                bestChildren = n;
            }
        }

        return best;
    }

    private static RectTransform FindBestKeypadBorderRect(SetIP setIp, GridLayoutGroup grid)
    {
        for (var t = grid.transform.parent; t != null && t != setIp.transform; t = t.parent)
        {
            var name = t.name ?? "";
            if (name.IndexOf("Border", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return t as RectTransform;
            }

            if (name.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return t as RectTransform;
            }

            if (name.IndexOf("Keypad", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return t as RectTransform;
            }
        }

        foreach (var o in grid.GetComponentsInParent<Outline>(true))
        {
            if (o != null && o.transform != null)
            {
                return o.transform as RectTransform;
            }
        }

        var parent = grid.transform.parent as RectTransform;
        var setRt = setIp.GetComponent<RectTransform>();
        if (parent != null && (setRt == null || parent != setRt))
        {
            return parent;
        }

        return grid.transform as RectTransform;
    }

    private static void TryAssignBuiltinFont(Text ut)
    {
        if (ut == null)
        {
            return;
        }

        try
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null)
            {
                ut.font = f;
                return;
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            var f2 = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f2 != null)
            {
                ut.font = f2;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void DestroyDhcpButton(string reason = null)
    {
        if (ModDebugLog.IsSetIpKeypadDhcpLogEnabled && !string.IsNullOrEmpty(reason))
        {
            ModDebugLog.Bootstrap();
            ModDebugLog.WriteLine($"setip-dhcp: destroy: {reason}");
        }

        if (_dhcpButtonGo != null)
        {
            UnityEngine.Object.Destroy(_dhcpButtonGo);
            _dhcpButtonGo = null;
        }

        _boundServerInstanceId = 0;
    }

    private static Button FindButtonByTransformName(SetIP setIp, string token)
    {
        foreach (var tr in setIp.gameObject.GetComponentsInChildren<Transform>(true))
        {
            if (tr == null || tr.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var b = tr.GetComponent<Button>();
            if (b != null)
            {
                return b;
            }

            b = tr.GetComponentInParent<Button>();
            if (b != null)
            {
                return b;
            }
        }

        return null;
    }

    private static Button FindButtonWithVisibleText(SetIP setIp, string label)
    {
        foreach (var btn in setIp.gameObject.GetComponentsInChildren<Button>(true))
        {
            if (btn == null)
            {
                continue;
            }

            foreach (var comp in btn.GetComponentsInChildren<Component>(true))
            {
                if (comp == null)
                {
                    continue;
                }

                var t = comp.GetType();
                if (t == typeof(Transform) || t == typeof(RectTransform))
                {
                    continue;
                }

                var txt = TryReadTextProperty(comp);
                if (txt != null && string.Equals(txt.Trim(), label, StringComparison.OrdinalIgnoreCase))
                {
                    return btn;
                }
            }
        }

        return null;
    }

    private static string TryReadTextProperty(Component c)
    {
        for (var bt = c.GetType(); bt != null && bt != typeof(Component) && bt != typeof(object); bt = bt.BaseType)
        {
            foreach (var p in bt.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!string.Equals(p.Name, "text", StringComparison.Ordinal) || !p.CanRead)
                {
                    continue;
                }

                try
                {
                    return p.GetValue(c)?.ToString();
                }
                catch
                {
                    // ignore
                }
            }
        }

        return null;
    }

    private static void SetButtonLabelText(GameObject buttonRoot, string text)
    {
        const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var c in buttonRoot.GetComponentsInChildren<Component>(true))
        {
            if (c == null)
            {
                continue;
            }

            var t = c.GetType();
            if (t == typeof(Transform) || t == typeof(RectTransform))
            {
                continue;
            }

            var setText = t.GetMethod("SetText", f, null, new[] { typeof(string) }, null);
            if (setText != null)
            {
                try
                {
                    setText.Invoke(c, new object[] { text });
                }
                catch
                {
                    // ignore
                }
            }

            var tp = t.GetProperty("text", f);
            if (tp != null && tp.CanWrite)
            {
                try
                {
                    tp.SetValue(c, text);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static void OnDhcpClicked()
    {
        var setIp = ResolveSetIPForTick();
        var srv = setIp?.server;
        if (srv == null)
        {
            return;
        }

        if (!DHCPManager.AssignDhcpToSingleServer(srv))
        {
            return;
        }

        var ip = DHCPManager.GetServerIP(srv);
        setIp.ipAddress = ip;
        TrySetIpTextFieldDisplayString(setIp, ip ?? "");
    }

    private static void TrySetIpTextFieldDisplayString(SetIP setIp, string ip)
    {
        var tfProp = typeof(SetIP).GetProperty("ipTextField", BindingFlags.Public | BindingFlags.Instance);
        var tf = tfProp?.GetValue(setIp);
        if (tf == null)
        {
            return;
        }

        for (var bt = tf.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)
        {
            var textProp = bt.GetProperty("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (textProp != null && textProp.CanWrite)
            {
                try
                {
                    textProp.SetValue(tf, ip);
                    return;
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
