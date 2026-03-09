# Changelog

All notable changes to NovaSCM are documented here.

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
