# NovaSCM — Debug sorgente GitHub (handoff per Claude)

**Data:** 2026-07-24  
**Repo:** https://github.com/ClaudioBecchis/NovaSCM.git  
**Copia locale verificata:** `Y:\NovaSCM`  
**Branch:** `main`  
**HEAD:** `f24de0a` — `fix(sec): SSRF webhook robusto + rimossa falsa protezione path-traversal`  
**Scope del debug:** **solo codice sorgente** (build, test, analisi statica).  
**Fuori scope (non richiesto):** deploy su CT, Proxmox, DHCP, rete live.

---

## 1. Esito in una riga

Il codice su `main` **compila** e **passa tutta la suite di test**.  
I bug critici documentati in `CLAUDE.md` (C-1…C-4) risultano **già fixati** nel sorgente.  
Restano problemi **non bloccanti** (path `version.json`, alias health, working tree sporco, versioni disallineate).

---

## 2. Ambiente di verifica

| Voce | Valore |
|------|--------|
| OS host | Windows |
| .NET SDK | 9.0.316 |
| Python | 3.14.6 |
| pytest | 9.1.1 |
| Drive DEV | `Y:\` → `\\Truenas\dev` (OK) |

---

## 3. Build

| Progetto | Comando | Esito |
|----------|---------|--------|
| Client WPF | `dotnet build PolarisManager.csproj -c Release` | **0 errori** |
| Agent | `dotnet build NovaSCMAgent\NovaSCMAgent.csproj -c Release` | **0 errori** |
| DeployScreen | `dotnet build DeployScreen\NovaSCMDeployScreen.csproj -c Release` | **0 errori** |

Output WPF: `bin\Release\net9.0-windows\NovaSCM.dll`.

---

## 4. Test

### 4.1 Server Python — **166/166 PASSED**

```bash
cd Y:\NovaSCM\server
# (env tipico documentato in CLAUDE.md)
NOVASCM_API_KEY=test pytest tests/ -q
```

Risultato verificato: `166 passed` (~4.6–4.9s), 1 warning deprecation su `pythonjsonlogger` (dipendenza esterna, non del progetto).

Copertura rilevante: health, auth, CR, workflow, deploy start/step, PXE boot/hosts/settings/file, autounattend, unattend-specialize, deploy-client auth, token scoping, webhook settings, timeout cleanup.

### 4.2 Agent C# — **16/16 PASSED**

```bash
dotnet test NovaSCMAgent.Tests\NovaSCMAgent.Tests.csproj -c Release
```

**Attenzione ambiente:** se eseguito da path SMB `Y:\`, fallisce con:

```text
Win32Exception (5): Accesso negato — testhost.exe
```

**Non è un bug del codice.** Workaround verificato: copiare `NovaSCMAgent.Tests\bin\Release\net9.0\*` su disco locale (es. `%TEMP%\NovaSCMAgent.Tests`) e lanciare:

```bash
dotnet test NovaSCMAgent.Tests.dll --nologo
```

Esito: 16 superati (Worker conditions + StepExecutor).

---

## 5. Bug critici storici (`CLAUDE.md`) — stato nel codice

| ID | Problema | Stato sorgente `main` | Dove |
|----|----------|----------------------|------|
| **C-1** | `DIST_DIR` / `_WINPE_DIR` non definiti → NameError | **FIXATO** | `server/api.py` L56–57 |
| **C-2** | Route `<n>` vs parametro funzione | **FIXATO** | `serve_pxe_file(name)` su `/api/pxe/file/<name>` ~L2521 |
| **C-3** | Autounattend PXE senza `ImageInstall`/`InstallTo` | **FIXATO** | XML ~L2775 + test `test_xml_has_install_to` |
| **C-4** | Dockerfile non copia `pxe_server.py` | **FIXATO** | `server/Dockerfile` L8: `COPY api.py pxe_server.py ./` |

I commenti `// BUG:` / `# BUG:` nel C#/Python sono **annotazioni di fix già applicati**, non ticket aperti.

---

## 6. Problemi reali residui nel sorgente

### P1 — Path `VERSION_FILE` non allineato al layout repo — **MEDIO**

```python
# server/api.py ~L1807
VERSION_FILE = os.path.join(os.path.dirname(DB), "version.json")
EXE_FILE     = os.path.join(os.path.dirname(DB), "NovaSCM.exe")
```

- In repo: `server/version.json` contiene `"version": "2.2.1"`.
- Se `NOVASCM_DB` è tipo `.../data/novascm.db`, il server cerca `.../data/version.json`.
- Se assente → fallback hardcoded **`1.0.0`** in `get_version()` (~L1846).

**Impatto:** auto-update client e client che leggono `/api/version` vedono versione falsa.

**Fix suggerito:** cercare in ordine:
1. `dirname(DB)/version.json`
2. `dirname(abspath(__file__))/version.json` (layout repo/Docker standard)
3. fallback esplicito

Stessa logica per `EXE_FILE` / artefatti download se applicabile.

### P2 — Health solo su `/health`, non su `/api/health` — **BASSO**

```python
@app.route("/health", methods=["GET"])  # ~L3148
def health():
    return jsonify({"status": "ok"})
```

- I test usano correttamente `GET /health`.
- `GET /api/health` non ha route dedicata; matcha il catch-all OPTIONS `/api/<path:_>` → **HTTP 405**.
- Monitoraggi/tool che assumono `/api/health` ottengono falso negativo.

**Fix suggerito:** alias:

```python
@app.route("/api/health", methods=["GET"])
def api_health():
    return health()
```

(o un unico handler con due `@app.route`).

### P3 — Versioni componenti disallineate — **DOCUMENTALE / PACKAGING**

| Componente | File | Versione |
|------------|------|----------|
| Client WPF | `PolarisManager.csproj` | **2.5.0** |
| Server package | `server/version.json` | **2.2.1** |
| Agent | `NovaSCMAgent.csproj` / `agent/version.txt` | **1.0.0** |

Non fa fallire i test; confonde release e auto-update.

### P4 — Working tree non pulito (locale)

```
 M server/web/deploy-client.html          # redesign UI (stile SCCM TSProgress), non committato
?? NovaSCMAgent/publish-win/
?? docs/ARCHITETTURA_SCCM_VS_NOVASCM.md
?? docs/INTEGRAZIONE_SCCM.md
?? docs/PIANO_DISM_ENROLLMENT_E2E.md
```

- Diff `deploy-client.html`: ~451 insert / 390 delete (titolo EN “Installation Progress”, rimossi font Google, UI più sobria).
- **Decisione richiesta:** commit o discard prima di altri lavori UI.

### P5 — Gap funzionale documentato (non fallimento test)

`docs/PIANO_DISM_ENROLLMENT_E2E.md` e commenti in `api.py`:

- Path **setup.exe / ImageInstall** ancora presente come flusso autounattend PXE.
- Path **DISM Apply-Image** validato in lab non è necessariamente l’unico percorso “prodotto” versionato end-to-end.
- I **166 test** coprono l’API; **non** sostituiscono un E2E WinPE reale.

### P6 — IP / CT storici ancora nel testo

Riferimenti a `192.168.1.100` / CT103 restano in `CLAUDE.md` (marcato storico), tooltip XAML, alcuni doc bugfix.  
`agent/install-windows.ps1` default: `YOUR-SERVER-IP` (OK).  
Fonte ambiente live (se serve a Claude per altro): `docs/AMBIENTE_NOVASCM.md` — **non** riletto in questo debug come truth operativa del codice.

### P7 — TFTP senza allow-list subnet (by design documentato)

`server/pxe_server.py`: commento SEC esplicito — tftpy non filtra IP sorgente; mitigazione firewall/VLAN. Non è un bug di regressione recente.

---

## 7. Route `/api/*` senza `@require_auth` (by design)

Verificato staticamente:

| Method | Path | Handler |
|--------|------|---------|
| OPTIONS | `/api/<path:_>` | cors_preflight |
| GET | `/api/boot/<mac>` | pxe_boot_script |
| POST | `/api/deploy/enroll` | deploy_enroll |
| GET | `/api/boot/file/ipxe.efi` | serve_ipxe_efi |
| GET | `/api/pxe/file/<name>` | serve_pxe_file (whitelist file) |
| GET | `/api/autounattend/<pc_name>` | serve_autounattend_pxe |
| GET | `/api/unattend-specialize/<pc_name>` | serve_unattend_specialize |

Protezione attesa: subnet allow-list / token monouso enrollment — non master API key.

---

## 8. Mappa sorgente (riferimento rapido)

```
NovaSCM/
├── PolarisManager.csproj          # WPF client v2.5.0
├── MainWindow.xaml(.cs)           # console (~6.6k LOC .cs)
├── NovaSCMAgent/ + .Tests/        # agent .NET + 16 test
├── DeployScreen/                  # UI deploy WPF
├── agent/novascm-agent.py         # agent Linux
├── server/
│   ├── api.py                     # ~2905 LOC (conteggio locale)
│   ├── pxe_server.py              # TFTP thread
│   ├── tests/test_api.py          # 166 test
│   ├── web/deploy-client.html     # (mod locale non committata)
│   ├── version.json               # 2.2.1
│   └── Dockerfile
└── deploy/                        # autounattend, postinstall, WinPE
```

Ultimi commit rilevanti su `main` (context fix recenti):

```
f24de0a fix(sec): SSRF webhook robusto + rimossa falsa protezione path-traversal
a583705 fix: tab Proxmox rotto sistematicamente, inventario remoto sempre fallito, altri bug agent
57f7b42 fix(sec): regressione whitelist token + 3 crash 500 nel flusso deploy server
59d33c2 fix: crash intermittente scan rete + injection PowerShell residua + 2 bug agent Python
48cb966 fix: su_errore fail-open (entrambi gli agent) + mismatch IT/EN nell'editor step
```

---

## 9. Cosa NON fare / non assumere

1. **Non** dichiarare failure di test se non riprodotti: suite locale era tutta verde.
2. **Non** confondere `testhost.exe Access denied su Y:\` con bug agent.
3. **Non** usare IP CT103 / `192.168.1.100` da `CLAUDE.md` come ambiente live senza `docs/AMBIENTE_NOVASCM.md`.
4. **Non** committare binari WinPE / `server/dist/*.wim` (gitignore / policy piano DISM).
5. Commenti `BUG:` nel codice = storia del fix, non lista TODO automatica.

---

## 10. Lavoro consigliato per Claude (priorità)

Ordine suggerito, solo codice:

1. **P1** — Fix risoluzione `VERSION_FILE` (e test: con DB in tmp e `version.json` accanto ad `api.py`, `/api/version` deve leggere `2.2.1` o valore del file repo).
2. **P2** — Aggiungere `GET /api/health` + test (200, body `status=ok`, no auth).
3. **P4** — Decidere su `deploy-client.html` (commit con messaggio chiaro **oppure** restore da HEAD).
4. **P3** — Allineare o documentare policy versioning (WPF 2.5.0 vs server 2.2.1 vs agent 1.0.0).
5. Opzionale: test che fallisce se `VERSION_FILE` punta solo a `dirname(DB)` senza fallback.

**Regola repo (memoria utente):** bugfixati su NovaSCM → commit + push GitHub, non lasciare solo locale.

---

## 11. Comandi di riproduzione (checklist Claude)

```powershell
cd Y:\NovaSCM
git status -sb
git log -1 --oneline

dotnet build PolarisManager.csproj -c Release --nologo
dotnet build NovaSCMAgent\NovaSCMAgent.csproj -c Release --nologo
dotnet build DeployScreen\NovaSCMDeployScreen.csproj -c Release --nologo

cd server
$env:NOVASCM_API_KEY = "test"
python -m pytest tests/ -q --tb=short

# Agent tests: preferire path locale se Y: nega testhost.exe
$dest = "$env:TEMP\NovaSCMAgent.Tests"
Copy-Item "Y:\NovaSCM\NovaSCMAgent.Tests\bin\Release\net9.0\*" $dest -Recurse -Force
dotnet test "$dest\NovaSCMAgent.Tests.dll" --nologo
```

---

## 12. Verifica onestà di questo report

Controllato realmente in sessione 2026-07-24:

- [x] Build 3 progetti .NET → 0 errori  
- [x] pytest → 166 passed  
- [x] xUnit da path locale → 16 passed  
- [x] Presenza `DIST_DIR`/`_WINPE_DIR`, `serve_pxe_file(name)`, `ImageInstall`, Dockerfile COPY  
- [x] Definizione `VERSION_FILE` e route `/health`  
- [x] `git status` / HEAD hash  
- [ ] **Non** rieseguito in questa handoff un secondo giro di test dopo scrittura di questo file  

Nessun dato inventato: numeri e path sopra sono quelli osservati nella sessione di debug.

---

*File generato per handoff a Claude Code / altro agente. Fonte: debug sorgente Grok su clone locale allineato a GitHub `main`.*
