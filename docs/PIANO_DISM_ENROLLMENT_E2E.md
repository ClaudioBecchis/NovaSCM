# Piano implementativo — DISM in produzione + enrollment E2E

**Progetto:** NovaSCM (`ClaudioBecchis/NovaSCM`)  
**Data:** 2026-07-19  
**Stato partenza:** catena PXE OK in lab; desktop Win11 raggiunto 2× via DISM **solo** su CT104 (iniezione manuale `boot.wim`); enrollment post-desktop **non ritestato** dopo `Wait-NetworkReady`; `api.py` produzione ancora su path setup.exe/ImageInstall.  
**Obiettivo:** portare il flusso “stile SCCM” (DISM Apply-Image + bcdboot + unattend specialize) nel codice ufficiale e chiudere enrollment → agent → workflow.

---

## 0. Principi

1. **Non inventare path/IP** — verificare sempre host live (CT104 lab vs CT112 prod vs doc CT103 obsoleto).
2. **Un solo percorso di enrollment** per il deploy OSD: `deploy_tokens` + `POST /api/deploy/enroll`.
3. **DISM in WinPE**, non `setup.exe` full unattend (offlineServicing fragile su ISO Retail).
4. **Ogni step del piano finisce con un criterio di accettazione verificabile** (log, HTTP status, file, screenshot).
5. **Nessuna modifica rete/DHCP/Proxmox di produzione** senza conferma esplicita dell’utente.
6. **Non committare asset binari** (`ipxe.efi`, `*.wim`, `wimboot`) — restano in `server/dist/` (gitignore).

---

## 1. Situazione attuale (baseline)

| Area | Stato | Dove |
|------|--------|------|
| PXE DHCP→TFTP→iPXE→`/api/boot/{mac}` | OK lab | CT104 / VM105, docs PXE luglio 2026 |
| Asset WinPE in `server/dist/winpe/` | Presenti in lab | `wimboot`, `BCD`, `boot.sdi`, `boot.wim`, `install.wim` |
| Apply OS via DISM | Validato **solo** lab | Script iniettato in `boot.wim` CT104, **non** in `api.py` |
| Apply OS via setup.exe | Codice prod | `_build_autounattend_xml_pxe()` + ImageInstall |
| ProductKey GVLK Retail | Fixato | Rimosso da generator (commit storico) |
| FirstLogon + registry EnrollToken | OK in lab | unattend specialize |
| `postinstall.ps1` + `Wait-NetworkReady` | In repo, non ritestato E2E | `deploy/postinstall.ps1` |
| `POST /api/deploy/enroll` | Codice OK | Legge `deploy_tokens` |
| Dual token (`enrollment_tokens` vs `deploy_tokens`) | Confusione operativa | `api.py` |
| NovaSCMAgent.exe | Compilabile, path download OK | `/api/download/agent` |
| DeployScreen .exe + route | Non flusso standard | Usare `deploy-client.html` |
| Test pytest | 148/149 (1 fail se wimboot presente) | Ambiente non isolato |
| Docs IP/CT | Inconsistenti | CLAUDE.md CT103 / .1.100 vs realtà CT112/104 |

---

## 2. Flusso target (end-to-end)

```
[Console] CR + workflow (o pxe_default_workflow_id)
    │
    ▼
[PC/VM] PXE boot → TFTP ipxe.efi → HTTP /api/boot/{mac}
    │         auto host + CR se MAC sconosciuto
    ▼
[iPXE] wimboot + BCD + boot.sdi + boot.wim (+ optional autounattend non usata per ImageInstall)
    │
    ▼
[WinPE] startnet.cmd (generato/iniettato in modo ripetibile):
    1. rete OK (opzionale wait)
    2. mappa share install.wim OPPURE usa WIM già in RAM/HTTP
    3. diskpart GPT (EFI + MSR + Windows)
    4. dism /Apply-Image /ImageFile:... /Index:N /ApplyDir:W:\
    5. bcdboot W:\Windows /s S: /f UEFI
    6. copia unattend-specialize.xml → W:\Windows\Panther\unattend.xml
    7. copia postinstall.ps1 → W:\Windows\Temp\
    8. (opz.) LabConfig bypass offline hive
    9. wpeutil reboot
    │
    ▼
[Windows] boot disco → specialize/oobe → AutoLogon
    FirstLogonCommands:
      - scrivi HKLM\SOFTWARE\NovaSCM EnrollToken + EnrollServer
      - esegui postinstall.ps1
    │
    ▼
[postinstall.ps1]
    Wait-NetworkReady → POST /api/deploy/enroll
    → session_key + pw_id
    → apri deploy-client.html (Edge kiosk)
    → scarica/installa NovaSCMAgent
    → agent poll workflow
    │
    ▼
[Agent] step workflow → report server → day-2 management
```

---

## 3. Fasi di lavoro

### Fase A — Documentazione e allineamento ambiente (0.5–1 giorno)

| ID | Task | File / azione | Accettazione |
|----|------|----------------|--------------|
| A1 | Tabella host live: chi fa cosa (lab CT104, prod CT112, IP, API key env) | `docs/AMBIENTE_NOVASCM.md` (nuovo) | Un solo file con IP/CT/servizi verificati `systemctl`/`pct list` |
| A2 | Marcare CLAUDE.md sezioni CT103/.1.100 come **STORICO** | `CLAUDE.md` | Nota in testa: “IP di riferimento → AMBIENTE_NOVASCM.md” |
| A3 | Checklist pre-test VM (cpu host, Secure Boot off, disco SATA, boot order) | Sezione in questo piano + wiki | Lista copiabile per ogni test |

**Non toccare:** DHCP UniFi, CT112 Reborn, finché A1 non è firmato.

---

### Fase B — Portare DISM nel prodotto (2–4 giorni)

Obiettivo: **riproducibile da repo**, non solo “WIM patchato a mano una volta”.

#### B1 — Script WinPE canonico nel repo

| ID | Task | File | Accettazione |
|----|------|------|--------------|
| B1.1 | Creare `deploy/winpe/startnet_dism.cmd` (o `.ps1` lanciato da cmd) basato sullo script lab validato | `deploy/winpe/startnet_dism.cmd` | Contenuto reviewabile in git; variabili documentate |
| B1.2 | Parametri esterni (non hardcode IP lab): `SERVER_URL`, `INSTALL_WIM` (UNC o path), `IMAGE_INDEX`, `PC_NAME` | stesso + commenti | Default da env o file `X:\novascm-pe.ini` se serve |
| B1.3 | Logging obbligatorio su `X:\pxe-startnet-last.log` e/o share | script | Ogni run lascia log con timestamp |
| B1.4 | `diskpart` script separato o inline documentato | `deploy/winpe/diskpart_gpt.txt` | GPT: EFI 260MB, MSR 16MB, resto Windows NTFS |

**Contenuto minimo `startnet_dism.cmd` (logica, non copia cieca):**

1. Attendi rete (ping gateway o TCP verso server).
2. Monta SMB se mode=smb (`net use`).
3. `diskpart /s diskpart_gpt.txt`.
4. `dism /Apply-Image /ImageFile:... /Index:%IMAGE_INDEX% /ApplyDir:W:\`.
5. `bcdboot W:\Windows /s S: /f UEFI`.
6. Copia `unattend-specialize.xml` e `postinstall.ps1`.
7. `wpeutil reboot`.

#### B2 — Unattend solo specialize/oobe (non ImageInstall full)

| ID | Task | File | Accettazione |
|----|------|------|--------------|
| B2.1 | Nuova funzione `_build_unattend_specialize_xml(pc, cr, token, server)` | `server/api.py` | XML senza ProductKey; FirstLogon scrive token + lancia postinstall |
| B2.2 | Endpoint `GET /api/unattend-specialize/<pc_name>` (subnet allow-list come autounattend) | `api.py` | 200 XML; 404 PC sconosciuto; test pytest |
| B2.3 | Deprecare per path DISM l’uso di ImageInstall in WinPE (tenere vecchio endpoint per compat temporanea) | `api.py` + README | Flag setting `pxe_apply_mode=dism|setup` default `dism` |

#### B3 — iPXE / boot: WinPE che esegue startnet, non setup full

| ID | Task | File | Accettazione |
|----|------|------|--------------|
| B3.1 | Script iPXE da `/api/boot/{mac}` carica **indice WinPE** (tipicamente index 1) di `boot.wim`, non “Windows Setup” come se fosse install | `api.py` generazione script iPXE | Documentato index usato |
| B3.2 | Procedura **ripetibile** di injection `startnet.cmd` in `boot.wim` | `deploy/winpe/README.md` + script `deploy/winpe/inject_startnet.ps1` | Un comando da Windows admin: inject + verify |
| B3.3 | Opzionale: servire `startnet_dism.cmd` via HTTP e copiarlo in PE all’avvio se PE ha rete prima di DISM | endpoint statico o `/api/pxe/file/` whitelist estesa | Solo se injection WIM è troppo pesante |

**Nota:** finché l’injection è manuale, il “prodotto” non è completo. B3.2 è **obbligatorio** per chiudere la fase.

#### B4 — Setting e default

| ID | Task | Accettazione |
|----|------|--------------|
| B4.1 | Settings: `pxe_apply_mode`, `pxe_image_index`, `pxe_install_wim_mode` (smb/http), `pxe_install_wim_path` | Esposti in `/api/settings` se possibile; altrimenti documentare write DB |
| B4.2 | Rimuovere default path obsoleti (`.201` o lab `.104`) dai default di codice — usare stringa vuota + errore chiaro | Boot fallisce con messaggio in log, non path fantasma |
| B4.3 | Documentare verifica indice: `Dism /Get-WimInfo` su ISO in uso | Sezione in `server/dist/README.md` |

#### B5 — Test automatici Fase B

| ID | Test | Accettazione |
|----|------|--------------|
| B5.1 | XML specialize contiene FirstLogon + nessuna ProductKey | pytest |
| B5.2 | Endpoint unattend-specialize 404/200 | pytest |
| B5.3 | Fix test `test_allowed_file_missing_returns_404`: isolare `_WINPE_DIR` a tmp senza file | 149/149 anche con dist/ popolato |
| B5.4 | Script inject dry-run su WIM di test (opz. CI skip se no WIM) | exit 0 locale |

**Exit criteria Fase B:** da repo pulito + dist asset + un inject, un operatore ottiene WinPE che fa DISM senza editare a mano il WIM “a memoria”.

---

### Fase C — Enrollment unico e postinstall (1–2 giorni)

| ID | Task | File | Accettazione |
|----|------|------|--------------|
| C1 | Documentare e imporre: OSD usa **solo** `deploy_tokens` + `/api/deploy/enroll` | `docs/` + commenti `api.py` | Tabella token in AMBIENTE o FAQ |
| C2 | Autounattend/specialize scrive token creato in `deploy_tokens` (stesso pezzo usato da enroll) | `api.py` generator | Token in DB e in XML/registry coerenti |
| C3 | Verificare `Wait-NetworkReady` (host:porta, 20×3s) | `deploy/postinstall.ps1` | Log “network ready” o fail esplicito |
| C4 | Se enroll fallisce: **non** silenziare in demo senza log | `postinstall.ps1` | File log `C:\ProgramData\NovaSCM\postinstall.log` |
| C5 | Dopo enroll: aprire **solo** `deploy-client.html` (non dipendere da DeployScreen.exe) | `postinstall.ps1` | URL con `pw_id` + `key` |
| C6 | Download agent via `/api/download/agent?key=` + install service | postinstall o step workflow | Servizio/processo agent presente |
| C7 | Test pytest enroll: token monouso, secondo uso 401, token sbagliato 401 | `tests/test_api.py` | Pass |

**Exit criteria Fase C:** da desktop già installato (o mock), enroll produce `session_key` + `pw_id` e compare richiesta nei log server.

---

### Fase D — Ritest E2E lab (1–2 sessioni, con utente)

Ambiente consigliato: **CT104 + VM105** (già usati), **non** toccare CT112 finché E2E non è verde.

| ID | Step | Criterio PASS |
|----|------|----------------|
| D1 | VM: `cpu: host`, Secure Boot off (`pre-enrolled-keys=0`), disco **SATA**, RAM ≥ 4GB | Config `qm config` |
| D2 | Boot order test: prima `net0` per DISM; dopo apply, gestire boot disco (attenzione: `qm set --boot` non vale su reboot guest interno — stop/start o ordine corretto nella stessa sessione QEMU) | Documentato in log sessione |
| D3 | CR pre-creata con workflow + MAC VM **oppure** default workflow PXE | Record in DB |
| D4 | PXE → WinPE → log startnet mostra DISM 100% + bcdboot OK | `pxe-startnet-last.log` |
| D5 | Reboot → desktop Windows (o OOBE AutoLogon) | Screenshot / console |
| D6 | Registry `HKLM\SOFTWARE\NovaSCM` ha EnrollToken (o già consumato) | `reg query` |
| D7 | Server riceve `POST /api/deploy/enroll` 200 | log Flask / nginx |
| D8 | `pc_workflows` in running; deploy-client raggiungibile | UI o API GET |
| D9 | Agent scaricato/in esecuzione; almeno 1 step reported | API + processo |
| D10 | (Opz.) Reboot mid-workflow resume | step successivo dopo reboot |

**Se D7 fallisce:** catturare `postinstall.log`, timing rete, token table (`deploy_tokens` used/expires).  
**Se D4 fallisce:** non mescolare con enrollment — fixare solo WinPE/DISM.

**Exit criteria Fase D:** checklist D4–D9 PASS su **un** ciclo non interrotto da `qm stop` a metà.

---

### Fase E — Produzione e pulizia (dopo D verde)

| ID | Task | Rischio | Note |
|----|------|---------|------|
| E1 | Decidere destinazione prod: CT112 (sostituire Reborn) vs nuovo CT | Alto | **Conferma utente** |
| E2 | Deploy api.py + pxe_server + dist + agent.exe + systemd | Medio | Backup DB prima |
| E3 | DHCP Option 66/67 verso host prod | Alto | **Conferma utente** |
| E4 | Allineare `version.json` server a client (es. 2.5.x) | Basso | |
| E5 | Deprecare path setup.exe o lasciare solo se `pxe_apply_mode=setup` | Basso | |
| E6 | Stub tab App/Cert: fuori scope di questo piano | — | Backlog day-2 SCCM |

---

## 4. File da creare / modificare (checklist codice)

### Nuovi

| File | Scopo |
|------|--------|
| `docs/PIANO_DISM_ENROLLMENT_E2E.md` | Questo piano |
| `docs/AMBIENTE_NOVASCM.md` | Host, IP, API key env, cosa gira dove |
| `deploy/winpe/startnet_dism.cmd` | Script DISM canonico |
| `deploy/winpe/diskpart_gpt.txt` | Partizioni |
| `deploy/winpe/inject_startnet.ps1` | Injection ripetibile in boot.wim |
| `deploy/winpe/README.md` | Come buildare PE e parametri |
| `deploy/winpe/unattend-specialize.template.xml` | Opz. template reviewabile |

### Modifiche

| File | Modifica |
|------|----------|
| `server/api.py` | `_build_unattend_specialize_xml`, endpoint, setting `pxe_apply_mode`, iPXE index WinPE, default path, commenti dual-token |
| `server/tests/test_api.py` | specialize + enroll + fix wimboot missing isolato |
| `deploy/postinstall.ps1` | log file obbligatorio; fail esplicito; solo deploy_tokens path |
| `server/dist/README.md` | Indice WIM, DISM mode, inject |
| `server/Dockerfile` | Documentare volume `dist/` o multi-stage note (TFTP in Docker resta limitato) |
| `CLAUDE.md` | Puntatore ad AMBIENTE + piano; non lasciare bug C-1 come aperti se già fix |
| `wiki/Deploy-Windows.md` | Flusso DISM per utenti |

### Non obiettivo di questo piano

- Tab App/Cert/OPSI reali  
- Software Center  
- NovaSCM-Electron / Avalonia  
- Sostituzione completa AdminService SCCM  
- Compilazione obbligatoria `NovaSCMDeployScreen.exe`

---

## 5. Mapping “mancanze SCCM” coperti da questo piano

| Pezzo SCCM | Copertura piano |
|------------|-----------------|
| WinPE + Apply Image (DISM) | Fase B + D |
| Task Sequence variabili (nome PC, domain, token) | CR + unattend specialize |
| Setup Windows / FirstLogon | B2 + C |
| Install agent + policy pull | C6 + D9 |
| Content apps / collections / SUP | **Fuori scope** (backlog) |

---

## 6. Rischi e mitigazioni

| Rischio | Mitigazione |
|---------|-------------|
| `qm set --boot` ignorato su reboot guest | Stop/start QEMU o boot order corretto prima del ciclo; documentato in D2 |
| OVMF riparte da PXE dopo disco bootabile | Dopo D4, forzare boot disco; NVRAM note in AMBIENTE |
| ISO Retail vs indice WIM sbagliato | `Get-WimInfo` obbligatorio B4.3 |
| Token sbagliato (`enrollment_tokens`) | C1–C2; naming in log |
| CPU `kvm64` crash wimboot | VM test sempre `cpu: host` |
| Disco virtio senza driver PE | Solo SATA in lab |
| Touch rete prod | Mai in A–D senza conferma |
| Test agent su `Y:\` Access denied | Eseguire test da disco locale `C:\build\` o copia tree |

---

## 7. Ordine di esecuzione consigliato (sintesi)

```
A1–A3  ambiente e docs
  → B1 script DISM in git
  → B2 unattend specialize + API
  → B3 inject ripetibile + iPXE
  → B4 settings/default
  → B5 pytest verdi
  → C1–C7 enrollment + log
  → D1–D10 E2E lab (utente presente per QM/DHCP se serve)
  → E solo dopo D verde + conferma esplicita
```

Stima complessiva: **~1–1.5 settimane** calendario se lab disponibile; meno se B3 inject è già automatizzabile in 1 sessione.

---

## 8. Definition of Done (prodotto)

- [ ] `pxe_apply_mode=dism` documentato e default
- [ ] Script DISM + inject procedure in repo
- [ ] Unattend specialize da API senza ProductKey
- [ ] Un solo path token per OSD
- [ ] postinstall logga e fa enroll dopo rete ready
- [ ] E2E lab: PXE → desktop → enroll 200 → agent vivo → ≥1 step
- [ ] pytest ≥149 pass con dist/ popolato (test file missing isolato)
- [ ] `docs/AMBIENTE_NOVASCM.md` riflette la realtà
- [ ] Nessuna modifica DHCP/CT prod senza conferma scritta utente

---

## 9. Prima azione immediata (prossima sessione)

1. Creare `docs/AMBIENTE_NOVASCM.md` verificando con comandi live CT/IP (A1).  
2. Copiare lo `startnet` lab validato in `deploy/winpe/startnet_dism.cmd` (B1.1) e diff con memoria `novascm-pxe-dism-breakthrough`.  
3. Aggiungere endpoint unattend-specialize (B2) + 3 test.  
4. Non toccare UniFi/CT112.

---

## 10. Riferimenti interni

- `docs/PXE_STATUS_20260705_CLAUDECODE.md` — catena rete, CT112 Reborn, asset dist  
- `docs/PXE_VM105_PROBLEMI_20260715.md` / `20260716.md` — wimboot, cpu host, fallimenti setup.exe  
- Memoria: `novascm-pxe-dism-breakthrough-20260716.md` — DISM validato, ProductKey, dual token, Wait-NetworkReady  
- `deploy/postinstall.ps1` — enroll + network wait  
- `server/api.py` — boot, autounattend, enroll, download agent  
- `CLAUDE.md` — visione (parte IP obsoleta)

---

*Piano creato per esecuzione guidata. Non autorizza da solo modifiche a DHCP, CT di produzione o firmware Proxmox.*
