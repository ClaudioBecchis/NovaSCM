# NovaSCM — Analisi Bug Post-Deploy v1.9.1
## Report per Claude Code — Patch v1.9.2

**Data:** 2026-03-10  
**Repository:** https://github.com/ClaudioBecchis/NovaSCM  
**Commit analizzati:** `ae0aac30ca` (v1.9.0) + `d3eac562ec` (v1.9.1)

---

## CORREZIONE ANALISI PRECEDENTE

Le 4 issue segnalate nel controllo precedente erano **errate**. Verifica puntuale:

| Issue segnalata | Verdetto | Motivazione |
|---|---|---|
| C-1 Mismatch screenshot `image_b64` | ❌ FALSO | Server legge `data.get("image_b64")` (riga 940), client invia `image_b64` → **corrispondono** |
| C-2 Endpoint `/hardware`, `/log`, `/screenshot` assenti | ❌ FALSO | Esistono come `post_pc_workflow_hardware/log/screenshot` (righe 907/921/936) |
| M-1 `pwId=0` se server non restituisce `id` | ❌ FALSO | Server restituisce `dict(pw)` che include sempre `id` dalla SELECT |
| M-2 `SendLogAsync` campo `text` vs server | ❌ FALSO | Server legge `data.get("text")` (riga 925), client invia `{ text }` → **corrispondono** |

---

## BUG REALI TROVATI (3)

---

### 🟡 R-1 — `[DllImport("user32.dll")]` senza `[SupportedOSPlatform("windows")]`

**File:** `NovaSCMAgent/Worker.cs`  
**Riga:** 137-138

**Problema:**  
Il metodo P/Invoke `GetSystemMetrics` è dichiarato senza attributo `[SupportedOSPlatform("windows")]`. L'analizzatore Roslyn CA1416 emette un warning perché `user32.dll` è Windows-only.

```csharp
// ATTUALE — manca l'attributo
[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern int GetSystemMetrics(int nIndex);
```

Non è un bug runtime (il metodo è raggiungibile solo da `CaptureScreenshotWindows` che ha già `[SupportedOSPlatform("windows")]`), ma causa **warning di build CA1416** che in pipeline CI con `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` bloccherebbe la compilazione.

**Fix — aggiungere l'attributo:**
```csharp
[System.Runtime.InteropServices.DllImport("user32.dll")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
private static extern int GetSystemMetrics(int nIndex);
```

---

### 🟡 R-2 — FK `ON DELETE SET NULL` su `pxe_hosts.cr_id` non garantita

**File:** `server/api.py`  
**Tabella:** `pxe_hosts`

**Problema:**  
La tabella `pxe_hosts` definisce:
```sql
cr_id INTEGER REFERENCES cr(id) ON DELETE SET NULL
```

SQLite **non applica vincoli FK per default**. Serve `PRAGMA foreign_keys = ON` attivato per ogni connessione. Se `get_db_ctx()` non lo attiva, eliminare una CR lascerà `pxe_hosts.cr_id` sporco con un ID ormai inesistente, causando errori silenti nelle JOIN delle query di listing.

**Fix — verificare `get_db_ctx` e aggiungere il PRAGMA se assente:**

```python
@contextmanager
def get_db_ctx():
    conn = sqlite3.connect(DB)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")   # ← aggiungere se assente
    conn.execute("PRAGMA journal_mode = WAL")
    try:
        yield conn
    finally:
        conn.close()
```

> **Nota:** verificare il file prima di applicare — il PRAGMA potrebbe già essere presente.

---

### 🔵 R-3 — Default `pxe_iventoy_ip` uguale all'IP di NovaSCM

**File:** `server/api.py`  
**Funzione:** `_PXE_SETTINGS_DEFAULTS`

**Problema:**  
```python
_PXE_SETTINGS_DEFAULTS = {
    "pxe_iventoy_ip":   "192.168.20.110",   # ← stesso IP del server NovaSCM
    "pxe_iventoy_port": "10809",
    ...
}
```

Il default `192.168.20.110` è l'IP del server NovaSCM stesso. Se iVentoy gira su un host separato (scenario comune), il boot iPXE andrà su `chain http://192.168.20.110:10809/boot.ipxe` e fallirà silenziosamente — iPXE cadrà su `local_boot` senza errore esplicito, rendendo difficile il debug.

**Fix — cambiare il default a stringa vuota + aggiungere validazione nell'endpoint `/api/boot/<mac>`:**

```python
_PXE_SETTINGS_DEFAULTS = {
    "pxe_iventoy_ip":   "",      # ← vuoto: forza configurazione esplicita
    "pxe_iventoy_port": "10809",
    ...
}
```

E in `pxe_boot_script`, aggiungere un controllo esplicito:

```python
iventoy_ip   = pxe_cfg.get("iventoy_ip", "")
iventoy_port = pxe_cfg.get("iventoy_port", "10809")

if action == "deploy" and not iventoy_ip:
    log.warning("PXE: iVentoy IP non configurato — fallback local boot per MAC=%s", norm_mac)
    return _ipxe_local(f"{pc_name} (iVentoy non configurato)"), 200
```

---

## RIEPILOGO

| ID | Gravità | File | Fix |
|---|---|---|---|
| R-1 | 🟡 Media | `NovaSCMAgent/Worker.cs` | Aggiungere `[SupportedOSPlatform("windows")]` su `GetSystemMetrics` |
| R-2 | 🟡 Media | `server/api.py` | Verificare PRAGMA FK in `get_db_ctx` |
| R-3 | 🔵 Bassa | `server/api.py` | Default `pxe_iventoy_ip` → `""` + guard in `pxe_boot_script` |

---

## STATO GENERALE CODEBASE

Il codice di `v1.9.0` + `v1.9.1` è **solido**. Tutti i componenti principali funzionano correttamente:

- ✅ Flusso hardware collect → invio → visualizzazione in UI
- ✅ Flusso log step → accumulato in `log_text`  
- ✅ Screenshot cattura al completamento → invio → visualizzazione
- ✅ PXE: TFTP + endpoint boot + auto-provisioning MAC
- ✅ Migrazioni DB (`hardware_json`, `log_text`, `screenshot_b64`) presenti
- ✅ `LaunchDeployScreen` → `ResumeAfterReboot` → `ClearState` coerenti

---

*Report generato il 2026-03-10 — NovaSCM v1.9.2 — patch R-1/R-2/R-3*
