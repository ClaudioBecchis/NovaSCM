# NovaSCM — Bug Analysis Round 9
## Versione: v2.1.0 | Commit: 7f0a55c

## Status Round 8 — Tutti Risolti ✅

| ID | Descrizione | Status |
|---|---|---|
| C-1 | POST /step — nessun @require_auth | ✅ RISOLTO |
| C-2 | GET /steps — nessun @require_auth | ✅ RISOLTO |
| C-3 | /download/deploy-screen — nessun @require_auth | ✅ RISOLTO |
| M-1 | delete_cr — orphan pc_workflows | ✅ RISOLTO |
| M-2 | version.json IP privato hardcoded | ✅ RISOLTO |
| R-2 | FK senza PRAGMA foreign_keys | ✅ RISOLTO |
| I-1 | elapsed_sec mai inviato | ✅ RISOLTO |
| I-2 | API key visibile in Task Manager | 🔵 PENDING |
| I-3 | Test usa endpoint non esistenti | 🔵 PENDING |

---

## Nuovi Bug v2.1.0

### 🔴 C-4 — API Key esposta nel DOM
**File**: `server/api.py` → `ui_index()` ~L1970

```python
html = html.replace("</head>", f'<meta name="x-api-key" content="{API_KEY}">\n</head>')
```

La chiave API viene iniettata nel meta tag HTML in chiaro. Chiunque faccia "View Source" la vede.

**Fix**: Eliminare il meta tag. Implementare login form o sessione cookie firmata.

---

### 🔴 C-5 — OSD scaricato via HTTP senza SHA256
**File**: `deploy/postinstall.ps1` ~L52

```powershell
Invoke-WebRequest -Uri "$PXE/NovaSCM.exe" -OutFile $osdExe -UseBasicParsing
Start-Process $osdExe ...
```

Binario scaricato via HTTP puro, senza auth e senza verifica SHA256. Rischio RCE via MITM.

**Fix**: Usare `$SERVER` (HTTPS), aggiungere `X-Api-Key`, verificare SHA256 prima di eseguire.

---

### 🟡 M-3 — URL agent installer errato
**File**: `deploy/postinstall.ps1` ~L183

```powershell
Invoke-WebRequest -Uri "$SERVER/agent/install.ps1"  # ❌ route non esiste
# Corretto:
Invoke-WebRequest -Uri "$SERVER/api/download/agent-install.ps1"  # ✅
```

---

### 🟡 M-4 — CORS wildcard su tutti gli endpoint
```python
response.headers["Access-Control-Allow-Origin"] = "*"
```
**Fix**: Restringere con `NOVASCM_CORS_ORIGINS` env var.

---

### 🔵 I-4 — /deploy-client esposta senza autenticazione
Route `/deploy-client` e `/web/<path>` servono HTML senza auth.

### 🔵 I-5 — deploy/deploy.html con credenziali hardcoded
File distribuito ai client con IP e API key nel sorgente JS.

---

## Priorità Fix
`C-4` → `C-5` → `M-3` → `M-4` → `I-4` → `I-5`
