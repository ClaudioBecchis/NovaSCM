# Changelog

All notable changes to NovaSCM are documented here.

---

## [1.7.7] - 2026-03-09

### Nuove funzionalità

- **CI/CD**: aggiunto `.github/workflows/test.yml` — pytest + xUnit su ogni push a `main`
- **Secret bootstrap**: se `NOVASCM_API_KEY` non è impostata, il server genera automaticamente una chiave sicura e la salva in `/data/.api_key`
- **Rate limiter Redis**: `docker-compose.yml` aggiornato con servizio `redis:7-alpine` e `NOVASCM_RATE_LIMIT_STORAGE=redis://redis:6379/0`
- **Logging JSON**: supporto opzionale `python-json-logger` via `NOVASCM_LOG_JSON=1`
- **Paginazione steps**: `GET /api/cr/<id>/steps` supporta `?page=&per_page=` (default 100, max 500); risposta: `{"page","per_page","total","items"}`
- **Test .NET**: nuovo progetto xUnit `NovaSCMAgent.Tests/` con 8 test per `StepExecutor` e `Worker`
- **AGENT_VER dinamica**: `agent/novascm-agent.py` legge la versione da `agent/version.txt` invece di hardcoded
- **`requirements.txt`**: aggiunto `python-json-logger==3.3.0`

### Bug fix

- **`delete_cr`**: eliminazione figli (`cr_steps`) ora avviene prima del padre (`cr`) — ordine FK corretto
- **`update_status`**: verifica `rowcount` prima del commit; restituisce 404 senza fare UPDATE inutile se CR non esiste

### Test

- 79/79 ✅ (78 precedenti + 1 nuovo: `test_get_steps_pagination`)

---

## [1.7.6] - 2026-03-09

### Bug fix

- **M-1**: `agent/novascm-agent.py` — `run_workflow()` ora verifica `pw_id` salvato in `state.json` vs workflow corrente prima di usare `resume_step`; allineato a `Worker.cs` BUG-8 (v1.7.3)
- **M-2**: `NovaSCMAgent/StepExecutor.cs` — `WindowsUpdate()`: rimosso `$criteria` dead code; aggiunto `$catArgs` passato correttamente a `Get-WindowsUpdate` e `Install-WindowsUpdate` con `-Category`

### Miglioramenti

- **I-1**: `agent/install-windows.ps1` — aggiunto step 3 verifica SHA256 dopo download `NovaSCMAgent.exe`
- **I-2**: `agent/install-linux.sh` — aggiunto step 5 verifica SHA256 dopo download `novascm-agent.py`; rinumerati passi successivi (6→7, 7→8)

---

## [1.7.5] - 2026-03-09

### Bug fix

- **C-1**: `server/api.py` — rimosso `@require_auth` da `/health`; il Docker healthcheck chiamava l'endpoint senza API key → 401 → container in loop restart
- **M-1**: `server/tests/test_api.py` — aggiornato `TestHealth`: rimosso `test_health_includes_db_field` (campo rimosso in v1.7.4), aggiunto `test_health_does_not_expose_db_path`; corretta variabile spuria in `test_health_status_ok`; **78/78 test passing**
- **M-2**: `NovaSCMApiService.cs` — `DownloadExeAsync` usava ancora `EnsureSuccessStatusCode()`; allineato al pattern di `SendAsync` con messaggio di errore dettagliato
- **M-3**: `NovaSCMApiService.cs` — `HttpResponseMessage` in `SendAsync` ora wrapped in `using` (resource leak in caso di eccezione)

---

## [1.7.4] - 2026-03-09

### Sicurezza

- **C-7 (completo)**: `server/api.py` — script installer scaricano direttamente il binario agente, verificano SHA256 prima di eseguire; aggiunto endpoint `GET /api/download/agent.sha256`

### Bug fix

- **M-2**: `server/api.py` — `/health` non espone più il percorso del DB nella risposta (era `{"status":"ok","db":"/data/novascm.db"}`, ora solo `{"status":"ok"}`)
- **M-7 (completo)**: `server/api.py` — aggiunto indice mancante `idx_pc_workflows_status ON pc_workflows(status)` (ora totale: 4 indici)

### Miglioramenti

- **I-2**: `NovaSCMAgent/ApiClient.cs` — `AgentVer` letto da `Assembly.GetExecutingAssembly()` invece di costante hardcoded `"1.0.0"`

---

## [1.7.3] - 2026-03-09

### Sicurezza

- **SEC-1**: `server/api.py` — XML injection in `get_autounattend()`: tutti i valori interpolati nel XML (`pc_name`, `admin_pass`, `dc_ip`, `domain`, `join_user`, `join_pass`, `odj_blob`) ora passano per `xml.sax.saxutils.escape()`
- **SEC-2**: `server/api.py` — RCE tramite pipe-to-shell: `agent-install.ps1` non usa più `Invoke-Expression`; `agent-install.sh` non usa più `curl|bash`; entrambi scaricano in file temporaneo e lo eseguono direttamente
- **SEC-3**: `server/api.py` — endpoint `/health` protetto con `@require_auth` (evitava fingerprinting senza autenticazione)

### Bug fix

- **BUG-1**: `server/api.py` — `update_step()` non validava il campo `tipo`; aggiunta la stessa whitelist presente in `add_step()`
- **BUG-2**: `server/api.py` — `update_workflow()` accettava nome vuoto; aggiunto controllo obbligatorio
- **BUG-3**: `server/api.py` — `report_wf_step()`: workflow non si completava se l'ultimo step aveva status `error`; aggiunto `'error'` al conteggio degli step terminati
- **BUG-4**: `server/api.py` — `init_db()` catturava tutte le eccezioni di migrazione; ora loga i warning per errori non-`duplicate column name`
- **BUG-5**: `server/api.py` — aggiunti indici SQLite su `cr(pc_name)`, `pc_workflows(pc_name)`, `pc_workflow_steps(pc_workflow_id)` per query frequenti dell'agent
- **BUG-6**: `NovaSCMAgent/ApiClient.cs` — `SetApiKey()` modificava `DefaultRequestHeaders` non thread-safe; rimosso in favore di `BuildRequest()` che aggiunge `X-Api-Key` per ogni singola richiesta
- **BUG-7**: `NovaSCMAgent/Program.cs` — rimossa registrazione DI `AddSingleton<AgentConfig>()` mai usata (Worker usa i metodi statici `AgentConfig.Load()`)
- **BUG-8**: `NovaSCMAgent/Worker.cs` — resume dopo reboot non verificava che `PwId` salvato corrispondesse al workflow corrente; aggiunto controllo con reset automatico dello stato in caso di mismatch
- **BUG-9**: `NovaSCMApiService.cs` — `ApiBase` calcolato con `Replace("/api/cr", "")` fragile; ora usa `Uri.Authority` per estrarre l'origine
- **BUG-10**: `NovaSCMApiService.cs` — `EnsureSuccessStatusCode()` lanciava eccezione generica; sostituito con controllo manuale che include il body della risposta nel messaggio di errore

### Miglioramenti

- **INFO-1**: `server/api.py` — storage rate limiter configurabile via env `NOVASCM_RATE_LIMIT_STORAGE` (default `memory://`, supporta `redis://...` in produzione)
- **INFO-2**: `agent/novascm-agent.py` — `load_config()` rileggeva il file JSON a ogni ciclo di polling; aggiunto cache con controllo mtime (rilettura solo se il file cambia)

---

## [1.7.2] - 2026-03-09

### Fix agente/server/installer

- **FIX-1**: `install-windows.ps1` — variabile `$NssmExe` non inizializzata causava crash immediato con `ErrorActionPreference=Stop`; aggiunta `$NssmExe = "$AgentDir\nssm.exe"` prima del blocco `if (-not (Test-Path $NssmExe))`
- **FIX-2**: `install-windows.ps1` — IP homelab hardcoded `192.168.20.110` nel default di `$ApiUrl`; sostituito con placeholder `YOUR-SERVER-IP` coerente con `install-linux.sh`
- **FIX-3**: `install-windows.ps1` — `$PollSec` dichiarato `[string]` ma usato come `[int]` nel JSON; corretto in `[int]`
- **FIX-4**: `server/Dockerfile` — `VOLUME ["/data"]` dichiarato prima di `RUN adduser`; Docker montava il volume prima che l'utente `novascm` esistesse; spostato dopo il blocco `RUN`; `--workers=1` → `--workers=2`
- **FIX-5**: `server/docker-compose.yml` — API key hardcoded nel file versionato; sostituita con `${NOVASCM_API_KEY}` letta da `.env`; aggiunto `server/.env.example`; aggiornato `.gitignore`
- **FIX-6**: `agent/install-linux.sh` — servizio systemd girava come `root`; aggiunto `User=novascm`/`Group=novascm`, creazione utente di sistema dedicato, hardening (`ProtectSystem`, `ProtectHome`, `NoNewPrivileges`, `ReadWritePaths`)
- **FIX-7**: `installer/NovaSCM.iss` — tre percorsi assoluti della macchina sviluppatore rimossi; `SourceDir`, `SetupIconFile`, `OutputDir` ora relativi; commento versione aggiornato
- **FIX-8**: `server/requirements.txt` — dipendenze Flask/Gunicorn/flask-limiter con versioni fisse; Dockerfile usa `COPY requirements.txt` invece di `pip install` inline
- **FIX-9**: `server/docker-compose.yml` — aggiunti resource limits (`memory: 256m`, `cpus: 0.50`)
- **FIX-10**: `installer/NovaSCM-Setup.nsi` — versione `1.0.0` → `1.7.1`; `RMDir` → `RMDir /r` (evita directory non vuota in uninstall)
- **FIX-11**: `NovaSCMAgent/Worker.cs` — versione hardcoded `v1.0.0` nel log; sostituita con `Assembly.GetExecutingAssembly().GetName().Version`
- **FIX-12**: `NovaSCMAgent/Worker.cs` — check `wf["error"] is null` fragile; sostituito con `wf.ContainsKey("workflow_nome") && wf["workflow_nome"] != null`
- **FIX-13**: `win-x64.pubxml` — `PublishReadyToRun=true` + `EnableCompressionInSingleFile=true` si contraddicono (R2R decompresso a ogni avvio); scelto Opzione A: R2R disabilitato, compressione attiva, `DebugType=none`

### Deploy

- Tab Deploy: timer installazione con fasi in tempo reale (▶ fase attiva, ✅ completata)
- `autounattend.xml specialize`: aggiunto `bcdedit /set {fwbootmgr} displayorder {bootmgr} /addfirst` per garantire boot da disco dopo Phase 1
- Istruzioni post-generazione aggiornate con le 3 fasi e avviso riavvii multipli normali

### Pulizia repo

- Rimossi dal tracking Git: `publish_v2/`, `publish_release/`, `publish_small/`, zip binari (~259 MB)

---

## [1.7.1] - 2026-03-09

### Sicurezza

- **BUG-NEW**: Validazione IP/hostname in `BtnRunScriptOnPc_Click` — il valore di `TxtScriptTarget.Text` veniva inserito senza validazione nella stringa PowerShell `Invoke-Command -ComputerName '{ip}'`; aggiunta validazione con `IPAddress.TryParse` + regex `^[a-zA-Z0-9\-\.]+$`; input non conformi restituiscono errore immediato senza esecuzione

### Qualità

- **INFO**: Rimosso il costruttore legacy `ProcessStartInfo(exe, args)` in 3 punti: `explorer.exe` per apertura cartella (usa `FileName`+`Arguments`), `powershell.exe` in `BtnRunScriptOnPc_Click` e `BtnRunScriptLocal_Click` (usa `ArgumentList`); questo elimina il rischio di argument splitting su path con spazi

---

## [1.7.0] - 2026-03-09

### Sicurezza

- **SEC-06**: Path traversal in `file_copy` step — `src`/`dst` provenienti dall'API potevano contenere `../` per uscire dalla directory e sovrascrivere file arbitrari; fix in `NovaSCMAgent/StepExecutor.cs`: `Path.GetFullPath()` normalizza il path prima dell'uso + blocco esplicito di `..` e `\0`; stessa fix applicata a `agent/novascm-agent.py` (`os.path.normpath` + `os.path.abspath` + check `..`/null byte)

---

## [1.6.9] - 2026-03-09

### Bugfix

- **BUG**: Tab "🖥️ PC Gestiti" congelava l'UI — `new PerformanceCounter(...)` (blocca il thread UI per secondi al primo accesso WMI) e `NetworkInterface.GetAllNetworkInterfaces()` (operazione lenta chiamata ogni 1.5s sul thread UI) entrambi spostati su background thread (`Task.Run`); il gauge NET ora usa un valore cached aggiornato ogni ~30s in background invece di interrogare le interfacce di rete a ogni tick
- **UX**: Crash dialog apre automaticamente il browser su GitHub Issues al momento della comparsa (senza richiedere clic utente) — il report viene inviato anche se l'utente chiude il dialog o se non interagisce

---

## [1.6.8] - 2026-03-09

### Sicurezza

- **SEC-05**: Rimosso parametro `shell` da `run_cmd()` in `agent/novascm-agent.py` — il parametro `shell=shell` era passato direttamente a `subprocess.run()`, lasciando aperta la via per `shell=True`; ora `shell=False` è hardcodato nella funzione e non può essere sovrascritto; rimossi tutti gli `shell=False` espliciti dai caller (ridondanti)

---

## [1.6.7] - 2026-03-09

### Bugfix

- **BUG**: Dashboard bloccata/congelata — `RefreshDashboardAsync` usava `new HttpClient()` inline (ARCH-01) puntando alla root URL invece di `/api/cr`, causando una risposta inattesa che poteva bloccare il `Dispatcher.Invoke` sincrono; refactored: usa `_apiSvc.GetDashboardJsonAsync()` (endpoint corretto + chiave API + HttpClient condiviso), aggiornamento UI diretto senza `Dispatcher.Invoke`, try/catch esterno con logging

---

## [1.6.6] - 2026-03-09

### Bugfix

- **BUG**: Auto-update da tab About non installava la nuova versione — il codice scaricava l'installer Inno Setup (`NovaSCM-vX.X.X-Setup.exe`) ma tentava di copiarlo sopra l'exe corrente, poi lo rilanciava come app; ora rileva correttamente i file con "Setup" nel nome e li esegue con `/VERYSILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /SP- /SUPPRESSMSGBOXES` (Inno Setup silent install); il path portable (`.exe` senza "Setup") mantiene il vecchio approccio copy+bat
- **BUG**: `BtnCheckUpdate_Click` restituiva subito con messaggio di errore se l'URL API NovaSCM non era configurato — rimosso il guard errato (gli aggiornamenti provengono da GitHub, non dall'API)

---

## [1.6.5] - 2026-03-09

### Bugfix

- **BUG**: `WorkflowStepWindow` — `NullReferenceException` in `CmbTipo_SelectionChanged` alla riga 76 — l'evento scattava durante `InitializeComponent()` prima che `TxtParametri` fosse inizializzato; aggiunto guard `if (TxtParametri == null) return` (fix issue #5 e #6)

---

## [1.6.4] - 2026-03-09

### Qualità (v5 analysis report)

- **NEW-E (completo)**: Aggiunto `EnsureApiConfigured()` a tutti gli 11 handler rimanenti — `MenuCrDebug`, `UpdateCrStatusAsync`, `MenuCrDelete`, `MenuCrDownloadXml`, `MenuCrGenUsb`, `BtnWfAddStep`, `BtnWfEditStep`, `SwapStepOrdineAsync`, `BtnWfDeleteStep`, `BtnWfAssign`, `BtnWfDeleteAssign`
- **SEC-03b**: Rimossa bash process substitution nel fallback temperature Proxmox — `paste <(cat ...)` → `for z in /sys/class/thermal/...` (POSIX-compatibile, funziona con dash/ash/sh)

---

## [1.6.3] - 2026-03-09

### Sicurezza e qualità (v4 analysis report)

- **BUG-04**: Credenziali cifrate con DPAPI (`ProtectedData.Protect`, scope `CurrentUser`) — `UnifiPass`, `AdminPass`, `NovaSCMApiKey` non vengono più scritte in chiaro in `config.json`; retrocompatibilità automatica al primo salvataggio
- **SEC-03**: Eliminata argument injection residua in 3 chiamate `ssh.exe`/`scp` (Proxmox temp + Deploy PXE) — migrate da `.Arguments` f-string a `.ArgumentList`; `StrictHostKeyChecking` cambiato da `no` a `accept-new` (meno esposto a MITM)
- **NEW-E**: Aggiunto `EnsureApiConfigured()` null-guard su `BtnCrCreate_Click` — messaggio chiaro invece di `NullReferenceException` se l'URL API non è configurato
- **SEC-04**: Warning UI in `BtnDeployGenerate_Click` quando il join AD è attivo — avvisa che la password è scritta in chiaro nel `postinstall.ps1`
- **NEW-F**: Confermato — `_apiCache` già passato correttamente al costruttore di `NovaSCMApiService` in `LoadConfig()`

---

## [1.6.2] - 2026-03-08

### Security fix — UI (argument injection)

- **SEC-02**: Eliminata vulnerabilità argument injection in `MainWindow.xaml.cs` — 5 chiamate `ProcessStartInfo` usavano `.Arguments` con f-string, permettendo injection tramite IP/path controllati dall'utente o dalla rete
  - `GetMacFromArp`: `arp -a {ip}` → `ArgumentList.Add("-a"); ArgumentList.Add(ip)`
  - `OpenSshTerminal` (Windows Terminal): `wt.exe ssh {target}` → `ArgumentList`
  - `OpenSshTerminal` (cmd fallback): `cmd.exe /k ssh {target}` → `ArgumentList`
  - `BtnRdp_Click`: `mstsc /v:{p.Ip}` → `ArgumentList.Add($"/v:{p.Ip}")`
  - `BtnInstallUpdate_Click`: `cmd.exe /C "{batPath}"` → `ArgumentList`
- Fix segnalato da **Aikido Security** (PR #2) + 2 pattern aggiuntivi identificati manualmente

---

## [1.6.1] - 2026-03-08

### Security fix — agent (command injection)

- **SEC-01 (Critico)**: Eliminata vulnerabilità command injection in `agent/novascm-agent.py` — `run_cmd()` usava `shell=True` quando il comando era una stringa, permettendo l'esecuzione di comandi arbitrari tramite dati controllati dall'API
  - `run_cmd()`: default cambiato a `shell=False`, aggiunto parametro `env` opzionale
  - `winget_install`: da f-string a lista `["winget", "install", "--id", pkg_id, ...]`
  - `apt_install`: da f-string a `["apt-get", "install", "-y", pkg]` con `env={"DEBIAN_FRONTEND": "noninteractive"}`
  - `snap_install`: da f-string a `["snap", "install", pkg]` + `"--classic"` condizionale
  - `file_copy`: da `copy /Y` / `cp -f` shell a `shutil.copy2(src, dst)` puro Python
  - `reboot`: `delay` validato come intero, da f-string a lista argomenti tipizzata

## [1.6.0] - 2026-03-08

### Bugfix e refactoring (v3 analysis report)

- **BUG-05 / ARCH-01**: Eliminati tutti i `new HttpClient()` inline nelle sezioni CR e Workflow — tutto il traffico HTTP verso NovaSCM API ora passa attraverso `NovaSCMApiService` con `static readonly HttpClient` condiviso (fix socket exhaustion)
- **BUG-10 (Flask)**: `init_db()` riscritta con `with get_db_ctx() as conn:` — eliminati gli ultimi 2 `conn = get_db()` bare rimasti
- **NEW-3**: Validator SSL ulteriormente ristretto — ora accetta **solo** `UntrustedRoot` (self-signed), blocca correttamente certificati scaduti, revocati e hostname errati (precedente v1.5.0 accettava qualsiasi `RemoteCertificateChainErrors`)
- **NEW-B**: try/catch aggiunto a 3 ulteriori handler `async void`: `BtnScanAll_Click`, `BtnCheckUpdate_Click`, `LstWorkflows_SelectionChanged`
- **NEW-1**: `AppVersion` in `App.xaml.cs` aggiornato a `1.6.0` (era 1.5.0 dopo fix precedente)
- **CS Fix**: Corretti 8 errori di compilazione post-refactoring — variabili `resp` (tipo `string`) trattate erroneamente con `.IsSuccessStatusCode` e `.Content.ReadAsStringAsync()`

---

## [1.5.0] - 2026-03-08

### Sicurezza e qualità (v2 analysis report)

- **NEW-B**: try/catch aggiunto a 15 handler `async void` privi di protezione (BtnMonitor, BtnSccmRefresh, MainTabs_SelectionChanged, BtnCrRefresh, BtnWfStep*, BtnSpeedTest, BtnMdns, BtnRunScript*, BtnPveRefresh, ...)
- **BUG-10**: Tutte le 28 route Flask ora usano `with get_db_ctx() as conn:` — nessuna connessione SQLite lasciata aperta in caso di eccezione
- **NEW-D**: Validazione regex `_wingetIdRegex` per i package ID winget in `AddPackage()` e `CrAddPackage()` — previene injection nel postinstall.ps1
- **NEW-A / ARCH-01**: `_apiSvc` (NovaSCMApiService) ora istanziato in `LoadConfig()` e aggiornato dinamicamente — fine del dead code
- **ARCH-01 + SEC**: `NovaSCMApiService` invia ora l'header `X-Api-Key` su tutte le richieste
- **NEW-C**: Eliminato `DangerousAcceptAnyServerCertificateValidator` e `(_, _, _, _) => true` SSL bypass in 3 punti (UniFi, SCCM, Proxmox) — sostituito con validator che accetta self-signed ma verifica l'hostname
- **IMP-NEW-04**: Suite pytest Flask (78 test) in `server/tests/test_api.py` — copre health, auth, CR CRUD, Workflow CRUD, Settings, PcWorkflows, Steps
- **IMP-NEW-05**: `flask-limiter` integrato in `server/api.py` — 300 req/min, 3000 req/ora per IP; graceful fallback se non installato
- **BUG**: `AppVersion` in `App.xaml.cs` corretto da `"1.1.0"` a `"1.5.0"` (era sfasato da v1.2.0)
- **Config**: Aggiunto campo `NovaSCMApiKey` in `AppConfig` + UI (PasswordBox nelle Impostazioni) + salvataggio in config.json

---

## [1.4.0] - 2026-03-08

### Nuove funzionalità

- **UI-04**: Badge counters live nei nodi nav sidebar — mostra `[N]` per Workflow running e CR pending
- **UI-07**: Sidebar collapse/expand con animazione fluida (pulsante ◀ nell'header)
- **UI-03**: Fade-in animato al cambio tab (160ms)
- **UI-02**: Toggle dark/light mode nelle Impostazioni tramite ModernWpf ThemeManager
- **UI-05**: Indicatore live pulse per stato agent nel tab PC (pallino verde/grigio)
- **UI-06**: Timeline orizzontale degli step nel tab Workflow (bubble numerate colorate per tipo step)
- **FEAT-04**: Drag-and-drop per riordinare gli step del workflow con salvataggio automatico sull'API
- **DX-01**: Config hot reload — FileSystemWatcher su `config.json`, ricarica senza riavvio con toast notifica
- **DX-02**: Log viewer integrato — pannello espandibile nella status bar (click su `📋 Log`)
- **PERF-03**: Cache HTTP con TTL (30-60s) e invalidazione automatica su POST/PUT/DELETE
- **ARCH-01**: `NovaSCMApiService.cs` — classe centralizzata per tutte le chiamate HTTP verso l'API NovaSCM

---

## [1.3.0] - 2026-03-07

### Nuove funzionalità

- **FEAT-01**: Dashboard con 4 stat card in tempo reale (PC online, Workflow attivi, CR aperte, Device) + activity feed + auto-refresh 30s
- **FEAT-02**: Sistema toast WPF nativo (`Notifier.cs`) senza dipendenze esterne — `WorkflowFailed()`, `PcOffline()`, `CertExpiringSoon()`
- **FEAT-03**: Ricerca globale `Ctrl+K` — overlay con TextBox, ricerca su device/workflow/PC, navigazione diretta
- **DX-03**: Shortcut tastiera — `Ctrl+K` ricerca, `F5`/`Ctrl+R` aggiorna, `Ctrl+N` nuova CR, `Ctrl+1-9` tab, `Escape` chiudi
- **PERF-01**: DataGrid virtualizzazione su tutti i grid (`Recycling` mode)
- **BUG-09**: Confronto versione con `System.Version.TryParse()` invece di `string.Compare`

---

## [1.2.0] - 2026-03-06

### Sicurezza e correzioni

- **BUG-01**: Autenticazione API con `hmac.compare_digest` (timing-safe)
- **BUG-02**: Eliminata shell injection in `StepExecutor.cs` — uso di `ProcessStartInfo.ArgumentList`
- **BUG-03/10**: SQLite WAL mode + `busy_timeout=5000` + 1 worker gunicorn
- **BUG-06**: `EvaluateCondition()` per condizioni step (os=windows/linux, hostname=...)
- **BUG-07**: Default `ApiUrl` non hardcoded in `AgentConfig.cs`
- **BUG-08**: Timestamp server-side nei report step
- **IMP-04/05**: Sanitizzazione package ID winget + logging Flask

---

## [1.1.0] - 2026-03-07

### Nuove funzionalità

- **Wake-on-LAN**: accensione remota dei PC dalla tab Rete con un click
- **SSH terminal**: apre una sessione SSH nel terminale (Windows Terminal o CMD)
- **Traceroute grafico**: visualizzazione hop-per-hop con latenza e IP per ogni salto
- **Speed test integrato**: test download/upload via Cloudflare con gauge ad arco animato
- **mDNS scanner**: scoperta automatica di dispositivi sulla rete locale via multicast DNS
- **Esporta CSV**: esportazione della lista dispositivi in formato CSV con SaveFileDialog
- **Script Library**: libreria di script PowerShell con 14 script predefiniti (inventario, gestione servizi, Windows Update, ecc.), editor integrato, esecuzione locale o remota via `Invoke-Command`
- **Auto-scan**: scansione rete automatica programmabile (ogni 5/15/30/60 minuti) con pulsante toggle
- **Note e tag dispositivi**: annotazioni e etichette per ogni dispositivo, salvate nel database SQLite
- **Notifiche toast**: notifiche di sistema nella system tray per eventi di rete (dispositivo online/offline)
- **Heatmap subnet**: visualizzazione densità IP per subnet con colori graduati
- **Matrix rain**: effetto visivo animato nella schermata di scansione
- **Gauge ad arco**: indicatori grafici per CPU, RAM, disco nel pannello dispositivi
- **Easter Egg Konami**: prova ↑↑↓↓←→←→BA nella finestra principale
- **Tab Wiki**: documentazione integrata nell'app con navigazione articoli
- **Pulsanti donazione**: supporto al progetto via Ko-fi/GitHub Sponsors

### Miglioramenti

- **Auto-update**: il controllo aggiornamenti usa ora GitHub Releases API per ottenere versione, note e link download direttamente dai release ufficiali
- **Ping sub-millisecondo**: risolto problema dei dispositivi locali che mostravano 0 ms; ora mostra "< 1 ms" con precisione tramite Stopwatch
- **UI overhaul**: pulsanti in stile light theme, status bar, etichette rivisitate per maggiore leggibilità
- **Tema chiaro unificato**: applicato a tutte le tab dell'applicazione
- **Fix tab startup**: risolto il problema della tab vuota all'avvio dell'applicazione
- **Fix link About**: corretti i link a polariscore.it e GitHub nella tab About

### Sicurezza

- Rimossi IP del server reale dagli script agente incorporati nel codice sorgente
- Aggiunto `config.json` al `.gitignore` per evitare il commit accidentale di credenziali

---

## [1.0.6] - 2026-03-01

### Rilascio iniziale

- Scansione rete con rilevamento IP, MAC, vendor, tipo dispositivo e porte aperte
- Vista tabella, mappa grafica e radar scan animato
- Gestione certificati WiFi EAP-TLS per autenticazione WPA2-Enterprise
- Generazione `autounattend.xml` + `postinstall.ps1` per Windows zero-touch deploy
- Workflow automation: sequenze di step automatizzati (cmd, PowerShell, winget, reboot)
- Fleet management: inventario hardware/software via WMI, RDP one-click
- Integrazione OPSI per distribuzione software
- Change Requests per tracciamento provisioning PC
- Database locale SQLite con sync server opzionale
- Live ping graph e monitor dispositivi in tempo reale
