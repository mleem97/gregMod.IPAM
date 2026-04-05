using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DHCPSwitches;

/// <summary>Tab completion and unambiguous command abbreviation for <see cref="CiscoLikeCliSession"/>.</summary>
internal static class CliAutocomplete
{
    internal static void ExpandAbbreviations(List<string> tokens, CiscoLikeCliSession session)
    {
        if (tokens == null || tokens.Count == 0)
        {
            return;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (ShouldSkipAbbreviation(tokens[i]))
            {
                continue;
            }

            var prefix = tokens.Take(i).ToList();
            var cand = GetCandidatesForPrefix(prefix, session);
            if (cand == null || cand.Count == 0)
            {
                continue;
            }

            var expanded = TryExpandUnambiguous(tokens[i], cand);
            if (expanded != null)
            {
                tokens[i] = expanded;
            }
        }
    }

    internal static bool TryTabComplete(ref string line, CiscoLikeCliSession session)
    {
        if (session == null)
        {
            return false;
        }

        var endsWithSpace = line.Length > 0 && char.IsWhiteSpace(line[^1]);
        var trimmed = line.TrimEnd();
        var parts = trimmed.Length == 0
            ? new List<string>()
            : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (parts.Count == 0 && !endsWithSpace)
        {
            var rootsEarly = GetCandidatesForPrefix(new List<string>(), session);
            if (rootsEarly.Count == 0)
            {
                return false;
            }

            line = rootsEarly.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First() + " ";
            return true;
        }

        List<string> prefixTok;
        string partial;
        if (endsWithSpace)
        {
            prefixTok = parts;
            partial = "";
        }
        else if (parts.Count == 0)
        {
            prefixTok = new List<string>();
            partial = "";
        }
        else
        {
            partial = parts[^1];
            prefixTok = parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : new List<string>();
        }

        var candidates = GetCandidatesForPrefix(prefixTok, session);
        if (candidates == null || candidates.Count == 0)
        {
            return false;
        }

        var matches = string.IsNullOrEmpty(partial)
            ? candidates
            : candidates.Where(c => c.StartsWith(partial, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
        {
            return false;
        }

        string newToken;
        if (matches.Count == 1)
        {
            newToken = matches[0];
        }
        else if (string.IsNullOrEmpty(partial))
        {
            return false;
        }
        else
        {
            newToken = LongestSharedPrefixAmong(matches, partial);
            if (string.Equals(newToken, partial, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var sb = new StringBuilder();
        for (var i = 0; i < prefixTok.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(prefixTok[i]);
        }

        if (prefixTok.Count > 0)
        {
            sb.Append(' ');
        }

        sb.Append(newToken);
        if (matches.Count == 1)
        {
            sb.Append(' ');
        }

        line = sb.ToString();
        return true;
    }

    private static bool ShouldSkipAbbreviation(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        if (token.Any(c => c is '.' or '/' or ':' or '\\'))
        {
            return true;
        }

        if (int.TryParse(token, out _))
        {
            return true;
        }

        return false;
    }

    private static string TryExpandUnambiguous(string partial, List<string> candidates)
    {
        if (string.IsNullOrEmpty(partial))
        {
            return null;
        }

        var matches = candidates.Where(c => c.StartsWith(partial, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count == 0)
        {
            return null;
        }

        return matches.Any(m => m.Equals(partial, StringComparison.OrdinalIgnoreCase)) ? partial : null;
    }

    private static string LongestSharedPrefixAmong(List<string> matches, string partial)
    {
        if (matches.Count == 0)
        {
            return partial;
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        var start = partial.Length;
        var sb = new StringBuilder(partial);
        for (var i = start; ; i++)
        {
            char? ch = null;
            foreach (var m in matches)
            {
                if (i >= m.Length)
                {
                    return sb.ToString();
                }

                var c = char.ToLowerInvariant(m[i]);
                if (ch == null)
                {
                    ch = c;
                }
                else if (ch.Value != c)
                {
                    return sb.ToString();
                }
            }

            sb.Append(matches[0][i]);
        }
    }

    private static List<string> GetCandidatesForPrefix(List<string> prefix, CiscoLikeCliSession s)
    {
        var mode = s.Mode;
        var kind = s.Kind;

        if (prefix.Count > 0 && prefix[0].Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return GetNoCommandCandidates(prefix, s);
        }

        return mode switch
        {
            CliExecMode.User => CandidatesUser(prefix, kind),
            CliExecMode.Privileged => CandidatesPrivileged(prefix, kind, s),
            CliExecMode.Config => CandidatesConfig(prefix, kind, s),
            CliExecMode.Interface => CandidatesInterface(prefix, kind, s),
            _ => new List<string>(),
        };
    }

    private static List<string> GetNoCommandCandidates(List<string> prefix, CiscoLikeCliSession s)
    {
        if (s.Mode != CliExecMode.Interface)
        {
            return new List<string>();
        }

        if (prefix.Count == 1)
        {
            if (s.Kind == NetworkDeviceKind.Router)
            {
                return new List<string> { "shutdown", "ip" };
            }

            if (s.Kind == NetworkDeviceKind.Layer2Switch)
            {
                return new List<string> { "switchport" };
            }
        }

        if (prefix.Count == 2 && prefix[1].Equals("ip", StringComparison.OrdinalIgnoreCase)
                              && s.Kind == NetworkDeviceKind.Router)
        {
            return new List<string> { "address" };
        }

        if (prefix.Count == 2 && prefix[1].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                              && s.Kind == NetworkDeviceKind.Layer2Switch)
        {
            return new List<string> { "access", "trunk" };
        }

        if (prefix.Count == 3 && prefix[1].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                              && prefix[2].Equals("access", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "vlan" };
        }

        if (prefix.Count == 3 && prefix[1].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                              && prefix[2].Equals("trunk", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "allowed" };
        }

        if (prefix.Count == 4 && prefix[1].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                              && prefix[2].Equals("trunk", StringComparison.OrdinalIgnoreCase)
                              && prefix[3].Equals("allowed", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "vlan" };
        }

        return new List<string>();
    }

    private static List<string> CandidatesUser(List<string> prefix, NetworkDeviceKind kind)
    {
        _ = kind;
        if (prefix.Count == 0)
        {
            return new List<string> { "enable", "show" };
        }

        if (prefix.Count == 1 && prefix[0].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "version" };
        }

        return new List<string>();
    }

    private static List<string> CandidatesPrivileged(List<string> prefix, NetworkDeviceKind kind, CiscoLikeCliSession s)
    {
        if (prefix.Count == 0)
        {
            return new List<string> { "configure", "disable", "show", "write", "ping" };
        }

        if (prefix.Count == 1 && prefix[0].Equals("configure", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "terminal" };
        }

        if (prefix.Count == 1 && prefix[0].Equals("write", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "memory", "erase" };
        }

        if (prefix.Count == 1 && prefix[0].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            var list = new List<string> { "running-config", "ip", "interfaces", "interface" };
            if (kind == NetworkDeviceKind.Layer2Switch)
            {
                list.Add("vlan");
            }

            return list;
        }

        if (prefix.Count == 2 && prefix[0].Equals("show", StringComparison.OrdinalIgnoreCase)
                               && prefix[1].Equals("ip", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "route" };
        }

        if (prefix.Count == 2 && prefix[0].Equals("show", StringComparison.OrdinalIgnoreCase)
                               && prefix[1].Equals("interface", StringComparison.OrdinalIgnoreCase))
        {
            return InterfaceNameCandidates(s);
        }

        if (prefix.Count == 2 && prefix[0].Equals("show", StringComparison.OrdinalIgnoreCase)
                               && prefix[1].Equals("interfaces", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "brief" };
        }

        if (prefix.Count == 2 && prefix[0].Equals("show", StringComparison.OrdinalIgnoreCase)
                               && prefix[1].Equals("vlan", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "brief" };
        }

        if (prefix.Count == 2 && prefix[0].Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "source", "-t" };
        }

        if (prefix.Count == 3 && prefix[0].Equals("ping", StringComparison.OrdinalIgnoreCase)
                              && prefix[2].Equals("source", StringComparison.OrdinalIgnoreCase))
        {
            return LocalRouterIpCandidates(s);
        }

        if (prefix.Count == 4 && prefix[0].Equals("ping", StringComparison.OrdinalIgnoreCase)
                              && prefix[2].Equals("source", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "-t" };
        }

        return new List<string>();
    }

    private static List<string> CandidatesConfig(List<string> prefix, NetworkDeviceKind kind, CiscoLikeCliSession s)
    {
        if (prefix.Count == 0)
        {
            var root = new List<string> { "hostname", "interface", "end", "exit" };
            if (kind == NetworkDeviceKind.Layer2Switch)
            {
                root.Add("vlan");
            }

            if (kind == NetworkDeviceKind.Router)
            {
                root.Add("ip");
            }

            return root;
        }

        if (prefix.Count == 1 && prefix[0].Equals("interface", StringComparison.OrdinalIgnoreCase))
        {
            return InterfaceNameCandidates(s);
        }

        if (prefix.Count == 1 && prefix[0].Equals("ip", StringComparison.OrdinalIgnoreCase) && kind == NetworkDeviceKind.Router)
        {
            return new List<string> { "route" };
        }

        return new List<string>();
    }

    private static List<string> LocalRouterIpCandidates(CiscoLikeCliSession s)
    {
        if (s.Kind != NetworkDeviceKind.Router)
        {
            return new List<string>();
        }

        var rc = DeviceConfigRegistry.GetOrCreateRouter(s.Target, NetworkDeviceClassifier.GetPortCount(s.Target));
        var list = new List<string>();
        foreach (var i in rc.Interfaces)
        {
            if (i.Shutdown || string.IsNullOrWhiteSpace(i.IpAddress))
            {
                continue;
            }

            list.Add(i.IpAddress.Trim());
        }

        return list;
    }

    private static List<string> InterfaceNameCandidates(CiscoLikeCliSession s)
    {
        var n = NetworkDeviceClassifier.GetPortCount(s.Target);
        var list = new List<string>(n * 2);
        for (var i = 0; i < n; i++)
        {
            list.Add($"Gi0/{i}");
            list.Add($"Fa0/{i}");
        }

        return list;
    }

    private static List<string> CandidatesInterface(List<string> prefix, NetworkDeviceKind kind, CiscoLikeCliSession s)
    {
        if (prefix.Count == 0)
        {
            var root = new List<string> { "exit", "end", "no" };
            if (kind == NetworkDeviceKind.Router)
            {
                root.AddRange(new[] { "ip", "shutdown", "encapsulation" });
            }
            else
            {
                root.Add("switchport");
            }

            return root;
        }

        if (kind == NetworkDeviceKind.Router && prefix.Count == 1 && prefix[0].Equals("ip", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "address" };
        }

        if (kind == NetworkDeviceKind.Router && prefix.Count == 1
                                              && prefix[0].Equals("encapsulation", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "dot1q" };
        }

        if (kind == NetworkDeviceKind.Layer2Switch && prefix.Count == 1
                                                     && prefix[0].Equals("switchport", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "mode", "access", "trunk" };
        }

        if (kind == NetworkDeviceKind.Layer2Switch && prefix.Count == 2
                                                     && prefix[0].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                                                     && prefix[1].Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "access", "trunk" };
        }

        if (kind == NetworkDeviceKind.Layer2Switch && prefix.Count == 2
                                                     && prefix[0].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                                                     && prefix[1].Equals("access", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "vlan" };
        }

        if (kind == NetworkDeviceKind.Layer2Switch && prefix.Count == 2
                                                     && prefix[0].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                                                     && prefix[1].Equals("trunk", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "allowed" };
        }

        if (kind == NetworkDeviceKind.Layer2Switch && prefix.Count == 3
                                                     && prefix[0].Equals("switchport", StringComparison.OrdinalIgnoreCase)
                                                     && prefix[1].Equals("trunk", StringComparison.OrdinalIgnoreCase)
                                                     && prefix[2].Equals("allowed", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { "vlan" };
        }

        return new List<string>();
    }
}
