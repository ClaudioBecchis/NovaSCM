using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PolarisManager;

class SoftwareRow
{
    public string Name      { get; set; } = "";
    public string Version   { get; set; } = "";
    public string Publisher { get; set; } = "";
}

class InventoryResult
{
    public string CpuName    { get; set; } = "";
    public int    CpuCores   { get; set; }
    public double CpuGhz     { get; set; }
    public double RamGb      { get; set; }
    public string OsName     { get; set; } = "";
    public string OsVersion  { get; set; } = "";
    public string LastBoot   { get; set; } = "";
    public string Disks      { get; set; } = "";
    public List<SoftwareRow> SoftwareList { get; set; } = [];
}

public partial class InventoryWindow : Window
{
    private readonly string _ip;
    private readonly string _adminUser;
    private readonly string _adminPass;
    private readonly ObservableCollection<SoftwareRow> _allSoftware = [];

    public InventoryWindow(string pcName, string ip, string adminUser, string adminPass)
    {
        InitializeComponent();
        _ip        = ip;
        _adminUser = adminUser;
        _adminPass = adminPass;

        TxtTitle.Text = $"📊  Inventario — {pcName}";
        TxtIp.Text    = ip;

        SoftwareGrid.ItemsSource = _allSoftware;
        Loaded += async (_, _) => await CollectAsync();
    }

    private static bool IsLocalIp(string ip)
        => NetworkInterface.GetAllNetworkInterfaces()
               .SelectMany(i => i.GetIPProperties().UnicastAddresses)
               .Any(a => a.Address.ToString() == ip);

    private async Task CollectAsync()
    {
        TxtStatus.Text          = "⏳  Raccolta dati in corso...";
        PanelSpinner.Visibility = Visibility.Visible;
        PanelContent.Visibility = Visibility.Collapsed;

        try
        {
            bool local  = IsLocalIp(_ip);
            var  result = await Task.Run(() => RunInventory(_ip, local, _adminUser, _adminPass));

            if (result == null)
            {
                TxtStatus.Text = "❌  Nessun dato — verifica che WinRM sia attivo e le credenziali siano corrette.";
                return;
            }

            TxtCpu.Text      = $"{result.CpuName}  ({result.CpuCores} core @ {result.CpuGhz:F1} GHz)";
            TxtRam.Text      = $"{result.RamGb:F1} GB";
            TxtOs.Text       = $"{result.OsName}  ({result.OsVersion})";
            TxtLastBoot.Text = result.LastBoot;
            TxtDisks.Text    = result.Disks;
            TxtSwCount.Text  = $"{result.SoftwareList.Count} software installati";

            foreach (var sw in result.SoftwareList)
                _allSoftware.Add(sw);

            PanelSpinner.Visibility = Visibility.Collapsed;
            PanelContent.Visibility = Visibility.Visible;
            TxtStatus.Text = $"✅  Completato — {result.SoftwareList.Count} software trovati";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"❌  Errore: {ex.Message}";
            App.Log($"[Inventory] {_ip} — {ex}");
        }
    }

    private static InventoryResult? RunInventory(string ip, bool local, string user, string pass)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"novascm_inv_{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(tmp, BuildScript(ip, local, user, pass));

            var psi = new ProcessStartInfo("powershell.exe",
                $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{tmp}\"")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var proc   = Process.Start(psi)!;
            var       stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(120_000);

            App.Log($"[Inventory] {ip} — exit={proc.ExitCode}, out={stdout.Length}ch");
            if (string.IsNullOrWhiteSpace(stdout)) return null;

            // Il JSON è il primo { ... } nell'output — LastIndexOf sbagliava finendo dentro l'array Software
            var start = stdout.IndexOf('{');
            var end   = stdout.LastIndexOf('}');
            if (start < 0 || end < start) return null;

            return ParseResult(stdout[start..(end + 1)]);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static InventoryResult ParseResult(string json)
    {
        var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new InventoryResult
        {
            CpuName   = Str(root, "CpuName"),
            CpuCores  = root.TryGetProperty("CpuCores",  out var c) ? c.GetInt32()  : 0,
            CpuGhz    = root.TryGetProperty("CpuGhz",    out var g) ? g.GetDouble() : 0,
            RamGb     = root.TryGetProperty("RamGb",     out var r) ? r.GetDouble() : 0,
            OsName    = Str(root, "OsName"),
            OsVersion = Str(root, "OsVersion"),
            LastBoot  = Str(root, "LastBoot"),
            Disks     = Str(root, "Disks"),
        };

        if (root.TryGetProperty("Software", out var swProp) &&
            swProp.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in swProp.EnumerateArray())
            {
                var name = (el.TryGetProperty("N", out var n) ? n.GetString() : null)?.Trim() ?? "";
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    result.SoftwareList.Add(new SoftwareRow
                    {
                        Name      = name,
                        Version   = (el.TryGetProperty("V", out var v) ? v.GetString() : null)?.Trim() ?? "",
                        Publisher = (el.TryGetProperty("P", out var p) ? p.GetString() : null)?.Trim() ?? "",
                    });
            }
        }

        return result;
    }

    private static string Str(JsonElement root, string key)
        => root.TryGetProperty(key, out var p) ? p.GetString()?.Trim() ?? "" : "";

    // ── Script PowerShell ──────────────────────────────────────────────────────
    private static string BuildScript(string ip, bool local, string user, string pass)
    {
        // Scritto come file .ps1 temporaneo → nessun problema di escape inline
        const string gather = @"
$cpu   = Get-WmiObject Win32_Processor | Select-Object -First 1
$cs    = Get-WmiObject Win32_ComputerSystem
$os    = Get-WmiObject Win32_OperatingSystem
$disks = Get-WmiObject Win32_LogicalDisk -Filter 'DriveType=3'

$sw  = @()
$sw += Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' `
         -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName } |
         Select-Object DisplayName, DisplayVersion, Publisher
$sw += Get-ItemProperty 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' `
         -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName } |
         Select-Object DisplayName, DisplayVersion, Publisher

$boot  = try { $os.ConvertToDateTime($os.LastBootUpTime).ToString('yyyy-MM-dd HH:mm') } catch { $os.LastBootUpTime }
$dText = ($disks | ForEach-Object { ('{0} {1:N0} GB (libero: {2:N0} GB)' -f $_.DeviceID,($_.Size/1GB),($_.FreeSpace/1GB)) }) -join '; '
$swArr = $sw | Sort-Object DisplayName | ForEach-Object {
    @{ N = [string]$_.DisplayName; V = [string]$_.DisplayVersion; P = [string]$_.Publisher }
}

[PSCustomObject]@{
    CpuName   = $cpu.Name.Trim()
    CpuCores  = [int]$cpu.NumberOfCores
    CpuGhz    = [math]::Round($cpu.MaxClockSpeed / 1000.0, 1)
    RamGb     = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    OsName    = [string]$os.Caption
    OsVersion = [string]$os.Version
    LastBoot  = $boot
    Disks     = $dText
    Software  = $swArr
} | ConvertTo-Json -Depth 3 -Compress
";
        if (local) return gather;

        // Remoto: wrappa in Invoke-Command con credenziali
        var safePass = pass.Replace("'", "''");
        return $@"
$secPass = ConvertTo-SecureString '{safePass}' -AsPlainText -Force
$cred    = New-Object System.Management.Automation.PSCredential('{user}', $secPass)
Invoke-Command -ComputerName {ip} -Credential $cred -ScriptBlock {{
{gather}
}} | ConvertTo-Json -Depth 3 -Compress
";
    }

    // ── UI ────────────────────────────────────────────────────────────────────
    private void TxtSearch_TextChanged(object s, TextChangedEventArgs e)
    {
        var filter = TxtSearch.Text.ToLowerInvariant();
        SoftwareGrid.ItemsSource = string.IsNullOrEmpty(filter)
            ? _allSoftware
            : (System.Collections.IEnumerable)_allSoftware
                .Where(sw => sw.Name.ToLowerInvariant().Contains(filter) ||
                             sw.Publisher.ToLowerInvariant().Contains(filter))
                .ToList();
    }

    private void BtnClose_Click(object s, RoutedEventArgs e) => Close();
}
