# NovaSCM — CLAUDE.md

## La visione

NovaSCM è un **sostituto completo di Microsoft SCCM**, self-hosted e open-source.
Un unico sistema che fa tutto: deploy Windows zero-touch via PXE, gestione software,
connessioni remote, monitoring, inventario, e certificati WiFi Enterprise.

**Tutto parte dal server. Tutto deve funzionare insieme. Ogni pezzo dipende dagli altri.**

---

## I componenti

### 1. Server Flask — il centro di tutto
- Proxmox CT 103 · `192.168.20.103:9091` · VLAN 20 Servers
- `server/api.py` (2585 righe) — API REST, DB SQLite, PXE boot, autounattend, workflow engine
- `server/pxe_server.py` — TFTP thread (serve ipxe.efi porta 69/udp)
- `server/web/index.html` — UI amministrazione (Alpine.js SPA)
- `server/web/deploy-client.html` — **schermata deploy** che gira sul PC durante l'installazione
- Docker + Gunicorn, `docker-compose.yml`

### 2. NovaSCMAgent — gira su ogni PC gestito, PER SEMPRE
- Si installa automaticamente durante il deploy
- **Non è solo per il deploy** — resta installato e gestisce il PC in modo permanente
- Gestisce: applicazioni, connessioni remote, servizi, inventario — come il ConfigMgr Agent di SCCM
- Polling continuo verso il server, esegue workflow assegnati
- `NovaSCMAgent/Worker.cs` + `StepExecutor.cs` + `ApiClient.cs` (.NET, Windows)
- `agent/novascm-agent.py` (Python, Linux)

### 3. NovaSCM.exe — console portatile
- Client WPF Windows, singolo `.exe`, no install
- Si collega al server per monitorare e gestire i deploy
- Network scanner, workflow editor, CR management, certificati WiFi
- `MainWindow.xaml.cs` (7290 righe)

### 4. DeployScreen — schermata deploy personalizzata
- Durante il deploy, sul PC viene mostrata una **grafica custom NovaSCM** al posto della schermata standard Windows
- Layout a 3 colonne: progresso (ring + barra), log output in tempo reale, pipeline step
- Header con info hardware (CPU, RAM, disco, MAC/IP)
- Polling verso `/api/pc-workflows/{pw_id}` ogni 3 secondi
- Overlay di completamento con screenshot finale e countdown reboot
- File: `server/web/deploy-client.html` — servito via HTTP, aperto in browser fullscreen

---

## Il flusso deploy completo (DEVE FUNZIONARE END-TO-END)

```
FASE 1 — PXE BOOT (automatico, zero intervento)
═══════════════════════════════════════════════════
PC si accende (VLAN 10 Trusted)
    │
    ▼
UCG-Fiber DHCP (VLAN 10)
    Option 66 = 192.168.20.103 (NovaSCM TFTP)
    Option 67 = ipxe.efi
    │
    ▼
TFTP :69 → NovaSCM serve ipxe.efi
    │
    ▼
iPXE → GET http://192.168.20.103:9091/api/boot/{mac}
    NovaSCM risponde con script iPXE
    Auto-crea CR + pxe_host se MAC sconosciuto
    │
    ▼
iPXE carica WinPE via HTTP:
    kernel  /api/pxe/file/wimboot
    initrd  /api/pxe/file/BCD           BCD
    initrd  /api/pxe/file/boot.sdi      boot.sdi
    initrd  /api/pxe/file/boot.wim      boot.wim
    initrd  /api/autounattend/{pc_name}  autounattend.xml
    boot

FASE 2 — INSTALLAZIONE WINDOWS (automatica)
═══════════════════════════════════════════════════
WinPE avvia Windows Setup con autounattend.xml dinamico:
    - Partizionamento disco (GPT: EFI + MSR + Windows)
    - Installazione Windows 11 Pro da install.wim
    - Locale it-IT, fuso orario W. Europe
    - Nome PC da CR (es. PC-A1B2C3)
    - Join dominio polariscore.it (se configurato)
    - Password admin da CR
    │
    ▼
FirstLogonCommands (autounattend.xml):
    1. Scrive enrollment token nel registry (HKLM\SOFTWARE\NovaSCM)
    2. Scrive server URL nel registry
    3. Copia postinstall.ps1
    4. Esegue postinstall.ps1

FASE 3 — POST-INSTALL + DEPLOY SCREEN (automatica)
═══════════════════════════════════════════════════
postinstall.ps1:
    1. Legge enrollment token dal registry
    2. POST /api/deploy/enroll → ottiene session_key + pw_id
    3. Rinomina PC con template MAC6
    4. POST /api/deploy/start → crea workflow deploy → ottiene pw_id
    5. ★ APRE DEPLOY SCREEN ★
       → Apre browser Edge in kiosk mode su:
         http://server:9091/deploy-client?pw_id={pw_id}&key={session_key}&hostname={pc_name}
       → Schermata fullscreen NovaSCM con:
         - Ring progress + barra percentuale
         - Pipeline step con connettori
         - Log output in tempo reale
         - Info hardware live
    6. Esegue gli step del workflow, riportando stato al server:
       - Step 1-4: già completati (partizione, formato, install, OOBE)
       - Step 5-8: driver (chipset, rete, audio, GPU)
       - Step 9-10: Windows Update
       - Step 11-13: runtime (.NET, VC++, agente sicurezza)
       - Step 14: firewall policy
       - Step 15-16: join dominio + GPO
       - Step 17: ★ certificato WiFi 802.1X ★
       - Step 18-21: Office, Outlook, OneDrive, profilo utente
       - Step 22: installa NovaSCMAgent (permanente)
       - Step 23: pulizia temp
       - Step 24: reboot finale
    │
    ▼
Deploy Screen mostra "Configurazione completata ✓"
    Screenshot finale inviato al server
    Countdown 30s → reboot
    │
    ▼
PC riavvia → NovaSCMAgent attivo → gestione permanente
```

### Certificati WiFi 802.1X (integrato nel deploy)
- Il certificato WiFi viene installato come **step del workflow** durante il deploy
- Profilo WiFi WPA2-Enterprise configurato automaticamente
- Per **dispositivi mobili** (telefoni, tablet): portale web dedicato con QR code
- Il portale è separato dal deploy, accessibile dalla web UI

---

## Rete PolarisCore

| Risorsa | IP | VLAN | Ruolo |
|---|---|---|---|
| UCG-Fiber | .10.1 / .20.1 | tutte | Router, firewall, DHCP |
| NovaSCM (CT 103) | 192.168.20.103 | 20 | Server API :9091, TFTP :69 |
| Windows Server AD | 192.168.20.12 | 20 | DC polariscore.it |
| Pi-hole | 192.168.20.253 | 20 | DNS |
| PC client | 192.168.10.x | 10 | Target deploy |

**Dominio:** `polariscore.it` (NON `.local`)
**DHCP Option 66/67:** da configurare su VLAN 10, next-server=192.168.20.103, filename=ipxe.efi

---

## Database — tabelle principali

| Tabella | Scopo |
|---|---|
| `cr` | Change Request — un PC da deployare |
| `workflows` | Template workflow (sequenza step) |
| `workflow_steps` | Singoli step di un workflow |
| `pc_workflows` | Assegnazione workflow → PC |
| `pc_workflow_steps` | Stato esecuzione ogni step per PC |
| `pxe_hosts` | Mappa MAC → PC per PXE boot |
| `pxe_boot_log` | Log boot PXE |
| `settings` | Config chiave-valore (PXE settings con prefisso `pxe_`) |
| `deploy_tokens` | Token monouso per enrollment agente |

---

## Endpoint API critici per il deploy

### Senza auth (subnet allow-list: VLAN 10 + 20)
| Endpoint | Ruolo |
|---|---|
| `GET /api/boot/<mac>` | Script iPXE per PXE boot |
| `GET /api/pxe/file/<n>` | Serve wimboot, BCD, boot.sdi, boot.wim |
| `GET /api/autounattend/<pc_name>` | XML autounattend dinamico |
| `POST /api/deploy/enroll` | Enrollment con token monouso → session_key |

### Con auth (API key o session token)
| Endpoint | Ruolo |
|---|---|
| `POST /api/deploy/start` | Crea workflow deploy → restituisce pw_id |
| `POST /api/deploy/<pw_id>/step` | Aggiorna stato step |
| `GET /api/pc-workflows/<pw_id>` | Stato workflow (deploy-client.html lo polla) |
| `POST /api/pc-workflows/<pw_id>/hardware` | Info hardware |
| `POST /api/pc-workflows/<pw_id>/screenshot` | Screenshot finale |

---

## ★ BUG CRITICI (il deploy non funziona senza questi fix)

### C-1 · `DIST_DIR` e `_WINPE_DIR` non definiti — api.py
Usati in 7 punti ma mai dichiarati → NameError.
```python
# Aggiungere dopo DB = os.environ.get(...) (riga ~120):
DIST_DIR   = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dist")
_WINPE_DIR = os.path.join(DIST_DIR, "winpe")
```

### C-2 · Route `<n>` vs funzione `name` — api.py riga 2223
Flask passa `n`, funzione aspetta `name` → TypeError.
Fix: rinominare parametro funzione in `n`, aggiornare occorrenze nel body.

### C-3 · PXE autounattend manca `<ImageInstall>` + `<InstallTo>` — api.py riga ~2354
Windows Setup non sa dove installare → prompt manuale.
Fix: aggiungere `<ImageInstall><OSImage><InstallTo>` come nella versione CR (riga 706-714).

### C-4 · Dockerfile non copia `pxe_server.py`
TFTP non si avvia nel container. Fix: `COPY api.py pxe_server.py ./`

---

## ★ BUG MEDI

### M-1 · PXE autounattend espone API key globale
`_build_autounattend_xml_pxe()` mette API key in chiaro. Fix: usare enrollment token.

### M-2 · `_get_pxe_settings()` default inconsistenti
Strippa prefisso `pxe_` ma defaults hanno prefisso. Fix: centralizzare.

### M-3 · `deploy/autounattend.xml` credenziali hardcoded (`Admin2026!`)
File legacy. Eliminare o pulire.

### M-4 · `deploy/pxe.conf` obsoleto (iVentoy non più in uso)
Eliminare o deprecare.

### M-5 · deploy-client.html ha dominio/IP/versione sbagliati ✅ FIXATO
Aggiornato: server default → `192.168.20.103:9091`, versione → `2.2.1`

### M-6 · `/deploy-client` richiede auth ma il browser durante il deploy non ha API key ✅ FIXATO
Fix applicato: accetta `key` dal query string come auth alternativa:
```python
@app.route("/deploy-client")
def ui_deploy_client():
    # Accetta session key da query param (usato durante il deploy)
    key = request.args.get("key", "")
    if key:
        with _ui_tokens_lock:
            exp = _ui_tokens.get(key)
        if not exp or exp < time.time():
            return "Token non valido o scaduto", 401
    else:
        # Fallback: richiedi auth normale
        token = request.headers.get("X-Api-Key", "")
        if not (hmac.compare_digest(token, API_KEY)):
            return "Non autorizzato", 401
    # ... serve HTML
```

### M-7 · Nessun test PXE ✅ FIXATO (30 test PXE aggiunti — totale 142/142)

---

## ★ INTEGRAZIONE DEPLOY SCREEN

Il `postinstall.ps1` deve aprire il browser in kiosk mode con l'URL deploy-client:
```powershell
$deployUrl = "$SERVER/deploy-client?pw_id=$PW_ID&key=$APIKEY&hostname=$PC&domain=polariscore.it&ver=2.1.0"
$edgePath = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if (Test-Path $edgePath) {
    Start-Process $edgePath "--kiosk `"$deployUrl`" --edge-kiosk-type=fullscreen" -WindowStyle Maximized
} else {
    Start-Process $deployUrl
}
```

---

## Convenzioni

- **Dominio:** `polariscore.it` ovunque (NON `.local`)
- **IP server:** `192.168.20.103` ovunque (NON `192.168.20.110`)
- **Date:** ISO 8601 UTC senza timezone
- **PC names:** uppercase, max 15 char, regex `^[A-Z0-9][A-Z0-9\-]{0,14}$`
- **MAC:** `AA:BB:CC:DD:EE:FF` uppercase
- **XML escape:** `_xe()` per XML, `_xe_ps()` per PowerShell
- **Test:** `NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v`

---

## ORDINE DI LAVORO

### Fase 1 — Sblocca PXE
1. Fix C-1: `DIST_DIR` e `_WINPE_DIR`
2. Fix C-2: parametro `serve_pxe_file`
3. Fix C-4: Dockerfile
4. Fix C-3: `<ImageInstall>` autounattend PXE
5. Fix M-2: default `_get_pxe_settings()`
6. Fix M-1: enrollment token autounattend PXE
7. Esegui test

### Fase 2 — Integra Deploy Screen
8. Aggiorna deploy-client.html (dominio, IP, versione)
9. Fix M-6: `/deploy-client` accetta session_key da query param
10. Aggiorna postinstall.ps1: browser Edge kiosk mode
11. Verifica polling deploy-client.html ↔ `/api/pc-workflows/{pw_id}`

### Fase 3 — Pulizia
12. Fix M-3: pulisci deploy/autounattend.xml
13. Fix M-4: depreca deploy/pxe.conf
14. Aggiorna IP e dominio in tutti i file

### Fase 4 — Test
15. Scrivi test PXE
16. Scrivi test deploy-client auth
17. Suite completa

---

*Ultimo aggiornamento: 2026-03-12 — v2.2.1*
