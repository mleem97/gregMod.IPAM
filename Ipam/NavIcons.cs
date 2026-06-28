using System.Collections.Generic;
using UnityEngine;

namespace GregModIPAM;

/// <summary>
/// Lucide-style 18×18 line icons procedurally generated as Texture2D.
/// Stroke width 1.5px equivalent, rounded caps, 2px padding.
/// </summary>
internal static class NavIcons
{
    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static bool _init;
    private const int S = 18;

    internal static void EnsureInit()
    {
        if (_init) return;
        _init = true;

        Cache["dashboard"]  = Make(DrawLayoutDashboard);
        Cache["devices"]    = Make(DrawNetwork);
        Cache["switch"]     = Make(DrawEthernetPort);
        Cache["router"]     = Make(DrawRouter);
        Cache["firewall"]   = Make(DrawShield);
        Cache["server"]     = Make(DrawServer);
        Cache["rack"]       = Make(DrawHddRack);
        Cache["ipam"]       = Make(DrawDatabase);
        Cache["ip"]         = Make(DrawBinary);
        Cache["prefix"]     = Make(DrawListTree);
        Cache["vlan"]       = Make(DrawLayers);
        Cache["dhcp"]       = Make(DrawBrackets);
        Cache["tutorial"]   = Make(DrawBookOpen);
        Cache["customers"]  = Make(DrawUsersRound);
        Cache["settings"]   = Make(DrawSettings);
        Cache["chevron_r"]  = Make(DrawChevronRight);
        Cache["chevron_d"]  = Make(DrawChevronDown);
    }

    internal static Texture2D Get(string key)
    {
        EnsureInit();
        return Cache.TryGetValue(key, out var t) ? t : null;
    }

    // ── Factory ──

    private static Texture2D Make(System.Action<int[,]> draw)
    {
        var px = new int[S, S];
        draw(px);
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[S * S];
        for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
                pixels[(S - 1 - y) * S + x] = px[x, y] > 0
                    ? new Color32(255, 255, 255, (byte)px[x, y])
                    : new Color32(0, 0, 0, 0);
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // ── Drawing helpers (Lucide-style strokes) ──

    private static void Dot(int[,] p, int x, int y, int a = 255) { if (x >= 0 && x < S && y >= 0 && y < S) p[x, y] = a; }

    private static void H(int[,] p, int x0, int x1, int y, int a = 255)
    {
        for (var x = x0; x <= x1; x++) Dot(p, x, y, a);
    }

    private static void V(int[,] p, int x, int y0, int y1, int a = 255)
    {
        for (var y = y0; y <= y1; y++) Dot(p, x, y, a);
    }

    private static void Box(int[,] p, int x0, int y0, int w, int h)
    {
        H(p, x0, x0 + w - 1, y0); H(p, x0, x0 + w - 1, y0 + h - 1);
        V(p, x0, y0, y0 + h - 1); V(p, x0 + w - 1, y0, y0 + h - 1);
    }

    private static void RBox(int[,] p, int x0, int y0, int w, int h)
    {
        H(p, x0 + 1, x0 + w - 2, y0); H(p, x0 + 1, x0 + w - 2, y0 + h - 1);
        V(p, x0, y0 + 1, y0 + h - 2); V(p, x0 + w - 1, y0 + 1, y0 + h - 2);
    }

    // ── Lucide Icons ──

    // layout-dashboard: 2×2 grid
    private static void DrawLayoutDashboard(int[,] p)
    {
        RBox(p, 2, 2, 6, 6); RBox(p, 10, 2, 6, 6);
        RBox(p, 2, 10, 6, 6); RBox(p, 10, 10, 6, 6);
    }

    // network: nodes connected by lines
    private static void DrawNetwork(int[,] p)
    {
        RBox(p, 6, 2, 6, 4); RBox(p, 2, 12, 6, 4); RBox(p, 10, 12, 6, 4);
        V(p, 9, 6, 9); H(p, 5, 9, 9); V(p, 5, 9, 12);
        Dot(p, 9, 9, 180); Dot(p, 5, 9, 180);
    }

    // ethernet-port: rectangle with port
    private static void DrawEthernetPort(int[,] p)
    {
        RBox(p, 3, 4, 12, 10);
        RBox(p, 6, 7, 6, 4);
        V(p, 9, 14, 16); H(p, 8, 10, 16);
    }

    // router: circle with arrows
    private static void DrawRouter(int[,] p)
    {
        H(p, 5, 12, 3); H(p, 5, 12, 14);
        V(p, 3, 5, 12); V(p, 14, 5, 12);
        Dot(p, 4, 4); Dot(p, 13, 4); Dot(p, 4, 13); Dot(p, 13, 13);
        H(p, 7, 10, 8); H(p, 7, 10, 10);
        V(p, 8, 7, 11); V(p, 10, 7, 11);
    }

    // shield: shield shape
    private static void DrawShield(int[,] p)
    {
        H(p, 3, 14, 3); V(p, 3, 3, 10);
        V(p, 14, 3, 10);
        Dot(p, 4, 11); Dot(p, 13, 11);
        Dot(p, 5, 12); Dot(p, 12, 12);
        Dot(p, 6, 13); Dot(p, 11, 13);
        H(p, 7, 10, 14);
        // check
        Dot(p, 7, 8); Dot(p, 8, 9); Dot(p, 9, 8); Dot(p, 10, 7);
    }

    // server: stacked rectangles
    private static void DrawServer(int[,] p)
    {
        RBox(p, 3, 2, 12, 4); RBox(p, 3, 7, 12, 4); RBox(p, 3, 12, 12, 4);
        Dot(p, 5, 4); Dot(p, 5, 9); Dot(p, 5, 14);
    }

    // hdd-rack: rack unit
    private static void DrawHddRack(int[,] p)
    {
        Box(p, 3, 1, 12, 16);
        H(p, 4, 13, 5); H(p, 4, 13, 10);
        Dot(p, 5, 3); Dot(p, 7, 3);
        Dot(p, 5, 7); Dot(p, 7, 7);
        Dot(p, 5, 12); Dot(p, 7, 12);
        V(p, 10, 2, 4); V(p, 10, 6, 9); V(p, 10, 11, 14);
    }

    // database: cylinder
    private static void DrawDatabase(int[,] p)
    {
        H(p, 4, 13, 3); H(p, 4, 13, 7); H(p, 4, 13, 14);
        V(p, 3, 3, 14); V(p, 14, 3, 14);
        Dot(p, 4, 4); Dot(p, 13, 4);
        Dot(p, 4, 8); Dot(p, 13, 8);
        H(p, 4, 13, 13, 180);
    }

    // binary: "10" text
    private static void DrawBinary(int[,] p)
    {
        V(p, 3, 3, 14); V(p, 5, 3, 14);
        H(p, 3, 5, 3); H(p, 3, 5, 7);
        // 0
        Box(p, 8, 3, 6, 12);
    }

    // list-tree: branches
    private static void DrawListTree(int[,] p)
    {
        V(p, 3, 2, 15);
        H(p, 3, 6, 5); H(p, 3, 6, 9); H(p, 3, 6, 13);
        RBox(p, 7, 4, 8, 3); RBox(p, 7, 8, 8, 3); RBox(p, 7, 12, 8, 3);
    }

    // layers: stacked parallelograms
    private static void DrawLayers(int[,] p)
    {
        H(p, 4, 13, 3); H(p, 3, 12, 7); H(p, 4, 13, 11); H(p, 3, 12, 15);
        Dot(p, 3, 4); Dot(p, 13, 4); Dot(p, 2, 8); Dot(p, 12, 8);
        Dot(p, 3, 12); Dot(p, 13, 12); Dot(p, 2, 16); Dot(p, 12, 16);
    }

    // brackets: [ ]
    private static void DrawBrackets(int[,] p)
    {
        V(p, 3, 3, 14); H(p, 3, 5, 3); H(p, 3, 5, 14);
        V(p, 14, 3, 14); H(p, 12, 14, 3); H(p, 12, 14, 14);
        Dot(p, 6, 6); Dot(p, 11, 11);
    }

    // book-open: open book
    private static void DrawBookOpen(int[,] p)
    {
        V(p, 9, 2, 14);
        H(p, 3, 8, 2); H(p, 3, 8, 14);
        V(p, 3, 2, 14);
        H(p, 10, 14, 2); H(p, 10, 14, 14);
        V(p, 14, 2, 14);
    }

    // users-round: two people
    private static void DrawUsersRound(int[,] p)
    {
        // left head
        Dot(p, 5, 3); Dot(p, 6, 3); Dot(p, 4, 4); Dot(p, 7, 4);
        Dot(p, 4, 5); Dot(p, 7, 5); Dot(p, 5, 6); Dot(p, 6, 6);
        // left body
        H(p, 3, 8, 9); V(p, 3, 10, 14); V(p, 8, 10, 14);
        // right head
        Dot(p, 11, 3); Dot(p, 12, 3); Dot(p, 10, 4); Dot(p, 13, 4);
        Dot(p, 10, 5); Dot(p, 13, 5); Dot(p, 11, 6); Dot(p, 12, 6);
        // right body
        H(p, 9, 14, 9); V(p, 9, 10, 14); V(p, 14, 10, 14);
    }

    // settings: gear
    private static void DrawSettings(int[,] p)
    {
        H(p, 7, 10, 1); H(p, 7, 10, 16);
        H(p, 1, 4, 7); H(p, 13, 16, 7);
        H(p, 2, 5, 11); H(p, 12, 15, 11);
        Dot(p, 2, 8); Dot(p, 2, 10);
        Dot(p, 15, 8); Dot(p, 15, 10);
        Box(p, 6, 6, 6, 6);
        Dot(p, 8, 8); Dot(p, 9, 8); Dot(p, 8, 9); Dot(p, 9, 9, 0);
    }

    // chevron-right: >
    private static void DrawChevronRight(int[,] p)
    {
        Dot(p, 6, 5); Dot(p, 7, 6); Dot(p, 8, 7);
        Dot(p, 9, 8); Dot(p, 8, 9); Dot(p, 7, 10); Dot(p, 6, 11);
        Dot(p, 10, 5); Dot(p, 11, 6); Dot(p, 12, 7);
        Dot(p, 13, 8); Dot(p, 12, 9); Dot(p, 11, 10); Dot(p, 10, 11);
    }

    // chevron-down: v
    private static void DrawChevronDown(int[,] p)
    {
        Dot(p, 5, 6); Dot(p, 6, 7); Dot(p, 7, 8); Dot(p, 8, 9);
        Dot(p, 9, 8); Dot(p, 10, 7); Dot(p, 11, 6);
        Dot(p, 5, 10); Dot(p, 6, 11); Dot(p, 7, 12); Dot(p, 8, 13);
        Dot(p, 9, 12); Dot(p, 10, 11); Dot(p, 11, 10);
    }
}
