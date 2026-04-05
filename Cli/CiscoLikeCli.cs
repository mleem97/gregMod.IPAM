using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DHCPSwitches;

public enum CliExecMode
{
    User,
    Privileged,
    Config,
    Interface,
}

/// <summary>Cisco IOS–style CLI state and command execution for one <see cref="NetworkSwitch"/>.</summary>
public sealed class CiscoLikeCliSession
{
    private readonly NetworkSwitch _switch;
    private readonly NetworkDeviceKind _kind;
    private CliExecMode _mode = CliExecMode.User;
    private RouterInterfaceConfig _ifaceFocus;
    private SwitchPortConfig _portFocus;

    public CiscoLikeCliSession(NetworkSwitch sw)
    {
        _switch = sw;
        _kind = NetworkDeviceClassifier.GetKind(sw);
        var ports = NetworkDeviceClassifier.GetPortCount(sw);
        if (_kind == NetworkDeviceKind.Router)
        {
            DeviceConfigRegistry.GetOrCreateRouter(sw, ports);
        }
        else
        {
            DeviceConfigRegistry.GetOrCreateSwitch(sw, ports);
        }
    }

    public string Prompt
    {
        get
        {
            var h = _kind == NetworkDeviceKind.Router
                ? DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch)).Hostname
                : DeviceConfigRegistry.GetOrCreateSwitch(_switch, NetworkDeviceClassifier.GetPortCount(_switch)).Hostname;

            return _mode switch
            {
                CliExecMode.User => $"{h}>",
                CliExecMode.Privileged => $"{h}#",
                CliExecMode.Config => $"{h}(config)#",
                CliExecMode.Interface => _kind == NetworkDeviceKind.Router
                    ? $"{h}(config-if)#"
                    : $"{h}(config-if)#",
                _ => ">",
            };
        }
    }

    public void Execute(string line, StringBuilder output)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var tokens = Tokenize(line);
        if (StripHelpMarker(tokens))
        {
            CliAutocomplete.ExpandAbbreviations(tokens, this);
            EmitContextHelp(tokens, output);
            return;
        }

        CliAutocomplete.ExpandAbbreviations(tokens, this);

        if (tokens.Count == 0)
        {
            return;
        }

        var cmd = tokens[0].ToLowerInvariant();
        if (cmd == "no" && tokens.Count > 1)
        {
            HandleNo(tokens, output);
            return;
        }

        switch (_mode)
        {
            case CliExecMode.User:
                ExecUser(tokens, output);
                break;
            case CliExecMode.Privileged:
                ExecPrivileged(tokens, output);
                break;
            case CliExecMode.Config:
                ExecConfig(tokens, output);
                break;
            case CliExecMode.Interface:
                ExecInterface(tokens, output);
                break;
        }
    }

    /// <summary>Used by <see cref="CliAutocomplete"/> (tab / abbreviation).</summary>
    internal CliExecMode Mode => _mode;

    /// <summary>Used by <see cref="CliAutocomplete"/>.</summary>
    internal NetworkDeviceKind Kind => _kind;

    /// <summary>Used by <see cref="CliAutocomplete"/>.</summary>
    internal NetworkSwitch Target => _switch;

    public bool TryTabComplete(ref string line) => CliAutocomplete.TryTabComplete(ref line, this);

    private void ResetSessionAfterErase()
    {
        _mode = CliExecMode.Privileged;
        _ifaceFocus = null;
        _portFocus = null;
    }

    private void HandleNo(List<string> tokens, StringBuilder output)
    {
        if (_mode == CliExecMode.Interface && _kind == NetworkDeviceKind.Router && tokens.Count >= 3
            && tokens[1].Equals("ip", StringComparison.OrdinalIgnoreCase)
            && tokens[2].Equals("address", StringComparison.OrdinalIgnoreCase))
        {
            if (_ifaceFocus != null)
            {
                _ifaceFocus.IpAddress = "";
                _ifaceFocus.SubnetMask = "";
                output.AppendLine("IP address removed from this interface.");
            }

            return;
        }

        var sub = tokens[1].ToLowerInvariant();
        if (_mode == CliExecMode.Interface && _kind == NetworkDeviceKind.Router && sub == "shutdown")
        {
            if (_ifaceFocus != null)
            {
                _ifaceFocus.Shutdown = false;
                output.AppendLine("Interface brought up (no shutdown).");
            }

            return;
        }

        if (_mode == CliExecMode.Interface && _kind == NetworkDeviceKind.Layer2Switch && sub == "switchport" && tokens.Count >= 4
            && tokens[2].Equals("access", StringComparison.OrdinalIgnoreCase) && tokens[3].Equals("vlan", StringComparison.OrdinalIgnoreCase))
        {
            if (_portFocus != null)
            {
                _portFocus.AccessVlan = 1;
                output.AppendLine("Reverted access VLAN to default (1).");
            }

            return;
        }

        if (_mode == CliExecMode.Config && _kind == NetworkDeviceKind.Router
            && tokens.Count >= 5
            && tokens[1].Equals("ip", StringComparison.OrdinalIgnoreCase)
            && tokens[2].Equals("route", StringComparison.OrdinalIgnoreCase))
        {
            var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
            var syn = new List<string>(tokens.Count - 1) { "ip", "route" };
            for (var i = 3; i < tokens.Count; i++)
            {
                syn.Add(tokens[i]);
            }

            if (!TryParseIpRouteTokens(syn, output, out var destStr, out _, out var nh))
            {
                return;
            }

            TryRemoveStaticRoute(rc, destStr, nh, output);
            return;
        }

        output.AppendLine("% Incomplete command.");
    }

    /// <summary>Removes static route(s) matching normalized prefix + next-hop (same forms as <c>ip route</c>).</summary>
    private static void TryRemoveStaticRoute(RouterRuntimeConfig rc, string destStr, string nh, StringBuilder output)
    {
        if (rc == null || string.IsNullOrWhiteSpace(destStr) || string.IsNullOrWhiteSpace(nh))
        {
            output.AppendLine("% Incomplete command.");
            return;
        }

        if (!RouteMath.TryParsePrefix(destStr.Trim(), out var wantNet, out var wantPl))
        {
            output.AppendLine("% Invalid destination prefix.");
            return;
        }

        var nhNorm = nh.Trim();
        var removed = 0;
        for (var i = rc.StaticRoutes.Count - 1; i >= 0; i--)
        {
            var r = rc.StaticRoutes[i];
            if (r == null || string.IsNullOrWhiteSpace(r.DestinationPrefix))
            {
                continue;
            }

            if (!RouteMath.TryParsePrefix(r.DestinationPrefix.Trim(), out var rNet, out var rPl))
            {
                continue;
            }

            if (rNet != wantNet || rPl != wantPl
                || !string.Equals(r.NextHop?.Trim(), nhNorm, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rc.StaticRoutes.RemoveAt(i);
            removed++;
        }

        if (removed == 0)
        {
            output.AppendLine("% Static route not in configuration (check prefix and next-hop match exactly).");
            return;
        }

        output.AppendLine(removed > 1
            ? $"Static routes removed: {destStr} via {nhNorm} ({removed} entries)"
            : $"Static route removed: {destStr} via {nhNorm}");
        ModDebugLog.Trace("cli", $"no ip route {destStr} via {nhNorm} removed={removed}");
    }

    private void ExecUser(List<string> tokens, StringBuilder output)
    {
        switch (tokens[0].ToLowerInvariant())
        {
            case "enable":
                _mode = CliExecMode.Privileged;
                break;
            case "show":
                if (tokens.Count >= 2 && tokens[1].Equals("version", StringComparison.OrdinalIgnoreCase))
                {
                    output.AppendLine("DHCPSwitches simulated IOS 15.x (mod).");
                }
                else
                {
                    output.AppendLine("% Use 'enable' first for full show commands.");
                }

                break;
            default:
                output.AppendLine("% Unknown command.");
                break;
        }
    }

    private void ExecPrivileged(List<string> tokens, StringBuilder output)
    {
        var c = tokens[0].ToLowerInvariant();
        switch (c)
        {
            case "configure":
                if (tokens.Count >= 2 && tokens[1].Equals("terminal", StringComparison.OrdinalIgnoreCase))
                {
                    _mode = CliExecMode.Config;
                }
                else
                {
                    output.AppendLine("% Incomplete command.");
                }

                break;
            case "show":
                ExecShow(tokens, output, privileged: true);
                break;
            case "write":
                if (tokens.Count >= 2 && tokens[1].Equals("memory", StringComparison.OrdinalIgnoreCase))
                {
                    output.AppendLine("Building configuration...");
                    if (DeviceConfigRegistry.TrySaveAllToDisk())
                    {
                        output.AppendLine("[OK] Configuration saved (see UserData/DHCPSwitches/saved_device_configs.json).");
                    }
                    else
                    {
                        output.AppendLine("% Error: could not write configuration file (see MelonLoader log).");
                    }
                }
                else if (tokens.Count >= 2 && tokens[1].Equals("erase", StringComparison.OrdinalIgnoreCase))
                {
                    var ports = NetworkDeviceClassifier.GetPortCount(_switch);
                    if (_kind == NetworkDeviceKind.Router)
                    {
                        DeviceConfigRegistry.EraseRouterConfig(_switch, ports);
                    }
                    else
                    {
                        DeviceConfigRegistry.EraseSwitchConfig(_switch, ports);
                    }

                    ResetSessionAfterErase();
                    output.AppendLine("Erasing startup configuration (this device only)...");
                    output.AppendLine("[OK] Running configuration reset to factory default.");
                    if (DeviceConfigRegistry.TrySaveAllToDisk())
                    {
                        output.AppendLine("[OK] Saved (other devices unchanged).");
                    }
                    else
                    {
                        output.AppendLine("% Warning: could not save to disk; erase is in memory until write memory succeeds.");
                    }

                    ModDebugLog.Trace("cli", $"write erase ({_kind})");
                }
                else
                {
                    output.AppendLine("% Incomplete command. Use: write memory | write erase");
                }

                break;
            case "ping":
                ExecPing(tokens, output);
                break;
            case "disable":
                _mode = CliExecMode.User;
                break;
            default:
                output.AppendLine("% Unknown command.");
                break;
        }
    }

    private void ExecConfig(List<string> tokens, StringBuilder output)
    {
        var c = tokens[0].ToLowerInvariant();
        switch (c)
        {
            case "hostname":
                if (tokens.Count < 2)
                {
                    output.AppendLine("% Incomplete command.");
                    return;
                }

                var name = tokens[1];
                if (_kind == NetworkDeviceKind.Router)
                {
                    DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch)).Hostname = name;
                }
                else
                {
                    DeviceConfigRegistry.GetOrCreateSwitch(_switch, NetworkDeviceClassifier.GetPortCount(_switch)).Hostname = name;
                }

                output.AppendLine($"Hostname set to {name}");
                break;
            case "interface":
                if (tokens.Count < 2)
                {
                    output.AppendLine("% Incomplete command.");
                    return;
                }

                if (!TryParseInterface(tokens[1], out var idx, output))
                {
                    return;
                }

                _mode = CliExecMode.Interface;
                if (_kind == NetworkDeviceKind.Router)
                {
                    var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                    _ifaceFocus = rc.Interfaces.Find(i => i.Index == idx) ?? rc.Interfaces[Mathf.Clamp(idx, 0, rc.Interfaces.Count - 1)];
                }
                else
                {
                    var sc = DeviceConfigRegistry.GetOrCreateSwitch(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                    _portFocus = sc.Ports.Find(p => p.PortIndex == idx) ?? sc.Ports[Mathf.Clamp(idx, 0, sc.Ports.Count - 1)];
                }

                break;
            case "vlan":
                if (_kind != NetworkDeviceKind.Layer2Switch)
                {
                    output.AppendLine("% Invalid for router (use interface subinterfaces on router).");
                    return;
                }

                if (tokens.Count < 2 || !int.TryParse(tokens[1], out var vid))
                {
                    output.AppendLine("% Incomplete command.");
                    return;
                }

                var swc = DeviceConfigRegistry.GetOrCreateSwitch(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                if (!swc.Vlans.Exists(v => v.Id == vid))
                {
                    swc.Vlans.Add(new SwitchVlanEntry { Id = vid, Name = $"VLAN{vid}" });
                }

                output.AppendLine($"VLAN {vid} created/selected.");
                break;
            case "ip":
                if (_kind != NetworkDeviceKind.Router)
                {
                    output.AppendLine("% ip route only valid on router in this mod.");
                    return;
                }

                if (tokens.Count >= 2 && tokens[1].Equals("route", StringComparison.OrdinalIgnoreCase))
                {
                    var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                    if (!TryParseIpRouteTokens(tokens, output, out var destStr, out var pl, out var nh))
                    {
                        return;
                    }

                    if (!RouterL3Validation.TryAcceptStaticRoute(rc, destStr, nh, out var staticErr))
                    {
                        output.AppendLine(staticErr);
                        return;
                    }

                    rc.StaticRoutes.Add(new StaticRouteEntry { DestinationPrefix = destStr, PrefixLength = pl, NextHop = nh });
                    output.AppendLine($"Static route added: {destStr} via {nh}");
                    ModDebugLog.Trace("cli", $"ip route added {destStr} via {nh}");
                }
                else
                {
                    output.AppendLine("% Incomplete command.");
                }

                break;
            case "end":
            case "exit":
                _mode = CliExecMode.Privileged;
                break;
            default:
                output.AppendLine("% Unknown command.");
                break;
        }
    }

    private void ExecInterface(List<string> tokens, StringBuilder output)
    {
        var c = tokens[0].ToLowerInvariant();
        if (c == "exit" || c == "end")
        {
            _mode = CliExecMode.Config;
            _ifaceFocus = null;
            _portFocus = null;
            return;
        }

        if (_kind == NetworkDeviceKind.Router && _ifaceFocus != null)
        {
            if (c == "ip" && tokens.Count >= 3 && tokens[1].Equals("address", StringComparison.OrdinalIgnoreCase))
            {
                string ipTok;
                string maskTok;
                if (tokens.Count >= 4)
                {
                    ipTok = tokens[2];
                    maskTok = tokens[3];
                }
                else if (tokens.Count == 3)
                {
                    var combined = tokens[2];
                    var slash = combined.IndexOf('/');
                    if (slash <= 0 || slash >= combined.Length - 1)
                    {
                        output.AppendLine("% Incomplete command.");
                        return;
                    }

                    ipTok = combined.Substring(0, slash);
                    maskTok = combined.Substring(slash);
                }
                else
                {
                    output.AppendLine("% Incomplete command.");
                    return;
                }

                if (!RouteMath.TryParseIpv4(ipTok.Trim(), out _))
                {
                    output.AppendLine("% Invalid IPv4 address.");
                    return;
                }

                if (!RouteMath.TryNormalizeSubnetToDotted(maskTok, out var dottedMask))
                {
                    output.AppendLine("% Invalid subnet mask or prefix (use 255.255.255.0 or /24).");
                    return;
                }

                var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                if (!RouterL3Validation.TryAcceptNewInterfaceAddress(rc, _ifaceFocus.Index, ipTok.Trim(), dottedMask, out var l3Err))
                {
                    output.AppendLine(l3Err);
                    return;
                }

                _ifaceFocus.IpAddress = ipTok.Trim();
                _ifaceFocus.SubnetMask = dottedMask;
                _ifaceFocus.Shutdown = false;
                output.AppendLine($"IP address set {_ifaceFocus.IpAddress} {_ifaceFocus.SubnetMask}");
                return;
            }

            if (c == "shutdown")
            {
                _ifaceFocus.Shutdown = true;
                output.AppendLine("Interface administratively down.");
                return;
            }

            if (c == "encapsulation" && tokens.Count >= 3 && tokens[1].Equals("dot1q", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(tokens[2], out var vlan))
                {
                    _ifaceFocus.NativeVlan = vlan;
                    output.AppendLine($"802.1Q VLAN {vlan} on {_ifaceFocus.Name}");
                }

                return;
            }
        }

        if (_kind == NetworkDeviceKind.Layer2Switch && _portFocus != null)
        {
            if (c == "switchport" && tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub == "mode" && tokens.Count >= 3)
                {
                    _portFocus.Mode = tokens[2].ToLowerInvariant();
                    _portFocus.Trunk = _portFocus.Mode == "trunk";
                    output.AppendLine($"Port mode {_portFocus.Mode}");
                    return;
                }

                if (sub == "access" && tokens.Count >= 3 && tokens[2].Equals("vlan", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 4
                    && int.TryParse(tokens[3], out var av))
                {
                    _portFocus.AccessVlan = av;
                    _portFocus.Trunk = false;
                    output.AppendLine($"Access VLAN {av}");
                    return;
                }

                if (sub == "trunk" && tokens.Count >= 3 && tokens[2].Equals("allowed", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 4
                    && tokens[3].Equals("vlan", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 5)
                {
                    _portFocus.AllowedVlanRaw = tokens[4];
                    output.AppendLine($"Trunk allowed VLANs {_portFocus.AllowedVlanRaw}");
                    return;
                }
            }
        }

        output.AppendLine("% Unknown or incomplete command.");
    }

    private void ExecShow(List<string> tokens, StringBuilder output, bool privileged)
    {
        if (!privileged)
        {
            output.AppendLine("% Use 'enable' first.");
            return;
        }

        if (tokens.Count < 2)
        {
            output.AppendLine("% Incomplete command.");
            return;
        }

        var a = tokens[1].ToLowerInvariant();
        if (a == "running-config" || a == "run")
        {
            DumpRunningConfig(output);
            return;
        }

        if (a == "ip" && tokens.Count >= 3 && tokens[2].Equals("route", StringComparison.OrdinalIgnoreCase))
        {
            if (_kind == NetworkDeviceKind.Router)
            {
                var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                output.AppendLine("Codes: C - connected, S - static");
                foreach (var r in rc.StaticRoutes)
                {
                    output.AppendLine($"S    {r.DestinationPrefix} via {r.NextHop}");
                }
            }
            else
            {
                output.AppendLine("% L2 switch has no IP routing table.");
            }

            return;
        }

        if (a == "interface")
        {
            if (tokens.Count < 3)
            {
                output.AppendLine("% Incomplete command.");
                return;
            }

            if (!TryParseInterface(tokens[2], out var ifIdx, output))
            {
                return;
            }

            EmitShowInterfaceDetail(ifIdx, output);
            return;
        }

        if (a == "interfaces" && tokens.Count >= 3 && tokens[2].Equals("brief", StringComparison.OrdinalIgnoreCase))
        {
            if (_kind == NetworkDeviceKind.Router)
            {
                var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                output.AppendLine("Interface  IP-Address      Media                    Link            Status  Protocol");
                foreach (var i in rc.Interfaces)
                {
                    SwitchPortHardwareProbe.GetDisplayHints(_switch, i.Index, NetworkDeviceKind.Router, out var media, out _);
                    var link = SwitchPortHardwareProbe.GetCliLinkStatus(_switch, i.Index, NetworkDeviceKind.Router, i.Shutdown);
                    var admin = i.Shutdown ? "down" : "up";
                    var hasL3 = !string.IsNullOrWhiteSpace(i.IpAddress) && !string.IsNullOrWhiteSpace(i.SubnetMask);
                    var lineProto = !i.Shutdown && hasL3 ? "up" : "down";
                    output.AppendLine($"{i.Name,-9}{i.IpAddress,-16}{media,-24}{link,-15}{admin,-8}{lineProto}");
                }
            }
            else
            {
                var sc = DeviceConfigRegistry.GetOrCreateSwitch(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
                output.AppendLine("Port   Mode      Trunk/VLAN       Media               Link");
                foreach (var p in sc.Ports)
                {
                    SwitchPortHardwareProbe.GetDisplayHints(_switch, p.PortIndex, NetworkDeviceKind.Layer2Switch, out var media, out _);
                    var link = SwitchPortHardwareProbe.GetCliLinkStatus(_switch, p.PortIndex, NetworkDeviceKind.Layer2Switch, false);
                    var vlanStr = p.Trunk ? $"trunk {p.AllowedVlanRaw}" : $"vlan {p.AccessVlan}";
                    output.AppendLine($"Fa0/{p.PortIndex,-2} {p.Mode,-9}{vlanStr,-16}{media,-20}{link}");
                }
            }

            return;
        }

        if (a == "vlan" && tokens.Count >= 3 && tokens[2].Equals("brief", StringComparison.OrdinalIgnoreCase))
        {
            if (_kind != NetworkDeviceKind.Layer2Switch)
            {
                output.AppendLine("% Use L2 switch for VLAN table.");
                return;
            }

            var sc = DeviceConfigRegistry.GetOrCreateSwitch(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
            foreach (var v in sc.Vlans)
            {
                output.AppendLine($"{v.Id,-5} {v.Name}");
            }

            return;
        }

        output.AppendLine("% Unknown show command.");
    }

    private void DumpRunningConfig(StringBuilder output)
    {
        if (_kind == NetworkDeviceKind.Router)
        {
            var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
            output.AppendLine($"hostname {rc.Hostname}");
            foreach (var i in rc.Interfaces)
            {
                output.AppendLine($"interface {i.Name}");
                if (!string.IsNullOrWhiteSpace(i.IpAddress))
                {
                    output.AppendLine($" ip address {i.IpAddress} {i.SubnetMask}");
                }

                if (i.NativeVlan.HasValue)
                {
                    output.AppendLine($" encapsulation dot1Q {i.NativeVlan.Value}");
                }

                output.AppendLine(i.Shutdown ? " shutdown" : " no shutdown");
                output.AppendLine("!");
            }

            foreach (var r in rc.StaticRoutes)
            {
                output.AppendLine($"ip route {r.DestinationPrefix} {r.NextHop}");
            }
        }
        else
        {
            var sc = DeviceConfigRegistry.GetOrCreateSwitch(_switch, NetworkDeviceClassifier.GetPortCount(_switch));
            output.AppendLine($"hostname {sc.Hostname}");
            foreach (var v in sc.Vlans)
            {
                output.AppendLine($"vlan {v.Id}");
                if (!string.IsNullOrWhiteSpace(v.Name))
                {
                    output.AppendLine($" name {v.Name}");
                }

                output.AppendLine("!");
            }

            foreach (var p in sc.Ports)
            {
                output.AppendLine($"interface FastEthernet0/{p.PortIndex}");
                output.AppendLine($" switchport mode {p.Mode}");
                if (!p.Trunk)
                {
                    output.AppendLine($" switchport access vlan {p.AccessVlan}");
                }
                else
                {
                    output.AppendLine($" switchport trunk allowed vlan {p.AllowedVlanRaw}");
                }

                output.AppendLine("!");
            }
        }
    }

    private void EmitShowInterfaceDetail(int idx, StringBuilder output)
    {
        var n = NetworkDeviceClassifier.GetPortCount(_switch);
        if (idx < 0 || idx >= n)
        {
            output.AppendLine("% Invalid interface index.");
            return;
        }

        SwitchPortHardwareProbe.GetDisplayHints(_switch, idx, _kind, out var media, out _);

        if (_kind == NetworkDeviceKind.Router)
        {
            var rc = DeviceConfigRegistry.GetOrCreateRouter(_switch, n);
            var i = rc.Interfaces.Find(x => x.Index == idx);
            if (i == null && idx < rc.Interfaces.Count)
            {
                i = rc.Interfaces[idx];
            }

            if (i == null)
            {
                output.AppendLine("% Internal error: interface not found.");
                return;
            }

            var link = SwitchPortHardwareProbe.GetCliLinkStatus(_switch, idx, NetworkDeviceKind.Router, i.Shutdown);
            var admin = i.Shutdown ? "administratively down" : "up";
            var hasL3 = !string.IsNullOrWhiteSpace(i.IpAddress) && !string.IsNullOrWhiteSpace(i.SubnetMask);
            var lineProto = !i.Shutdown && hasL3 ? "up" : "down";
            output.AppendLine($"{i.Name} is {admin}, line protocol is {lineProto}");
            if (hasL3)
            {
                output.AppendLine($"  Internet address is {i.IpAddress} {i.SubnetMask}");
            }

            output.AppendLine($"  Pluggable media: {media}");
            output.AppendLine($"  Line status: {link}");
            return;
        }

        var sc = DeviceConfigRegistry.GetOrCreateSwitch(_switch, n);
        var p = sc.Ports.Find(x => x.PortIndex == idx);
        if (p == null && idx < sc.Ports.Count)
        {
            p = sc.Ports[idx];
        }

        if (p == null)
        {
            output.AppendLine("% Internal error: port not found.");
            return;
        }

        var linkL2 = SwitchPortHardwareProbe.GetCliLinkStatus(_switch, idx, NetworkDeviceKind.Layer2Switch, false);
        var trunkInfo = p.Trunk ? $"trunk (allowed {p.AllowedVlanRaw})" : $"access VLAN {p.AccessVlan}";
        output.AppendLine($"FastEthernet0/{idx} is {p.Mode}, {trunkInfo}");
        output.AppendLine($"  Pluggable media: {media}");
        output.AppendLine($"  Line status: {linkL2}");
    }

    private void ExecPing(List<string> tokens, StringBuilder output)
    {
        if (tokens.Count < 2)
        {
            output.AppendLine("% Incomplete command.");
            return;
        }

        var ipArg = tokens[1];
        if (!RouteMath.TryParseIpv4(ipArg, out _))
        {
            output.AppendLine("% Invalid IPv4 address.");
            return;
        }

        var continuous = false;
        string sourceOpt = null;
        var i = 2;
        while (i < tokens.Count)
        {
            if (tokens[i].Equals("-t", StringComparison.OrdinalIgnoreCase))
            {
                continuous = true;
                i++;
                continue;
            }

            if (tokens[i].Equals("source", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokens.Count)
                {
                    output.AppendLine("% Incomplete: ping <ipv4> source <ipv4>  (optional -t at end)");
                    return;
                }

                sourceOpt = tokens[i + 1];
                i += 2;
                continue;
            }

            output.AppendLine("% Usage: ping <ipv4>  |  ping <ipv4> -t  |  ping <ipv4> source <ipv4>  |  ... -t");
            return;
        }

        if (sourceOpt != null)
        {
            if (_kind != NetworkDeviceKind.Router)
            {
                output.AppendLine("% source is only supported on routers.");
                return;
            }

            if (!IsLocalRouterInterfaceIp(_switch, sourceOpt))
            {
                output.AppendLine("% Invalid source — must be an IP on a no-shutdown interface of this router.");
                return;
            }
        }

        if (!PingTargetResolver.TryResolveTransformForIpv4(ipArg, _switch, out var tgt, out var label))
        {
            ModDebugLog.WriteLine($"ping fail: {ipArg} unresolved target (no Server/router/contract match)");
            ModDebugLog.Trace("ping", $"FAIL unresolved target ip={ipArg}");
            output.AppendLine($"% Ping {ipArg}: host unreachable (no matching Server or router interface in scene).");
            return;
        }

        ModDebugLog.Trace("ping", $"target ip={ipArg} label={label ?? ipArg} tgt={tgt?.name}");

        var pathBuf = new List<Vector3>(32);
        Transform pathRoot = _switch != null ? _switch.transform : null;
        var egressNote = "";

        if (_kind == NetworkDeviceKind.Router)
        {
            if (!RouterForwarding.TryGetEgressInterfaceIndex(_switch, ipArg, out var egressIdx, out var routeDetail))
            {
                ModDebugLog.WriteLine($"ping fail: {ipArg} no route ({routeDetail})");
                ModDebugLog.Trace("ping", $"FAIL no route: {routeDetail}");
                output.AppendLine($"% Ping {ipArg}: no route in running-config ({routeDetail}).");
                return;
            }

            ModDebugLog.Trace("ping", $"route OK: {routeDetail}");

            // Source may be any local interface IP (like IOS extended ping); egress follows the FIB, not the source interface.
            if (sourceOpt != null
                && ModDebugLog.IsTraceEnabled
                && RouterForwarding.TryGetInterfaceIndexForLocalIp(_switch, sourceOpt, out var srcIfaceIdx)
                && srcIfaceIdx != egressIdx)
            {
                ModDebugLog.Trace(
                    "ping",
                    $"source IP on Gi0/{srcIfaceIdx}, packet egress Gi0/{egressIdx} ({routeDetail})");
            }

            if (!SwitchPortHardwareProbe.TryPrepareRouterPingEgress(_switch, egressIdx, out pathRoot, out var cableErr, out var cableLog))
            {
                ModDebugLog.WriteLine($"ping fail: {ipArg} Gi0/{egressIdx} cable check: {cableLog}");
                ModDebugLog.Trace("ping", $"FAIL cable Gi0/{egressIdx}: {cableLog}");
                output.AppendLine($"% Ping {ipArg}: {cableErr}");
                return;
            }

            ModDebugLog.Trace("ping", $"cable OK Gi0/{egressIdx}: {cableLog} pathRoot={pathRoot?.name}");
            egressNote = $" out Gi0/{egressIdx}";
        }

        if (pathRoot == null)
        {
            output.AppendLine("% Internal error: ping path root.");
            return;
        }

        PingPacketPaths.BuildPathFromTransform(_switch, pathRoot, tgt, pathBuf);
        ModDebugLog.Trace("ping", $"path vertices={pathBuf.Count} (visual hop graph)");
        var pathArr = new Vector3[pathBuf.Count];
        for (var j = 0; j < pathBuf.Count; j++)
        {
            pathArr[j] = pathBuf[j];
        }

        DHCPSwitchesBehaviour.BeginPingBurst(pathArr, continuous);

        var hostLabel = string.IsNullOrEmpty(label) ? ipArg : label;
        var srcNote = sourceOpt != null ? $" from {sourceOpt}" : "";
        output.AppendLine(
            $"Ping {ipArg} ({hostLabel}){srcNote}{egressNote}{(continuous ? " — continuous (-t); Ctrl+C or close terminal to stop" : "")}:");
        if (!continuous)
        {
            output.AppendLine("!!!!");
            output.AppendLine("Success rate is 100 percent (4/4), round-trip min/avg/max = 1/2/4 ms");
        }
        else
        {
            output.AppendLine("Continuous echo — packets along cables; Ctrl+C to stop.");
        }
    }

    private static bool IsLocalRouterInterfaceIp(NetworkSwitch sw, string ip)
    {
        if (sw == null || string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var t = ip.Trim();
        var rc = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
        foreach (var iface in rc.Interfaces)
        {
            if (iface.Shutdown || string.IsNullOrWhiteSpace(iface.IpAddress))
            {
                continue;
            }

            if (string.Equals(iface.IpAddress.Trim(), t, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Accepts <c>ip route 10.0.0.0/8 192.168.1.1</c> (4 tokens after <c>ip</c>) or
    /// <c>ip route 10.0.0.0 255.0.0.0 192.168.1.1</c> / <c>ip route 10.0.0.0 /8 192.168.1.1</c> (5+ tokens).
    /// </summary>
    private bool TryParseIpRouteTokens(List<string> tokens, StringBuilder output, out string destStr, out int pl, out string nh)
    {
        destStr = null;
        nh = null;
        pl = 0;

        if (tokens.Count == 4 && tokens[2].Contains("/", StringComparison.Ordinal))
        {
            if (!RouteMath.TryParsePrefix(tokens[2], out var net, out pl))
            {
                output.AppendLine("% Invalid destination prefix.");
                return false;
            }

            if (!RouteMath.TryParseIpv4(tokens[3], out _))
            {
                output.AppendLine("% Invalid next-hop.");
                return false;
            }

            nh = tokens[3];
            destStr = pl == 0 ? "0.0.0.0/0" : $"{RouteMath.FormatIpv4(net)}/{pl}";
            return true;
        }

        if (tokens.Count < 5)
        {
            output.AppendLine("% Usage: ip route <dest/pfx> <next-hop>  OR  ip route <dest> <mask|/len> <next-hop>");
            return false;
        }

        var dest = tokens[2];
        var maskOrLen = tokens[3];
        nh = tokens[4];
        pl = 32;
        if (maskOrLen.StartsWith("/", StringComparison.Ordinal))
        {
            int.TryParse(maskOrLen.TrimStart('/'), out pl);
        }
        else if (RouteMath.TryParseIpv4(maskOrLen, out var mk))
        {
            pl = RouteMath.MaskToPrefixLength(mk);
            if (pl < 0)
            {
                pl = 32;
            }
        }

        if (dest.Contains("/", StringComparison.Ordinal))
        {
            if (!RouteMath.TryParsePrefix(dest, out var net2, out var pl2))
            {
                output.AppendLine("% Invalid destination prefix.");
                return false;
            }

            pl = pl2;
            destStr = pl == 0 ? "0.0.0.0/0" : $"{RouteMath.FormatIpv4(net2)}/{pl}";
        }
        else
        {
            var combined = pl == 0 ? "0.0.0.0/0" : $"{dest}/{pl}";
            if (!RouteMath.TryParsePrefix(combined, out var net3, out var pl3))
            {
                output.AppendLine("% Invalid destination/mask combination.");
                return false;
            }

            pl = pl3;
            destStr = $"{RouteMath.FormatIpv4(net3)}/{pl}";
        }

        if (!RouteMath.TryParseIpv4(nh, out _))
        {
            output.AppendLine("% Invalid next-hop.");
            return false;
        }

        return true;
    }

    private static bool TryParseInterface(string spec, out int index, StringBuilder output)
    {
        index = -1;
        spec = spec.Trim();
        var s = spec.ToLowerInvariant();
        if (s.StartsWith("gi0/", StringComparison.OrdinalIgnoreCase) || s.StartsWith("fa0/", StringComparison.OrdinalIgnoreCase)
                                                                     || s.StartsWith("fastethernet0/", StringComparison.OrdinalIgnoreCase)
                                                                     || s.StartsWith("gigabitethernet0/", StringComparison.OrdinalIgnoreCase))
        {
            var slash = spec.LastIndexOf('/');
            if (slash < 0 || slash >= spec.Length - 1)
            {
                output.AppendLine("% Invalid interface.");
                return false;
            }

            if (!int.TryParse(spec.Substring(slash + 1), out index))
            {
                output.AppendLine("% Invalid interface index.");
                return false;
            }

            return true;
        }

        output.AppendLine("% Supported: Gi0/n or Fa0/n");
        return false;
    }

    private static List<string> Tokenize(string line)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        foreach (var ch in line.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (cur.Length > 0)
                {
                    list.Add(cur.ToString());
                    cur.Clear();
                }
            }
            else
            {
                cur.Append(ch);
            }
        }

        if (cur.Length > 0)
        {
            list.Add(cur.ToString());
        }

        return list;
    }

    /// <summary>Trailing <c>?</c> or <c>word?</c> marks a Cisco-style help request; the marker is removed from <paramref name="tokens"/>.</summary>
    private static bool StripHelpMarker(List<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var last = tokens[^1].Replace('\uFF1F', '?');
        tokens[^1] = last;
        if (string.Equals(last, "?", StringComparison.Ordinal))
        {
            tokens.RemoveAt(tokens.Count - 1);
            return true;
        }

        if (last.Length > 1 && last.EndsWith("?", StringComparison.Ordinal))
        {
            var w = last.Substring(0, last.Length - 1);
            if (w.Length == 0)
            {
                tokens.RemoveAt(tokens.Count - 1);
            }
            else
            {
                tokens[^1] = w;
            }

            return true;
        }

        return false;
    }

    private void EmitContextHelp(List<string> ctx, StringBuilder output)
    {
        if (ctx.Count > 0 && ctx[0].Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            EmitNoContextHelp(ctx, output);
            return;
        }

        switch (_mode)
        {
            case CliExecMode.User:
                EmitUserHelp(ctx, output);
                break;
            case CliExecMode.Privileged:
                EmitPrivilegedHelp(ctx, output);
                break;
            case CliExecMode.Config:
                EmitConfigHelp(ctx, output);
                break;
            case CliExecMode.Interface:
                EmitInterfaceHelp(ctx, output);
                break;
            default:
                output.AppendLine("% No help in this mode.");
                break;
        }
    }

    private void EmitNoContextHelp(List<string> ctx, StringBuilder output)
    {
        if (_mode == CliExecMode.Config && _kind == NetworkDeviceKind.Router)
        {
            if (ctx.Count <= 1)
            {
                output.AppendLine("ip route <dest/pfx> <next-hop>");
                output.AppendLine("ip route <dest> <mask|/len> <next-hop>");
                return;
            }

            if (ctx.Count >= 2 && ctx[1].Equals("ip", StringComparison.OrdinalIgnoreCase))
            {
                output.AppendLine("route <dest/pfx> <next-hop>");
                output.AppendLine("route <dest> <mask|/len> <next-hop>");
                return;
            }
        }

        if (_mode != CliExecMode.Interface)
        {
            output.AppendLine("% 'no' help: use ? in (config) for routes, or in (config-if) for interface.");
            return;
        }

        if (ctx.Count <= 1)
        {
            if (_kind == NetworkDeviceKind.Router)
            {
                output.AppendLine("shutdown");
                output.AppendLine("ip address");
            }
            else
            {
                output.AppendLine("switchport access vlan");
            }

            return;
        }

        if (_kind == NetworkDeviceKind.Router && ctx.Count >= 2 && ctx[1].Equals("ip", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine("address");
            return;
        }

        output.AppendLine("% Unrecognized 'no' help context.");
    }

    private void EmitUserHelp(List<string> ctx, StringBuilder output)
    {
        if (ctx.Count == 0)
        {
            output.AppendLine("enable");
            output.AppendLine("show");
            return;
        }

        if (ctx.Count == 1 && ctx[0].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine("version");
            return;
        }

        output.AppendLine("% Unrecognized command.");
    }

    private void EmitPrivilegedHelp(List<string> ctx, StringBuilder output)
    {
        if (ctx.Count == 0)
        {
            output.AppendLine("configure");
            output.AppendLine("disable");
            output.AppendLine("show");
            output.AppendLine("write");
            output.AppendLine("ping");
            return;
        }

        var a = ctx[0].ToLowerInvariant();
        if (a == "configure" && ctx.Count == 1)
        {
            output.AppendLine("terminal");
            return;
        }

        if (a == "show" && ctx.Count == 1)
        {
            output.AppendLine("running-config");
            output.AppendLine("ip route");
            output.AppendLine("interfaces brief");
            output.AppendLine("interface <Gi0/n|Fa0/n>");
            if (_kind == NetworkDeviceKind.Layer2Switch)
            {
                output.AppendLine("vlan brief");
            }

            return;
        }

        if (a == "write" && ctx.Count == 1)
        {
            output.AppendLine("memory");
            output.AppendLine("erase");
            return;
        }

        if (a == "ping" && ctx.Count == 1)
        {
            output.AppendLine("<ipv4>");
            output.AppendLine("source <ipv4>  (router: any local interface IP; egress still follows the route)");
            output.AppendLine("-t");
            return;
        }

        if (a == "ping" && ctx.Count == 2)
        {
            output.AppendLine("source <ipv4>");
            output.AppendLine("-t");
            return;
        }

        if (a == "ping" && ctx.Count >= 3 && ctx[2].Equals("source", StringComparison.OrdinalIgnoreCase) && ctx.Count == 3)
        {
            output.AppendLine("<ipv4>");
            output.AppendLine("-t");
            return;
        }

        if (a == "show" && ctx.Count >= 2 && ctx[1].Equals("ip", StringComparison.OrdinalIgnoreCase) && ctx.Count == 2)
        {
            output.AppendLine("route");
            return;
        }

        if (a == "show" && ctx.Count >= 2 && ctx[1].Equals("interfaces", StringComparison.OrdinalIgnoreCase) && ctx.Count == 2)
        {
            output.AppendLine("brief");
            return;
        }

        if (a == "show" && ctx.Count >= 2 && ctx[1].Equals("interface", StringComparison.OrdinalIgnoreCase) && ctx.Count == 2)
        {
            output.AppendLine("Gi0/<n>");
            output.AppendLine("Fa0/<n>");
            return;
        }

        if (a == "show" && ctx.Count >= 2 && ctx[1].Equals("vlan", StringComparison.OrdinalIgnoreCase) && ctx.Count == 2)
        {
            output.AppendLine("brief");
            return;
        }

        output.AppendLine("% Unrecognized command.");
    }

    private void EmitConfigHelp(List<string> ctx, StringBuilder output)
    {
        if (ctx.Count == 0)
        {
            output.AppendLine("hostname");
            output.AppendLine("interface");
            if (_kind == NetworkDeviceKind.Layer2Switch)
            {
                output.AppendLine("vlan");
            }

            if (_kind == NetworkDeviceKind.Router)
            {
                output.AppendLine("ip route ...");
                output.AppendLine("no ip route ...");
            }

            output.AppendLine("end");
            output.AppendLine("exit");
            return;
        }

        var a = ctx[0].ToLowerInvariant();
        if (a == "interface" && ctx.Count == 1)
        {
            output.AppendLine("Gi0/<n>");
            output.AppendLine("Fa0/<n>");
            return;
        }

        if (a == "vlan" && ctx.Count == 1)
        {
            output.AppendLine("<id>");
            return;
        }

        if (a == "hostname" && ctx.Count == 1)
        {
            output.AppendLine("<name>");
            return;
        }

        if (a == "ip" && ctx.Count == 1 && _kind == NetworkDeviceKind.Router)
        {
            output.AppendLine("route <dest/pfx> <next-hop>");
            output.AppendLine("route <dest> <mask|/len> <next-hop>");
            return;
        }

        output.AppendLine("% Unrecognized command.");
    }

    private void EmitInterfaceHelp(List<string> ctx, StringBuilder output)
    {
        if (ctx.Count == 0)
        {
            if (_kind == NetworkDeviceKind.Router)
            {
                output.AppendLine("ip address <a> <mask>");
                output.AppendLine("no ip address");
                output.AppendLine("shutdown");
                output.AppendLine("encapsulation dot1Q <vlan>");
            }
            else
            {
                output.AppendLine("switchport mode <access|trunk>");
                output.AppendLine("switchport access vlan <id>");
                output.AppendLine("switchport trunk allowed vlan <list>");
            }

            output.AppendLine("exit");
            output.AppendLine("end");
            return;
        }

        var a = ctx[0].ToLowerInvariant();
        if (_kind == NetworkDeviceKind.Router && a == "ip" && ctx.Count == 1)
        {
            output.AppendLine("address <a> <mask>");
            return;
        }

        if (_kind == NetworkDeviceKind.Router && a == "encapsulation" && ctx.Count == 1)
        {
            output.AppendLine("dot1Q <vlan>");
            return;
        }

        if (_kind == NetworkDeviceKind.Layer2Switch && a == "switchport" && ctx.Count == 1)
        {
            output.AppendLine("mode");
            output.AppendLine("access");
            output.AppendLine("trunk");
            return;
        }

        if (_kind == NetworkDeviceKind.Layer2Switch && a == "switchport" && ctx.Count == 2 && ctx[1].Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine("access");
            output.AppendLine("trunk");
            return;
        }

        if (_kind == NetworkDeviceKind.Layer2Switch && a == "switchport" && ctx.Count == 2 && ctx[1].Equals("access", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine("vlan <id>");
            return;
        }

        if (_kind == NetworkDeviceKind.Layer2Switch && a == "switchport" && ctx.Count == 2 && ctx[1].Equals("trunk", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine("allowed vlan <list>");
            return;
        }

        if (_kind == NetworkDeviceKind.Layer2Switch && a == "switchport" && ctx.Count >= 3 && ctx[1].Equals("trunk", StringComparison.OrdinalIgnoreCase)
            && ctx[2].Equals("allowed", StringComparison.OrdinalIgnoreCase) && ctx.Count == 3)
        {
            output.AppendLine("vlan <list>");
            return;
        }

        output.AppendLine("% Unrecognized command.");
    }
}
