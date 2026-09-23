using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace GregModIPAM.Web;

// React-WebUI-Backend: HttpListener auf 127.0.0.1 (Default 8177), statische
// Dateien aus webRoot (Vite-dist) + JSON-API unter /api/*.
//
// Unity-Regel: Szenen-/GameObject-Zugriffe laufen NUR im Main-Thread.
// HTTP-Handler übergeben Arbeit per EnqueueMainThread (blockiert max. 8s),
// GregModIPAMMod.OnUpdate ruft Drain() auf. Reine Lese-Operationen ohne
// Unity-Kontakt laufen direkt im HTTP-Thread.
internal static class IpamWebServer
{
    private sealed class MainThreadWork
    {
        public Func<object> Fn;
        public object Result;
        public string Error;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
    }

    private static readonly ConcurrentQueue<MainThreadWork> _mainQueue = new ConcurrentQueue<MainThreadWork>();
    private static readonly List<string> _logRing = new List<string>();
    private static readonly object _logLock = new object();
    private const int MaxLogLines = 200;

    private static HttpListener _listener;
    private static CancellationTokenSource _cts;
    private static string _webRoot = "";
    private static int _port;

    private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly Dictionary<string, string> _mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", "text/html" },
        { ".js", "application/javascript" },
        { ".css", "text/css" },
        { ".json", "application/json" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
        { ".txt", "text/plain" },
    };

    public static bool Running => _listener != null && _listener.IsListening;

    public static int Port => _port;

    public static void PushLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (_logLock)
        {
            _logRing.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            while (_logRing.Count > MaxLogLines) _logRing.RemoveAt(0);
        }
    }

    public static void Start(int port, string webRoot)
    {
        if (Running) return;
        _port = port;
        _webRoot = webRoot ?? "";
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _cts = new CancellationTokenSource();
            _listener.Start();
            Task.Run(() => ServeLoop(_cts.Token));
            MelonLogger.Msg($"[IPAM][Web] WebUI läuft: http://127.0.0.1:{port}/ (Root: {_webRoot})");
            PushLog($"WebUI gestartet auf Port {port}");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[IPAM][Web] Start fehlgeschlagen (Port {port}): {ex.GetBaseException().Message}");
            try { _listener?.Close(); } catch { }
            _listener = null;
        }
    }

    public static void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_listener != null && _listener.IsListening) _listener.Stop();
        }
        catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    // Pro Frame aus OnUpdate: Main-Thread-Arbeit abarbeiten.
    public static void Drain()
    {
        for (var i = 0; i < 32; i++)
        {
            if (!_mainQueue.TryDequeue(out var work) || work == null) return;
            try
            {
                work.Result = work.Fn?.Invoke();
            }
            catch (Exception ex)
            {
                work.Error = ex.GetBaseException().Message;
            }
            finally
            {
                try { work.Done.Set(); } catch { }
            }
        }
    }

    private static object EnqueueMainThread(Func<object> fn, out string error)
    {
        error = null;
        var work = new MainThreadWork { Fn = fn };
        _mainQueue.Enqueue(work);
        if (!work.Done.Wait(TimeSpan.FromSeconds(8)))
        {
            error = "Main-Thread-Timeout (Spiel pausiert oder Szene lädt?)";
            return null;
        }

        error = work.Error;
        return work.Result;
    }

    private static void ServeLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx = null;
            try
            {
                ctx = _listener.GetContext();
            }
            catch
            {
                return;
            }

            try
            {
                Handle(ctx);
            }
            catch (Exception ex)
            {
                try
                {
                    WriteJson(ctx.Response, 500, new { ok = false, error = ex.GetBaseException().Message });
                }
                catch { }
                try { ctx.Response.Close(); } catch { }
            }
        }
    }

    private static void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var path = req.Url.AbsolutePath ?? "/";

        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            HandleApi(ctx, path.Substring(4), req, res);
            return;
        }

        ServeStatic(res, path);
    }

    private static void ServeStatic(HttpListenerResponse res, string path)
    {
        string rel = path == "/" ? "index.html" : path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string full = "";
        try
        {
            full = Path.GetFullPath(Path.Combine(_webRoot, rel));
            string root = Path.GetFullPath(_webRoot);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar) && !string.Equals(full, root))
            {
                WriteText(res, 403, "text/plain", "forbidden");
                return;
            }
        }
        catch
        {
            WriteText(res, 400, "text/plain", "bad path");
            return;
        }

        if (!File.Exists(full))
        {
            // SPA-Fallback: unbekannte Pfade -> index.html (React-Router).
            var index = Path.Combine(_webRoot, "index.html");
            if (File.Exists(index)) full = index;
            else
            {
                WriteText(res, 404, "text/html",
                    "<h1>IPAM WebUI: kein Frontend gefunden</h1><p>web/dist nach UserData/gregMod.IPAM/web kopieren (siehe README).</p>");
                return;
            }
        }

        try
        {
            var bytes = File.ReadAllBytes(full);
            res.StatusCode = 200;
            res.ContentType = _mime.TryGetValue(Path.GetExtension(full), out var mime) ? mime : "application/octet-stream";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            WriteText(res, 500, "text/plain", "read failed: " + ex.GetBaseException().Message);
            return;
        }

        try { res.Close(); } catch { }
    }

    private static string ReadBody(HttpListenerRequest req)
    {
        try
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
            {
                return reader.ReadToEnd() ?? "";
            }
        }
        catch { return ""; }
    }

    private static Dictionary<string, string> ParseBody(string raw)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw ?? "{}", _json);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return map;
            foreach (var kv in doc)
            {
                try
                {
                    map[kv.Key] = kv.Value.ValueKind == JsonValueKind.String
                        ? kv.Value.GetString() ?? ""
                        : kv.Value.ToString();
                }
                catch { }
            }

            return map;
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void WriteJson(HttpListenerResponse res, int status, object payload)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _json));
            res.StatusCode = status;
            res.ContentType = "application/json";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { }
        try { res.Close(); } catch { }
    }

    private static void WriteText(HttpListenerResponse res, int status, string mime, string text)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            res.StatusCode = status;
            res.ContentType = mime;
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { }
        try { res.Close(); } catch { }
    }

    private static Server FindServer(int instanceId)
    {
        Server found = null;
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<Server>();
            if (all == null) return null;
            foreach (var s in all)
            {
                if (s == null) continue;
                try
                {
                    if (s.GetInstanceID() == instanceId) { found = s; break; }
                }
                catch { }
            }
        }
        catch { }
        return found;
    }

    private static void HandleApi(HttpListenerContext ctx, string route, HttpListenerRequest req, HttpListenerResponse res)
    {
        var method = req.HttpMethod ?? "GET";
        var body = (method == "POST" || method == "DELETE") ? ParseBody(ReadBody(req)) : null;

        switch (route.ToLowerInvariant())
        {
            case "/status":
            {
                var result = EnqueueMainThread(() =>
                {
                    var servers = UnityEngine.Object.FindObjectsOfType<Server>();
                    int count = 0;
                    try { count = servers != null ? servers.Length : 0; } catch { }
                    string scene = "";
                    try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? ""; } catch { }
                    bool overlay = false;
                    try { overlay = IPAMOverlay.IsVisible; } catch { }
                    bool dhcp = false;
                    try { dhcp = LicenseManager.IsDHCPUnlocked; } catch { }
                    return (object)new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "mod", "gregMod.IPAM" },
                        { "scene", scene },
                        { "servers", count },
                        { "dhcpUnlocked", dhcp },
                        { "overlayVisible", overlay },
                    };
                }, out string error);
                if (error != null) { WriteJson(res, 503, new { ok = false, error }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/servers":
            {
                var result = EnqueueMainThread(() =>
                {
                    var list = new List<Dictionary<string, object>>();
                    Server[] all = null;
                    try { all = UnityEngine.Object.FindObjectsOfType<Server>(); } catch { }
                    if (all == null) return (object)list;
                    foreach (var s in all)
                    {
                        if (s == null) continue;
                        try
                        {
                            var row = new Dictionary<string, object>();
                            int id = 0;
                            try { id = s.GetInstanceID(); } catch { continue; }
                            row["id"] = id;
                            try { row["name"] = DeviceInventoryReflection.GetDisplayName(s) ?? ""; } catch { row["name"] = ""; }
                            try { row["ip"] = DHCPManager.GetServerIP(s) ?? ""; } catch { row["ip"] = ""; }
                            try { row["customer"] = IPAMOverlay.GetCustomerDisplayName(s) ?? ""; } catch { row["customer"] = ""; }
                            try { row["type"] = DeviceInventoryReflection.GetServerFormFactorLabel(s) ?? ""; } catch { row["type"] = ""; }
                            try
                            {
                                var state = ServerPowerController.TryGetTrackedServerPowerState(id);
                                row["power"] = state.HasValue ? (state.Value ? "on" : "off") : "unknown";
                            }
                            catch { row["power"] = "unknown"; }
                            list.Add(row);
                        }
                        catch { }
                    }

                    return (object)list;
                }, out string error2);
                if (error2 != null) { WriteJson(res, 503, new { ok = false, error = error2 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/dhcp/assign-all":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                var result = EnqueueMainThread(() =>
                {
                    try { DHCPManager.AssignAllServers(); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    PushLog("DHCP assign-all via WebUI");
                    return (object)new { ok = true };
                }, out string error3);
                if (error3 != null) { WriteJson(res, 503, new { ok = false, error = error3 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/dhcp/assign-one":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                if (body == null || !body.TryGetValue("id", out var idRaw) || !int.TryParse(idRaw, out var oneId))
                {
                    WriteJson(res, 400, new { ok = false, error = "id (instanceId) required" });
                    return;
                }

                var result = EnqueueMainThread(() =>
                {
                    var server = FindServer(oneId);
                    if (server == null) return (object)new { ok = false, error = "server not found" };
                    Server[] all = null;
                    try { all = UnityEngine.Object.FindObjectsOfType<Server>(); } catch { }
                    string ip = null;
                    try { ip = DHCPManager.GetNextFreeIpForServer(server, all); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (string.IsNullOrEmpty(ip)) return (object)new { ok = false, error = "no free IP" };
                    bool applied = false;
                    try { applied = DHCPManager.SetServerIP(server, ip, skipUsableListCheck: true); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (applied) PushLog($"DHCP assign-one via WebUI: {ip}");
                    return (object)new { ok = applied, ip = applied ? ip : null };
                }, out string error4);
                if (error4 != null) { WriteJson(res, 503, new { ok = false, error = error4 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/server/ip":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                if (body == null || !body.TryGetValue("id", out var idRaw2) || !int.TryParse(idRaw2, out var sid)
                    || !body.TryGetValue("ip", out var newIp) || string.IsNullOrWhiteSpace(newIp))
                {
                    WriteJson(res, 400, new { ok = false, error = "id + ip required" });
                    return;
                }

                var result = EnqueueMainThread(() =>
                {
                    var server = FindServer(sid);
                    if (server == null) return (object)new { ok = false, error = "server not found" };
                    bool applied = false;
                    try { applied = DHCPManager.SetServerIP(server, newIp.Trim(), skipUsableListCheck: true); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (applied) PushLog($"Set IP via WebUI: {newIp.Trim()}");
                    return (object)new { ok = applied };
                }, out string error5);
                if (error5 != null) { WriteJson(res, 503, new { ok = false, error = error5 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/server/rename":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                if (body == null || !body.TryGetValue("id", out var idRaw3) || !int.TryParse(idRaw3, out var rid)
                    || !body.TryGetValue("name", out var newName) || string.IsNullOrWhiteSpace(newName))
                {
                    WriteJson(res, 400, new { ok = false, error = "id + name required" });
                    return;
                }

                var result = EnqueueMainThread(() =>
                {
                    var server = FindServer(rid);
                    if (server == null) return (object)new { ok = false, error = "server not found" };
                    bool renamed = false;
                    try { renamed = DeviceNamingWriter.TrySetServerDisplayName(server, newName.Trim()); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (renamed) PushLog($"Rename via WebUI: {newName.Trim()}");
                    return (object)new { ok = renamed };
                }, out string error6);
                if (error6 != null) { WriteJson(res, 503, new { ok = false, error = error6 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/server/power":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                if (body == null || !body.TryGetValue("id", out var idRaw4) || !int.TryParse(idRaw4, out var pid))
                {
                    WriteJson(res, 400, new { ok = false, error = "id required" });
                    return;
                }

                var result = EnqueueMainThread(() =>
                {
                    bool toggled = false;
                    try { toggled = ServerPowerController.TryToggleServerByInstanceId(pid); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (toggled) PushLog("Power toggle via WebUI");
                    return (object)new { ok = toggled };
                }, out string error7);
                if (error7 != null) { WriteJson(res, 503, new { ok = false, error = error7 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/scopes":
            {
                if (method == "GET")
                {
                    var result = EnqueueMainThread(() =>
                    {
                        var list = new List<Dictionary<string, object>>();
                        try
                        {
                            foreach (var s in IpamDataStore.GetDhcpScopes())
                            {
                                if (s == null) continue;
                                list.Add(new Dictionary<string, object>
                                {
                                    { "id", s.Id ?? "" },
                                    { "name", s.Name ?? "" },
                                    { "level", s.Level ?? "" },
                                    { "cidr", s.Cidr ?? "" },
                                    { "priority", s.Priority },
                                });
                            }
                        }
                        catch { }
                        return (object)list;
                    }, out string error8);
                    if (error8 != null) { WriteJson(res, 503, new { ok = false, error = error8 }); return; }
                    WriteJson(res, 200, result);
                    return;
                }

                if (method == "POST")
                {
                    if (body == null || !body.TryGetValue("name", out var sname) || string.IsNullOrWhiteSpace(sname)
                        || !body.TryGetValue("cidr", out var scidr) || string.IsNullOrWhiteSpace(scidr))
                    {
                        WriteJson(res, 400, new { ok = false, error = "name + cidr required" });
                        return;
                    }

                    body.TryGetValue("level", out var slevel);
                    var result = EnqueueMainThread(() =>
                    {
                        bool added = false;
                        string err = null;
                        try { added = IpamDataStore.TryAddDhcpScope(sname.Trim(), string.IsNullOrWhiteSpace(slevel) ? "Global" : slevel.Trim(), scidr.Trim(), null, null, 0, out err); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                        if (added) PushLog($"Scope added via WebUI: {sname.Trim()} {scidr.Trim()}");
                        return (object)new { ok = added, error = added ? null : err };
                    }, out string error9);
                    if (error9 != null) { WriteJson(res, 503, new { ok = false, error = error9 }); return; }
                    WriteJson(res, 200, result);
                    return;
                }

                if (method == "DELETE")
                {
                    string delId = null;
                    try
                    {
                        var q = req.Url.Query ?? "";
                        foreach (var part in q.TrimStart('?').Split('&'))
                        {
                            var kv = part.Split('=');
                            if (kv.Length == 2 && kv[0] == "id") delId = Uri.UnescapeDataString(kv[1]);
                        }
                    }
                    catch { }
                    if (string.IsNullOrWhiteSpace(delId))
                    {
                        WriteJson(res, 400, new { ok = false, error = "id query required" });
                        return;
                    }

                    var result = EnqueueMainThread(() =>
                    {
                        bool deleted = false;
                        try
                        {
                            if (Guid.TryParse(delId, out var gid))
                                deleted = IpamDataStore.TryDeleteDhcpScope(gid, out _);
                        }
                        catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                        if (deleted) PushLog("Scope deleted via WebUI");
                        return (object)new { ok = deleted };
                    }, out string error10);
                    if (error10 != null) { WriteJson(res, 503, new { ok = false, error = error10 }); return; }
                    WriteJson(res, 200, result);
                    return;
                }

                WriteJson(res, 405, new { ok = false, error = "GET/POST/DELETE required" });
                return;
            }

            case "/logs":
            {
                List<string> lines;
                lock (_logLock) { lines = new List<string>(_logRing); }
                WriteJson(res, 200, lines);
                return;
            }

            case "/overlay":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                if (body == null || !body.TryGetValue("visible", out var visRaw) || !bool.TryParse(visRaw, out var visible))
                {
                    WriteJson(res, 400, new { ok = false, error = "visible (true/false) required" });
                    return;
                }

                var result = EnqueueMainThread(() =>
                {
                    try { IPAMOverlay.IsVisible = visible; } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    return (object)new { ok = true };
                }, out string error11);
                if (error11 != null) { WriteJson(res, 503, new { ok = false, error = error11 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/racks/mounts":
            {
                var result = EnqueueMainThread(() =>
                {
                    var list = new List<Dictionary<string, object>>();
                    RackMount[] all = null;
                    try { all = UnityEngine.Object.FindObjectsOfType<RackMount>(); } catch { }
                    if (all == null) return (object)list;
                    foreach (var m in all)
                    {
                        if (m == null) continue;
                        try
                        {
                            var go = m.gameObject;
                            if (go == null || !go.scene.IsValid() || !go.scene.isLoaded) continue;
                            var row = new Dictionary<string, object>();
                            try { row["id"] = m.GetInstanceID(); } catch { continue; }
                            try { row["template"] = m.rackTemplateId ?? ""; } catch { row["template"] = ""; }
                            try { row["instantiated"] = m.isRackInstantiated; } catch { row["instantiated"] = false; }
                            try
                            {
                                var p = go.transform.position;
                                row["x"] = Math.Round(p.x, 1);
                                row["y"] = Math.Round(p.y, 1);
                                row["z"] = Math.Round(p.z, 1);
                            }
                            catch { row["x"] = 0; row["y"] = 0; row["z"] = 0; }
                            list.Add(row);
                        }
                        catch { }
                    }

                    return (object)list;
                }, out string error12);
                if (error12 != null) { WriteJson(res, 503, new { ok = false, error = error12 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/racks/install":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                if (body == null || !body.TryGetValue("id", out var idRaw5) || !int.TryParse(idRaw5, out var mountId))
                {
                    WriteJson(res, 400, new { ok = false, error = "id required" });
                    return;
                }

                body.TryGetValue("cheat", out var cheatRaw);
                bool.TryParse(cheatRaw, out var cheat);
                var result = EnqueueMainThread(() =>
                {
                    RackMount mount = null;
                    try
                    {
                        foreach (var m in UnityEngine.Object.FindObjectsOfType<RackMount>())
                        {
                            if (m == null) continue;
                            try { if (m.GetInstanceID() == mountId) { mount = m; break; } } catch { }
                        }
                    }
                    catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (mount == null) return (object)new { ok = false, error = "mount not found" };
                    try
                    {
                        var routine = mount.InstallRack(cheat, 0, false);
                        if (routine == null) return (object)new { ok = false, error = "no routine" };
                        MelonCoroutines.Start(PumpRoutine(routine));
                        PushLog($"Rack-Aufbau via WebUI (mount {mountId}{(cheat ? ", cheat" : "")})");
                    }
                    catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    return (object)new { ok = true };
                }, out string error13);
                if (error13 != null) { WriteJson(res, 503, new { ok = false, error = error13 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/racks/list":
            {
                var result = EnqueueMainThread(() =>
                {
                    var list = new List<Dictionary<string, object>>();
                    Rack[] all = null;
                    try { all = UnityEngine.Object.FindObjectsOfType<Rack>(); } catch { }
                    if (all == null) return (object)list;
                    foreach (var r in all)
                    {
                        if (r == null) continue;
                        try
                        {
                            var go = r.gameObject;
                            if (go == null || !go.scene.IsValid() || !go.scene.isLoaded) continue;
                            var row = new Dictionary<string, object>();
                            try { row["id"] = r.GetInstanceID(); } catch { continue; }
                            try { row["name"] = go.name ?? ""; } catch { row["name"] = ""; }
                            try
                            {
                                var p = go.transform.position;
                                row["x"] = Math.Round(p.x, 1);
                                row["z"] = Math.Round(p.z, 1);
                            }
                            catch { row["x"] = 0; row["z"] = 0; }
                            list.Add(row);
                        }
                        catch { }
                    }

                    return (object)list;
                }, out string error14);
                if (error14 != null) { WriteJson(res, 503, new { ok = false, error = error14 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/racks/templates":
            {
                var result = EnqueueMainThread(() =>
                {
                    var list = new List<Dictionary<string, object>>();
                    try
                    {
                        var templates = RackTemplateStore.LoadAll();
                        if (templates == null) return (object)list;
                        foreach (var t in templates)
                        {
                            if (t == null) continue;
                            try
                            {
                                list.Add(new Dictionary<string, object>
                                {
                                    { "id", t.templateId ?? "" },
                                    { "price", t.price },
                                });
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    return (object)list;
                }, out string error15);
                if (error15 != null) { WriteJson(res, 503, new { ok = false, error = error15 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            case "/racks/apply-template":
            {
                if (method != "POST") { WriteJson(res, 405, new { ok = false, error = "POST required" }); return; }
                if (body == null || !body.TryGetValue("rackId", out var rackIdRaw) || !int.TryParse(rackIdRaw, out var rackId)
                    || !body.TryGetValue("templateId", out var templateId) || string.IsNullOrWhiteSpace(templateId))
                {
                    WriteJson(res, 400, new { ok = false, error = "rackId + templateId required" });
                    return;
                }

                var result = EnqueueMainThread(() =>
                {
                    Rack rack = null;
                    try
                    {
                        foreach (var r in UnityEngine.Object.FindObjectsOfType<Rack>())
                        {
                            if (r == null) continue;
                            try { if (r.GetInstanceID() == rackId) { rack = r; break; } } catch { }
                        }
                    }
                    catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (rack == null) return (object)new { ok = false, error = "rack not found" };
                    RackTemplate template = null;
                    try { template = RackTemplateStore.Load(templateId.Trim()); } catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    if (template == null) return (object)new { ok = false, error = "template not found" };
                    try
                    {
                        var routine = RackTemplateApplier.Apply(rack, template);
                        if (routine == null) return (object)new { ok = false, error = "no routine" };
                        MelonCoroutines.Start(PumpRoutine(routine));
                        PushLog($"Template via WebUI: {templateId.Trim()}");
                    }
                    catch (Exception ex) { return (object)new { ok = false, error = ex.GetBaseException().Message }; }
                    return (object)new { ok = true };
                }, out string error16);
                if (error16 != null) { WriteJson(res, 503, new { ok = false, error = error16 }); return; }
                WriteJson(res, 200, result);
                return;
            }

            default:
                WriteJson(res, 404, new { ok = false, error = "unknown api route" });
                return;
        }
    }

    // Il2Cpp-Coroutine über verwaltete Pumpe starten (MoveNext pro Frame).
    private static System.Collections.IEnumerator PumpRoutine(Il2CppSystem.Collections.IEnumerator routine)
    {
        while (true)
        {
            bool more = false;
            try { more = routine.MoveNext(); }
            catch { yield break; }
            if (!more) yield break;
            yield return null;
        }
    }
}
