/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.World.Player;
using LoneEftDmaRadar.Tarkov.World.Exits;
using LoneEftDmaRadar.Misc.JSON;
using LoneEftDmaRadar.UI;
using LoneEftDmaRadar.UI.Maps;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.Web.WebRadar.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Open.Nat;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Net.WebSockets;

namespace LoneEftDmaRadar.Web.WebRadar
{
    internal static class WebRadarServer
    {
        private static WebRadarConfig Config { get; } = Program.Config.WebRadar;
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static WebApplication? _host;
        private static CancellationTokenSource? _workerCts;
        private static Task? _workerTask;
        private static TimeSpan _tickRate;
        private static int _upnpPort = -1;

        private static WebRadarUpdate _latest = new();
        private static string _authCookieToken = string.Empty;

        private const string AuthCookieName = "wr_auth";

        public static bool IsRunning => _host is not null;

        public static async Task StartAsync(string ip, int port, TimeSpan tickRate, bool upnp)
        {
            await StopAsync();

            _tickRate = tickRate;
            _authCookieToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

            var bindIp = ip;
            if (upnp && IsLoopbackHost(ip))
                bindIp = "0.0.0.0";

            ThrowIfInvalidBindParameters(bindIp, port);

            if (upnp)
                await ConfigureUPnPAsync(port);

            var builder = WebApplication.CreateBuilder();
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, port);
            });

            _host = builder.Build();

            var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            SyncDesktopMapsToWebRoot();

                        _host.Use(async (context, next) =>
                        {
                                if (context.Request.Path.StartsWithSegments("/health"))
                                {
                                        await next();
                                        return;
                                }

                                if (IsAuthorized(context))
                                {
                                        await next();
                                        return;
                                }

                            var providedPassword = GetProvidedPassword(context);
                                if (!string.IsNullOrWhiteSpace(providedPassword) && string.Equals(providedPassword, Config.Password, StringComparison.Ordinal))
                                {
                                        SetAuthorizedCookie(context);
                                        await next();
                                        return;
                                }

                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                if (context.Request.Path.StartsWithSegments("/api"))
                                {
                                        context.Response.ContentType = "text/plain";
                                        await context.Response.WriteAsync("Unauthorized. Open with ?password=YOUR_PASSWORD first.");
                                        return;
                                }

                                context.Response.ContentType = "text/html; charset=utf-8";
                                await context.Response.WriteAsync("""
<!doctype html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Web Radar Locked</title>
    <style>
        body { font-family: Segoe UI, sans-serif; margin: 0; min-height: 100vh; display: grid; place-items: center; background: #0e1116; color: #dbe2ef; }
        .card { width: min(92vw, 420px); background: #171c24; border: 1px solid #2a3340; border-radius: 14px; padding: 18px; }
        h2 { margin: 0 0 10px 0; font-size: 18px; }
        p { margin: 0 0 14px 0; color: #9fb0c8; }
        form { display: flex; gap: 8px; }
        input { flex: 1; padding: 10px; border-radius: 8px; border: 1px solid #324156; background: #0f141c; color: #e5edf8; }
        button { padding: 10px 14px; border: 0; border-radius: 8px; background: #2f80ed; color: white; cursor: pointer; }
    </style>
</head>
<body>
    <div class="card">
        <h2>Web Radar Locked</h2>
        <p>Enter the session password to access this radar.</p>
        <form method="get" action="/">
            <input name="password" type="password" placeholder="Session password" required />
            <button type="submit">Unlock</button>
        </form>
    </div>
</body>
</html>
""");
                        });

            _host.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot)
            });

            _host.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                RequestPath = ""
            });

            _host.UseWebSockets();

            _host.MapGet("/ws/radar", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("This endpoint only accepts WebSocket requests.");
                    return;
                }

                Logging.WriteLine("[WebRadar][WS] incoming /ws/radar request");

                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                Logging.WriteLine("[WebRadar][WS] accepted /ws/radar");

                try
                {
                    while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                    {
                        try
                        {
                            var safe = CreateSafeUpdateSnapshot();
                            var json = JsonSerializer.Serialize(safe, AppJsonContext.Default.WebRadarUpdate);
                            var bytes = Encoding.UTF8.GetBytes(json);

                            await socket.SendAsync(
                                new ArraySegment<byte>(bytes),
                                WebSocketMessageType.Text,
                                true,
                                context.RequestAborted);
                        }
                        catch (Exception ex)
                        {
                            Logging.WriteLine($"[WebRadar][WS] send frame failed: {ex.Message}");
                        }

                        await Task.Delay(_tickRate, context.RequestAborted);
                    }
                }
                catch (OperationCanceledException)
                {
                    Logging.WriteLine("[WebRadar][WS] canceled");
                }
                catch (Exception ex)
                {
                    Logging.WriteLine($"[WebRadar][WS] loop error: {ex}");
                }

                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }

                Logging.WriteLine("[WebRadar][WS] closed /ws/radar");
            });
            _host.MapGet("/health", () => Results.Text("OK"));

            _host.MapGet("/api/default-data", async context =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, "DEFAULT_DATA.json");
                if (!File.Exists(path))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync("DEFAULT_DATA.json not found.");
                    return;
                }

                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "public, max-age=3600";
                await context.Response.SendFileAsync(path);
            });

            await _host.StartAsync();
            StartWorker();
        }

        public static async Task StopAsync()
        {
            _workerCts?.Cancel();
            if (_workerTask is not null)
            {
                try { await _workerTask; } catch { }
            }
            _workerTask = null;
            _workerCts?.Dispose();
            _workerCts = null;

            if (_host is not null)
            {
                await _host.StopAsync();
                await _host.DisposeAsync();
                _host = null;
            }

            if (_upnpPort > 0)
            {
                await CleanupUPnPAsync(_upnpPort);
                _upnpPort = -1;
            }
        }

        public static async Task<string> GetExternalIPAsync()
        {
            try
            {
                return await QueryUPnPForIPAsync();
            }
            catch
            {
                try
                {
                    var ipServices = new[]
                    {
                        "https://api.ipify.org",
                        "https://icanhazip.com",
                        "https://ifconfig.me/ip"
                    };

                    foreach (var service in ipServices)
                    {
                        try
                        {
                            var response = await HttpClient.GetStringAsync(service);
                            var ip = response.Trim();
                            if (IPAddress.TryParse(ip, out _))
                                return ip;
                        }
                        catch
                        {
                            // Best-effort fallback chain.
                        }
                    }
                }
                catch
                {
                    // Keep localhost fallback.
                }

                return "127.0.0.1";
            }
        }

        public static string? GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static void StartWorker()
        {
            _workerCts = new CancellationTokenSource();
            _workerTask = Task.Run(() => WorkerRoutineAsync(_workerCts.Token));
        }

        private static async Task WorkerRoutineAsync(CancellationToken ct)
        {
            try
            {
                using var timer = new PeriodicTimer(_tickRate);
                while (await timer.WaitForNextTickAsync(ct))
                {
                    try
                    {
                        var inRaid = Program.State == AppState.InRaid
                            || (Memory.Game?.InRaid ?? false)
                            || !string.IsNullOrWhiteSpace(Memory.MapID);

                        if (inRaid)
                        {
                            var mapId = Memory.MapID;
                            var players = Memory.Players;
                            _latest.InGame = true;
                            _latest.InRaid = true;
                            _latest.MapID = mapId;
                            _latest.WebMapId = mapId;
                            _latest.SendTime = DateTime.UtcNow;
                            _latest.AimLineLength = Program.Config.UI.AimLineLength;
                            _latest.MaxDistance = Program.Config.UI.MaxDistance;
                            _latest.TeammateAimlines = Program.Config.UI.TeammateAimlines;
                            _latest.AIAimlines = Program.Config.UI.AIAimlines;
                            _latest.DeathMarkerColorHex = ToCssHex(SKPaints.PaintDeathMarker.Color);
                            _latest.CorpseColorHex = ToCssHex(SKPaints.TextCorpse.Color);

                            var map = EftMapManager.LoadMap(mapId);
                            _latest.Map = map is not null
                                ? WebRadarMapConverter.Convert(mapId, map.Config)
                                : null;

                            if (map is not null)
                            {
                                var mapCfg = map.Config;
                                _latest.Players = players?
                                    .Select(x => WebRadarPlayer.Create(x, mapCfg))
                                    .Where(IsValidPlayer)
                                    .ToArray();

                                _latest.Loot = Memory.Loot?.FilteredLoot?
                                    .Select(x => WebRadarLoot.Create(x, mapCfg))
                                    .Where(IsValidLoot)
                                    .ToArray();

                                var exits = Memory.Exits;
                                _latest.Exfils = exits?
                                    .OfType<Exfil>()
                                    .Select(x => WebRadarExfil.Create(x, mapCfg))
                                    .Where(IsValidExfil)
                                    .ToArray();

                                _latest.Transits = exits?
                                    .OfType<TransitPoint>()
                                    .Select(x => WebRadarTransit.Create(x, mapCfg))
                                    .Where(IsValidTransit)
                                    .ToArray();
                            }
                            else
                            {
                                _latest.Loot = Array.Empty<WebRadarLoot>();
                                _latest.Exfils = Array.Empty<WebRadarExfil>();
                                _latest.Transits = Array.Empty<WebRadarTransit>();
                            }
                        }
                        else
                        {
                            _latest.InGame = false;
                            _latest.InRaid = false;
                            _latest.MapID = null;
                            _latest.WebMapId = null;
                            _latest.SendTime = DateTime.UtcNow;
                            _latest.AimLineLength = Program.Config.UI.AimLineLength;
                            _latest.MaxDistance = Program.Config.UI.MaxDistance;
                            _latest.TeammateAimlines = Program.Config.UI.TeammateAimlines;
                            _latest.AIAimlines = Program.Config.UI.AIAimlines;
                            _latest.DeathMarkerColorHex = ToCssHex(SKPaints.PaintDeathMarker.Color);
                            _latest.CorpseColorHex = ToCssHex(SKPaints.TextCorpse.Color);
                            _latest.Map = null;
                            _latest.Players = null;
                            _latest.Loot = null;
                            _latest.Exfils = null;
                            _latest.Transits = null;
                        }
                        _latest.Version++;
                    }
                    catch
                    {
                        // Keep worker alive.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on stop
            }
            catch (Exception ex)
            {
                MessageBox.Show(RadarWindow.Handle, $"WebRadarServer Worker Thread Crashed:\n{ex}", "Web Radar Server", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ThrowIfInvalidBindParameters(string ip, int port)
        {
            try
            {
                if (port is < 1024 or > 65535)
                    throw new ArgumentException("Invalid Port. We recommend using a Port between 50000-60000.");
                var ipObj = IPAddress.Parse(ip);
                using var socket = new Socket(ipObj.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(ipObj, port));
                socket.Close();
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException($"Invalid Bind Parameters. Use a valid Bind IP (ex: 0.0.0.0), and a port number between 50000-60000.\n" +
                    $"SocketException: {ex.Message}");
            }
        }

        private static bool IsLoopbackHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
        }

        private static void SyncDesktopMapsToWebRoot()
        {
            try
            {
                var sourceMaps = Path.Combine(AppContext.BaseDirectory, "Maps");
                var targetMaps = Path.Combine(AppContext.BaseDirectory, "wwwroot", "Maps");

                if (!Directory.Exists(sourceMaps))
                    return;

                Directory.CreateDirectory(targetMaps);
                CopyDirectoryRecursive(sourceMaps, targetMaps);
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[WebRadar] map sync failed: {ex.Message}");
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destination = Path.Combine(targetDir, fileName);
                File.Copy(file, destination, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var subDirName = Path.GetFileName(subDir);
                var targetSubDir = Path.Combine(targetDir, subDirName);
                Directory.CreateDirectory(targetSubDir);
                CopyDirectoryRecursive(subDir, targetSubDir);
            }
        }

        private static bool IsAuthorized(HttpContext context)
        {
            if (!_host?.Lifetime.ApplicationStarted.IsCancellationRequested ?? true)
                return false;

            var token = context.Request.Cookies[AuthCookieName];
            return !string.IsNullOrWhiteSpace(token) && string.Equals(token, _authCookieToken, StringComparison.Ordinal);
        }

        private static void SetAuthorizedCookie(HttpContext context)
        {
            context.Response.Cookies.Append(AuthCookieName, _authCookieToken, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = false,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(12)
            });
        }

        private static string GetProvidedPassword(HttpContext context)
        {
            var direct = context.Request.Query["password"].ToString();
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            var refererRaw = context.Request.Headers.Referer.ToString();
            if (string.IsNullOrWhiteSpace(refererRaw))
                return string.Empty;

            if (!Uri.TryCreate(refererRaw, UriKind.Absolute, out var referer))
                return string.Empty;

            var q = referer.Query;
            if (string.IsNullOrWhiteSpace(q))
                return string.Empty;

            if (q.StartsWith("?", StringComparison.Ordinal))
                q = q[1..];

            foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (!string.Equals(Uri.UnescapeDataString(kv[0]), "password", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (kv.Length < 2)
                    return string.Empty;

                return Uri.UnescapeDataString(kv[1]);
            }

            return string.Empty;
        }

        private static async Task<NatDevice?> TryDiscoverNatAsync()
        {
            try
            {
                var d = new NatDiscoverer();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                return await d.DiscoverDeviceAsync(PortMapper.Upnp, cts);
            }
            catch
            {
                // ignore
            }

            try
            {
                var d = new NatDiscoverer();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                return await d.DiscoverDeviceAsync(PortMapper.Pmp, cts);
            }
            catch
            {
                return null;
            }
        }

        private static async Task ConfigureUPnPAsync(int port)
        {
            try
            {
                var upnp = await TryDiscoverNatAsync();
                if (upnp is null)
                    return;

                await upnp.CreatePortMapAsync(new Mapping(
                    protocol: Protocol.Tcp,
                    privatePort: port,
                    publicPort: port,
                    lifetime: 86400,
                    description: "Lone WebRadar"));

                _upnpPort = port;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"ERROR Setting up UPnP: {ex.Message}");
            }
        }

        private static async Task CleanupUPnPAsync(int port)
        {
            try
            {
                var nat = await TryDiscoverNatAsync();
                if (nat is null)
                    return;

                await nat.DeletePortMapAsync(new Mapping(Protocol.Tcp, port, port));
            }
            catch
            {
                // best effort
            }
        }

        private static async Task<string> QueryUPnPForIPAsync()
        {
            var upnp = await TryDiscoverNatAsync();
            if (upnp is null)
                throw new InvalidOperationException("UPnP/NAT-PMP gateway not found.");

            var ip = await upnp.GetExternalIPAsync();
            return ip.ToString();
        }

        private static bool IsValidPlayer(WebRadarPlayer player)
        {
            return IsFinite(player.Position.X)
                && IsFinite(player.Position.Y)
                && IsFinite(player.Position.Z)
                && IsFinite(player.Rotation.X)
                && IsFinite(player.Rotation.Y)
                && IsFinite(player.Yaw);
        }

            private static string ToCssHex(SkiaSharp.SKColor color)
            {
                return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
            }

        private static bool IsValidLoot(WebRadarLoot loot)
        {
            return IsFinite(loot.X)
                && IsFinite(loot.Y)
                && IsFinite(loot.Z);
        }

        private static bool IsValidExfil(WebRadarExfil exfil)
        {
            return IsFinite(exfil.X)
                && IsFinite(exfil.Y)
                && IsFinite(exfil.Z);
        }

        private static bool IsValidTransit(WebRadarTransit transit)
        {
            return IsFinite(transit.X)
                && IsFinite(transit.Y)
                && IsFinite(transit.Z);
        }

        private static WebRadarUpdate CreateSafeUpdateSnapshot()
        {
            var snapshot = new WebRadarUpdate
            {
                Version = _latest.Version,
                InGame = _latest.InGame,
                InRaid = _latest.InRaid,
                MapID = _latest.MapID,
                WebMapId = _latest.WebMapId,
                SendTime = _latest.SendTime,
                AimLineLength = _latest.AimLineLength,
                MaxDistance = _latest.MaxDistance,
                TeammateAimlines = _latest.TeammateAimlines,
                AIAimlines = _latest.AIAimlines,
                Players = Array.Empty<WebRadarPlayer>(),
                Loot = Array.Empty<WebRadarLoot>(),
                Exfils = Array.Empty<WebRadarExfil>(),
                Transits = Array.Empty<WebRadarTransit>(),
                Map = null
            };

            try
            {
                snapshot.AimLineLength = Math.Clamp(_latest.AimLineLength, 0, 10000);
                snapshot.MaxDistance = IsFinite(_latest.MaxDistance) && _latest.MaxDistance > 0f
                    ? _latest.MaxDistance
                    : 350f;
                snapshot.TeammateAimlines = _latest.TeammateAimlines;
                snapshot.AIAimlines = _latest.AIAimlines;

                _ = JsonSerializer.Serialize(snapshot.AimLineLength, AppJsonContext.Default.Int32);
                _ = JsonSerializer.Serialize(snapshot.MaxDistance, AppJsonContext.Default.Single);
                _ = JsonSerializer.Serialize(snapshot.TeammateAimlines, AppJsonContext.Default.Boolean);
                _ = JsonSerializer.Serialize(snapshot.AIAimlines, AppJsonContext.Default.Boolean);
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[WebRadar] Invalid aimline settings payload filtered: {ex.Message}");
                snapshot.AimLineLength = 1500;
                snapshot.MaxDistance = 350f;
                snapshot.TeammateAimlines = false;
                snapshot.AIAimlines = true;
            }

            try
            {
                var players = _latest.Players?
                    .Where(IsValidPlayer)
                    .ToArray() ?? Array.Empty<WebRadarPlayer>();

                _ = JsonSerializer.Serialize(players, AppJsonContext.Default.WebRadarPlayerArray);
                snapshot.Players = players;
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[WebRadar] Invalid players payload filtered: {ex.Message}");
            }

            try
            {
                var loot = _latest.Loot?
                    .Where(IsValidLoot)
                    .ToArray() ?? Array.Empty<WebRadarLoot>();

                _ = JsonSerializer.Serialize(loot, AppJsonContext.Default.WebRadarLootArray);
                snapshot.Loot = loot;
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[WebRadar] Invalid loot payload filtered: {ex.Message}");
            }

            try
            {
                var exfils = _latest.Exfils?
                    .Where(IsValidExfil)
                    .ToArray() ?? Array.Empty<WebRadarExfil>();

                _ = JsonSerializer.Serialize(exfils, AppJsonContext.Default.WebRadarExfilArray);
                snapshot.Exfils = exfils;
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[WebRadar] Invalid exfil payload filtered: {ex.Message}");
            }

            try
            {
                var transits = _latest.Transits?
                    .Where(IsValidTransit)
                    .ToArray() ?? Array.Empty<WebRadarTransit>();

                _ = JsonSerializer.Serialize(transits, AppJsonContext.Default.WebRadarTransitArray);
                snapshot.Transits = transits;
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[WebRadar] Invalid transit payload filtered: {ex.Message}");
            }

            try
            {
                var map = SanitizeMap(_latest.Map);
                if (map is not null)
                {
                    _ = JsonSerializer.Serialize(map, AppJsonContext.Default.WebRadarMapInfo);
                    snapshot.Map = map;
                }
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[WebRadar] Invalid map payload filtered: {ex.Message}");
            }

            return snapshot;
        }

        private static WebRadarMapInfo SanitizeMap(WebRadarMapInfo map)
        {
            if (map is null)
                return null;

            var safe = new WebRadarMapInfo
            {
                Id = map.Id ?? string.Empty,
                X = IsFinite(map.X) ? map.X : 0f,
                Y = IsFinite(map.Y) ? map.Y : 0f,
                Scale = IsFinite(map.Scale) && map.Scale > 0f ? map.Scale : 1f,
                SvgScale = IsFinite(map.SvgScale) && map.SvgScale > 0f ? map.SvgScale : 1f,
                DisableDimming = map.DisableDimming,
                Layers = new List<WebRadarMapLayer>()
            };

            if (map.Layers is null)
                return safe;

            foreach (var layer in map.Layers)
            {
                if (layer is null)
                    continue;

                safe.Layers.Add(new WebRadarMapLayer
                {
                    MinHeight = IsFiniteNullable(layer.MinHeight) ? layer.MinHeight : null,
                    MaxHeight = IsFiniteNullable(layer.MaxHeight) ? layer.MaxHeight : null,
                    CannotDimLowerLayers = layer.CannotDimLowerLayers,
                    Filename = layer.Filename ?? string.Empty
                });
            }

            return safe;
        }

        private static bool IsFiniteNullable(float? value) => !value.HasValue || IsFinite(value.Value);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

