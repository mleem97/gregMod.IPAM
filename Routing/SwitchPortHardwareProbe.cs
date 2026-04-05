using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Best-effort read of per-port pluggable media and physical/link hints from the game <see cref="NetworkSwitch"/>
/// via reflection and transform naming heuristics. Falls back to model-based defaults when the game exposes nothing.
/// </summary>
internal static class SwitchPortHardwareProbe
{
    private static readonly string[] SwitchPortCollectionNames =
    {
        "Ports", "ports", "SwitchPorts", "switchPorts", "NetworkPorts", "networkPorts",
        "Interfaces", "interfaces", "PortList", "portList", "EthernetPorts", "ethernetPorts",
    };

    private static readonly string[] IndexedPortMethodNames =
    {
        "GetPort", "getPort", "GetSwitchPort", "GetNetworkPort", "GetEthernetPort", "GetInterface",
    };

    private static readonly string[] MediaPropertyNames =
    {
        "MediaType", "mediaType", "ModuleType", "moduleType", "TransceiverType", "transceiverType",
        "SfpType", "sfpType", "OpticType", "opticType", "PortType", "portType", "TypeName", "typeName",
        "HardwareType", "hardwareType", "PhysicalMedia", "physicalMedia", "PluggableType", "pluggableType",
        "Media", "media", "Transceiver", "transceiver", "ModuleName", "moduleName",
    };

    private static readonly string[] CablePropertyNames =
    {
        "CableConnected", "cableConnected", "HasCable", "hasCable", "IsCableConnected", "isCableConnected",
        "CableInserted", "cableInserted", "CablePresent", "cablePresent", "IsPlugged", "isPlugged",
        "ModulePresent", "modulePresent", "SfpPresent", "sfpPresent", "TransceiverPresent", "transceiverPresent",
        "HasModule", "hasModule", "HasTransceiver", "hasTransceiver",
    };

    private static readonly string[] LinkPropertyNames =
    {
        "PhysicalLink", "physicalLink", "PhysicalLinkUp", "physicalLinkUp", "LinkUp", "linkUp", "IsLinkUp", "isLinkUp",
        "CableLink", "cableLink", "Connected", "connected", "IsConnected", "isConnected",
    };

    internal static void GetDisplayHints(NetworkSwitch sw, int portIndex, NetworkDeviceKind kind, out string media, out string cable)
    {
        media = DefaultMediaWhenUnknown(kind);
        cable = "unknown";

        if (sw == null || portIndex < 0)
        {
            return;
        }

        if (TryReflectPortState(sw, portIndex, out var m, out var c))
        {
            if (!string.IsNullOrEmpty(m))
            {
                media = NormalizeReflectedMedia(m, kind);
            }

            if (!string.IsNullOrEmpty(c))
            {
                cable = c;
            }

            return;
        }

        if (TryHierarchyProbe(sw, portIndex, kind, out m, out c))
        {
            if (!string.IsNullOrEmpty(m))
            {
                media = m;
            }

            if (!string.IsNullOrEmpty(c))
            {
                cable = c;
            }
        }
    }

    /// <summary>CLI default when we cannot read a specific transceiver (not a compatibility list).</summary>
    private static string DefaultMediaWhenUnknown(NetworkDeviceKind kind)
    {
        return kind == NetworkDeviceKind.Router ? "no module" : "RJ45";
    }

    private static string NormalizeReflectedMedia(string raw, NetworkDeviceKind kind)
    {
        if (kind != NetworkDeviceKind.Router || string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var blob = NormalizeNameBlob(raw);
        if (LooksLikeFullCompatibilityList(blob))
        {
            return DefaultMediaWhenUnknown(kind);
        }

        var inferred = InferSfpFamilyFromBlob(blob);
        return inferred ?? raw.Trim();
    }

    private static bool LooksLikeFullCompatibilityList(string blob)
    {
        return blob.Contains("sfp") && blob.Contains("rj45") && blob.Contains("fiber") && blob.Contains("qsfp");
    }

    private static string TryInferSfpFromPortSubtree(Transform portRoot, NetworkDeviceKind kind)
    {
        if (portRoot == null || kind != NetworkDeviceKind.Router)
        {
            return null;
        }

        var activeBlob = CollectSubtreeNameBlob(portRoot, activeOnly: true);
        var inferred = InferSfpFamilyFromBlob(activeBlob);
        if (inferred != null)
        {
            return inferred;
        }

        // Some builds keep the inserted module on disabled children or only expose SKU on inactive LODs.
        var fullBlob = CollectSubtreeNameBlob(portRoot, activeOnly: false);
        inferred = InferSfpFamilyFromBlob(fullBlob);
        if (inferred != null)
        {
            return inferred;
        }

        if (activeBlob.Length == 0 && fullBlob.Length == 0)
        {
            return "empty";
        }

        if (IsOnlyStructuralJunk(fullBlob))
        {
            return "empty";
        }

        return "module present";
    }

    private static string CollectSubtreeNameBlob(Transform portRoot, bool activeOnly)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var tr in portRoot.GetComponentsInChildren<Transform>(true))
        {
            if (tr == null)
            {
                continue;
            }

            if (activeOnly && !tr.gameObject.activeInHierarchy)
            {
                continue;
            }

            var n = tr.gameObject.name;
            if (string.IsNullOrEmpty(n) || IsNoiseTransformName(n))
            {
                continue;
            }

            sb.Append(' ');
            sb.Append(n);
        }

        return NormalizeNameBlob(sb.ToString());
    }

    private static bool IsNoiseTransformName(string name)
    {
        var t = name.Trim();
        if (t.Length == 0)
        {
            return true;
        }

        return Regex.IsMatch(t, @"^(Mesh|Quad|Cube|Collider|Renderer|LOD\d*|LOD Group|default)$", RegexOptions.IgnoreCase);
    }

    private static bool IsOnlyStructuralJunk(string blob)
    {
        if (string.IsNullOrEmpty(blob))
        {
            return true;
        }

        if (blob.Contains("sfp") || blob.Contains("module") || blob.Contains("optic") || blob.Contains("rj45")
            || blob.Contains("fiber") || blob.Contains("qsfp") || blob.Contains("transceiver") || blob.Contains("lc")
            || blob.Contains("pluggable") || blob.Contains("cage") || blob.Contains("slot"))
        {
            return false;
        }

        return blob.Length < 24;
    }

    /// <summary>Map combined Unity names (module + mesh) to a single user-facing line.</summary>
    private static string InferSfpFamilyFromBlob(string blob)
    {
        if (string.IsNullOrEmpty(blob))
        {
            return null;
        }

        if (blob.Contains("qsfp"))
        {
            return "QSFP+ (unsupported)";
        }

        if (blob.Contains("sfp28"))
        {
            return "SFP28 fiber";
        }

        if (blob.Contains("rj45"))
        {
            return "SFP+ RJ45";
        }

        if (blob.Contains("fiber") || blob.Contains("optic") || blob.Contains("lc") || blob.Contains("duplex"))
        {
            return "SFP+ fiber";
        }

        if (blob.Contains("sfp") || blob.Contains("transceiver") || blob.Contains("pluggable") || blob.Contains("module"))
        {
            return "SFP+ (type unknown)";
        }

        return null;
    }

    private static string NormalizeNameBlob(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        var s = raw.ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", " ");
        return s.Trim();
    }

    /// <summary>Best child <see cref="Transform"/> for this logical index (Gi0/<paramref name="portIndex"/>), for cable attach / ping egress.</summary>
    internal static bool TryGetPortAttachmentTransform(NetworkSwitch sw, int portIndex, out Transform anchor, int minScore = 120)
    {
        anchor = null;
        if (sw == null || portIndex < 0 || sw.transform == null)
        {
            return false;
        }

        anchor = FindBestPortAnchor(sw.transform, portIndex, minScore);
        return anchor != null;
    }

    private static int ScorePortAnchor(string name, int idx)
    {
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }

        // Game UI often labels front-panel ports 1..N; Gi0/(N-1) faces "Port N".
        var humanPort = idx + 1;
        if (Regex.IsMatch(name, $@"\bPort\s*{humanPort}\b", RegexOptions.IgnoreCase))
        {
            return 165;
        }

        if (Regex.IsMatch(name, $@"\bGi0\s*/\s*{idx}\b", RegexOptions.IgnoreCase))
        {
            return 200;
        }

        if (Regex.IsMatch(name, $@"\bGi0\s*_\s*{idx}\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(name, $@"\bGigabitEthernet0\s*/\s*{idx}\b", RegexOptions.IgnoreCase))
        {
            return 190;
        }

        if (Regex.IsMatch(name, $@"\bEth0\s*/\s*{idx}\b", RegexOptions.IgnoreCase))
        {
            return 180;
        }

        if (Regex.IsMatch(name, $@"\bSFP_?{idx}\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(name, $@"\bSfp{idx}\b", RegexOptions.IgnoreCase))
        {
            return 170;
        }

        var lower = name.ToLowerInvariant();
        if (lower.Contains("sfp") && Regex.IsMatch(name, $@"\b{idx}\b"))
        {
            return 120;
        }

        return 0;
    }

    private static Transform FindBestPortAnchor(Transform root, int portIndex, int minScore = 120)
    {
        Transform best = null;
        var bestScore = 0;
        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
        {
            var score = ScorePortAnchor(tr.gameObject.name ?? "", portIndex);
            if (score > bestScore)
            {
                bestScore = score;
                best = tr;
            }
        }

        return bestScore >= minScore ? best : null;
    }

    private static bool TryReflectPortState(NetworkSwitch sw, int portIndex, out string media, out string cable)
    {
        media = null;
        cable = null;
        var t = sw.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var methodName in IndexedPortMethodNames)
        {
            try
            {
                var m = t.GetMethod(methodName, flags, null, new[] { typeof(int) }, null);
                if (m == null)
                {
                    continue;
                }

                var obj = m.Invoke(sw, new object[] { portIndex });
                if (TryReadMediaAndCableFromObject(obj, out var med, out var cab))
                {
                    media = med;
                    cable = cab;
                    return true;
                }
            }
            catch
            {
                // Il2Cpp / stripped methods
            }
        }

        foreach (var collName in SwitchPortCollectionNames)
        {
            try
            {
                object collection = null;
                var p = t.GetProperty(collName, flags);
                if (p?.CanRead == true)
                {
                    collection = p.GetValue(sw);
                }
                else
                {
                    var f = t.GetField(collName, flags);
                    collection = f?.GetValue(sw);
                }

                if (TryGetByIndex(collection, portIndex, out var elem)
                    && TryReadMediaAndCableFromObject(elem, out var med, out var cab))
                {
                    media = med;
                    cable = cab;
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool TryGetByIndex(object collection, int index, out object element)
    {
        element = null;
        if (collection == null || index < 0)
        {
            return false;
        }

        if (collection is Array arr)
        {
            if (index >= arr.Length)
            {
                return false;
            }

            element = arr.GetValue(index);
            return element != null;
        }

        if (collection is IList list)
        {
            if (index >= list.Count)
            {
                return false;
            }

            element = list[index];
            return element != null;
        }

        return TryGetIl2CppReferenceArrayItem(collection, index, out element);
    }

    /// <summary>Il2CppReferenceArray&lt;T&gt; is not IList; read Length + indexer via reflection.</summary>
    private static bool TryGetIl2CppReferenceArrayItem(object arrayObj, int index, out object element)
    {
        element = null;
        if (arrayObj == null || index < 0)
        {
            return false;
        }

        var t = arrayObj.GetType();
        int length;
        try
        {
            var lenProp = t.GetProperty("Length", BindingFlags.Public | BindingFlags.Instance)
                          ?? t.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (lenProp == null)
            {
                return false;
            }

            length = Convert.ToInt32(lenProp.GetValue(arrayObj));
        }
        catch
        {
            return false;
        }

        if (index >= length)
        {
            return false;
        }

        try
        {
            var item = t.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance, null, null, new[] { typeof(int) }, null);
            if (item != null && item.GetIndexParameters().Length == 1)
            {
                element = item.GetValue(arrayObj, new object[] { index });
                return true;
            }

            var getItem = t.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
            if (getItem != null)
            {
                element = getItem.Invoke(arrayObj, new object[] { index });
                return true;
            }
        }
        catch
        {
            // Il2Cpp / stripped
        }

        return false;
    }

    private static object GetCableLinkSwitchPortsArray(NetworkSwitch sw)
    {
        if (sw == null)
        {
            return null;
        }

        const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = sw.GetType();
        try
        {
            var prop = t.GetProperty("cableLinkSwitchPorts", bf);
            if (prop?.CanRead == true)
            {
                return prop.GetValue(sw);
            }

            var fld = t.GetField("cableLinkSwitchPorts", bf);
            return fld?.GetValue(sw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Game-native per-port cable state (<c>NetworkSwitch.cableLinkSwitchPorts</c> / <c>CableLink.cableIDsOnLink</c>).
    /// Returns false when the field is missing or unreadable so callers can fall back to geometry heuristics.
    /// </summary>
    private static bool TryReadGameCableLinkPort(NetworkSwitch sw, int portIndex, out bool hasCable, out Transform ropeAttach, out string detail)
    {
        hasCable = false;
        ropeAttach = null;
        detail = null;
        if (sw == null || portIndex < 0)
        {
            return false;
        }

        try
        {
            var ports = GetCableLinkSwitchPortsArray(sw);
            if (ports == null)
            {
                return false;
            }

            if (!TryGetByIndex(ports, portIndex, out var linkObj))
            {
                detail = "cableLinkSwitchPorts index out of range or unreadable";
                return false;
            }

            if (linkObj == null)
            {
                hasCable = false;
                detail = "null CableLink slot";
                return true;
            }

            var idsReadable = TryReadIntMember(linkObj, "cableIDsOnLink", out var cableIds);
            var speedReadable = TryReadFloatMember(linkObj, "connectionSpeed", out var connSpeed);

            if (idsReadable)
            {
                detail = $"cableIDsOnLink={cableIds}";
                if (cableIds <= 0)
                {
                    hasCable = false;
                }
                else if (speedReadable)
                {
                    hasCable = connSpeed > 0.0001f;
                    detail += $", connectionSpeed={connSpeed}";
                }
                else
                {
                    hasCable = true;
                }
            }
            else if (speedReadable)
            {
                hasCable = connSpeed > 0.0001f;
                detail = $"connectionSpeed={connSpeed}";
            }
            else
            {
                return false;
            }

            ropeAttach = TryGetCableLinkRopeAttach(linkObj);
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static bool TryReadIntMember(object obj, string name, out int value)
    {
        value = 0;
        if (obj == null || string.IsNullOrEmpty(name))
        {
            return false;
        }

        for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    var v = p.GetValue(obj);
                    if (v is int i)
                    {
                        value = i;
                        return true;
                    }

                    if (v != null && int.TryParse(v.ToString(), out var parsed))
                    {
                        value = parsed;
                        return true;
                    }
                }

                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    if (v is int i2)
                    {
                        value = i2;
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool TryReadFloatMember(object obj, string name, out float value)
    {
        value = 0f;
        if (obj == null || string.IsNullOrEmpty(name))
        {
            return false;
        }

        for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    var v = p.GetValue(obj);
                    if (v is float f)
                    {
                        value = f;
                        return true;
                    }

                    if (v is double d)
                    {
                        value = (float)d;
                        return true;
                    }
                }

                var fld = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fld != null)
                {
                    var v = fld.GetValue(obj);
                    if (v is float f2)
                    {
                        value = f2;
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static Transform TryGetCableLinkRopeAttach(object linkObj)
    {
        if (linkObj == null)
        {
            return null;
        }

        var t = linkObj.GetType();
        try
        {
            var m = t.GetMethod("GetRopeAttachPoint", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (m != null)
            {
                var r = m.Invoke(linkObj, null) as Transform;
                if (r != null)
                {
                    return r;
                }
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            var p = t.GetProperty("ropeAttachPoint", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            return p?.GetValue(linkObj) as Transform;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadMediaAndCableFromObject(object obj, out string media, out string cable)
    {
        media = null;
        cable = null;
        if (obj == null)
        {
            return false;
        }

        bool? cablePrefer = null;
        bool? linkFallback = null;

        for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object val;
                try
                {
                    val = p.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                var pn = p.Name;
                if (IsNamedLike(pn, MediaPropertyNames))
                {
                    var s = FormatMediaValue(val);
                    if (s != null)
                    {
                        media ??= s;
                    }
                }
                else if (IsNamedLike(pn, CablePropertyNames))
                {
                    if (TryCoerceBool(val, out var b))
                    {
                        cablePrefer = b;
                    }
                }
                else if (IsNamedLike(pn, LinkPropertyNames))
                {
                    if (TryCoerceBool(val, out var b))
                    {
                        linkFallback = b;
                    }
                }
            }
        }

        if (cablePrefer.HasValue)
        {
            cable = cablePrefer.Value ? "plugged" : "unplugged";
        }
        else if (linkFallback.HasValue)
        {
            cable = linkFallback.Value ? "link-up" : "no-link";
        }

        return media != null || cable != null;
    }

    private static bool IsNamedLike(string name, string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.Equals(name, c, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatMediaValue(object val)
    {
        if (val == null)
        {
            return null;
        }

        if (val is string s)
        {
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        var t = val.GetType();
        if (t.IsEnum)
        {
            return Enum.GetName(t, val) ?? val.ToString();
        }

        return val.ToString();
    }

    private static bool TryCoerceBool(object val, out bool b)
    {
        b = false;
        switch (val)
        {
            case bool bb:
                b = bb;
                return true;
            case int i:
                b = i != 0;
                return true;
            case long l:
                b = l != 0;
                return true;
            default:
                return false;
        }
    }

    private static bool TryHierarchyProbe(NetworkSwitch sw, int portIndex, NetworkDeviceKind kind, out string media, out string cable)
    {
        media = null;
        cable = null;
        var root = sw.transform;
        if (root == null)
        {
            return false;
        }

        if (kind == NetworkDeviceKind.Router)
        {
            var anchor = FindBestPortAnchor(root, portIndex);
            if (anchor != null)
            {
                var inferred = TryInferSfpFromPortSubtree(anchor, kind);
                if (!string.IsNullOrEmpty(inferred))
                {
                    media = inferred;
                    return true;
                }
            }
        }

        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
        {
            var go = tr.gameObject;
            var name = go.name ?? "";
            if (!NameLikelyForPort(name, portIndex))
            {
                continue;
            }

            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null || c is Transform)
                {
                    continue;
                }

                TryReadMediaAndCableFromObject(c, out var med, out var cabStr);
                if (!string.IsNullOrEmpty(med))
                {
                    media ??= NormalizeReflectedMedia(med, kind);
                }

                if (!string.IsNullOrEmpty(cabStr) && string.IsNullOrEmpty(cable))
                {
                    cable = cabStr;
                }
            }
        }

        return media != null || cable != null;
    }

    private static bool NameLikelyForPort(string name, int idx)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (Regex.IsMatch(name, $@"Gi0\s*/\s*{idx}\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(name, $@"Fa0\s*/\s*{idx}\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(name, $@"(?:^|[^0-9]){idx}([^0-9]|$)"))
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("port") || lower.Contains("sfp") || lower.Contains("eth") || lower.Contains("interface")
                   || lower.Contains("socket") || lower.Contains("plug");
        }

        return false;
    }

    private enum CableElectricHint
    {
        Indeterminate,
        LikelyUp,
        LikelyDown,
    }

    private static CableElectricHint ClassifyCableHintString(string cable)
    {
        if (string.IsNullOrWhiteSpace(cable))
        {
            return CableElectricHint.Indeterminate;
        }

        var c = cable.Trim().ToLowerInvariant();
        if (c == "unknown")
        {
            return CableElectricHint.Indeterminate;
        }

        if (c.Contains("unplug") || c.Contains("no-link") || c.Contains("no link") || c.Contains("disconnect"))
        {
            return CableElectricHint.LikelyDown;
        }

        if (c.Contains("plugged") || c == "plugged" || c.Contains("link-up") || c.Contains("link up"))
        {
            return CableElectricHint.LikelyUp;
        }

        if (c == "connected")
        {
            return CableElectricHint.LikelyUp;
        }

        return CableElectricHint.Indeterminate;
    }

    /// <summary>CLI column: connected / not connected / unknown / admin down.</summary>
    internal static string GetCliLinkStatus(NetworkSwitch sw, int portIndex, NetworkDeviceKind kind, bool adminShutdown)
    {
        if (adminShutdown)
        {
            return "admin down";
        }

        if (TryReadGameCableLinkPort(sw, portIndex, out var gameCable, out _, out _))
        {
            return gameCable ? "connected" : "not connected";
        }

        GetDisplayHints(sw, portIndex, kind, out _, out var cable);
        var hint = ClassifyCableHintString(cable);
        if (hint == CableElectricHint.LikelyDown)
        {
            return "not connected";
        }

        if (hint == CableElectricHint.LikelyUp)
        {
            return "connected";
        }

        if (TryGetPortAttachmentTransform(sw, portIndex, out var anchor, 80))
        {
            return PortCableProximity.AnyVertexWithin(anchor.position, 1.2f) ? "connected" : "not connected";
        }

        return "unknown";
    }

    /// <summary>Block ping unless the egress port has a cable (reflection or cable geometry near port socket).</summary>
    internal static bool TryPrepareRouterPingEgress(
        NetworkSwitch sw,
        int egressIdx,
        out Transform pathRoot,
        out string errorToUser,
        out string logDetail)
    {
        pathRoot = null;
        errorToUser = null;
        logDetail = null;
        if (sw == null)
        {
            errorToUser = "internal error (no device).";
            logDetail = "sw null";
            return false;
        }

        if (TryReadGameCableLinkPort(sw, egressIdx, out var gameCable, out var ropeAttach, out var gameDetail))
        {
            if (!gameCable)
            {
                errorToUser = $"Gi0/{egressIdx} is not connected (no cable).";
                logDetail = $"CableLink: {gameDetail}";
                return false;
            }

            if (ropeAttach != null)
            {
                pathRoot = ropeAttach;
            }
            else if (TryGetPortAttachmentTransform(sw, egressIdx, out var namedAnchor, 80)
                     || TryGetPortAttachmentTransform(sw, egressIdx, out namedAnchor, 120))
            {
                pathRoot = namedAnchor;
            }
            else
            {
                pathRoot = sw.transform;
            }

            logDetail = $"CableLink ok ({gameDetail})";
            return true;
        }

        GetDisplayHints(sw, egressIdx, NetworkDeviceKind.Router, out _, out var cable);
        var hint = ClassifyCableHintString(cable);

        if (hint == CableElectricHint.LikelyDown)
        {
            errorToUser = $"Gi0/{egressIdx} is not connected (no cable).";
            logDetail = $"reflect: {cable}";
            return false;
        }

        var haveLooseAnchor = TryGetPortAttachmentTransform(sw, egressIdx, out var anchor80, 80);

        if (hint == CableElectricHint.LikelyUp)
        {
            if (haveLooseAnchor)
            {
                pathRoot = anchor80;
            }
            else if (TryGetPortAttachmentTransform(sw, egressIdx, out var a, 120)
                     || TryGetPortAttachmentTransform(sw, egressIdx, out a, 100))
            {
                pathRoot = a;
            }
            else
            {
                pathRoot = sw.transform;
            }

            logDetail = "reflect link up";
            return true;
        }

        if (!haveLooseAnchor)
        {
            errorToUser = $"cannot resolve chassis socket for Gi0/{egressIdx}; cannot verify cable.";
            logDetail = "no port anchor (min score 80)";
            return false;
        }

        if (!PortCableProximity.AnyVertexWithin(anchor80.position, 1.2f))
        {
            errorToUser = $"Gi0/{egressIdx} is not connected (no cable near this port).";
            logDetail = $"no LineRenderer vertex within 1.2m of {anchor80.name}";
            return false;
        }

        pathRoot = anchor80;
        logDetail = "geometry near port anchor";
        return true;
    }
}
