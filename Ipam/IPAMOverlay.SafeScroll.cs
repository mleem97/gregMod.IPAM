using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

// Game update broke Il2CppInterop unstripping for `GUI.BeginScrollView` / `GUI.Scroller` / scrollbar APIs.
// Manual scroll: viewport clip group + inner translate group (standard IMGUI pattern), custom thumb via
// GetControlID (same fix as ImguiButtonOnce), wheel via Event + Input.mouseScrollDelta fallback.
public static partial class IPAMOverlay
{
    private const float SafeScrollScrollbarThickness = 14f;
    private const float SafeScrollWheelStep = 20f;
    private const float SafeScrollMinThumbSize = 24f;
    private const int SafeScrollThumbControlBase = unchecked((int)0xD1C0_7000);

    private static bool _safeScrollManualFallback;
    private static bool _safeScrollFallbackLogged;
    private static readonly Stack<SafeScrollFrame> _safeScrollStack = new Stack<SafeScrollFrame>(8);

    private struct SafeScrollFrame
    {
        public bool ManualMode;
        public int GroupDepth;
    }

    internal static Vector2 SafeBeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect)
    {
        if (!_safeScrollManualFallback)
        {
            try
            {
                var v = GUI.BeginScrollView(position, scrollPosition, viewRect);
                _safeScrollStack.Push(new SafeScrollFrame { ManualMode = false, GroupDepth = 0 });
                return v;
            }
            catch (Exception ex)
            {
                _safeScrollManualFallback = true;
                if (!_safeScrollFallbackLogged)
                {
                    _safeScrollFallbackLogged = true;
                    try
                    {
                        ModLogging.Warning(
                            "DHCPSwitches: GUI.BeginScrollView unsupported on this game build ("
                            + ex.GetType().Name
                            + "), switching IPAM scroll views to manual fallback.");
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        return BeginManualScrollView(position, scrollPosition, viewRect);
    }

    internal static void SafeScrollForceReset()
    {
        while (_safeScrollStack.Count > 0)
        {
            var top = _safeScrollStack.Pop();
            if (top.ManualMode)
            {
                for (var i = 0; i < top.GroupDepth; i++)
                {
                    try
                    {
                        GUI.EndGroup();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            else
            {
                try
                {
                    GUI.EndScrollView();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    internal static void SafeEndScrollView()
    {
        if (_safeScrollStack.Count == 0)
        {
            return;
        }

        var top = _safeScrollStack.Pop();
        if (top.ManualMode)
        {
            for (var i = 0; i < top.GroupDepth; i++)
            {
                try
                {
                    GUI.EndGroup();
                }
                catch
                {
                    // ignore
                }
            }

            return;
        }

        try
        {
            GUI.EndScrollView();
        }
        catch
        {
            // ignore
        }
    }

    private static Vector2 BeginManualScrollView(Rect position, Vector2 scroll, Rect view)
    {
        var needsVertical = view.height > position.height + 0.5f;
        var needsHorizontal = view.width > position.width + 0.5f;

        var scrollbarW = needsVertical ? SafeScrollScrollbarThickness : 0f;
        var scrollbarH = needsHorizontal ? SafeScrollScrollbarThickness : 0f;

        var viewport = new Rect(
            position.x,
            position.y,
            position.width - scrollbarW,
            position.height - scrollbarH);

        var maxScrollY = Mathf.Max(0f, view.height - viewport.height);
        var maxScrollX = Mathf.Max(0f, view.width - viewport.width);

        scroll = ApplyManualScrollWheel(viewport, scroll, maxScrollX, maxScrollY, needsVertical, needsHorizontal);

        if (needsVertical)
        {
            var trackRect = new Rect(
                position.xMax - scrollbarW,
                position.y,
                scrollbarW,
                viewport.height);
            scroll.y = DrawCustomScrollbar(trackRect, scroll.y, viewport.height, view.height, false, 0);
            scroll.y = Mathf.Clamp(scroll.y, 0f, maxScrollY);
        }
        else
        {
            scroll.y = 0f;
        }

        if (needsHorizontal)
        {
            var trackRect = new Rect(
                position.x,
                position.yMax - scrollbarH,
                viewport.width,
                scrollbarH);
            scroll.x = DrawCustomScrollbar(trackRect, scroll.x, viewport.width, view.width, true, 1);
            scroll.x = Mathf.Clamp(scroll.x, 0f, maxScrollX);
        }
        else
        {
            scroll.x = 0f;
        }

        var depth = 0;
        try
        {
            GUI.BeginGroup(viewport);
            depth++;
            GUI.BeginGroup(new Rect(-scroll.x, -scroll.y, view.width, view.height));
            depth++;
        }
        catch
        {
            for (var i = 0; i < depth; i++)
            {
                try
                {
                    GUI.EndGroup();
                }
                catch
                {
                    // ignore
                }
            }

            depth = 0;
        }

        _safeScrollStack.Push(new SafeScrollFrame { ManualMode = true, GroupDepth = depth });
        return scroll;
    }

    private static Vector2 ApplyManualScrollWheel(
        Rect viewport,
        Vector2 scroll,
        float maxScrollX,
        float maxScrollY,
        bool needsVertical,
        bool needsHorizontal)
    {
        if (Event.current == null || Event.current.type != EventType.ScrollWheel || !TryConsumeScrollWheelThisFrame())
        {
            return scroll;
        }

        var mouse = Event.current.mousePosition;
        if (!viewport.Contains(mouse))
        {
            return scroll;
        }

        var delta = Event.current.delta.y * SafeScrollWheelStep;
        try
        {
            Event.current.Use();
        }
        catch
        {
            // ignore
        }

        if (Mathf.Abs(delta) < 0.001f)
        {
            return scroll;
        }

        if (needsVertical)
        {
            scroll.y = Mathf.Clamp(scroll.y + delta, 0f, maxScrollY);
        }
        else if (needsHorizontal)
        {
            scroll.x = Mathf.Clamp(scroll.x + delta, 0f, maxScrollX);
        }

        return scroll;
    }

    private static float DrawCustomScrollbar(
        Rect track,
        float value,
        float viewportSize,
        float contentSize,
        bool horizontal,
        int axisIndex)
    {
        DrawTintedRect(track, new Color(0.10f, 0.12f, 0.15f, 0.85f));

        var maxValue = Mathf.Max(0f, contentSize - viewportSize);
        if (maxValue <= 0f || viewportSize <= 0f || contentSize <= 0f)
        {
            return 0f;
        }

        var trackLen = horizontal ? track.width : track.height;
        var thumbLen = Mathf.Clamp(trackLen * (viewportSize / contentSize), SafeScrollMinThumbSize, trackLen);
        var travel = Mathf.Max(1f, trackLen - thumbLen);
        var thumbStart = (value / maxValue) * travel;

        Rect thumbRect;
        if (horizontal)
        {
            thumbRect = new Rect(track.x + thumbStart, track.y + 2f, thumbLen, track.height - 4f);
        }
        else
        {
            thumbRect = new Rect(track.x + 2f, track.y + thumbStart, track.width - 4f, thumbLen);
        }

        var controlHint = SafeScrollThumbControlBase
                          + HashCode.Combine((int)track.x, (int)track.y, (int)track.width, axisIndex);
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, thumbRect);
        var e = Event.current;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (GUI.enabled && e.button == 0)
                {
                    if (thumbRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        _safeScrollDragGrabOffset = horizontal
                            ? (e.mousePosition.x - thumbRect.x)
                            : (e.mousePosition.y - thumbRect.y);
                        e.Use();
                    }
                    else if (track.Contains(e.mousePosition))
                    {
                        var clickPos = horizontal ? e.mousePosition.x : e.mousePosition.y;
                        var thumbPos = horizontal ? thumbRect.x : thumbRect.y;
                        var step = viewportSize * 0.9f;
                        value = Mathf.Clamp(value + (clickPos < thumbPos ? -step : step), 0f, maxValue);
                        e.Use();
                    }
                }

                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    var anchor = horizontal ? track.x : track.y;
                    var current = horizontal ? e.mousePosition.x : e.mousePosition.y;
                    var thumbCorner = Mathf.Clamp(current - _safeScrollDragGrabOffset, anchor, anchor + travel);
                    var t = (thumbCorner - anchor) / travel;
                    value = Mathf.Clamp(t * maxValue, 0f, maxValue);
                    e.Use();
                }

                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }

                break;

            case EventType.Repaint:
                var hot = GUIUtility.hotControl == id;
                var thumbColor = hot
                    ? new Color(0.65f, 0.72f, 0.80f, 1f)
                    : new Color(0.45f, 0.50f, 0.55f, 0.95f);
                DrawTintedRect(thumbRect, thumbColor);
                break;
        }

        return value;
    }

    private static float _safeScrollDragGrabOffset;
    private static int _safeScrollLastWheelFrame = -1;

    private static bool TryConsumeScrollWheelThisFrame()
    {
        if (_safeScrollLastWheelFrame == Time.frameCount)
        {
            return false;
        }

        _safeScrollLastWheelFrame = Time.frameCount;
        return true;
    }
}
