# Architettura target — da SCCM (originale) a NovaSCM

**Documento di progettazione**  
**Data:** 2026-07-19  
**Stato:** design di riferimento (da implementare / allineare al codice)  
**Principio guida:** stesso *modello mentale* di Microsoft Configuration Manager (SCCM), implementazione open-source, self-hosted, **senza IP o path fissi nel codice**.

Documenti collegati:
- `docs/PIANO_DISM_ENROLLMENT_E2E.md` — piano tecnico DISM + enrollment  
- `CLAUDE.md` — visione storica (IP/CT possono essere obsoleti)  
- Sessioni lab: PXE catena OK; DISM → desktop validato in lab  

---

# Parte A — Flusso SCCM originale (come funziona davvero)

## A.1 Ruoli del prodotto Microsoft

| Ruolo SCCM | Responsabilità |
|------------|----------------|
| **Site server** | Database, configurazione, scheduling, reporting |
| **Management Point (MP)** | I client chiedono *policy* e riportano stato/inventory |
| **Distribution Point (DP)** | Host del *content*: WIM, boot image, driver, app, update |
| **PXE service point / PXE su DP** | Risponde al boot di rete, consegna WinPE |
| **Software Update Point** | Patch (WSUS) |
| **Console amministrativa** | Task Sequence, collection, deploy, monitor |
| **ConfigMgr Client** | Agent permanente su ogni PC gestito |
| **Software Center** | UI utente finale per app/self-service (day-2) |

I client **non** hanno l’IP del server “inciso” per sempre: usano **site assignment**, **boundary group**, DNS/AD e policy per trovare MP e DP anche se l’infrastruttura si sposta.

---

## A.2 Preparazione (prima che un PC si accenda)

1. **Import OS image** (`install.wim`) → distribuito sui DP  
2. **Boot image** (WinPE custom con tool/driver di rete) → PXE enabled  
3. **Driver packages** / **applications** / account di join dominio  
4. **Task Sequence** = sequenza ordinata di passi (il “cuore” OSD)  
5. **Deploy** della TS verso una **Collection** (es. *All Unknown Computers* o collection di MAC importati)  
6. Opzionale: **import computer** (MAC/GUID → nome PC + variabili TS)

Finché non c’è content sui DP e una TS deployata, il PXE non ha nulla di utile da eseguire.

---

## A.3 Flusso OSD end-to-end (Task Sequence)

```
═══════════════════════════════════════════════════════════════
 FASE 0 — ADMIN (console)
═══════════════════════════════════════════════════════════════
  Content su DP + Task Sequence + Deploy su Collection
  (known computer: MAC già in DB; unknown: collection generica)

═══════════════════════════════════════════════════════════════
 FASE 1 — NETWORK BOOT
═══════════════════════════════════════════════════════════════
  PC power-on → firmware PXE/UEFI
       │
       ▼
  DHCP (+ Option 66/67 oppure IP Helper verso DP PXE)
       │
       ▼
  Servizio PXE consegna / avvia **Boot Image (WinPE)**
       │
       ▼
  WinPE + motore Task Sequence ConfigMgr

═══════════════════════════════════════════════════════════════
 FASE 2 — WINPE (fuori dal Windows finale)
═══════════════════════════════════════════════════════════════
  Tipica sequenza TS:

  1. Partition Disk          → GPT: EFI + MSR + Windows (+ recovery opz.)
  2. Apply Operating System  → **DISM Apply-Image** del install.wim
                               (NON un maxi-setup.exe con unattend “tutto”)
  3. Apply Windows Settings  → nome PC, locale, product key se serve
  4. Apply Network Settings  → workgroup/domain prep
  5. Apply Device Drivers    → package o automatic
  6. Setup Windows and ConfigMgr
       → unattend **minimo** (specialize / oobe)
       → prepara installazione client ConfigMgr
  7. Restart Computer        → esce da WinPE, boot dal disco

═══════════════════════════════════════════════════════════════
 FASE 3 — PRIMO BOOT WINDOWS (specialize / OOBE)
═══════════════════════════════════════════════════════════════
  Windows applica specialize/oobe
  Nome macchina, (spesso) domain join
  Client ConfigMgr si installa / si registra al site
  La Task Sequence **riprende** dal passo successivo (stato persistito)

═══════════════════════════════════════════════════════════════
 FASE 4 — FULL OS (ancora dentro la TS)
═══════════════════════════════════════════════════════════════
  Install Applications / Packages
  Install Software Updates
  Script, BitLocker, agent sicurezza, ecc.
  Restart se necessario (resume TS)
  Fine Task Sequence → PC “provisioned”

═══════════════════════════════════════════════════════════════
 FASE 5 — DAY-2 (senza TS, per sempre)
═══════════════════════════════════════════════════════════════
  ConfigMgr Client:
    • policy cycle verso Management Point
    • download content da Distribution Point (boundary)
    • Software Center (app)
    • updates, compliance, inventory HW/SW
    • remote tools
```

### Punto critico (spesso frainteso)

SCCM **non** si basa su “un solo autounattend.xml gigante che installa Windows come un DVD”.  
Si basa su:

| In WinPE | Dopo il reboot |
|----------|----------------|
| **DISM Apply-Image** + **bcdboot** | Unattend leggero + **client** + resto della TS |

NovaSCM adotta **lo stesso modello** (`pxe_apply_mode=dism`).

---

## A.4 Known vs Unknown computer

| | Known | Unknown |
|--|--------|---------|
| **Prima del boot** | MAC/GUID importato, nome e variabili già note | Nessun record |
| **Collection** | Mirata / import | *All Unknown Computers* (o equivalente) |
| **Risultato** | Nome e app set prevedibili | Nome generato da regola + TS generica |

---

## A.5 Day-2 (dopo l’imaging)

La Task Sequence **finisce**.  
Il **client** resta e lavora a **policy**:

- Collection membership → deployment app/update  
- Boundary → quale DP usare  
- Inventory → console / report  
- Compliance baseline  

Senza client permanente non c’è “SCCM”: c’è solo un installatore one-shot.

---

## A.6 Indipendenza dalla rete fisica

| Bisogno | Come lo risolve SCCM |
|---------|----------------------|
| Server con IP diversi per sito | Boundary group, DP multipli, DNS |
| Client che si sposta di subnet | Riassegnazione boundary / MP |
| Contenuti grandi | DP locali, peer cache |
| Admin lontano dal site | Console remota verso site server |

**Regola:** infrastruttura e indirizzi sono **configurazione**, non codice.

---

# Parte B — Mappa concettuale SCCM → NovaSCM

| Concetto SCCM | Concetto NovaSCM | Note |
|---------------|------------------|------|
| Site server + MP | **Server API** (`server/api.py`) | Un processo: policy, CR, workflow, PXE HTTP |
| Distribution Point | **Content store** `server/dist/` + share/HTTP WIM | Boot files + install.wim |
| PXE su DP | **TFTP** (`pxe_server.py`) + **iPXE** + `/api/boot/{mac}` | Option 66/67 lato DHCP del *sito* |
| Console | **NovaSCM.exe** | Configura server, CR, workflow, monitor |
| Task Sequence | **Workflow** (+ passi built-in OSD) | Sequenza step con reboot-resume |
| TS variables / computer association | **Change Request (CR)** | Nome PC, domain, MAC, token, … |
| Collection + Deploy TS | **Assegnazione workflow** + `pxe_default_workflow_id` | Known = CR; unknown = default PXE |
| Boot image + script PE | **boot.wim** + **startnet DISM** | Injection build o wimboot runtime |
| Apply Operating System | **`dism /Apply-Image` + `bcdboot`** | Path ufficiale |
| Setup Windows and ConfigMgr | **unattend specialize + postinstall + enroll** | Agent permanente |
| ConfigMgr Client | **NovaSCMAgent** | Poll, step, inventory |
| Software Center / App model | **(Fase futura)** workflow app / catalogo | Non bloccante per OSD v1 |
| Boundary / site URL | **Server Base URL** + **Public URL** in settings | Mai IP hardcoded |
| MP policy | **API + agent poll** | Semplificato rispetto a SCCM |

---

# Parte C — Come DEVE essere NovaSCM (design target)

## C.1 Principi non negoziabili

1. **Un server logico, N indirizzi possibili**  
   - Admin e PC usano un **Base URL configurabile** (IP o meglio DNS).  
   - Nessun `192.168.x.x` di default nel codice di produzione.

2. **NovaSCM.exe configura il server; il server esegue il deploy**  
   - La console scrive settings/CR/workflow via API.  
   - PXE/PE/agent **leggono solo il server**.

3. **OSD = DISM in WinPE**, come SCCM Apply OS.  
   - `setup.exe` full unattend = legacy opzionale, non default.

4. **Agent permanente dopo l’imaging**  
   - Come ConfigMgr Client: non solo “script di install”.

5. **Due livelli di personalizzazione**  
   - **Default sito** (`pxe_*` settings) → unknown computer  
   - **CR per macchina** → known computer (override)

6. **Un solo percorso enrollment OSD**  
   - `deploy_tokens` + `POST /api/deploy/enroll`  
   - L’agent salva Base URL ricevuto all’enroll.

7. **Separazione chiaro/scuro**  
   - Config locale exe = “a quale server mi collego io admin”.  
   - Settings server = “come si installano i PC di questo sito”.

---

## C.2 Componenti e responsabilità

```
┌──────────────────────────────────────────────────────────────┐
│  NovaSCM.exe (console admin)                                 │
│  • Connessione: Server URL + API key (locale, DPAPI)         │
│  • Profilo sito: GET/PUT /api/pxe/settings                   │
│  • CR, Workflow, host PXE, monitor, status                   │
│  • NON esegue DISM; NON è il boot server                     │
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTPS/HTTP + API key
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  Server NovaSCM (un host per sito, IP/DNS variabili)         │
│  • API REST + DB (CR, workflow, settings, token, log)        │
│  • TFTP ipxe.efi                                             │
│  • HTTP: boot script, file PE, WIM, unattend, enroll, agent  │
│  • Content: dist/winpe/*, install.wim (locale o UNC/URL)     │
└───────┬───────────────────────────────┬──────────────────────┘
        │ PXE / HTTP                    │ poll / enroll
        ▼                               ▼
┌───────────────────┐         ┌───────────────────────────────┐
│ PC in imaging     │         │ PC gestito (day-2)            │
│ WinPE → DISM →    │  ──►    │ NovaSCMAgent                  │
│ Windows → enroll  │         │ workflow, inventory, …        │
└───────────────────┘         └───────────────────────────────┘
```

**Rete del sito (fuori da NovaSCM, ma obbligatoria):**  
DHCP Option 66/67 (o IP Helper) verso l’host che espone TFTP/HTTP NovaSCM **di quel sito**.

---

## C.3 Modello dati (minimo coerente con SCCM)

| Entità | Equivalente SCCM | Campi chiave |
|---------|------------------|--------------|
| **settings** (`pxe_*`) | Site settings / boot defaults | public_url, apply_mode, wim path/index, domain defaults, default_workflow_id, pc_prefix, auto_provision |
| **cr** | Device + TS variables | pc_name, mac, domain, ou, join_*, admin_pass, status, workflow link |
| **pxe_hosts** | PXE boot association | mac, pc_name, boot_action, cr_id |
| **workflows** / **workflow_steps** | Task Sequence definition | nome, step type, ordine, condizioni |
| **pc_workflows** / steps | TS execution instance | status, progress, log |
| **deploy_tokens** | One-time bootstrap secret | token, pc_name, expires, used |
| **agent registration** | Client registration | hostname, last_seen, server_url salvato sul client |

---

## C.4 Settings che NovaSCM.exe DEVE poter impostare sul server

Tutto ciò che il server usa in imaging **passa da API**, editabile dalla console:

| Gruppo | Chiavi / campi (esempi) | Usato da |
|--------|-------------------------|----------|
| **Identità server pubblica** | `pxe_static_url` (Base URL visto dai PC) | iPXE, unattend, postinstall, agent download |
| **Provisioning** | `pxe_enabled`, `pxe_auto_provision`, `pxe_pc_prefix` | `/api/boot/{mac}` |
| **Dominio default** | domain, ou, dc_ip, join user/pass, admin pass | CR auto + unattend |
| **Workflow default** | `pxe_default_workflow_id` | Unknown computer post-enroll |
| **Apply OS (DISM)** | `pxe_apply_mode=dism`, `pxe_image_index`, `pxe_install_wim_mode`, `pxe_install_wim_path` | startnet / PE |
| **SMB content** (se mode=smb) | user/pass/domain share | WinPE `net use` |
| **Stato** | read-only da `/api/pxe/status` | Console: “pronto al deploy?” |

Regole:

- Se manca `pxe_static_url` o path WIM → **errore esplicito** al boot/status, non path fantasma.  
- Password: write-only in UI (mascherate in GET come già in API).  
- **Nessun default che punti a un lab PolarisCore.**

---

## C.5 Flusso target NovaSCM (allineato a SCCM)

### Fase 0 — Admin prepara il sito (console)

```
1. NovaSCM.exe → Server URL + API key (questa postazione admin)
2. Impostazioni Deploy/PXE → salva su server:
   - Public URL (come i PC raggiungono il server)
   - Path/indice install.wim, mode smb|http
   - apply_mode = dism
   - domain defaults, workflow default
3. (Opz.) Crea CR per MAC noti (known computers)
4. Verifica /api/pxe/status: TFTP + file PE + WIM raggiungibile
5. DHCP del sito: Option 66/67 → host NovaSCM di quel sito
```

Equivalente SCCM: content su DP + TS deploy + (opz.) import computer.

---

### Fase 1 — Network boot

```
PC → DHCP → TFTP ipxe.efi
   → HTTP {public_url}/api/boot/{mac}
        • normalizza MAC
        • trova o crea pxe_host + CR (se auto_provision)
        • risponde script iPXE (wimboot + boot.wim WinPE + file necessari)
```

Equivalente SCCM: PXE → boot image WinPE.

---

### Fase 2 — WinPE / Apply OS (DISM)  ★ come SCCM

```
startnet (in PE, da injection ripetibile o wimboot):
  1. Attendi rete
  2. Risolvi content: UNC o download da {public_url} / path settings
  3. diskpart GPT (EFI + MSR + Windows)
  4. dism /Apply-Image /ImageFile:... /Index:{settings} /ApplyDir:W:\
  5. bcdboot W:\Windows /s S: /f UEFI
  6. Copia unattend-specialize.xml  → W:\Windows\Panther\unattend.xml
     (generato da server: nome PC, locale, FirstLogon, token, EnrollServer={public_url})
  7. Copia postinstall.ps1 → W:\Windows\Temp\
  8. (opz.) LabConfig / driver minimali rete se servono in full OS
  9. wpeutil reboot
```

Equivalente SCCM: Partition → **Apply OS** → Setup Windows and ConfigMgr (parte PE) → Restart.

**Non** in questa fase: Office, 20 app, WU completo (quello è Fase 4 / workflow).

---

### Fase 3 — Primo boot Windows + “Setup client”

```
specialize / oobe / AutoLogon
FirstLogonCommands:
  • scrive EnrollToken + EnrollServer (public_url) in registry
  • avvia postinstall.ps1

postinstall.ps1:
  1. Wait-NetworkReady verso EnrollServer
  2. POST {EnrollServer}/api/deploy/enroll  (deploy_tokens)
  3. riceve session_key + pw_id (+ conferma base URL)
  4. apre deploy-client (progress UI) se abilitato
  5. scarica/installa NovaSCMAgent (servizio)
  6. agent si registra e inizia poll
```

Equivalente SCCM: primo boot + install client + ripresa TS.

---

### Fase 4 — Workflow post-OS (Task Sequence “full OS”)

```
Agent esegue workflow assegnato (CR o default PXE):
  driver extra, Windows Update, runtime, domain join se non fatto,
  WiFi 802.1X, app, reboot con resume, cleanup
Deploy-client mostra progresso (poll pc_workflows)
Fine workflow → CR/stato “completed”
```

Equivalente SCCM: passi TS dopo “Setup Windows and ConfigMgr”.

---

### Fase 5 — Day-2

```
NovaSCMAgent permanente:
  • heartbeat / inventory
  • nuovi workflow assegnati dalla console
  • (futuro) catalogo app, update policy, compliance
```

Equivalente SCCM: client + Software Center / policy (versione ridotta all’inizio).

---

## C.6 Known / Unknown in NovaSCM

| | Known computer | Unknown computer |
|--|----------------|------------------|
| **Prima** | Admin crea **CR** (MAC, nome, domain, workflow) in NovaSCM.exe | Nessuna CR |
| **Al PXE** | Match MAC → usa CR | `auto_provision=1` → crea CR + nome da `pc_prefix`+MAC |
| **Workflow** | Quello della CR | `pxe_default_workflow_id` (obbligatorio se si vuole deploy utile) |
| **Senza default workflow** | — | Boot/imaging possibili ma **niente** automazione post-OS |

Come SCCM: unknown senza deploy TS utile = macchina “non gestita” dopo l’OS.

---

## C.7 Flusso dati URL (IP che cambiano)

```
Admin PC                Server                    Target PC
────────                ──────                    ─────────
config locale:          settings DB:
  ApiUrl ────────────►    (ascolta su 0.0.0.0:9091)
  ApiKey                    pxe_static_url = https://novascm.sito.it
                            (o http://IP-attuale:9091)

                        boot/unattend/enroll usano
                        SOLO pxe_static_url
                              │
                              ▼
                        Agent salva BaseUrl all'enroll
                        e continua a usarlo
```

Se l’IP del server cambia:

1. Aggiornare DNS (ideale) **oppure** `pxe_static_url` + config admin in NovaSCM.exe  
2. DHCP Option 66 se cambia l’host PXE  
3. PC già enrollati: DNS stabile **oppure** re-enroll / update config agent (da prevedere)

---

## C.8 Cosa è in scope “NovaSCM = SCCM core OSD” (v1)

**Deve funzionare end-to-end:**

- [x] Modello DISM (scelta architetturale)  
- [ ] Settings completi da NovaSCM.exe → server  
- [ ] Public URL obbligatorio e usato ovunque in imaging  
- [ ] WinPE script DISM ripetibile (non solo lab manuale)  
- [ ] Unattend specialize + postinstall + enroll unico  
- [ ] Agent install + un workflow post-OS  
- [ ] CR known + unknown con default workflow  
- [ ] Monitor stato (CR, pc_workflows, pxe status)  

**Esplicitamente dopo (day-2 avanzato, non blocca OSD):**

- Software Center / catalogo app con detection  
- SUP/patch enterprise  
- Collections query-based  
- Multi-DP / multi-site nativo (si può iniziare con un server per sito)  
- Integrazione read-write SCCM esistente  

---

## C.9 Anti-pattern da evitare (lezioni da SCCM + dal lab)

| Anti-pattern | Perché no | Cosa fare |
|--------------|-----------|-----------|
| Maxi-unattend + setup.exe come default | Fragile (offlineServicing, product key) | DISM + unattend minimo |
| IP del lab nel codice | Rompe ogni altro sito | Settings + public URL |
| Due tipi di token enrollment confusi | Enroll silenzioso fallito | Solo `deploy_tokens` per OSD |
| Console che salva solo in locale i parametri deploy | Server “cieco” al PXE | PUT `/api/pxe/settings` |
| Workflow default assente + auto_provision | PC installato ma orfano | Default workflow obbligatorio se auto_provision |
| Agent solo durante il deploy poi rimosso | Non è SCCM | Agent permanente |
| Documentare un solo CT/IP come “il” server | Confusione operativa | `AMBIENTE_*.md` per sito |

---

# Parte D — Sequenza operativa ideale (runbook mentale)

```
[1] Installi Server NovaSCM su host del sito (qualsiasi IP)
[2] Metti content in dist/ (o share) — boot.wim, install.wim, wimboot, …
[3] Da NovaSCM.exe: URL+key → salvi profilo PXE/DISM/domain/workflow
[4] Status verde
[5] DHCP punta a questo host
[6] (Opz.) CR per i MAC che vuoi nominare
[7] Accendi PC → PXE → DISM → Windows → enroll → agent → workflow
[8] Console mostra CR completed + PC gestito
[9] Day-2: assegni altri workflow dall’exe
```

Se un passo fallisce, si debugga **solo quel layer** (rete PXE ≠ DISM ≠ enroll ≠ workflow), come si fa con i log TS di SCCM (`smsts.log` ≈ log startnet + postinstall + agent + API).

---

# Parte E — Diagramma unico di riferimento

```
                    ┌─────────────────────┐
                    │   NovaSCM.exe       │
                    │   (admin console)   │
                    └──────────┬──────────┘
                               │ configura / monitora
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                    SERVER NOVASCM                            │
│  settings | CR | workflow | tokens | content | TFTP | API    │
└───────────┬──────────────────────────────┬───────────────────┘
            │ Fasi 1–2 PXE + DISM          │ Fasi 3–5 enroll + agent
            ▼                              ▼
     ┌─────────────┐                 ┌──────────────┐
     │ WinPE DISM  │──── reboot ────►│ Windows +    │
     │ Apply Image │                 │ NovaSCMAgent │
     └─────────────┘                 └──────────────┘
            ▲                              │
            │         stesso public_url    │
            └──────────────────────────────┘
```

---

# Parte F — Sintesi esecutiva

| Domanda | Risposta di design |
|---------|-------------------|
| Su cosa si ispira NovaSCM? | **Flusso OSD SCCM reale** (TS + DISM + client), non un ghost installer generico |
| Come si installa Windows? | **DISM Apply-Image + bcdboot** in WinPE |
| Chi decide dominio/WIM/workflow? | **Admin via NovaSCM.exe → settings server** (+ CR per macchina) |
| Gli IP sono fissi? | **No.** Base URL / public URL / DHCP di sito |
| Cosa resta dopo il deploy? | **NovaSCMAgent** (come ConfigMgr Client) |
| Cos’è una CR? | Computer association + variabili TS |
| Cos’è un workflow? | Task Sequence (post-OS e day-2) |
| Priorità implementativa | Settings da exe → DISM ripetibile → enroll unico → E2E → day-2 app |

---

## Prossimi documenti / lavoro

1. Implementazione UI settings in NovaSCM.exe (in corso / Claude Code) — deve rispettare **C.4** e **C.1**.  
2. Esecuzione `PIANO_DISM_ENROLLMENT_E2E.md` per portare DISM dal lab al prodotto.  
3. `AMBIENTE_NOVASCM.md` per sito (IP reali, senza confonderli col design).  

---

*Questo file è la specifica di “come deve essere” NovaSCM rispetto a SCCM. In caso di conflitto con codice legacy (setup.exe default, IP in CLAUDE.md), prevale questo design finché non si aggiorna esplicitamente.*
