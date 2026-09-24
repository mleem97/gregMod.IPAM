using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace GregModIPAM
{
    // Router + firewall admin (as in game: subnets, routes, rules).
    public static partial class IPAMOverlay
    {
        // ── Select ─────────────────────────────────────────────────────────────

        private static Router SelectedRouter()
        {
            try
            {
                var sw = _selectedNetworkSwitch;
                if (sw == null) return null;
                var go = sw.gameObject;
                if (go == null) return null;
                return go.GetComponent<Router>();
            }
            catch
            {
                return null;
            }
        }

        private static Firewall SelectedFirewall()
        {
            try
            {
                var sw = _selectedNetworkSwitch;
                if (sw == null) return null;
                var go = sw.gameObject;
                if (go == null) return null;
                return go.GetComponent<Firewall>();
            }
            catch
            {
                return null;
            }
        }

        // ── Formular-State ───────────────────────────────────────────────────

        private static string _rtVlanBuf = "";
        private static string _rtCidrBuf = "";
        private static string _rtSrcVlanBuf = "";
        private static string _rtSrcIpBuf = "";
        private static string _rtDstVlanBuf = "";
        private static string _rtDstIpBuf = "";
        private static string _routerError = "";

        private static string _fwPortIdxBuf = "";
        private static string _fwSrcBuf = "";
        private static string _fwDstBuf = "";
        private static string _fwNetPortBuf = "";
        private static int _fwProtoIdx = 1;
        private static bool _fwBidir = true;
        private static bool _fwAllow = true;
        private static string _fwTestVlanBuf = "";
        private static string _fwTestResult = "";
        private static string _firewallError = "";

        private static readonly string[] FwProtocols = { "TCP", "UDP", "Both" };

        // ── Router-Panel ─────────────────────────────────────────────────────

        internal static void DrawRouterManagement(ref float y, float x0, float cardW)
        {
            var router = SelectedRouter();
            if (router == null)
            {
                GUI.Label(new Rect(x0, y, cardW, 24f), "Select a router above to manage subnets and routes.", _stHint);
                y += 28f;
                return;
            }

            int asn = 0;
            try { asn = router.asn; } catch { }
            GUI.Label(new Rect(x0, y, cardW, SectionTitleH), $"Router management — ASN {asn}", _stSectionTitle);
            y += SectionTitleH + 4f;

            // Owned subnets
            List<Router.OwnedSubnet> subnets = new();
            try
            {
                var list = router.ownedSubnets;
                if (list != null)
                    foreach (var s in list)
                    {
                        if (s != null) subnets.Add(s);
                    }
            }
            catch { }

            GUI.Label(new Rect(x0, y, cardW, 20f), $"Owned subnets ({subnets.Count})", _stFormLabel);
            y += 22f;
            GUI.Label(new Rect(x0, y, 70f, TableHeaderH), "VLAN", _stTableHeaderText);
            GUI.Label(new Rect(x0 + 74f, y, cardW - 150f, TableHeaderH), "Subnet", _stTableHeaderText);
            y += TableHeaderH;

            var shownSubnets = Math.Min(subnets.Count, 12);
            for (var i = 0; i < shownSubnets; i++)
            {
                var s = subnets[i];
                int vlan = 0;
                string cidr = "";
                try { vlan = s.vlanId; } catch { }
                try { cidr = s.subnetCidr ?? ""; } catch { }
                GUI.Label(new Rect(x0, y, 70f, TableRowH), vlan.ToString(), _stTableCell);
                GUI.Label(new Rect(x0 + 74f, y, cardW - 150f, TableRowH), cidr, _stTableCell);
                if (ImguiButtonOnce(new Rect(x0 + cardW - 70f, y + 2f, 66f, TableRowH - 4f), "Remove", 9420 + i, _stMutedBtn))
                {
                    try
                    {
                        router.RemoveSubnet(vlan);
                        _routerError = "";
                    }
                    catch (Exception ex) { _routerError = ex.GetBaseException().Message; }
                }

                y += TableRowH;
            }

            if (subnets.Count > shownSubnets)
            {
                GUI.Label(new Rect(x0, y, cardW, 20f), $"… +{subnets.Count - shownSubnets} more", _stHint);
                y += 22f;
            }

            DrawIpamFormTextField(new Rect(x0, y, 70f, 22f), IpamFormFocusRouterSubnetVlan, 4, IpamTextFieldKind.VlanIdDigits);
            DrawIpamFormTextField(new Rect(x0 + 74f, y, cardW - 150f, 22f), IpamFormFocusRouterSubnetCidr, 18, IpamTextFieldKind.Cidr);
            if (ImguiButtonOnce(new Rect(x0 + cardW - 70f, y, 66f, 22f), "Add", 9440, _stPrimaryBtn))
            {
                if (!int.TryParse((_rtVlanBuf ?? "").Trim(), out var vlan))
                {
                    _routerError = "VLAN must be a number.";
                }
                else if (string.IsNullOrWhiteSpace(_rtCidrBuf))
                {
                    _routerError = "CIDR required (e.g. 10.0.5.0/24).";
                }
                else
                {
                    try
                    {
                        if (router.AddSubnet(vlan, _rtCidrBuf.Trim()))
                        {
                            _routerError = "";
                            _rtVlanBuf = "";
                            _rtCidrBuf = "";
                        }
                        else
                        {
                            _routerError = "Router rejected the subnet.";
                        }
                    }
                    catch (Exception ex) { _routerError = ex.GetBaseException().Message; }
                }
            }

            y += 26f;

            // Routes
            List<Router.RouteEntry> routes = new();
            try
            {
                var list = router.routes;
                if (list != null)
                    foreach (var r in list)
                    {
                        if (r != null) routes.Add(r);
                    }
            }
            catch { }

            GUI.Label(new Rect(x0, y, cardW, 20f), $"Routes ({routes.Count})", _stFormLabel);
            y += 22f;
            GUI.Label(new Rect(x0, y, 44f, TableHeaderH), "ID", _stTableHeaderText);
            GUI.Label(new Rect(x0 + 48f, y, 150f, TableHeaderH), "Source", _stTableHeaderText);
            GUI.Label(new Rect(x0 + 202f, y, 150f, TableHeaderH), "Target", _stTableHeaderText);
            y += TableHeaderH;

            var shownRoutes = Math.Min(routes.Count, 12);
            for (var i = 0; i < shownRoutes; i++)
            {
                var r = routes[i];
                int rid = 0, sv = 0, tv = 0;
                string sip = "", tip = "";
                try { rid = r.routeId; } catch { }
                try { sv = r.sourceVlanId; } catch { }
                try { sip = r.sourceIp ?? ""; } catch { }
                try { tv = r.targetVlanId; } catch { }
                try { tip = r.targetIp ?? ""; } catch { }
                GUI.Label(new Rect(x0, y, 44f, TableRowH), rid.ToString(), _stTableCell);
                GUI.Label(new Rect(x0 + 48f, y, 150f, TableRowH), $"{sv}/{sip}", _stTableCell);
                GUI.Label(new Rect(x0 + 202f, y, 150f, TableRowH), $"{tv}/{tip}", _stTableCell);
                if (ImguiButtonOnce(new Rect(x0 + cardW - 70f, y + 2f, 66f, TableRowH - 4f), "Remove", 9450 + i, _stMutedBtn))
                {
                    try
                    {
                        router.RemoveRoute(rid);
                        _routerError = "";
                    }
                    catch (Exception ex) { _routerError = ex.GetBaseException().Message; }
                }

                y += TableRowH;
            }

            if (routes.Count > shownRoutes)
            {
                GUI.Label(new Rect(x0, y, cardW, 20f), $"… +{routes.Count - shownRoutes} more", _stHint);
                y += 22f;
            }

            DrawIpamFormTextField(new Rect(x0, y, 52f, 22f), IpamFormFocusRouterRouteSrcVlan, 4, IpamTextFieldKind.VlanIdDigits);
            DrawIpamFormTextField(new Rect(x0 + 56f, y, 120f, 22f), IpamFormFocusRouterRouteSrcIp, 18, IpamTextFieldKind.Cidr);
            DrawIpamFormTextField(new Rect(x0 + 180f, y, 52f, 22f), IpamFormFocusRouterRouteDstVlan, 4, IpamTextFieldKind.VlanIdDigits);
            DrawIpamFormTextField(new Rect(x0 + 236f, y, 120f, 22f), IpamFormFocusRouterRouteDstIp, 18, IpamTextFieldKind.Cidr);
            if (ImguiButtonOnce(new Rect(x0 + cardW - 70f, y, 66f, 22f), "Add", 9441, _stPrimaryBtn))
            {
                if (!int.TryParse((_rtSrcVlanBuf ?? "").Trim(), out var sv)
                    || !int.TryParse((_rtDstVlanBuf ?? "").Trim(), out var tv)
                    || string.IsNullOrWhiteSpace(_rtSrcIpBuf)
                    || string.IsNullOrWhiteSpace(_rtDstIpBuf))
                {
                    _routerError = "Route needs src VLAN/IP and dst VLAN/IP.";
                }
                else
                {
                    try
                    {
                        var id = router.AddRoute(sv, _rtSrcIpBuf.Trim(), tv, _rtDstIpBuf.Trim());
                        _routerError = id >= 0 ? "" : "Router rejected the route.";
                        if (id >= 0)
                        {
                            _rtSrcVlanBuf = "";
                            _rtSrcIpBuf = "";
                            _rtDstVlanBuf = "";
                            _rtDstIpBuf = "";
                        }
                    }
                    catch (Exception ex) { _routerError = ex.GetBaseException().Message; }
                }
            }

            y += 26f;

            if (ImguiButtonOnce(new Rect(x0, y, 150f, 24f), "Reapply all routes", 9442, _stMutedBtn))
            {
                try
                {
                    router.ReapplyAllRoutes();
                    _routerError = "";
                }
                catch (Exception ex) { _routerError = ex.GetBaseException().Message; }
            }

            if (ImguiButtonOnce(new Rect(x0 + 158f, y, 170f, 24f), "Sync same-ASN routers", 9443, _stMutedBtn))
            {
                try
                {
                    router.SyncRoutesWithSameAsn();
                    _routerError = "";
                }
                catch (Exception ex) { _routerError = ex.GetBaseException().Message; }
            }

            y += 28f;

            if (!string.IsNullOrEmpty(_routerError))
            {
                GUI.Label(new Rect(x0, y, cardW, 20f), _routerError, _stError);
                y += 22f;
            }
        }

        // ── Firewall-Panel ───────────────────────────────────────────────────

        internal static void DrawFirewallManagement(ref float y, float x0, float cardW)
        {
            var fw = SelectedFirewall();
            if (fw == null)
            {
                GUI.Label(new Rect(x0, y, cardW, 24f), "Select a firewall above to manage rules.", _stHint);
                y += 28f;
                return;
            }

            string cluster = "";
            try { cluster = fw.clusterIP ?? ""; } catch { }
            GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "Firewall management" + (cluster.Length > 0 ? $" — cluster {cluster}" : ""), _stSectionTitle);
            y += SectionTitleH + 4f;

            List<Firewall.FilterRule> rules = new();
            try
            {
                var list = fw.filterRules;
                if (list != null)
                    foreach (var r in list)
                    {
                        if (r != null) rules.Add(r);
                    }
            }
            catch { }

            GUI.Label(new Rect(x0, y, cardW, 20f), $"Rules ({rules.Count})", _stFormLabel);
            y += 22f;
            GUI.Label(new Rect(x0, y, 40f, TableHeaderH), "Port", _stTableHeaderText);
            GUI.Label(new Rect(x0 + 44f, y, 170f, TableHeaderH), "Source", _stTableHeaderText);
            GUI.Label(new Rect(x0 + 218f, y, 170f, TableHeaderH), "Dest", _stTableHeaderText);
            GUI.Label(new Rect(x0 + 392f, y, 90f, TableHeaderH), "Action", _stTableHeaderText);
            y += TableHeaderH;

            var shownRules = Math.Min(rules.Count, 12);
            for (var i = 0; i < shownRules; i++)
            {
                var r = rules[i];
                int port = 0, netport = 0;
                string src = "", dst = "", proto = "", mode = "";
                try { port = r.portIndex; } catch { }
                try { src = r.sourceIpCidr ?? ""; } catch { }
                try { dst = r.destIpCidr ?? ""; } catch { }
                try { netport = r.networkPort; } catch { }
                try { proto = r.protocol.ToString(); } catch { }
                try { mode = (r.allow ? "ALLOW" : "DENY") + (r.bidirectional ? " ⇄" : " →"); } catch { }
                GUI.Label(new Rect(x0, y, 40f, TableRowH), port.ToString(), _stTableCell);
                GUI.Label(new Rect(x0 + 44f, y, 170f, TableRowH), $"{src}:{netport}", _stTableCell);
                GUI.Label(new Rect(x0 + 218f, y, 170f, TableRowH), dst, _stTableCell);
                GUI.Label(new Rect(x0 + 392f, y, 90f, TableRowH), $"{proto} {mode}", _stTableCell);
                if (ImguiButtonOnce(new Rect(x0 + cardW - 70f, y + 2f, 66f, TableRowH - 4f), "Remove", 9460 + i, _stMutedBtn))
                {
                    try
                    {
                        RemoveFirewallRuleByMatch(fw, port, src, dst, netport, proto, mode.StartsWith("ALLOW"));
                        _firewallError = "";
                    }
                    catch (Exception ex) { _firewallError = ex.GetBaseException().Message; }
                }

                y += TableRowH;
            }

            if (rules.Count > shownRules)
            {
                GUI.Label(new Rect(x0, y, cardW, 20f), $"… +{rules.Count - shownRules} more", _stHint);
                y += 22f;
            }

            DrawIpamFormTextField(new Rect(x0, y, 44f, 22f), IpamFormFocusFirewallRulePort, 4, IpamTextFieldKind.VlanIdDigits);
            DrawIpamFormTextField(new Rect(x0 + 48f, y, 150f, 22f), IpamFormFocusFirewallRuleSrc, 20, IpamTextFieldKind.Cidr);
            DrawIpamFormTextField(new Rect(x0 + 202f, y, 150f, 22f), IpamFormFocusFirewallRuleDst, 20, IpamTextFieldKind.Cidr);
            DrawIpamFormTextField(new Rect(x0 + 356f, y, 60f, 22f), IpamFormFocusFirewallRuleNetPort, 5, IpamTextFieldKind.VlanIdDigits);
            y += 26f;

            var protoLabel = FwProtocols[Mathf.Clamp(_fwProtoIdx, 0, FwProtocols.Length - 1)];
            if (ImguiButtonOnce(new Rect(x0, y, 90f, 22f), protoLabel, 9470, _stMutedBtn))
            {
                _fwProtoIdx = (_fwProtoIdx + 1) % FwProtocols.Length;
            }

            _fwBidir = ImguiToggleOnce(new Rect(x0 + 98f, y, 110f, 22f), _fwBidir, 9471, new GUIContent("bidir"));
            _fwAllow = ImguiToggleOnce(new Rect(x0 + 212f, y, 110f, 22f), _fwAllow, 9472, new GUIContent("allow"));
            if (ImguiButtonOnce(new Rect(x0 + cardW - 70f, y, 66f, 22f), "Add", 9473, _stPrimaryBtn))
            {
                if (!int.TryParse((_fwPortIdxBuf ?? "").Trim(), out var port)
                    || !int.TryParse((_fwNetPortBuf ?? "").Trim(), out var netport)
                    || string.IsNullOrWhiteSpace(_fwSrcBuf)
                    || string.IsNullOrWhiteSpace(_fwDstBuf))
                {
                    _firewallError = "Rule needs port, source, dest and network port.";
                }
                else
                {
                    try
                    {
                        Firewall.Protocol proto;
                        try { proto = (Firewall.Protocol)Enum.Parse(typeof(Firewall.Protocol), protoLabel, true); }
                        catch { proto = Firewall.Protocol.TCP; }
                        fw.AddRule(port, _fwSrcBuf.Trim(), _fwDstBuf.Trim(), netport, proto, _fwBidir, _fwAllow);
                        _firewallError = "";
                        _fwPortIdxBuf = "";
                        _fwSrcBuf = "";
                        _fwDstBuf = "";
                        _fwNetPortBuf = "";
                    }
                    catch (Exception ex) { _firewallError = ex.GetBaseException().Message; }
                }
            }

            y += 26f;

            if (ImguiButtonOnce(new Rect(x0, y, 150f, 24f), "Sync from cluster", 9474, _stMutedBtn))
            {
                try
                {
                    fw.SyncRulesFromCluster();
                    _firewallError = "";
                }
                catch (Exception ex) { _firewallError = ex.GetBaseException().Message; }
            }

            if (ImguiButtonOnce(new Rect(x0 + 158f, y, 150f, 24f), "Broadcast cluster", 9475, _stMutedBtn))
            {
                try
                {
                    fw.BroadcastRulesToCluster();
                    _firewallError = "";
                }
                catch (Exception ex) { _firewallError = ex.GetBaseException().Message; }
            }

            y += 28f;

            GUI.Label(new Rect(x0, y, cardW, 20f), "Traffic test (port / vlan / src / dst / netport):", _stFormLabel);
            y += 22f;
            DrawIpamFormTextField(new Rect(x0, y, 44f, 22f), IpamFormFocusFirewallTestVlan, 4, IpamTextFieldKind.VlanIdDigits);
            if (ImguiButtonOnce(new Rect(x0 + 52f, y, 70f, 22f), "Test", 9476, _stMutedBtn))
            {
                if (!int.TryParse((_fwPortIdxBuf ?? "").Trim(), out var tport)
                    || !int.TryParse((_fwTestVlanBuf ?? "").Trim(), out var tvlan)
                    || !int.TryParse((_fwNetPortBuf ?? "").Trim(), out var tnet)
                    || string.IsNullOrWhiteSpace(_fwSrcBuf)
                    || string.IsNullOrWhiteSpace(_fwDstBuf))
                {
                    _fwTestResult = "Fill port, vlan, src, dst, netport first.";
                }
                else
                {
                    try
                    {
                        Firewall.Protocol proto;
                        try { proto = (Firewall.Protocol)Enum.Parse(typeof(Firewall.Protocol), protoLabel, true); }
                        catch { proto = Firewall.Protocol.TCP; }
                        var allowed = fw.IsTrafficAllowed(tport, tvlan, _fwSrcBuf.Trim(), _fwDstBuf.Trim(), tnet, proto);
                        _fwTestResult = allowed ? "ALLOWED" : "DENIED";
                    }
                    catch (Exception ex) { _fwTestResult = ex.GetBaseException().Message; }
                }
            }

            if (!string.IsNullOrEmpty(_fwTestResult))
            {
                GUI.Label(new Rect(x0 + 130f, y, cardW - 130f, 22f), _fwTestResult, _stTableCell);
            }

            y += 26f;

            if (!string.IsNullOrEmpty(_firewallError))
            {
                GUI.Label(new Rect(x0, y, cardW, 20f), _firewallError, _stError);
                y += 22f;
            }
        }

        // RemoveRule needs a VLAN id not stored in FilterRule —
        // so list slice + cluster broadcast instead of rare vanilla path.
        private static void RemoveFirewallRuleByMatch(Firewall fw, int port, string src, string dst, int netport, string proto, bool allow)
        {
            if (fw == null) return;
            try
            {
                var list = fw.filterRules;
                if (list == null) return;
                for (var i = 0; i < list.Count; i++)
                {
                    Firewall.FilterRule r = null;
                    try { r = list[i]; } catch { continue; }
                    if (r == null) continue;
                    int rp = -1, rn = -1;
                    string rs = null, rd = null, rpr = null;
                    bool ra = false, rb = false;
                    try { rp = r.portIndex; } catch { continue; }
                    try { rs = r.sourceIpCidr ?? ""; } catch { }
                    try { rd = r.destIpCidr ?? ""; } catch { }
                    try { rn = r.networkPort; } catch { }
                    try { rpr = r.protocol.ToString(); } catch { }
                    try { ra = r.allow; } catch { }
                    try { rb = r.bidirectional; } catch { }
                    if (rp == port
                        && string.Equals(rs, src, StringComparison.Ordinal)
                        && string.Equals(rd, dst, StringComparison.Ordinal)
                        && rn == netport
                        && string.Equals(rpr, proto, StringComparison.OrdinalIgnoreCase)
                        && ra == allow)
                    {
                        // bidirectional bewusst ignoriert (Vanilla toggelt je Richtung).
                        list.RemoveAt(i);
                        try { fw.BroadcastRulesToCluster(); } catch { }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[IPAM] Firewall rule delete failed: {ex.GetBaseException().Message}");
            }
        }

        // ── Heights for RecomputeContentHeight ─────────────────────────────────

        internal static float RouterDetailHeight()
        {
            if (_devicesSub != DevicesSubSection.Routers) return 0f;
            var router = SelectedRouter();
            if (router == null) return 32f;
            int subnets = 0, routes = 0;
            try
            {
                var s = router.ownedSubnets;
                if (s != null) subnets = Math.Min(s.Count, 12);
            }
            catch { }

            try
            {
                var r = router.routes;
                if (r != null) routes = Math.Min(r.Count, 12);
            }
            catch { }

            return SectionTitleH + 4f + 22f + TableHeaderH
                + subnets * TableRowH + 26f
                + 22f + TableHeaderH + routes * TableRowH + 26f
                + 28f + 28f + 24f;
        }

        internal static float FirewallDetailHeight()
        {
            if (_devicesSub != DevicesSubSection.Firewall) return 0f;
            var fw = SelectedFirewall();
            if (fw == null) return 32f;
            int rules = 0;
            try
            {
                var list = fw.filterRules;
                if (list != null) rules = Math.Min(list.Count, 12);
            }
            catch { }

            return SectionTitleH + 4f + 22f + TableHeaderH
                + rules * TableRowH + 26f + 26f
                + 28f + 28f + 26f + 26f + 24f;
        }
    }
}
