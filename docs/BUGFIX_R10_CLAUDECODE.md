# NovaSCM — Bug Report Round 10
**Versione:** v2.1.0 | **Commit:** c3ca934 | **Stack:** Python/Flask + C# WPF

## Status Round 1-9: 7/15 risolti

| ID | Descrizione | Status |
|---|---|---|
| C-1..C-3 | Auth mancante su 3 route | ✅ RISOLTO |
| M-1, M-2, R-2, I-1 | Orphan records, IP hardcoded, FK, elapsed_sec | ✅ RISOLTO |
| C-4, C-5, M-3, M-4, I-4, I-5 | API key DOM, OSD HTTP, URL errato, CORS, UI no-auth | 🔴 PENDING |
| I-2, I-3 | Task Manager key, test endpoints | 🔵 PENDING |

## Nuovi Bug Round 10

### 🔴 C-6 — pxe_boot_script() senza auth → enumerazione MAC
Endpoint `/api/boot/<mac>` pubblico. Con `pxe_auto_provision=1` (default) crea automaticamente CR con credenziali di dominio per MAC non verificati.

**Fix:** HMAC token nell'URL iPXE; `pxe_auto_provision=0` default.

### 🔴 C-7 — Catena DOM key + CORS → account takeover
C-4 (key nel meta tag) + M-4 (CORS wildcard) = accesso completo cross-origin a tutte le API autenticate.

**Fix:** rimuovere meta tag + restringere CORS (entrambi necessari).

### 🟡 M-5 — Auto-provisioning con credenziali dominio non verificate
`pxe_auto_provision=1` deposita `default_join_pass`/`default_admin_pass` in CR create per MAC anonimi.

### 🟡 M-6 — deploy_start() cancella storico deploy senza archivio
`DELETE FROM pc_workflows WHERE pc_name=?` distrugge tutto lo storico ad ogni riavvio.

### 🟡 M-7 — API key in chiaro negli installer .ps1/.sh generati
`$ApiKey = "..."` in chiaro nel corpo del file scaricabile.

### 🔵 I-6 — Race condition boot_count
`UPDATE boot_count+1` non serializzato sotto carico.

### 🔵 I-7 — Connessione SQLite per ogni request
`Open()` ad ogni handler → contesa WAL sotto carico deploy multipli.

---
## Priorità Fix
`C-4+C-7` → `M-4+C-7` → `C-5` → `M-3` → `M-5` → `M-6,M-7` → `C-6,I-6,I-7`
