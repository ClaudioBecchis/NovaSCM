using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NovaSCMAgent;

public class HardwareData
{
    public string Cpu  { get; set; } = "";
    public string Ram  { get; set; } = "";
    public string Disk { get; set; } = "";
    public string Mac  { get; set; } = "";
    public string Ip   { get; set; } = "";
}

public static class HardwareCollector
{
    public static HardwareData Collect() => new()
    {
        Cpu  = GetCpu(),
        Ram  = GetRam(),
        Disk = GetDisk(),
        Mac  = GetMac(),
        Ip   = GetIp(),
    };

    private static string GetCpu()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var name = key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                var line = File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
                if (line != null) return line.Split(':')[1].Trim();
            }
            catch { }
        }
        return $"{Environment.ProcessorCount} core";
    }

    private static string GetRam()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref ms))
                    return $"{ms.ullTotalPhys / 1024.0 / 1024.0 / 1024.0:F0} GB";
            }
            catch { }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                var line = File.ReadLines("/proc/meminfo")
                    .FirstOrDefault(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
                if (line != null)
                {
                    var kb = long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                    return $"{kb / 1024 / 1024} GB";
                }
            }
            catch { }
        }
        return "N/A";
    }

    private static string GetDisk()
    {
        try
        {
            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
            if (drive != null)
                return $"{drive.TotalSize / 1024.0 / 1024.0 / 1024.0:F0} GB";
        }
        catch { }
        return "N/A";
    }

    private static string GetMac()
    {
        try
        {
            var ni = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    n.GetPhysicalAddress().GetAddressBytes().Length == 6);
            if (ni != null)
                return string.Join(":", ni.GetPhysicalAddress().GetAddressBytes()
                    .Select(b => b.ToString("X2")));
        }
        catch { }
        return "N/A";
    }

    private static string GetIp()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            var ip   = host.AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                  && !System.Net.IPAddress.IsLoopback(a));
            return ip?.ToString() ?? "N/A";
        }
        catch { return "N/A"; }
    }

    // ── Windows P/Invoke ──────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint  dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile,
                     ullAvailPageFile, ullTotalVirtual, ullAvailVirtual,
                     ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
