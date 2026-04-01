using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DHCPSwitches;

public static class IPAMOverlay
{
    public static bool IsVisible { get; set; }

    private static Rect _windowRect = new(50f, 50f, 900f, 600f);
    private static Vector2 _scroll = Vector2.zero;
    private static string _selectedIP = string.Empty;
    private static Server _selectedServer;

    public static void Draw()
    {
        if (!IsVisible)
        {
            return;
        }

        _windowRect = GUI.Window(9001, _windowRect, (GUI.WindowFunction)DrawWindow, "IPAM / Network Dashboard");
    }

    private static void DrawWindow(int id)
    {
        var dhcpUnlocked = LicenseManager.IsDHCPUnlocked;
        var ipamUnlocked = LicenseManager.IsIPAMUnlocked;

        GUI.Label(new Rect(10, 25, 400, 20), $"DHCP: {(dhcpUnlocked ? "AN" : "AUS")} | IPAM: {(ipamUnlocked ? "AN" : "AUS")}");

        if (GUI.Button(new Rect(10, 50, 170, 24), "Auto-DHCP (alle Server)") && dhcpUnlocked)
        {
            DHCPManager.AssignAllServers();
        }

        if (GUI.Button(new Rect(190, 50, 160, 24), DHCPManager.IsFlowPaused ? "Flow fortsetzen" : "Flow pausieren"))
        {
            DHCPManager.ToggleFlow();
        }

        if (GUI.Button(new Rect(820, 25, 70, 24), "Schliessen"))
        {
            IsVisible = false;
        }

        GUI.Box(new Rect(5, 80, _windowRect.width - 10, 1), string.Empty);

        if (!ipamUnlocked)
        {
            GUI.Label(new Rect(10, 100, 600, 40), "IPAM-Lizenz nicht freigeschaltet.");
            GUI.DragWindow();
            return;
        }

        _scroll = GUI.BeginScrollView(
            new Rect(5, 85, _windowRect.width - 10, _windowRect.height - 190),
            _scroll,
            new Rect(0, 0, _windowRect.width - 30, GetTotalContentHeight()));

        DrawRackGrid();

        GUI.EndScrollView();

        if (_selectedServer != null)
        {
            DrawServerDetail();
        }

        GUI.DragWindow();
    }

    private static void DrawRackGrid()
    {
        var servers = UnityEngine.Object.FindObjectsOfType<Server>();
        var switches = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();

        var x = 10f;
        var y = 5f;
        var slotW = 210f;
        var slotH = 24f;
        var padding = 4f;
        var cols = 4;
        var col = 0;

        GUI.Label(new Rect(x, y, 300, 18), "Switches");
        y += 22f;

        foreach (var sw in switches)
        {
            var slotRect = new Rect(x + col * (slotW + padding), y, slotW, slotH);
            if (GUI.Button(slotRect, $"SW {sw.name}"))
            {
                MelonLogger.Msg($"Switch ausgewaehlt: {sw.name}");
            }

            col++;
            if (col >= cols)
            {
                col = 0;
                y += slotH + padding;
            }
        }

        if (col > 0)
        {
            col = 0;
            y += slotH + padding;
        }

        y += 10f;
        GUI.Label(new Rect(x, y, 300, 18), "Server");
        y += 22f;

        foreach (var server in servers)
        {
            var ip = DHCPManager.GetServerIP(server);
            var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
            var label = hasIp ? $"{server.name} [{ip}]" : $"{server.name} [keine IP]";

            var slotRect = new Rect(x + col * (slotW + padding), y, slotW, slotH);
            if (GUI.Button(slotRect, label))
            {
                _selectedServer = server;
                _selectedIP = ip;
            }

            col++;
            if (col >= cols)
            {
                col = 0;
                y += slotH + padding;
            }
        }
    }

    private static void DrawServerDetail()
    {
        var panelY = _windowRect.height - 100f;
        GUI.Box(new Rect(5, panelY, _windowRect.width - 10, 95), string.Empty);

        var currentIp = _selectedServer != null ? DHCPManager.GetServerIP(_selectedServer) : string.Empty;
        GUI.Label(new Rect(15, panelY + 8, 500, 20), $"Server: {_selectedServer?.name} | IP: {currentIp}");

        GUI.Label(new Rect(15, panelY + 32, 80, 20), "Neue IP:");
        _selectedIP = GUI.TextField(new Rect(100, panelY + 32, 140, 20), _selectedIP);

        if (GUI.Button(new Rect(250, panelY + 32, 80, 20), "Setzen") && _selectedServer != null)
        {
            DHCPManager.SetServerIP(_selectedServer, _selectedIP);
        }

        if (GUI.Button(new Rect(340, panelY + 32, 110, 20), "DHCP Auto-IP") && _selectedServer != null)
        {
            DHCPManager.SetServerIP(_selectedServer, string.Empty);
        }

        if (GUI.Button(new Rect(460, panelY + 32, 80, 20), "Schliessen"))
        {
            _selectedServer = null;
            _selectedIP = string.Empty;
        }
    }

    private static float GetTotalContentHeight()
    {
        try
        {
            var serverCount = UnityEngine.Object.FindObjectsOfType<Server>().Length;
            var switchCount = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>().Length;
            return Mathf.Max(300f, (serverCount + switchCount) * 32f + 120f);
        }
        catch
        {
            return 300f;
        }
    }
}
