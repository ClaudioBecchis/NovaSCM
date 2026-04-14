using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NovaSCM.Services;

/// <summary>
/// Servizio per tool di rete: ping, WoL, ARP, traceroute, port scan.
/// Estrae la business logic da MainWindow (Tab Network + PC).
/// </summary>
public static class NetworkToolsService
{
    /// <summary>Invia ICMP ping e ritorna latenza in ms (-1 se fallito).</summary>
    public static async Task<long> PingAsync(string host, int timeoutMs = 2000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Verifica se una porta TCP è aperta.</summary>
    public static async Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs = 500)
    {
        try
        {
            using var tcp = new TcpClient();
            var task = tcp.ConnectAsync(host, port);
            return await Task.WhenAny(task, Task.Delay(timeoutMs)) == task && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Invia Wake-on-LAN magic packet.</summary>
    public static async Task SendWolAsync(string macAddress, string broadcastIp = "255.255.255.255")
    {
        var mac = macAddress.Replace(":", "").Replace("-", "");
        if (mac.Length != 12) throw new ArgumentException("MAC non valido");

        var macBytes = Enumerable.Range(0, 6)
            .Select(i => Convert.ToByte(mac.Substring(i * 2, 2), 16))
            .ToArray();

        var packet = Enumerable.Repeat((byte)0xFF, 6)
            .Concat(Enumerable.Repeat(macBytes, 16).SelectMany(b => b))
            .ToArray();

        using var client = new UdpClient();
        client.EnableBroadcast = true;
        await client.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Parse(broadcastIp), 9));
    }

    /// <summary>Query ARP table per ottenere MAC da IP.</summary>
    public static string? GetMacFromArp(string ip)
    {
        try
        {
            var psi = new ProcessStartInfo("arp", $"-a {ip}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains(ip)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var mac = parts[1].Trim().Replace('-', ':').ToUpperInvariant();
                    if (mac.Length == 17) return mac;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Esegue traceroute e ritorna lista di hop.</summary>
    public static async Task<List<(int Hop, string Ip, long Ms)>> TracerouteAsync(
        string host, int maxHops = 30, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var results = new List<(int, string, long)>();
        using var ping = new Ping();

        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var options = new PingOptions(ttl, true);
                var reply = await ping.SendPingAsync(host, timeoutMs, new byte[32], options);
                var ip = reply.Address?.ToString() ?? "*";
                var ms = reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired
                    ? reply.RoundtripTime : -1;
                results.Add((ttl, ip, ms));

                if (reply.Status == IPStatus.Success) break;
            }
            catch
            {
                results.Add((ttl, "*", -1));
            }
        }
        return results;
    }
}
