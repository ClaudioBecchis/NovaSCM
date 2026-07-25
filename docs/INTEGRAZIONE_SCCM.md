# Integrazione NovaSCM ↔ Microsoft Configuration Manager (SCCM)

**Data:** 2026-07-19  
**Obiettivo:** far convivere e collaborare NovaSCM con un site ConfigMgr reale — non sostituire i binari Microsoft e non scaricare un agent “free” da Internet.

---

## 1. Principio

| Ruolo | Chi lo fa |
|-------|-----------|
| **Imaging OSD (PXE, DISM, unattend, enroll base)** | **NovaSCM** (path open-source che controlliamo) |
| **Gestione day-2 Microsoft (app, update, compliance, Software Center)** | **SCCM** + **ConfigMgr Client** |
| **Visibilità unificata / orchestrazione** | **NovaSCM.exe** (console) + API AdminService |

**Non facciamo:** riusare `CcmExec` come motore di NovaSCM, né emulare un Management Point.

**Facciamo:**
1. **Connect** — NovaSCM legge (e in seguito scrive in modo controllato) il site via AdminService  
2. **Hand-off** — dopo DISM/OS, workflow installa `ccmsetup` dal **vostro** site  
3. **Inventory bridge** — stesso PC visto in CR NovaSCM e in SMS_R_System  
4. **Opzionale** — coesistenza NovaSCMAgent + CCM (periodi di migrazione)

---

## 2. Prerequisiti (obbligatori)

Senza questi l’integrazione non parte:

1. **Site Configuration Manager** funzionante (lab o produzione)  
2. **Site code** (es. `P01`)  
3. **Management Point** FQDN raggiungibile dai client  
4. **Cartella Client** del site:
   ```text
   \\<SITE-SERVER>\SMS_<SITECODE>\Client\ccmsetup.exe
   ```
5. Account con permessi:
   - lettura AdminService (e in fase 2: membership collection se serve)
6. Certificati / HTTPS AdminService tipicamente:
   ```text
   https://<SMSProvider>/AdminService/
   ```

L’agent **non si scarica da Microsoft come pacchetto standalone** (confermato docs ufficiali). Si prende solo da questa share.

---

## 3. Architettura target

```
┌──────────────────────┐         AdminService REST          ┌─────────────────────┐
│   NovaSCM.exe        │◄──────────────────────────────────►│  ConfigMgr Site      │
│   Tab SCCM + Deploy  │   devices, collections, status     │  MP / DP / DB        │
└──────────┬───────────┘                                    └──────────▲──────────┘
           │ settings / CR / workflow                                    │
           ▼                                                             │
┌──────────────────────┐     PXE + DISM + postinstall        ccmsetup  │
│   NovaSCM Server     │──────────────────────────────────► PC ────────┘
│   API + content      │     (opz.) NovaSCMAgent
└──────────────────────┘
```

**Due URL configurabili (mai hardcoded):**
- `NovaSCMApiUrl` — server open-source  
- `SccmAdminServiceUrl` + credenziali — site Microsoft  
- `SccmClientShare` — UNC a `...\Client\`  
- `SccmSiteCode`, `SccmMpFqdn`

---

## 4. Fasi di implementazione

### Fase 0 — Inventario ambiente (1 sessione)

- [ ] Site server, site code, MP FQDN  
- [ ] Test share: `dir \\server\SMS_XXX\Client\ccmsetup.exe`  
- [ ] Test AdminService (browser/Postman) con account service  
- [ ] Documentare in `docs/AMBIENTE_NOVASCM.md` (per sito)

### Fase 1 — Connect (sola lettura) ★ priorità

**Obiettivo:** dalla console NovaSCM vedi il mondo SCCM.

| Pezzo | Dettaglio |
|-------|-----------|
| Config UI | Tab Impostazioni → blocco SCCM: URL AdminService, user/pass o Windows auth, site code |
| Service | `SccmAdminServiceClient.cs` (WPF) e/o proxy sul server Flask |
| Query | `GET .../AdminService/wmi/SMS_R_System` (device) |
| | `SMS_Collection`, membership, client activity se esposto |
| UI | Tab **Console SCCM** già presente: popolare con dati reali (oggi spesso stub/demo) |
| Auth | Preferire HTTPS + account dedicato minimo privilegio |

Endpoint tipici (documentazione community/MS):

```http
GET https://<SMSProvider>/AdminService/wmi/SMS_R_System
GET https://<SMSProvider>/AdminService/wmi/SMS_Collection
GET https://<SMSProvider>/AdminService/wmi/SMS_R_System(<ResourceId>)
```

**Criterio di accettazione:** lista device SCCM in NovaSCM.exe senza aprire la console Microsoft.

### Fase 2 — Hand-off client ConfigMgr (post-OSD)

**Obiettivo:** PC imaging con NovaSCM (DISM) riceve l’agent Microsoft e si iscrive al site.

Workflow step (tipo `cmd` / `powershell`):

```bat
\\FILES\SCCM\Client\ccmsetup.exe /mp:%MP% SMSSITECODE=%SITE% /forceinstall
```

Parametri da **settings/CR** (non fissi):

| Variabile | Esempio |
|-----------|---------|
| `SccmClientShare` | `\\cm01\SMS_P01\Client` |
| `SccmMpFqdn` | `cm01.contoso.local` |
| `SccmSiteCode` | `P01` |

Sequenza consigliata nel workflow post-OS:

1. Rete OK + dominio (se richiesto dal site)  
2. Copia locale o esecuzione da UNC di `ccmsetup.exe`  
3. Attendi fine (`ccmsetup.log`)  
4. Verifica servizio `CcmExec`  
5. (Opz.) lascia o disinstalla NovaSCMAgent a seconda della policy

**Criterio di accettazione:** PC uscito da NovaSCM OSD compare in ConfigMgr con client “Active”.

Log da controllare:

```text
C:\Windows\ccmsetup\Logs\ccmsetup.log
```

### Fase 3 — Bridge identità (CR ↔ Resource)

| Azione | Come |
|--------|------|
| Match | Nome PC / SMBIOS GUID / MAC |
| CR NovaSCM | salva `sccm_resource_id` quando trovato |
| UI | link “Apri in SCCM” / stato client |
| Auto | dopo enroll, job che interroga AdminService per hostname |

### Fase 4 — Scritture controllate (solo se serve)

Esempi utili (permessi elevati, da fare con cautela):

- Aggiungere device a **collection** via AdminService (pattern già usato in TS community)  
- Trigger **policy machine** non sempre esposto in modo semplice — spesso si resta su lettura + hand-off  

**Non** obiettivo iniziale: deploy application SCCM da NovaSCM (lasciare alla console MS).

### Fase 5 — Policy prodotto (scelta esplicita)

Documentare per ogni sito una delle tre modalità:

| Modalità | OSD | Day-2 | Agent |
|----------|-----|-------|--------|
| **A. Hybrid** | NovaSCM | SCCM | CCM (+ opz. NovaSCMAgent solo imaging) |
| **B. Parallel** | NovaSCM | entrambi | entrambi (migrazione) |
| **C. Nova only** | NovaSCM | NovaSCM | solo NovaSCMAgent |

Default consigliato se “voglio integrare SCCM”: **A. Hybrid**.

---

## 5. Cosa configurare in NovaSCM.exe

```
Impostazioni → SCCM / ConfigMgr
├── Abilita integrazione: [x]
├── AdminService URL: https://cm01.contoso.local/AdminService
├── Auth: Windows / Basic (service account)
├── Site code: P01
├── Management Point: cm01.contoso.local
├── Client share: \\cm01\SMS_P01\Client
├── Test connessione
└── Salva (locale DPAPI + opz. mirror su server NovaSCM settings)
```

Deploy workflow template built-in:

```text
"Installa ConfigMgr Client"
  step: powershell/cmd ccmsetup da Client share
```

---

## 6. Sicurezza

- Account **dedicato** (non Domain Admin) con diritti ConfigMgr minimi per AdminService  
- Segreti solo DPAPI / env server, mai in git  
- HTTPS e validazione hostname certificato (già direzione CHANGELOG NovaSCM)  
- Non esporre AdminService su Internet senza CMG / design MS  

---

## 7. Cosa non fare

- Cercare `ccmsetup` su download pubblici non ufficiali  
- Copiare `C:\Windows\CCM` da un PC random come “installer”  
- Far dipendere il PXE NovaSCM dal site SCCM (restano indipendenti)  
- Duplicare patch/WU aggressivi su entrambi gli agent senza coordinamento  

---

## 8. Ordine di lavoro consigliato (team)

1. **Fase 0** — avete un site? (sì/no). Se no: lab ConfigMgr o solo path Nova-only.  
2. **Fase 1** — AdminService read-only in tab SCCM.  
3. **Fase 2** — step workflow `ccmsetup` dopo DISM verde.  
4. **Fase 3** — match CR ↔ device.  
5. Solo dopo: collection write / automazioni avanzate.

---

## 9. Criterio “integrazione fatta” (MVP)

- [ ] Da NovaSCM.exe: connessione AdminService OK  
- [ ] Lista device SCCM visibile  
- [ ] Settings: share client + MP + site code configurabili (IP/URL non hardcoded)  
- [ ] Un PC deployato con NovaSCM DISM riceve CCM client e compare nel site  
- [ ] Documento ambiente per il sito compilato  

---

## 10. Riferimenti

- [Client installation methods](https://learn.microsoft.com/en-us/intune/configmgr/core/clients/deploy/plan/client-installation-methods)  
- [How to deploy clients to Windows](https://learn.microsoft.com/en-us/intune/configmgr/core/clients/deploy/deploy-clients-to-windows-computers)  
- [Admin service usage](https://learn.microsoft.com/en-us/intune/configmgr/develop/adminservice/usage)  
- Architettura OSD NovaSCM: `docs/ARCHITETTURA_SCCM_VS_NOVASCM.md`  
- Piano DISM: `docs/PIANO_DISM_ENROLLMENT_E2E.md`  

---

*Integrazione = site reale Microsoft + hand-off + console unificata. Non = agent MS redistribuito open-source.*
