using System.Diagnostics;
using System.Text.Json.Nodes;

namespace NovaSCMAgent;

public record StepResult(bool? Ok, string Output);  // Ok=null → skipped

public class StepExecutor
{
    private readonly ILogger<StepExecutor> _log;
    private static readonly bool IsWindows = OperatingSystem.IsWindows();
    private static readonly bool IsLinux   = OperatingSystem.IsLinux();
    private const int StepTimeoutSec = 600;

    public StepExecutor(ILogger<StepExecutor> log) => _log = log;

    public async Task<StepResult> ExecuteAsync(JsonObject step, CancellationToken ct)
    {
        var tipo      = step["tipo"]?.GetValue<string>() ?? "";
        var platform  = step["platform"]?.GetValue<string>() ?? "all";
        var myOs      = IsWindows ? "windows" : "linux";
        var parametri = step["parametri"]?.GetValue<string>() ?? "{}";
        var p         = JsonNode.Parse(parametri)?.AsObject() ?? new JsonObject();

        // Skip se step non è per questa piattaforma
        if (platform != "all" && platform != myOs)
        {
            _log.LogInformation("  SKIP platform={Plat} (sono {Os})", platform, myOs);
            return new(null, $"Skipped: platform={platform}");
        }

        _log.LogInformation("  Tipo={Tipo} Platform={Plat}", tipo, platform);

        return tipo switch
        {
            "winget_install"  => await WingetInstall(p, ct),
            "apt_install"     => await AptInstall(p, ct),
            "snap_install"    => await SnapInstall(p, ct),
            "ps_script"       => await PsScript(p, ct),
            "shell_script"    => await ShellScript(p, ct),
            "reg_set"         => await RegSet(p, ct),
            "systemd_service" => await SystemdService(p, ct),
            "file_copy"       => await FileCopy(p, ct),
            "windows_update"  => await WindowsUpdate(p, ct),
            "reboot"          => Reboot(p),
            "message"         => Message(p),
            _                 => new(false, $"Tipo sconosciuto: {tipo}")
        };
    }

    // ── Implementazioni ───────────────────────────────────────────────────────

    private async Task<StepResult> WingetInstall(JsonObject p, CancellationToken ct)
    {
        var id = p["id"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(id)) return new(false, "Parametro 'id' mancante");
        // BUG-02: lista argomenti — nessuna interpolazione shell
        return await RunArgs(["winget", "install", "--id", id,
            "--silent", "--accept-package-agreements", "--accept-source-agreements"], ct: ct);
    }

    private async Task<StepResult> AptInstall(JsonObject p, CancellationToken ct)
    {
        var pkg = p["package"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(pkg)) return new(false, "Parametro 'package' mancante");
        // SEC: ArgumentList — nessuna interpolazione shell con pkg controllato dall'API
        return await RunArgs(["apt-get", "install", "-y", pkg],
            env: new() { ["DEBIAN_FRONTEND"] = "noninteractive" }, ct: ct);
    }

    private async Task<StepResult> SnapInstall(JsonObject p, CancellationToken ct)
    {
        var pkg     = p["package"]?.GetValue<string>() ?? "";
        var classic = p["classic"]?.GetValue<bool>() == true;
        if (string.IsNullOrEmpty(pkg)) return new(false, "Parametro 'package' mancante");
        // SEC: ArgumentList — nessuna interpolazione shell con pkg controllato dall'API
        var argv = classic
            ? new[] { "snap", "install", pkg, "--classic" }
            : new[] { "snap", "install", pkg };
        return await RunArgs(argv, ct: ct);
    }

    private async Task<StepResult> PsScript(JsonObject p, CancellationToken ct)
    {
        var script = p["script"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(script)) return new(false, "Parametro 'script' mancante");
        // BUG-02: script passato come argomento separato, nessuna interpolazione
        var exe = IsWindows ? "powershell.exe" : "pwsh";
        return await RunArgs([exe, "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script], ct: ct);
    }

    private async Task<StepResult> ShellScript(JsonObject p, CancellationToken ct)
    {
        var script = p["script"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(script)) return new(false, "Parametro 'script' mancante");
        // BUG-02: lista argomenti — script non interpolato nella stringa del processo
        return IsWindows
            ? await RunArgs(["cmd.exe",  "/c",   script], ct: ct)
            : await RunArgs(["bash",     "-c",   script], ct: ct);
    }

    private static readonly HashSet<string> _validRegTypes =
        ["REG_SZ", "REG_DWORD", "REG_QWORD", "REG_BINARY", "REG_EXPAND_SZ", "REG_MULTI_SZ"];

    private async Task<StepResult> RegSet(JsonObject p, CancellationToken ct)
    {
        if (!IsWindows) return new(null, "Skipped: reg_set è solo Windows");
        var path  = p["path"]?.GetValue<string>()  ?? "";
        var name  = p["name"]?.GetValue<string>()  ?? "";
        var value = p["value"]?.GetValue<string>() ?? "";
        var type  = p["type"]?.GetValue<string>()  ?? "REG_SZ";
        // BUG-02: whitelist tipo + lista argomenti
        if (!_validRegTypes.Contains(type)) return new(false, $"Tipo registro non valido: {type}");
        return await RunArgs(["reg", "add", path, "/v", name, "/t", type, "/d", value, "/f"], ct: ct);
    }

    private static readonly HashSet<string> _validSystemdActions =
        ["start", "stop", "enable", "disable", "restart", "reload", "status"];

    private async Task<StepResult> SystemdService(JsonObject p, CancellationToken ct)
    {
        if (!IsLinux) return new(null, "Skipped: systemd_service è solo Linux");
        var name   = p["name"]?.GetValue<string>()   ?? "";
        var action = p["action"]?.GetValue<string>() ?? "start";
        if (string.IsNullOrEmpty(name)) return new(false, "Parametro 'name' mancante");
        // BUG-02: whitelist azione + lista argomenti
        if (!_validSystemdActions.Contains(action)) return new(false, $"Azione systemd non valida: {action}");
        return await RunArgs(["systemctl", action, name], ct: ct);
    }

    private Task<StepResult> FileCopy(JsonObject p, CancellationToken ct)
    {
        var src = p["src"]?.GetValue<string>() ?? "";
        var dst = p["dst"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst))
            return Task.FromResult(new StepResult(false, "Parametri 'src' e 'dst' obbligatori"));
        try
        {
            // SEC: File.Copy puro .NET — nessuna shell, nessun rischio injection su src/dst
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
            return Task.FromResult(new StepResult(true, $"Copiato: {src} → {dst}"));
        }
        catch (Exception ex) { return Task.FromResult(new StepResult(false, ex.Message)); }
    }

    private static readonly HashSet<string> _validWuCategories = ["all", "security", "critical"];

    private async Task<StepResult> WindowsUpdate(JsonObject p, CancellationToken ct)
    {
        if (!IsWindows) return new(null, "Skipped: windows_update è solo Windows");

        // Parametri:
        //   category: "all" | "security" | "critical" (default: all)
        //   exclude_drivers: true/false (default: false)
        //   reboot_after: true/false (default: false — gestito come step reboot separato)
        var category       = p["category"]?.GetValue<string>()       ?? "all";
        var excludeDrivers = p["exclude_drivers"]?.GetValue<bool>()   ?? false;
        var rebootAfter    = p["reboot_after"]?.GetValue<bool>()      ?? false;
        // SEC: whitelist — category viene interpolato nel PS script
        if (!_validWuCategories.Contains(category))
            return new(false, $"Categoria aggiornamenti non valida: {category}");

        // Script PowerShell che usa PSWindowsUpdate
        // Installa il modulo se mancante, poi scarica e installa gli aggiornamenti
        var script = $$"""
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

            # Installa PSWindowsUpdate se mancante
            if (-not (Get-Module -ListAvailable -Name PSWindowsUpdate)) {
                Write-Output 'Installazione modulo PSWindowsUpdate...'
                Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope AllUsers | Out-Null
                Install-Module PSWindowsUpdate -Force -Scope AllUsers -AllowClobber -SkipPublisherCheck
            }
            Import-Module PSWindowsUpdate -Force

            # Costruisce filtro categoria
            $criteria = 'IsInstalled=0'
            {{(category == "security" ? "\"$criteria += ' AND CategoryIDs contains ''36FC9E60-2D2C-4A0B-A0A0-25D4B0C4B8B3'''" : "")}}
            {{(category == "critical" ? "\"$criteria += ' AND AutoSelectOnWebSites=1'" : "")}}

            Write-Output "Ricerca aggiornamenti (categoria: {{category}})..."
            $updates = Get-WindowsUpdate -AcceptAll {{(excludeDrivers ? "-NotCategory 'Drivers'" : "")}} -Verbose 2>&1
            $count = ($updates | Measure-Object).Count
            Write-Output "Aggiornamenti disponibili: $count"

            if ($count -eq 0) {
                Write-Output 'Sistema aggiornato — nessun aggiornamento da installare'
                exit 0
            }

            Write-Output 'Installazione aggiornamenti in corso...'
            $result = Install-WindowsUpdate -AcceptAll {{(excludeDrivers ? "-NotCategory 'Drivers'" : "")}} {{(rebootAfter ? "" : "-IgnoreReboot")}} -Verbose 2>&1
            $result | ForEach-Object { Write-Output $_ }

            $failed = $result | Where-Object { $_ -match 'failed|error' }
            if ($failed) { Write-Warning "Alcuni aggiornamenti falliti: $failed"; exit 1 }
            Write-Output "Installazione completata. $count aggiornamenti applicati."
            exit 0
        """;

        _log.LogInformation("  Windows Update — categoria={Cat} exclude_drivers={Ed}", category, excludeDrivers);
        // SEC: ArgumentList — script passato come argomento separato a -Command
        return await RunArgs(["powershell.exe", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                              "-Command", script], ct: ct);
    }

    private StepResult Reboot(JsonObject p)
    {
        var delay = p["delay"]?.GetValue<int>() ?? 5;
        _log.LogInformation("  Riavvio programmato tra {Delay}s", delay);
        // SEC: ArgumentList — delay è già int, ma allineato al pattern standard
        var psi = new ProcessStartInfo("shutdown") { UseShellExecute = false };
        if (IsWindows)
        {
            psi.ArgumentList.Add("/r");
            psi.ArgumentList.Add("/t");
            psi.ArgumentList.Add(delay.ToString());
        }
        else
        {
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add($"+{Math.Max(1, delay / 60)}");
        }
        Process.Start(psi);
        return new(true, $"Riavvio programmato tra {delay}s");
    }

    private StepResult Message(JsonObject p)
    {
        var text = p["text"]?.GetValue<string>() ?? "";
        _log.LogInformation("  MSG: {Text}", text);
        return new(true, text);
    }

    // ── Runner comandi ────────────────────────────────────────────────────────

    // BUG-02: RunArgs usa ArgumentList — ogni argomento è passato senza shell, nessuna injection
    private async Task<StepResult> RunArgs(string[] argv, Dictionary<string, string>? env = null,
                                           CancellationToken ct = default)
    {
        if (argv.Length == 0) return new(false, "Nessun comando specificato");
        var psi = new ProcessStartInfo(argv[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        for (int i = 1; i < argv.Length; i++)
            psi.ArgumentList.Add(argv[i]);
        if (env != null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        try
        {
            using var proc = new Process { StartInfo = psi };
            var sbOut = new System.Text.StringBuilder();
            var sbErr = new System.Text.StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(StepTimeoutSec));
            await proc.WaitForExitAsync(cts.Token);
            var output = (sbOut.ToString() + sbErr.ToString()).Trim();
            return new(proc.ExitCode == 0, output);
        }
        catch (OperationCanceledException) { return new(false, $"Timeout dopo {StepTimeoutSec}s"); }
        catch (Exception ex)               { return new(false, ex.Message); }
    }

    private async Task<StepResult> Run(string cmd, string? args = null,
                                       Dictionary<string, string>? env = null,
                                       CancellationToken ct = default)
    {
        // Se args è null, usa shell
        string exe, arguments;
        if (args is null)
        {
            (exe, arguments) = IsWindows
                ? ("cmd.exe",  $"/c {cmd}")
                : ("bash",     $"-c \"{cmd.Replace("\"", "\\\"")}\"");
        }
        else { exe = cmd; arguments = args; }

        var psi = new ProcessStartInfo(exe, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        if (env != null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        try
        {
            using var proc = new Process { StartInfo = psi };
            var sbOut = new System.Text.StringBuilder();
            var sbErr = new System.Text.StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(StepTimeoutSec));
            await proc.WaitForExitAsync(cts.Token);

            var output = (sbOut.ToString() + sbErr.ToString()).Trim();
            return new(proc.ExitCode == 0, output);
        }
        catch (OperationCanceledException)
        {
            return new(false, $"Timeout dopo {StepTimeoutSec}s");
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }
}
