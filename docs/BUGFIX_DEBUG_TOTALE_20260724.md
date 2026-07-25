# NovaSCM — Debug Totale 2026-07-24/25
## Versione: v2.5.0 | Commit HEAD: 506632f

**Repository:** https://github.com/ClaudioBecchis/NovaSCM  
**Stack:** Python 3 · Flask · SQLite (WAL) · C# WPF · .NET 9 · xUnit  
**Scope:** Client WPF, Server API, Agent C#, Agent Python, DeployScreen  
**Test:** 185/185 ✅ | Build: 0 errori, 0 warning

---

## Riepilogo esecutivo

Debug totale su tutti e quattro i componenti. Corretti **14 bug** in 12 task,
di cui 2 critici (sicurezza), 5 significativi, 7 minori. Nessuna regressione.

---

## Task completati

### Task 1 — Gauge RAM: memoria di sistema reale
**File:** `MainWindow.xaml.cs`  
**Problema:** `Environment.WorkingSet` mostrava la RAM usata dal processo NovaSCM,
non la RAM di sistema. Il gauge nella Dashboard era inutile per il monitoraggio.  
**Fix:** Sostituito con `ComputerInfo.TotalPhysicalMemory` / `AvailablePhysicalMemory`
(Microsoft.VisualBasic.Devices).  
**Commit:** `b269949`

---

### Task 2 — ApiCache thread-safe
**File:** `MainWindow.xaml.cs`  
**Problema:** `ApiCache` (dizionario in-memory usato da polling background + UI)
non aveva lock. Rischio `InvalidOperationException` su modifiche concorrenti.  
**Fix:** Aggiunto `private readonly object _lock = new()` e lock su tutti i metodi.  
**Commit:** `b269949`

---

### Task 3 — Guard anti-scan-concorrenti
**File:** `MainWindow.xaml.cs`  
**Problema:** Clic ripetuto su "Scansione" dalla Dashboard avviava scan concorrenti
che corrompevano `_netRows`.  
**Fix:** Flag `_scanning` con `try/finally` in `RunScanAsync()`.  
**Commit:** `b269949`

---

### Task 4 — DeployScreen: caso `Skip` mancante
**File:** `DeployScreen/MainWindow.xaml.cs`  
**Problema:** `ColorStepRow` non gestiva `StepStatus.Skip` → riga con colore/icona di default
errati durante i deploy con step condizionali.  
**Fix:** Aggiunto `case StepStatus.Skip:` con stile grigio e icona "⤼".  
**Commit:** `ab2a96e`

---

### Task 5 — Notifier: toast sovrapposti
**File:** `Notifier.cs`  
**Problema:** Toast multipli si sovrapponevano (stesso `Top`/`Left`), rendendo illeggibili
le notifiche successive alla prima.  
**Fix:** Lista statica `_open` + metodo `Restack()` che ricalcola la posizione verticale
di tutti i toast aperti su `Loaded`/`Closed`.  
**Commit:** `ab2a96e`

---

### Task 6 — Export CSV/HTML: escaping mancante
**File:** `MainWindow.xaml.cs`  
**Problema:**  
- CSV: valori non quotati RFC 4180 → file corrotto se il campo conteneva virgole/newline.  
- HTML report: `d.Ip` e `d.Mac` non passati per `HtmlEncode` → XSS potenziale.  
**Fix:**  
- `static string Csv(string v)` con doppio-apice per RFC 4180.  
- `HtmlEncode(d.Ip)` e `HtmlEncode(d.Mac)` nel report HTML.  
**Commit:** `ab2a96e`

---

### Task 7 — HardwareCollector: MAC/IP coerenti
**File:** `NovaSCMAgent/HardwareCollector.cs`  
**Problema:** Su macchine con più NIC (VPN, Docker, Hyper-V), `GetMac()` e `GetIp()`
potevano scegliere interfacce diverse → il server riceveva MAC/IP di NIC diverse.  
**Fix:** Metodo unico `PrimaryNic()` che preferisce la NIC con gateway IPv4 default;
entrambi `GetMac()` e `GetIp()` lo chiamano.  
**Commit:** `8404dc0`

---

### Task 8 — AgentConfig: logging errori visibile su servizio Windows
**File:** `NovaSCMAgent/AgentConfig.cs`  
**Problema:** Errori di boot loggati solo su `Console.Error` (invisibile quando l'agent
gira come servizio Windows).  
**Fix:** `LogLine()` scrive sia su `Console.Error` sia su `logs/bootstrap.log`.  
**Commit:** `8404dc0`

---

### Task 9 — DownloadExeAsync: timeout troppo corto
**File:** `NovaSCMApiService.cs`  
**Problema:** `DownloadExeAsync` usava `_http` con timeout 12s — troppo corto per
scaricare binari su LAN lenta o file grandi. Il self-update dell'agent falliva silenziosamente.  
**Fix:** `private static readonly HttpClient _dlHttp = new() { Timeout = TimeSpan.FromMinutes(5) }`.  
**Commit:** `8404dc0`

---

### Task 10 — `.deploy_key`: mai cancellato dopo l'uso
**File:** `DeployScreen/MainWindow.xaml.cs`  
**Problema:** `Worker.cs` scriveva la API key in chiaro in `%ProgramData%\NovaSCM\.deploy_key`
e passava `keyfile=<path>` a DeployScreen. Tuttavia `Config.Parse` non gestiva `keyfile=`
(solo `key=`), quindi il file veniva scritto ma mai letto né cancellato.  
**Fix:** Aggiunto `case "keyfile":` in `Config.Parse`: legge il file, salva `c.ApiKey`,
cancella il file immediatamente. Aggiunto `using System.IO`.  
**Commit:** `2bcd232`

---

### Task 11 — TimeZone: interpolazione XML senza escaping
**File:** `MainWindow.xaml.cs`  
**Problema:** `{cfg.TimeZone}` in `BuildAutounattendXml` non era wrappato in `Xe()`.
Un valore importato da XML con caratteri speciali (`<`, `>`, `&`) avrebbe corrotto
il file `autounattend.xml` generato.  
**Fix:** `<TimeZone>{Xe(cfg.TimeZone)}</TimeZone>`.  
**Commit:** `506632f`

---

### Task 12 — Vari leak e bug minori

**File:** `MainWindow.xaml.cs`, `DeviceDetailWindow.xaml.cs`  
**Commit:** `506632f`

| Sottobug | Dettaglio | Fix |
|----------|-----------|-----|
| `_monitorCts` leak | CTS mai disposed prima di reassegnazione/stop | `_monitorCts?.Dispose()` su stop + riassegnazione |
| OU djoin injection | OU con `"` rompeva il comando PowerShell generato | Escape backtick-quote: `ou.Replace("\"", "\`\"")` |
| Deadlock pipe stdout/stderr | `ReadToEnd()` su stdout → blocca se stderr riempie il buffer | `ReadToEndAsync()` concorrente in `Task.Run(async ...)` |
| scp orphan su timeout | `WaitForExit(30s)` ignora il valore di ritorno; `ExitCode` potrebbe lanciare | `if (!proc.WaitForExit(30_000)) { proc.Kill(); throw... }` |
| `DeviceDetailWindow._cts` leak | CTS non disposed su `Closed` | `_cts?.Cancel(); _cts?.Dispose()` |
| `ArcPath` dead code | Chiamata `ArcPath(...)` + 3 variabili calcolate e mai usate ogni gauge tick | Rimosso blocco inutilizzato |

---

## Bug di sicurezza (sessioni precedenti, inclusi per completezza)

### fix(sec): IDOR cross-PC — token scoped non legato al PC
**Commit:** `015be02`  
Un deploy_token per PC-A poteva chiamare endpoint di PC-B.  
Fix: `_SCOPED_PC_BINDING` + `_resolve_request_pc_name()` + `_check_scoped_pc_binding()`.

### fix(sec): password admin hardcoded nel repo pubblico
**Commit:** `e66b55d`  
`"Polaris2026!"` hardcoded in `api.py`. Sostituito con `_resolve_admin_pass(d)` che
genera e persiste una password casuale per ogni CR.

---

## Stato finale

| Componente | Stato |
|------------|-------|
| `server/api.py` | ✅ 185/185 test, 0 warning |
| `PolarisManager.csproj` | ✅ 0 errori, 0 warning |
| `NovaSCMAgent.csproj` | ✅ 0 errori, 0 warning |
| `NovaSCMDeployScreen.csproj` | ✅ 0 errori, 0 warning |
| CI GitHub Actions | ✅ pytest + dotnet build (windows-latest) |
