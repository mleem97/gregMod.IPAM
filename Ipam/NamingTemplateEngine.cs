using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DHCPSwitches;

internal sealed class DeviceNamingContext
{
    public string Row = "";
    public string Col = "";
    public string Grid = "";
    public string Ru = "";
    public string Customer = "";
    public string CustomerShort = "";
    public string Role = "";
    public string Color = "";
    public string Ip = "";
    public string Octet = "";
    public string Prefix = "";
    public string Tenant = "";
    public string Type = "SRV";
    public string RackId = "";
    public int CustomerId = -1;
    public int SelectionIndex;
}

internal sealed class NamingApplyOptions
{
    public string Pattern = "";
    public int SeqStart = 1;
    public int SeqStep = 1;
    public int SeqPad;
    public NamingCounterScope CounterScope = NamingCounterScope.PerApply;
    public NamingSortOrder SortOrder = NamingSortOrder.RackU;
    public bool DryRun;
    public bool PersistCounterState = true;
    /// <summary>User-entered rack row letter (e.g. B) when pattern uses {row}.</summary>
    public string ManualRow = "";
    /// <summary>User-entered rack column number (e.g. 25) when pattern uses {col}.</summary>
    public string ManualCol = "";
}

internal sealed class NamingPreviewRow
{
    public Server Server;
    public string OldName = "";
    public string NewName = "";
    public string Warning = "";
}

internal static class NamingTemplateEngine
{
    private static readonly Regex TokenRx = new(
        @"\{([^}:]+)(?::(\d+))?\}",
        RegexOptions.CultureInvariant);

    internal static List<NamingPreviewRow> BuildPreview(
        IReadOnlyList<Server> servers,
        CustomerBase customer,
        NamingApplyOptions options)
    {
        var rows = new List<NamingPreviewRow>();
        if (servers == null || servers.Count == 0 || string.IsNullOrWhiteSpace(options?.Pattern))
        {
            return rows;
        }

        var sorted = SortServers(servers, options.SortOrder);
        var counterMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var letterMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sorted.Count; i++)
        {
            var srv = sorted[i];
            if (srv == null)
            {
                continue;
            }

            var ctx = BuildContext(srv, customer, i, options);
            var scopeKey = BuildCounterScopeKey(ctx, options.CounterScope);
            if (!counterMap.ContainsKey(scopeKey))
            {
                var persisted = NamingConventionStore.GetCounterValue("seq:" + scopeKey, options.SeqStart);
                counterMap[scopeKey] = persisted;
            }

            if (!letterMap.ContainsKey(scopeKey))
            {
                letterMap[scopeKey] = 0;
            }

            var seqVal = counterMap[scopeKey];
            var letterVal = letterMap[scopeKey];
            var newName = RenderPattern(options.Pattern, ctx, seqVal, options.SeqPad, letterVal);
            counterMap[scopeKey] = seqVal + Math.Max(1, options.SeqStep);
            letterMap[scopeKey] = letterVal + 1;

            var oldName = DeviceInventoryReflection.GetDisplayName(srv);
            var row = new NamingPreviewRow
            {
                Server = srv,
                OldName = oldName,
                NewName = newName,
            };

            if (string.IsNullOrWhiteSpace(newName))
            {
                row.Warning = "empty name";
            }
            else if (seenNames.TryGetValue(newName, out var dup))
            {
                row.Warning = $"duplicate of row {dup + 1}";
            }
            else
            {
                seenNames[newName] = rows.Count;
            }

            rows.Add(row);
        }

        return rows;
    }

    internal static bool TryApply(
        IReadOnlyList<NamingPreviewRow> preview,
        NamingApplyOptions options,
        CustomerBase customer,
        out string error)
    {
        error = null;
        if (preview == null || preview.Count == 0)
        {
            error = "Nothing to apply.";
            return false;
        }

        var dup = preview.FirstOrDefault(r => !string.IsNullOrEmpty(r.Warning) && r.Warning.StartsWith("duplicate", StringComparison.OrdinalIgnoreCase));
        if (dup != null)
        {
            error = "Collision: two devices would get the same name. Adjust pattern or sort order.";
            return false;
        }

        if (options.DryRun)
        {
            return true;
        }

        var lineByServerInstanceId = GameSubnetHelper.BuildAssetManagementDeviceLineByServerInstanceId();
        var applied = 0;
        foreach (var row in preview)
        {
            if (row?.Server == null || string.IsNullOrWhiteSpace(row.NewName))
            {
                continue;
            }

            DeviceNamingWriter.TrySetServerDisplayName(row.Server, row.NewName, lineByServerInstanceId);
            NamingConventionStore.SetOverrideName(row.Server, row.NewName);
            applied++;
        }

        if (applied == 0)
        {
            error = "No names were applied.";
            return false;
        }

        if (options.PersistCounterState)
        {
            PersistCountersFromPreview(preview, customer, options);
        }

        GameSubnetHelper.RebuildAssetManagementDeviceLineServerCache();
        GameSubnetHelper.ReapplyNamingOverridesToAssetLineCache(
            preview.Where(r => r?.Server != null).Select(r => r.Server));
        return true;
    }

    private static void PersistCountersFromPreview(
        IReadOnlyList<NamingPreviewRow> preview,
        CustomerBase customer,
        NamingApplyOptions options)
    {
        var sorted = preview.Where(r => r?.Server != null).Select(r => r.Server).ToList();
        if (sorted.Count == 0)
        {
            return;
        }

        var counterMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var sortedServers = SortServers(sorted, options.SortOrder);
        for (var i = 0; i < sortedServers.Count; i++)
        {
            var ctx = BuildContext(sortedServers[i], customer, i, options);
            var scopeKey = BuildCounterScopeKey(ctx, options.CounterScope);
            if (!counterMap.ContainsKey(scopeKey))
            {
                counterMap[scopeKey] = NamingConventionStore.GetCounterValue("seq:" + scopeKey, options.SeqStart);
            }

            counterMap[scopeKey] += Math.Max(1, options.SeqStep);
        }

        foreach (var kv in counterMap)
        {
            NamingConventionStore.SetCounterValue("seq:" + kv.Key, kv.Value);
        }
    }

    internal static List<Server> SortServers(IReadOnlyList<Server> servers, NamingSortOrder order)
    {
        var list = servers.Where(s => s != null).ToList();
        switch (order)
        {
            case NamingSortOrder.Selection:
                return list;
            case NamingSortOrder.Ip:
                return list.OrderBy(s => IpSortKey(DHCPManager.GetServerIP(s))).ThenBy(s => s.GetInstanceID()).ToList();
            case NamingSortOrder.Grid:
                return list.OrderBy(s => GridSortKey(s)).ThenBy(s => RuSortKey(s)).ThenBy(s => s.GetInstanceID()).ToList();
            default:
                return list.OrderBy(s => GridSortKey(s)).ThenByDescending(s => RuSortKey(s)).ThenBy(s => s.GetInstanceID()).ToList();
        }
    }

    internal static DeviceNamingContext BuildContext(
        Server server,
        CustomerBase customer,
        int selectionIndex,
        NamingApplyOptions options = null)
    {
        var ctx = new DeviceNamingContext { SelectionIndex = selectionIndex, Type = "SRV" };
        if (server == null)
        {
            return ctx;
        }

        TryFillRackFields(server, ctx);
        ApplyManualRackOverrides(ctx, options);

        var cb = customer ?? GameSubnetHelper.FindCustomerBaseForServer(server);
        if (cb != null)
        {
            ctx.CustomerId = cb.customerID;
            ctx.Customer = GetCustomerDisplayName(cb);
            ctx.CustomerShort = NamingConventionStore.GetCustomerAbbreviation(cb.customerID, ctx.Customer);
        }

        var ff = DeviceInventoryReflection.GetServerFormFactorLabel(server);
        ctx.Role = FormFactorToRole(ff);
        ctx.Color = GetServerColorLabel(server);

        var ip = DHCPManager.GetServerIP(server);
        if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
        {
            ctx.Ip = ip.Trim();
            if (Ipv4Rfc1918.TryParseIpv4(ctx.Ip, out var ipBe))
            {
                ctx.Octet = ((ipBe >> 0) & 0xFFu).ToString(CultureInfo.InvariantCulture);
            }

            if (TryGetMostSpecificContainingPrefix(ctx.Ip, IpamDataStore.GetPrefixes(), out var prefix)
                && prefix != null)
            {
                ctx.Prefix = (prefix.Name ?? "").Trim();
                ctx.Tenant = (prefix.Tenant ?? "").Trim();
            }
        }

        return ctx;
    }

    internal static string RenderPattern(string pattern, DeviceNamingContext ctx, int seq, int seqPad, int letterIndex)
    {
        if (string.IsNullOrEmpty(pattern) || ctx == null)
        {
            return "";
        }

        return TokenRx.Replace(pattern, m =>
        {
            var token = m.Groups[1].Value;
            var width = 0;
            if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
            {
                width = w;
            }

            return ResolveToken(token, width, ctx, seq, seqPad, letterIndex);
        });
    }

    private static string NormalizeTokenKey(string token)
    {
        return (token ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static void ApplyManualRackOverrides(DeviceNamingContext ctx, NamingApplyOptions options)
    {
        if (ctx == null || options == null)
        {
            return;
        }

        var manualRow = (options.ManualRow ?? "").Trim();
        if (!string.IsNullOrEmpty(manualRow))
        {
            ctx.Row = manualRow.ToUpperInvariant();
        }

        var manualCol = (options.ManualCol ?? "").Trim();
        if (!string.IsNullOrEmpty(manualCol))
        {
            ctx.Col = manualCol;
        }

        if (!string.IsNullOrEmpty(ctx.Row) && !string.IsNullOrEmpty(ctx.Col))
        {
            ctx.Grid = ctx.Row + ctx.Col;
        }
    }

    private static string ResolveToken(string token, int width, DeviceNamingContext ctx, int seq, int defaultPad, int letterIndex)
    {
        switch (NormalizeTokenKey(token))
        {
            case "row": return ctx.Row;
            case "col": return ctx.Col;
            case "grid": return ctx.Grid;
            case "ru": return ctx.Ru;
            case "customer": return ctx.Customer;
            case "customershort": return ctx.CustomerShort;
            case "role":
            case "unitsize":
            case "unit":
                return ctx.Role;
            case "color": return ctx.Color;
            case "ip": return ctx.Ip;
            case "octet": return ctx.Octet;
            case "prefix": return ctx.Prefix;
            case "tenant": return ctx.Tenant;
            case "type": return ctx.Type;
            case "seq":
            case "num":
            case "n":
                var pad = width > 0 ? width : defaultPad;
                return pad > 0 ? seq.ToString("D" + pad.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : seq.ToString(CultureInfo.InvariantCulture);
            case "letter":
            case "alpha":
                return IndexToLetters(letterIndex);
            default:
                return "";
        }
    }

    private static string IndexToLetters(int index)
    {
        index = Math.Max(0, index);
        var n = index;
        var sb = new System.Text.StringBuilder();
        do
        {
            sb.Insert(0, (char)('A' + (n % 26)));
            n = n / 26 - 1;
        }
        while (n >= 0);

        return sb.ToString();
    }

    private static string FormFactorToRole(string formFactor)
    {
        return formFactor switch
        {
            "3 U" => "U3",
            "7 U" => "U7",
            _ => "",
        };
    }

    private static string GetServerColorLabel(Server server)
    {
        if (server == null)
        {
            return "";
        }

        // Scene instance name first — catalog prefab for a type index is often one shared color (e.g. Yellow).
        try
        {
            var sn = server.name ?? "";
            var fromScene = ParseServerColorTokenFromAssetName(sn);
            if (!string.IsNullOrEmpty(fromScene))
            {
                return fromScene;
            }
        }
        catch
        {
            // Il2Cpp
        }

        try
        {
            if (DeviceInventoryReflection.TryGetServerCatalogPrefabAssetName(server, out var pn)
                && !string.IsNullOrEmpty(pn))
            {
                var fromCatalog = ParseServerColorTokenFromAssetName(pn);
                if (!string.IsNullOrEmpty(fromCatalog))
                {
                    return fromCatalog;
                }
            }
        }
        catch
        {
            // Il2Cpp
        }

        return "";
    }

    private static string ParseServerColorTokenFromAssetName(string assetName)
    {
        if (string.IsNullOrEmpty(assetName))
        {
            return "";
        }

        var m = Regex.Match(assetName, @"Server\.([A-Za-z]+)[12]", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return "";
        }

        var token = m.Groups[1].Value;
        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private static void TryFillRackFields(Server server, DeviceNamingContext ctx)
    {
        var iid = server.GetInstanceID();
        foreach (var rack in RackDataStore.GetRacks())
        {
            if (rack?.Mounts == null)
            {
                continue;
            }

            foreach (var m in rack.Mounts)
            {
                if (m == null
                    || m.SceneInstanceId != iid
                    || !string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ctx.RackId = rack.Id ?? "";
                ctx.Ru = m.StartU.ToString(CultureInfo.InvariantCulture);
                var row = (rack.GridRow ?? "").Trim();
                if (row.Length > 0)
                {
                    ctx.Row = row.ToUpperInvariant();
                }

                if (rack.GridColumn > 0)
                {
                    ctx.Col = rack.GridColumn.ToString(CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrEmpty(ctx.Row) && !string.IsNullOrEmpty(ctx.Col))
                {
                    ctx.Grid = ctx.Row + ctx.Col;
                }
                else if (!string.IsNullOrWhiteSpace(rack.DisplayName))
                {
                    ctx.Grid = rack.DisplayName.Trim();
                }

                return;
            }
        }
    }

    private static string BuildCounterScopeKey(DeviceNamingContext ctx, NamingCounterScope scope)
    {
        return scope switch
        {
            NamingCounterScope.PerRack => "rack:" + (ctx.RackId ?? ""),
            NamingCounterScope.PerCustomer => "cust:" + ctx.CustomerId.ToString(CultureInfo.InvariantCulture),
            NamingCounterScope.PerRow => "row:" + (ctx.Row ?? ""),
            NamingCounterScope.PerCol => "col:" + (ctx.Col ?? ""),
            _ => "apply",
        };
    }

    private static ulong IpSortKey(string ip)
    {
        if (!Ipv4Rfc1918.TryParseIpv4((ip ?? "").Trim(), out var be))
        {
            return ulong.MaxValue;
        }

        return be;
    }

    private static string GridSortKey(Server server)
    {
        var ctx = new DeviceNamingContext();
        TryFillRackFields(server, ctx);
        return (ctx.Row ?? "") + "|" + (ctx.Col ?? "").PadLeft(4, '0');
    }

    private static int RuSortKey(Server server)
    {
        var ctx = new DeviceNamingContext();
        TryFillRackFields(server, ctx);
        return int.TryParse(ctx.Ru, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ru) ? ru : 0;
    }

    private static bool TryGetMostSpecificContainingPrefix(string ip, IReadOnlyList<IpamPrefixEntry> all, out IpamPrefixEntry winner)
    {
        winner = null;
        var bestLen = -1;
        foreach (var q in all)
        {
            if (q == null)
            {
                continue;
            }

            var qc = (q.Cidr ?? "").Trim();
            if (!RouteMath.TryParseIpv4Cidr(qc, out _, out var qLen))
            {
                continue;
            }

            if (!RouteMath.IsIpv4InCidr(ip, qc))
            {
                continue;
            }

            if (qLen > bestLen)
            {
                bestLen = qLen;
                winner = q;
            }
        }

        return winner != null;
    }

    private static string GetCustomerDisplayName(CustomerBase cb)
    {
        if (cb == null)
        {
            return "";
        }

        try
        {
            var n = cb.customerItem != null ? cb.customerItem.customerName : null;
            if (!string.IsNullOrWhiteSpace(n))
            {
                return n.Trim();
            }
        }
        catch
        {
            // Il2Cpp
        }

        return "";
    }
}
