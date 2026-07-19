# NovaSCM — Nova Software Configuration Manager

> Open source fleet & network manager per infrastrutture IT Windows e Linux.

[![Release](https://img.shields.io/github/v/release/ClaudioBecchis/NovaSCM?label=versione)](https://github.com/ClaudioBecchis/NovaSCM/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/download/dotnet/9.0)

![NovaSCM Banner](https://novascm.polariscore.it/assets/banner.png)

---

## Cos'è NovaSCM

NovaSCM è un'alternativa open source a Microsoft SCCM per la gestione di reti e flotte di PC. È composto da due parti: un **client Windows** (console WPF) e un **server API** (Python, multipiattaforma) — vedi [Download](#download) e [Server API](#server-api). Il client offre:

- **Scansione rete** multi-VLAN con rilevamento vendor OUI
- **Deploy Windows zero-touch** via PXE (autounattend.xml + postinstall.ps1)
- **Workflow e automazione** — sequenze di step eseguite dall'agent sui PC gestiti
- **Change Request** — tracciamento completo del ciclo di vita provisioning PC
- **Certificati WiFi EAP-TLS** — autenticazione enterprise senza password
- **Dashboard** — metriche in tempo reale (PC online, workflow attivi, CR aperte)
- **Gestione Proxmox** — VM e container direttamente dall'interfaccia
- **Console SCCM** — visualizzatore read-only per ambienti SCCM esistenti

---

## Download

**[↓ Scarica NovaSCM.exe](https://github.com/ClaudioBecchis/NovaSCM/releases/latest)**

Requisiti: Windows 10/11 (64-bit) · [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)

> ⚠️ **Questo è solo il client** (la console di gestione, solo Windows). Scansione rete, ping e Wake-on-LAN funzionano subito da soli. Per il deploy PXE, i workflow e la gestione flotta completa serve anche il **server API** — un componente separato, multipiattaforma (Docker/Linux/Windows/macOS), vedi la sezione [Server API](#server-api) più sotto.

---

## Architettura

```
NovaSCM/
├── MainWindow.xaml/.cs        — UI principale WPF (MVVM)
├── ViewModels/                — ViewModel per ogni tab (9 ViewModel)
│   ├── MainViewModel.cs
│   ├── NetworkViewModel.cs
│   ├── SettingsViewModel.cs
│   ├── WorkflowViewModel.cs
│   ├── ChangeRequestViewModel.cs
│   ├── DeployViewModel.cs
│   ├── DashboardViewModel.cs
│   ├── ProxmoxViewModel.cs
│   └── ...
├── Services/
│   ├── ConfigService.cs       — Config JSON + DPAPI encrypt/decrypt
│   └── NetworkToolsService.cs — Ping, WoL, ARP, Port scan, Traceroute
├── Commands/
│   └── RelayCommand.cs        — ICommand, AsyncRelayCommand
├── NovaSCMAgent/              — Agent .NET 9 Worker Service
├── agent/                     — Agent Python (legacy/Linux)
└── server/
    └── api.py                 — API Flask + SQLite (~2700 righe)
```

---

## Deploy PXE — Flow completo

```
PC boot → DHCP Option 66/67 → TFTP NovaSCM
    ↓
iPXE → GET /api/boot/{mac} → crea host + CR automaticamente
    ↓
WinPE → autounattend.xml dinamico → Windows Setup
    ↓
postinstall.ps1 → enrollment token → installa NovaSCMAgent
    ↓
Agent poll ogni 30s → esegue workflow → riavvio → riprende da dove era rimasto
```

### Configurare la rete per il boot PXE

Il PC da installare deve ricevere via DHCP due opzioni che puntano al server NovaSCM:
- **Option 66 (boot server)**: l'IP del server NovaSCM (es. `192.168.1.50`)
- **Option 67 (boot filename)**: `ipxe.efi`

Questo si configura sul router/gateway che eroga il DHCP alla rete dove si trova il PC (non nel client NovaSCM). Ogni gateway/router ha un percorso diverso per queste opzioni (spesso "opzioni DHCP avanzate" o "network boot"); consulta la documentazione del tuo dispositivo di rete.

### Cos'è una Change Request (CR) e come si crea

Una **CR** è la "pratica" di provisioning associata a un PC: nome macchina, dominio da joinare, workflow da eseguire, stato del ciclo di vita. **Va creata dal client NovaSCM.exe** (sezione Change Request → Nuova), **prima** di accendere il PC via rete, specificando almeno:
- Nome PC
- Dominio (campo obbligatorio)
- Il **workflow da eseguire** — questo è il passaggio che rende effettivo il deploy

**Importante:** se il PC fa boot PXE e nessuna CR esiste ancora per il suo MAC, il server ne crea una automaticamente — ma **senza un workflow assegnato l'azione resta "solo boot locale"**, il deploy Windows non parte. Per far sì che ogni PC sconosciuto riceva subito un workflow di default, imposta un workflow predefinito in **Impostazioni PXE** del client (si traduce nel campo `pxe_default_workflow_id` lato server). In assenza di questa impostazione, crea sempre la CR manualmente dal client prima di avviare il PC.

---

## Workflow e automazione

I workflow sono sequenze di step configurabili via GUI, assegnabili a singoli PC o gruppi. L'agent esegue ogni step e riporta il risultato al server.

**Tipi di step supportati:**

| Tipo | Descrizione |
|------|-------------|
| `powershell` | Esegue script PowerShell |
| `cmd` / `shell` | Esegue comando shell |
| `winget_install` | Installa pacchetto via winget |
| `reg_set` | Imposta chiave di registro |
| `file_copy` | Copia file |
| `reboot` | Riavvia il PC (con resume automatico post-reboot) |
| `wait` | Attende N secondi |
| `systemd_service` | Gestisce servizi Linux |

**Condizioni:** ogni step può essere condizionato a `windows`, `linux` o `hostname=NOME-PC`.

**Reboot resume:** l'agent salva lo stato in modo atomico prima del riavvio e riprende dal passo successivo automaticamente.

---

## Server API

Il server è un'API Flask + SQLite (porta 9091 di default). È un componente **separato** dal client: il client (NovaSCM.exe) gira solo su Windows, il server invece gira ovunque ci sia Python o Docker — Linux, Windows, macOS.

**Quale opzione scegliere:**
- **Non hai esperienza con Linux o server** → Opzione 1 (Docker). È la via più semplice, funziona uguale su Windows/Mac/Linux.
- **Hai già un server/VPS Linux e sai usare il terminale** → Opzione 2 (Python nativo), con l'esempio di servizio systemd incluso sotto.
- **Usi già Proxmox** → Opzione 3, automatizza tutto in un comando. Se non sai cos'è Proxmox, ignora questa opzione.

### Opzione 1 — Docker (consigliato, qualsiasi sistema operativo, nessuna esperienza Linux richiesta)

Prerequisiti: [Docker Desktop](https://www.docker.com/products/docker-desktop/) installato (Windows/Mac) o Docker Engine (Linux), e [Git](https://git-scm.com/downloads) per scaricare il codice. Apri un terminale (su Windows: PowerShell) ed esegui:

```bash
git clone https://github.com/ClaudioBecchis/NovaSCM.git
cd NovaSCM/server
docker compose up -d
```

L'API sarà disponibile su `http://localhost:9091`. Vedi `server/README.md` per le variabili d'ambiente (API key, abilitazione PXE, ecc.).

### Opzione 2 — Python nativo (richiede un minimo di dimestichezza con la riga di comando)

Prerequisito: [Python 3.12](https://www.python.org/downloads/) installato (su Linux il comando è spesso `python3` invece di `python`).

```bash
cd NovaSCM/server
pip install -r requirements.txt
python api.py
```

Questo comando avvia il server solo finché il terminale resta aperto. Per farlo girare in background in modo persistente su **Linux con systemd**, esempio completo:

```bash
# 1. Crea il file di unit (sostituisci /percorso/NovaSCM con il path reale)
sudo tee /etc/systemd/system/novascm.service << 'EOF'
[Unit]
Description=NovaSCM Server
After=network.target

[Service]
WorkingDirectory=/percorso/NovaSCM/server
ExecStart=/usr/bin/python3 api.py
Restart=always

[Install]
WantedBy=multi-user.target
EOF

# 2. Attiva e avvia il servizio
sudo systemctl daemon-reload
sudo systemctl enable --now novascm
```

Su **Windows**, per farlo persistere in background si può usare [NSSM](https://nssm.cc/) o il Task Scheduler configurato manualmente (nessun equivalente diretto di systemd).

### Opzione 3 — Proxmox LXC automatico

> Salta questa opzione se non usi già Proxmox — non è necessaria per far funzionare NovaSCM.

Se il tuo ambiente è un container LXC Proxmox (Debian), lo script `server/deploy/install-flask-ct.sh` automatizza tutto (dipendenze, Samba, unit systemd incluso):

```bash
./server/deploy/install-flask-ct.sh <CTID> <API_KEY> <SERVER_IP>
```

### Collegare il client al server

Una volta che il server è avviato (con una qualsiasi delle 3 opzioni sopra):

1. Recupera l'API key: con Docker/Python nativo viene generata al primo avvio e salvata in `data/.api_key` (o stampata nei log all'avvio); con lo script Proxmox l'hai passata tu come argomento.
2. Apri NovaSCM.exe → **Impostazioni** → **URL API NovaSCM**.
3. Inserisci l'indirizzo del server, es. `http://localhost:9091` (stessa macchina) o `http://192.168.x.x:9091` (server su un'altra macchina/rete).
4. Inserisci l'API key nel campo corrispondente.

Da qui il client comunica col server per tutte le funzioni che lo richiedono (deploy PXE, workflow, gestione flotta).

**Endpoint principali:**

| Metodo | Endpoint | Descrizione |
|--------|----------|-------------|
| GET | `/health` | Healthcheck (no auth) |
| GET/POST | `/api/cr` | Lista / crea Change Request |
| PUT | `/api/cr/<id>/status` | Cambia stato CR |
| GET | `/api/boot/<mac>` | Boot PXE (iPXE) |
| GET | `/api/cr/<id>/autounattend.xml` | XML per WinPE |
| POST | `/api/enrollment-token` | Token monouso per agent |
| GET | `/api/workflows` | Lista workflow |
| GET | `/api/version` | Versione (auto-update) |

**Autenticazione:** header `X-Api-Key` oppure query param `?key=`. Confronto timing-safe con `hmac.compare_digest`. Token enrollment monouso con scadenza automatica.

---

## NovaSCM Agent

Servizio Windows (.NET 9 Worker) che gira in background sui PC gestiti.

```bash
# Installazione da PowerShell admin
iwr http://<SERVER>:9091/api/download/agent-install.ps1 | iex
```

**Come dialoga con il server:** l'agent fa polling HTTP verso il server ogni 30 secondi (nessuna porta in ingresso richiesta sul PC gestito). Ad ogni poll: controlla se ci sono workflow assegnati, esegue lo step successivo, riporta l'esito al server (`/api/cr/by-name/<pc>/step`). Se il PC viene riavviato durante un workflow (es. dopo un `winget_install` che lo richiede), l'agent riprende automaticamente dal passo successivo al riavvio.

**Sicurezza:**
- Comandi eseguiti con `ArgumentList` — no shell injection
- API key passata via file temporaneo con ACL restrittiva (non env var)
- Retry 3× con backoff esponenziale su errori di rete
- State file scritto in modo atomico (temp + rename) — crash-safe

---

## Sicurezza

- DPAPI per credenziali in config (fallback plain:base64 se DPAPI non disponibile)
- Security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Cache-Control`
- SSRF protection su webhook e validazione URL agent
- Enrollment token UUID → `secrets.token_hex(32)`
- SQLite WAL mode — no lock su letture concorrenti

---

## Changelog

**19/07/2026** — Audit e pulizia client WPF:
- Status bar in fondo alla finestra mostrava numeri statici scritti a mano ("8/10 online", "4 cert", "3/4 PC"), mai collegati a dati reali — ora legge i valori veri dal server
- Tab ribbon "Visualizza"/"Strumenti" non facevano nulla al click — ora navigano rispettivamente a Change Request e Rete
- 11 pulsanti (certificati EAP-TLS, pacchetti OPSI, aggiornamento agent) mostravano messaggi che simulavano un'azione riuscita seguiti da "(demo)" — sostituiti con messaggi onesti "non ancora implementato": queste funzioni richiedono integrazione con servizi esterni (CA/Certportal, sistema OPSI) non ancora collegati
- Creazione Change Request: la validazione (campi obbligatori mancanti, o server non configurato) falliva silenziosamente con solo un piccolo testo colorato, facile da non notare — ora mostra un popup esplicito
- Corretta configurazione DHCP Option 66 e riferimenti IP obsoleti nell'ambiente di test PXE, che impedivano al deploy di completarsi

---

## Cosa manca da integrare

Funzionalità presenti nell'interfaccia ma non ancora collegate a una vera implementazione. Contributi benvenuti.

**Certificati WiFi EAP-TLS** (tab Certificati — Genera/Revoca)
- Serve una CA (Certificate Authority) reale che emetta certificati X.509 client, o l'integrazione con un servizio esterno tipo un "Certportal" (menzionato nel codice come dipendenza prevista ma non implementata)
- Il pulsante Revoca dovrebbe invalidare il certificato lato CA/RADIUS, non solo aggiornare un flag locale

**Gestione pacchetti OPSI** (tab OPSI — Wizard creazione, Upload, Aggiorna, Elimina, Invio script)
- Serve uno storage reale per gli installer (upload file, versionamento)
- L'esecuzione remota richiede un canale verso l'agent sul PC target (l'agent già fa polling ogni 30s — si potrebbe estendere il protocollo esistente invece di crearne uno nuovo)
- Il tab ha CRUD parziale: lista/stato pacchetti funziona, le azioni di modifica no

**Aggiornamento agent da remoto** (tab PC — pulsante Aggiorna agent)
- L'agent espone già un endpoint `/api/version` per il self-update automatico (vedi sezione Server API) — questo pulsante dovrebbe forzare quel check invece di essere uno stub

---

## Build

```bash
# Debug
dotnet build PolarisManager.csproj -c Debug

# Release (richiede .NET 9 installato sul target)
dotnet publish PolarisManager.csproj -c Release -r win-x64 --self-contained false -o publish/
```

---

## Sito web

[novascm.polariscore.it](https://novascm.polariscore.it)

---

## Licenza

MIT — © 2026 [Claudio Becchis](https://github.com/ClaudioBecchis) · [PolarisCore.it](https://polariscore.it)
