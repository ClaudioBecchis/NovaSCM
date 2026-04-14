using System.Net.Http;
using System.Windows.Input;
using NovaSCM.Commands;
using NovaSCM.Services;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel per il tab Impostazioni — config persistente con DPAPI.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private Dictionary<string, string> _config = new();

    // ── Certportal ──
    private string _certportalUrl = "";
    public string CertportalUrl { get => _certportalUrl; set => SetProperty(ref _certportalUrl, value); }

    // ── UniFi ──
    private string _unifiUrl = "";
    public string UnifiUrl { get => _unifiUrl; set => SetProperty(ref _unifiUrl, value); }

    private string _unifiUser = "";
    public string UnifiUser { get => _unifiUser; set => SetProperty(ref _unifiUser, value); }

    private string _unifiPass = "";
    public string UnifiPass { get => _unifiPass; set => SetProperty(ref _unifiPass, value); }

    // ── WiFi ──
    private string _wifiSsid = "";
    public string WifiSsid { get => _wifiSsid; set => SetProperty(ref _wifiSsid, value); }

    private string _radiusIp = "";
    public string RadiusIp { get => _radiusIp; set => SetProperty(ref _radiusIp, value); }

    private string _certValidityDays = "365";
    public string CertValidityDays { get => _certValidityDays; set => SetProperty(ref _certValidityDays, value); }

    // ── Organization ──
    private string _orgName = "";
    public string OrgName { get => _orgName; set => SetProperty(ref _orgName, value); }

    private string _orgDomain = "";
    public string OrgDomain { get => _orgDomain; set => SetProperty(ref _orgDomain, value); }

    // ── Network Scan ──
    private string _scanSubnets = "";
    public string ScanSubnets { get => _scanSubnets; set => SetProperty(ref _scanSubnets, value); }

    // ── NovaSCM API ──
    private string _apiUrl = "";
    public string ApiUrl { get => _apiUrl; set => SetProperty(ref _apiUrl, value); }

    private string _apiKey = "";
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }

    // ── Admin ──
    private string _adminUser = "";
    public string AdminUser { get => _adminUser; set => SetProperty(ref _adminUser, value); }

    private string _adminPass = "";
    public string AdminPass { get => _adminPass; set => SetProperty(ref _adminPass, value); }

    // ── Status ──
    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    // ── Comandi ──
    public ICommand SaveCommand => new RelayCommand(Save);
    public ICommand TestConnectionCommand => new AsyncRelayCommand(TestConnectionAsync);

    public void Load()
    {
        _config = ConfigService.Load();
        CertportalUrl    = Get("CertportalUrl");
        UnifiUrl         = Get("UnifiUrl");
        UnifiUser        = Get("UnifiUser");
        UnifiPass        = ConfigService.Decrypt(GetEncrypted("UnifiPass", "UnifiPassE"));
        // Vecchio formato: "Ssid" → nuovo: "WifiSsid"
        WifiSsid         = Get("WifiSsid", Get("Ssid"));
        RadiusIp         = Get("RadiusIp");
        // Vecchio formato: "CertDays" → nuovo: "CertValidityDays"
        CertValidityDays = Get("CertValidityDays", Get("CertDays", "365"));
        OrgName          = Get("OrgName");
        // Vecchio formato: "Domain" → nuovo: "OrgDomain"
        OrgDomain        = Get("OrgDomain", Get("Domain"));
        // Vecchio formato: "ScanNetworks" → nuovo: "ScanSubnets"
        ScanSubnets      = Get("ScanSubnets", Get("ScanNetworks"));
        ApiUrl           = Get("NovaSCMApiUrl");
        ApiKey           = ConfigService.Decrypt(GetEncrypted("NovaSCMApiKey", "NovaSCMApiKeyE"));
        AdminUser        = Get("AdminUser");
        AdminPass        = ConfigService.Decrypt(GetEncrypted("AdminPass", "AdminPassE"));
    }

    private string Get(string key, string def = "") =>
        _config.GetValueOrDefault(key, def);

    /// <summary>
    /// Legge il campo cifrato: prima prova la chiave "nuova" (valore già cifrato),
    /// poi la chiave "vecchia" con suffisso E (formato AppConfig pre-MVVM).
    /// </summary>
    private string GetEncrypted(string newKey, string legacyKeyE)
    {
        var v = _config.GetValueOrDefault(newKey, "");
        if (!string.IsNullOrEmpty(v)) return v;
        return _config.GetValueOrDefault(legacyKeyE, "");
    }

    public void Save()
    {
        _config["CertportalUrl"] = CertportalUrl;
        _config["UnifiUrl"] = UnifiUrl;
        _config["UnifiUser"] = UnifiUser;
        _config["UnifiPass"] = ConfigService.Encrypt(UnifiPass);
        _config["WifiSsid"] = WifiSsid;
        _config["RadiusIp"] = RadiusIp;
        _config["CertValidityDays"] = CertValidityDays;
        _config["OrgName"] = OrgName;
        _config["OrgDomain"] = OrgDomain;
        _config["ScanSubnets"] = ScanSubnets;
        _config["NovaSCMApiUrl"] = ApiUrl;
        _config["NovaSCMApiKey"] = ConfigService.Encrypt(ApiKey);
        _config["AdminUser"] = AdminUser;
        _config["AdminPass"] = ConfigService.Encrypt(AdminPass);

        ConfigService.Save(_config);
        StatusMessage = "Impostazioni salvate";
    }

    public async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            StatusMessage = "URL API non configurato";
            return;
        }
        StatusMessage = "Test connessione...";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync($"{ApiUrl.TrimEnd('/')}/health");
            StatusMessage = resp.IsSuccessStatusCode
                ? "Connessione OK"
                : $"Errore: HTTP {(int)resp.StatusCode}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
        }
    }
}
