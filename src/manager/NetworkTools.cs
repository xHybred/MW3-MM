using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace MW3ModManager;

internal static class NetworkTools
{
    private const string ServiceDescription = "MW3 Mod Manager";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    internal static string GameExe(string gameDir) => Path.Combine(gameDir, "iw5sp.exe");

    internal sealed record LocalNetwork(IPAddress Address, IPAddress Gateway, string Adapter, NetworkInterfaceType Type);

    internal static LocalNetwork? FindLocalNetwork()
    {
        var candidates = new List<LocalNetwork>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            var address = props.UnicastAddresses
                .Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
            var gateway = props.GatewayAddresses
                .Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !x.Equals(IPAddress.Any));
            if (address is not null && gateway is not null)
                candidates.Add(new LocalNetwork(address, gateway, nic.Name, nic.NetworkInterfaceType));
        }

        return candidates
            .OrderBy(x => AdapterScore(x))
            .FirstOrDefault();
    }

    private static int AdapterScore(LocalNetwork network)
    {
        var name = network.Adapter.ToLowerInvariant();
        var score = 0;
        if (name.Contains("vpn") || name.Contains("tap") || name.Contains("tun") || name.Contains("wireguard") ||
            name.Contains("tailscale") || name.Contains("zerotier") || name.Contains("hamachi") ||
            name.Contains("radmin") || name.Contains("virtual") || name.Contains("vmware") ||
            name.Contains("hyper-v") || name.Contains("wsl"))
        {
            score += 100;
        }
        if (network.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            score -= 2;
        if (IsLanAddress(network.Address)) score -= 4;
        if (IsLanAddress(network.Gateway)) score -= 2;
        return score;
    }

    private static bool IsLanAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork &&
               (bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127));
    }

    internal static async Task<string> CheckAsync(string? gameDir, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Checking the local network...");
        var local = FindLocalNetwork();
        if (local is null) return "No active IPv4 network connection was found.";

        var lines = new List<string>
        {
            $"Adapter: {local.Adapter} ({local.Type})",
            $"Local IPv4: {local.Address}",
            $"Gateway: {local.Gateway}",
            $"Game executable: {(gameDir is not null && File.Exists(GameExe(gameDir)) ? "found" : "not found")}",
        };
        if (!IsLanAddress(local.Address) || !IsLanAddress(local.Gateway))
            lines.Add("Adapter warning: this is not a normal home-LAN address. It may be a VPN or virtual adapter.");

        var gateway = await DiscoverGatewayAsync(local, ct).ConfigureAwait(false);
        if (gateway is null)
        {
            lines.Add("UPnP: no Internet Gateway Device answered. The router may have UPnP disabled.");
            lines.Add("A port mapping can still be added manually on the router if you regain access.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"UPnP: {gateway.ServiceType}");
        try
        {
            var external = await GetExternalAddressAsync(gateway, ct).ConfigureAwait(false);
            lines.Add($"Router external IP: {external}");
            var publicIp = await GetPublicAddressAsync(ct).ConfigureAwait(false);
            lines.Add($"Public IP: {publicIp ?? "unknown"}");
            if (external is not null && publicIp is not null)
            {
                lines.Add(external.Equals(publicIp, StringComparison.OrdinalIgnoreCase)
                    ? "CGNAT: no (router address matches the public address)"
                    : "CGNAT: likely (router address differs from the public address)");
            }
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            lines.Add("UPnP address query: " + ex.Message);
        }

        lines.Add("Port mappings: press Fix connection to request TCP/UDP 3074 and 27014-27050.");
        return string.Join(Environment.NewLine, lines);
    }

    internal static async Task<string> FixAsync(string? gameDir, IProgress<string>? progress, CancellationToken ct)
    {
        var lines = new List<string>();

        if (gameDir is not null && File.Exists(GameExe(gameDir)))
        {
            progress?.Report("Adding a Windows Firewall rule...");
            try
            {
                lines.Add(await Task.Run(() => AllowFirewall(gameDir!), ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                lines.Add("Firewall: " + ex.Message);
            }
        }
        else
        {
            lines.Add("Firewall: the game executable was not found. Set the game folder first.");
        }

        progress?.Report("Looking for the router...");
        var local = FindLocalNetwork();
        if (local is null)
        {
            lines.Add("Network: no active IPv4 connection was found.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"Adapter: {local.Adapter} ({local.Type})");
        lines.Add($"Local IPv4: {local.Address}");
        lines.Add($"Gateway: {local.Gateway}");
        if (!IsLanAddress(local.Address) || !IsLanAddress(local.Gateway))
            lines.Add("Adapter warning: this is not a normal home-LAN address. It may be a VPN or virtual adapter.");

        var gateway = await DiscoverGatewayAsync(local, ct).ConfigureAwait(false);
        if (gateway is null)
        {
            lines.Add("UPnP: no Internet Gateway Device answered. The router may have UPnP disabled.");
            lines.Add("Both players still need an open path; this tool cannot change the other player's router.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"UPnP: {gateway.ServiceType}");
        string? external = null;
        string? publicIp = null;
        var cgnat = false;
        try
        {
            external = await GetExternalAddressAsync(gateway, ct).ConfigureAwait(false);
            lines.Add($"Router external IP: {external ?? "unknown"}");
            publicIp = await GetPublicAddressAsync(ct).ConfigureAwait(false);
            lines.Add($"Public IP: {publicIp ?? "unknown"}");
            cgnat = external is not null && IPAddress.TryParse(external, out var externalAddress) && IsPrivateAddress(externalAddress);
            if (cgnat)
            {
                lines.Add("CGNAT warning: the router's WAN address is private, so this connection is behind another NAT.");
            }
            else if (external is not null && publicIp is not null && !external.Equals(publicIp, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("CGNAT warning: the router address differs from the public address. Port forwarding may not help.");
            }
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            lines.Add("Public address check: " + ex.Message);
        }

        if (cgnat)
        {
            lines.Add("Port mappings: skipped. Port forwarding on this router cannot be reached from the internet while it is behind CGNAT.");
            lines.Add("Use a relay/VPN or ask the ISP for a public IP. If the other player has a public path, let them host.");
            return string.Join(Environment.NewLine, lines);
        }

        var ok = 0;
        var failed = 0;
        var ports = new[] { 3074 }.Concat(Enumerable.Range(27014, 37)).ToArray();
        foreach (var protocol in new[] { "TCP", "UDP" })
        {
            foreach (var port in ports)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await AddPortMappingAsync(gateway, port, protocol, ct).ConfigureAwait(false);
                    ok++;
                }
                catch (Exception ex) when (IsNetworkError(ex))
                {
                    failed++;
                }
                if ((ok + failed) % 8 == 0)
                {
                    progress?.Report($"Mapping ports... {ok + failed}/{ports.Length * 2}");
                }
            }
        }

        lines.Add($"Port mappings: {ok} added, {failed} failed.");
        if (failed > 0)
        {
            lines.Add("If the count is low, UPnP may be disabled or the router may limit the number of mappings.");
        }
        lines.Add("Both players should run Check and Fix connection. This cannot open the other player's router.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ShortPath(string path)
    {
        try
        {
            var buffer = new StringBuilder(1024);
            return GetShortPathName(path, buffer, (uint)buffer.Capacity) > 0 ? buffer.ToString() : path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint size);

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 ||
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && b[1] == 168) ||
            (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||
            (b[0] == 169 && b[1] == 254) ||
            b[0] == 127;
    }
    private static bool IsNetworkError(Exception ex) =>
        ex is HttpRequestException or IOException or SocketException or TaskCanceledException or InvalidOperationException or System.Xml.XmlException or OperationCanceledException;

    private static async Task<string> AllowFirewall(string gameDir)
    {
        var game = GameExe(gameDir);
        if (!File.Exists(game)) throw new InvalidOperationException("iw5sp.exe was not found. Set the game folder first.");

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        AppendRule(script, "MW3 Mod Manager iw5sp in", "in", game);
        AppendRule(script, "MW3 Mod Manager iw5sp out", "out", game);
        script.AppendLine("echo OK");

        var result = RunElevatedCommand(script.ToString());
        if (!result.Contains("OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The firewall command did not finish. " + result.Trim());
        return "Firewall: allowed iw5sp.exe inbound and outbound.";
    }

    private static void AppendRule(StringBuilder script, string name, string direction, string program)
    {
        var safeName = name.Replace("\"", "\"\"");
        var safeProgram = ShortPath(program).Replace("\"", "\"\"");
        script.AppendLine($"netsh advfirewall firewall delete rule name=\"{safeName}\" >nul 2>&1");
        script.AppendLine($"netsh advfirewall firewall add rule name=\"{safeName}\" dir={direction} action=allow program=\"{safeProgram}\" enable=yes profile=any");
        script.AppendLine("if errorlevel 1 exit /b 1");
    }

    private static string RunElevatedCommand(string script)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MW3Mod");
        Directory.CreateDirectory(dir);
        var scriptPath = Path.Combine(dir, "network.cmd");
        var logPath = Path.Combine(dir, "network.log");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        if (File.Exists(logPath)) File.Delete(logPath);

        var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var info = new ProcessStartInfo
        {
            FileName = comspec,
            Arguments = $"/d /c \"\"{scriptPath}\" > \"{logPath}\" 2>&1\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows did not start the firewall helper.");
            process.WaitForExit();
            var output = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            if (process.ExitCode != 0) throw new InvalidOperationException("The firewall helper failed. " + output.Trim());
            return output;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("The firewall change was cancelled.");
        }
        finally
        {
            try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch (IOException) { }
        }
    }

    private sealed record UpnpGateway(string ServiceType, Uri ControlUrl, IPAddress LocalAddress);

    private static async Task<UpnpGateway?> DiscoverGatewayAsync(LocalNetwork local, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
            var request = Encoding.ASCII.GetBytes(
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 2\r\n" +
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n");
            await udp.SendAsync(request, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), ct).ConfigureAwait(false);
            try
            {
                await udp.SendAsync(request, new IPEndPoint(local.Gateway, 1900), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var locations = new List<string>();
            try
            {
                while (true)
                {
                    var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    var text = Encoding.ASCII.GetString(response.Buffer);
                    foreach (var line in text.Split("\r\n"))
                    {
                        if (!line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase)) continue;
                        var location = line[9..].Trim();
                        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && !locations.Contains(uri.ToString()))
                            locations.Add(uri.ToString());
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }

            foreach (var location in locations)
            {
                try
                {
                    var xmlText = await Http.GetStringAsync(location, ct).ConfigureAwait(false);
                    var doc = XDocument.Parse(xmlText);
                    var baseUri = new Uri(location);
                    var service = doc.Descendants()
                        .Where(x => x.Name.LocalName == "service")
                        .FirstOrDefault(x =>
                        {
                            var type = x.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? "";
                            return type.Contains("WANIPConnection", StringComparison.OrdinalIgnoreCase) ||
                                   type.Contains("WANPPPConnection", StringComparison.OrdinalIgnoreCase);
                        });
                    if (service is null) continue;
                    var typeName = service.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? "";
                    var control = service.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value;
                    if (string.IsNullOrWhiteSpace(control)) continue;
                    var controlUri = Uri.TryCreate(control, UriKind.Absolute, out var absolute)
                        ? absolute
                        : new Uri(baseUri, control);
                    return new UpnpGateway(typeName, controlUri, local.Address);
                }
                catch (Exception ex) when (IsNetworkError(ex))
                {
                }
            }
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
        }
        return null;
    }

    private static async Task<string?> GetExternalAddressAsync(UpnpGateway gateway, CancellationToken ct)
    {
        var body = await SoapAsync(gateway, "GetExternalIPAddress", "", ct).ConfigureAwait(false);
        return ValueOf(body, "NewExternalIPAddress");
    }

    private static async Task AddPortMappingAsync(UpnpGateway gateway, int port, string protocol, CancellationToken ct)
    {
        var args =
            "<NewRemoteHost></NewRemoteHost>" +
            $"<NewExternalPort>{port}</NewExternalPort>" +
            $"<NewProtocol>{protocol}</NewProtocol>" +
            $"<NewInternalPort>{port}</NewInternalPort>" +
            $"<NewInternalClient>{gateway.LocalAddress}</NewInternalClient>" +
            "<NewEnabled>1</NewEnabled>" +
            $"<NewPortMappingDescription>{ServiceDescription}</NewPortMappingDescription>" +
            "<NewLeaseDuration>0</NewLeaseDuration>";
        await SoapAsync(gateway, "AddPortMapping", args, ct).ConfigureAwait(false);
    }

    private static async Task<string> SoapAsync(UpnpGateway gateway, string action, string arguments, CancellationToken ct)
    {
        var body =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body>" +
            $"<u:{action} xmlns:u=\"{gateway.ServiceType}\">{arguments}</u:{action}>" +
            "</s:Body></s:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, gateway.ControlUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{gateway.ServiceType}#{action}\"");
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = ValueOf(text, "errorCode");
            var description = ValueOf(text, "errorDescription");
            throw new InvalidOperationException($"UPnP {action} failed ({response.StatusCode}{(code is null ? "" : " " + code)}{(description is null ? "" : ": " + description)}).");
        }
        return text;
    }

    private static string? ValueOf(string xml, string localName)
    {
        try
        {
            return XDocument.Parse(xml).Descendants()
                .FirstOrDefault(x => x.Name.LocalName == localName)?.Value;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static async Task<string?> GetPublicAddressAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync("https://api.ipify.org", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var text = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
            return IPAddress.TryParse(text, out var ip) ? ip.ToString() : null;
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            return null;
        }
    }
}
