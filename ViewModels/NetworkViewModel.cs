using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Input;
using NovaSCM.Commands;
using NovaSCM.Services;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel per il tab Network — scansione rete, device detection, vendor lookup.
/// La parte di visualizzazione (radar, heatmap, map) resta nel code-behind per Canvas access.
/// </summary>
public class NetworkViewModel : ViewModelBase
{
    // ── Collections ──
    public ObservableCollection<DeviceRow> Devices { get; } = new();
    public ObservableCollection<double> PingHistory { get; } = new();

    // ── Scan State ──
    private bool _isScanning;
    public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }

    private int _scanProgress;
    public int ScanProgress { get => _scanProgress; set => SetProperty(ref _scanProgress, value); }

    private int _scanTotal;
    public int ScanTotal { get => _scanTotal; set => SetProperty(ref _scanTotal, value); }

    private int _devicesFound;
    public int DevicesFound { get => _devicesFound; set => SetProperty(ref _devicesFound, value); }

    private string _scanStatus = "Pronto";
    public string ScanStatus { get => _scanStatus; set => SetProperty(ref _scanStatus, value); }

    // ── Selected ──
    private DeviceRow? _selectedDevice;
    public DeviceRow? SelectedDevice { get => _selectedDevice; set => SetProperty(ref _selectedDevice, value); }

    private bool _isPinging;
    public bool IsPinging { get => _isPinging; set => SetProperty(ref _isPinging, value); }

    // ── Comandi ──
    public ICommand StopScanCommand => new RelayCommand(StopScan, () => IsScanning);

    private CancellationTokenSource? _scanCts;

    public void StopScan()
    {
        _scanCts?.Cancel();
        IsScanning = false;
        ScanStatus = "Scansione annullata";
    }

    /// <summary>
    /// Scansiona una subnet CIDR. Callback per ogni device trovato (per aggiornare UI/Canvas).
    /// </summary>
    public async Task ScanSubnetAsync(string cidr, Func<DeviceRow, Task>? onDeviceFound = null,
        int concurrency = 50, CancellationToken ct = default)
    {
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _scanCts.Token;
        IsScanning = true;
        DevicesFound = 0;
        Devices.Clear();

        // Parse CIDR
        if (!TryParseCidr(cidr, out var baseIp, out var maskBits))
        {
            ScanStatus = "CIDR non valido";
            IsScanning = false;
            return;
        }

        var hostCount = (int)Math.Pow(2, 32 - maskBits) - 2;
        ScanTotal = hostCount;
        ScanProgress = 0;
        ScanStatus = $"Scansione {cidr} — 0/{hostCount}";

        var baseBytes = baseIp.GetAddressBytes();
        var baseInt = (uint)(baseBytes[0] << 24 | baseBytes[1] << 16 | baseBytes[2] << 8 | baseBytes[3]);
        var semaphore = new SemaphoreSlim(concurrency);

        var tasks = Enumerable.Range(1, hostCount).Select(async i =>
        {
            await semaphore.WaitAsync(token);
            try
            {
                var ipInt = baseInt + (uint)i;
                var ip = new IPAddress(new[]
                {
                    (byte)(ipInt >> 24), (byte)(ipInt >> 16),
                    (byte)(ipInt >> 8), (byte)ipInt
                });

                var ms = await NetworkToolsService.PingAsync(ip.ToString(), 1500);
                Interlocked.Increment(ref _scanProgress);
                ScanProgress = _scanProgress;

                if (ms >= 0)
                {
                    var mac = NetworkToolsService.GetMacFromArp(ip.ToString()) ?? "—";
                    var device = new DeviceRow
                    {
                        Ip = ip.ToString(),
                        Mac = mac,
                        PingMs = ms,
                        Status = "Online",
                    };
                    Interlocked.Increment(ref _devicesFound);
                    DevicesFound = _devicesFound;

                    if (onDeviceFound != null)
                        await onDeviceFound(device);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally { semaphore.Release(); }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }

        IsScanning = false;
        ScanStatus = $"Completato — {DevicesFound} device trovati";
    }

    private static bool TryParseCidr(string cidr, out IPAddress ip, out int bits)
    {
        ip = IPAddress.None;
        bits = 24;
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var parsed)) return false;
        if (!int.TryParse(parts[1], out bits) || bits < 8 || bits > 30) return false;
        ip = parsed;
        return true;
    }
}

/// <summary>Riga device nella griglia di rete.</summary>
public class DeviceRow : ViewModelBase
{
    private string _ip = "";
    public string Ip { get => _ip; set => SetProperty(ref _ip, value); }

    private string _mac = "—";
    public string Mac { get => _mac; set => SetProperty(ref _mac, value); }

    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _vendor = "";
    public string Vendor { get => _vendor; set => SetProperty(ref _vendor, value); }

    private string _icon = "💻";
    public string Icon { get => _icon; set => SetProperty(ref _icon, value); }

    private string _deviceType = "";
    public string DeviceType { get => _deviceType; set => SetProperty(ref _deviceType, value); }

    private string _connectionType = "";
    public string ConnectionType { get => _connectionType; set => SetProperty(ref _connectionType, value); }

    private string _status = "Offline";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private string _certStatus = "";
    public string CertStatus { get => _certStatus; set => SetProperty(ref _certStatus, value); }

    private long _pingMs = -1;
    public long PingMs { get => _pingMs; set => SetProperty(ref _pingMs, value); }

    private string _notes = "";
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    private string _lastSeen = "";
    public string LastSeen { get => _lastSeen; set => SetProperty(ref _lastSeen, value); }
}
