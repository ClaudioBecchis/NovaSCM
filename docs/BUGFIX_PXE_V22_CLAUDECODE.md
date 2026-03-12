# NovaSCM — Code Review Completa: PXE Integration
## Report per Claude Code — v2.2.1-review (combinato)

**Data:** 2026-03-12
**Repository:** https://github.com/ClaudioBecchis/NovaSCM
**Stack:** Python 3.12 · Flask · SQLite (WAL) · tftpy · Alpine.js
**File analizzati:** `server/api.py` (2585 righe), `server/pxe_server.py` (95 righe), `server/web/index.html` (1665 righe), `server/docker-compose.yml`, `server/Dockerfile`, `server/requirements.txt`, `server/tests/test_api.py`, `deploy/autounattend.xml`, `deploy/postinstall.ps1`, `deploy/pxe.conf`, `docs/PXE_INTEGRATION_CLAUDECODE.md`

---

## ISTRUZIONI PER CLAUDE CODE

> **Questo report contiene tutti i bug trovati nell'integrazione PXE v2.2.0 di NovaSCM.**
>
> **Stato attuale:** la maggior parte degli endpoint PXE è **già implementata** in `api.py`.
> Le tabelle `pxe_hosts` e `pxe_boot_log` esistono. La UI ha il tab PXE.
> I bug qui sotto sono difetti nel codice **già presente**, non feature mancanti.
>
> **Regole:**
> - Fixare i bug nell'ordine indicato (🔴 critici prima, poi 🟡, poi 🔵)
> - NON riscrivere endpoint funzionanti — modificare solo le righe indicate
> - NON aggiungere riferimenti a iVentoy
> - Usare `datetime.now(datetime.timezone.utc)` per tutti i timestamp (MAI `utcnow()`)
> - Rispettare lo stile del codice esistente (Flask, SQLite, `get_db_ctx()`)
> - Ogni fix deve essere testabile con `pytest tests/` senza rompere i 112 test esistenti
> - Per ogni fix: applicare la modifica, verificare che il server si avvii, eseguire i test
>
> ```bash
> cd server && NOVASCM_DB=/tmp/test.db NOVASCM_API_KEY=test pytest tests/ -v
> ```

---

## SOMMARIO

| ID | Severità | File | Descrizione |
|---|---|---|---|
| C-1 | 🔴 CRITICAL | `api.py` | `DIST_DIR` e `_WINPE_DIR` mai definiti → `NameError` a runtime |
| C-2 | 🔴 CRITICAL | `api.py` | Route `<n>` vs parametro `name` in `serve_pxe_file` → `TypeError` |
| C-3 | 🔴 CRITICAL | `api.py` | PXE autounattend manca `<ImageInstall>` + `<InstallTo>` → install manuale |
| C-4 | 🔴 CRITICAL | `Dockerfile` | `pxe_server.py` non copiato nel container → TFTP mai avviato |
| C-5 | 🔴 CRITICAL | `api.py` | API key globale esposta in autounattend PXE senza auth |
| H-1 | 🟠 HIGH | `api.py` | Timestamp misti UTC/locale nello stesso DB (15+12 righe) |
| H-2 | 🟠 HIGH | `api.py` | Nessuna validazione `boot_action` in CRUD create/update |
| H-3 | 🟠 HIGH | `test_api.py` | Zero test per 12 endpoint PXE |
| M-1 | 🟡 MEDIUM | `api.py` | `_get_pxe_settings()` non usa i default di `_PXE_SETTINGS_DEFAULTS` |
| M-2 | 🟡 MEDIUM | `api.py` | `send_file` importato 4 volte localmente dentro funzioni |
| M-3 | 🟡 MEDIUM | `index.html` | `editPxeHost()` e `openPxeHostModal()` sono stub `alert()` |
| M-4 | 🟡 MEDIUM | `deploy/pxe.conf` | Riferimenti stale a iVentoy + `snponly.efi` |
| M-5 | 🟡 MEDIUM | `deploy/autounattend.xml` | Credenziali hardcoded + IP obsoleti |
| I-1 | 🔵 INFO | `api.py` | Cleanup boot log dentro transazione boot (potenziale lentezza) |
| I-2 | 🔵 INFO | `api.py` | `import ipaddress` a riga 1910 invece che in cima |
| I-3 | 🔵 INFO | `api.py` | Due funzioni autounattend divergenti — da unificare |
| I-4 | 🔵 INFO | `CLAUDE.md` | IP network obsoleto (`.110` vs `.103`) |
| I-5 | 🔵 INFO | doc | Template `_build_autounattend_xml` nel doc è minimale vs codice |
| I-6 | 🔵 INFO | doc | `datetime.utcnow()` deprecato proposto nel documento PXE |

---

## 🔴 C-1 · `DIST_DIR` e `_WINPE_DIR` non definiti — server/api.py

**Impatto:** `NameError` a runtime su QUALSIASI richiesta PXE, status, o deploy-screen.

Le variabili `DIST_DIR` e `_WINPE_DIR` sono usate in 7 punti (righe 2237, 2239, 2493, 2495, 2518, 2521, 2577) ma **mai dichiarate** nel file.

**Endpoint rotti:**
- `GET /api/pxe/file/<name>` — serve wimboot/BCD/boot.sdi/boot.wim
- `GET /api/pxe/status` — health check PXE
- `GET /api/download/deploy-screen` — download DeployScreen.exe

**Fix — aggiungere nella sezione di configurazione in cima al file (dopo riga ~120, vicino a `_PUBLIC_URL`):**

```python
# ── PXE file paths ───────────────────────────────────────────────────────────
DIST_DIR   = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dist")
_WINPE_DIR = os.path.join(DIST_DIR, "winpe")
```

**Nota:** `os.path.abspath(__file__)` garantisce path assoluto anche se `api.py` è invocato da una working directory diversa (es. gunicorn). `WEB_DIR` (riga 2527) dovrebbe essere spostato nella stessa sezione per coerenza.

**Test:** `curl http://localhost:9091/api/pxe/status -H "X-Api-Key: test"` deve restituire JSON (non 500).

---

## 🔴 C-2 · Route `<n>` vs parametro `name` — server/api.py riga 2223-2224

**Impatto:** `TypeError` a runtime quando iPXE richiede wimboot/BCD/boot.sdi/boot.wim.

```python
@app.route("/api/pxe/file/<n>", methods=["GET"])
def serve_pxe_file(name: str):    # ← Flask passa `n`, funzione aspetta `name`
```

Flask inietta il parametro URL come keyword argument con il nome usato nella route (`n`), ma la funzione lo aspetta come `name` → `TypeError: serve_pxe_file() got an unexpected keyword argument 'n'`.

**Fix — allineare il parametro della route con la funzione:**

```python
@app.route("/api/pxe/file/<name>", methods=["GET"])
def serve_pxe_file(name: str):
```

---

## 🔴 C-3 · PXE autounattend mancante `<ImageInstall>` + `<InstallTo>` — server/api.py riga ~2354

**Impatto:** WinPE si avvia ma Windows Setup non sa su quale partizione installare → prompt manuale → automazione completamente rotta.

La funzione `_build_autounattend_xml_pxe()` (riga 2269) ha `<InstallFrom>` con `<MetaData><Key>/IMAGE/INDEX</Key>` ma **manca** il wrapper `<ImageInstall><OSImage>` con `<InstallTo>`.

Confrontare con la versione CR `get_autounattend()` (riga 706-714) che lo fa correttamente.

**Fix — sostituire il blocco `<InstallFrom>` nella funzione `_build_autounattend_xml_pxe()` (righe ~2354-2362) con:**

```python
      <ImageInstall>
        <OSImage>
          <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
          <InstallFrom>
            <MetaData wcm:action="add">
              <Key>/IMAGE/NAME</Key><Value>Windows 11 Pro</Value>
            </MetaData>
          </InstallFrom>
          <WillShowUI>OnError</WillShowUI>
        </OSImage>
      </ImageInstall>
```

**Nota:** usa `/IMAGE/NAME` (come la versione CR) invece di `/IMAGE/INDEX` — più affidabile tra diverse ISO.

---

## 🔴 C-4 · Dockerfile non copia `pxe_server.py` — server/Dockerfile riga 8

**Impatto:** nel container Docker, `from pxe_server import ...` fallisce con `ImportError`. Il fallback graceful in api.py (righe 17-27) cattura l'errore silenziosamente, quindi il container si avvia ma il **TFTP non funzionerà mai** in Docker.

```dockerfile
COPY api.py .       # ← solo api.py, manca pxe_server.py
COPY web/ ./web/
```

**Fix:**

```dockerfile
COPY api.py pxe_server.py ./
COPY web/ ./web/
```

---

## 🔴 C-5 · API key globale esposta nell'autounattend PXE — server/api.py riga 2279

**Impatto:** l'API key del server è inserita **in chiaro** nell'autounattend.xml servito dall'endpoint `/api/autounattend/<pc_name>` che è **senza autenticazione** (protetto solo da subnet allow-list). Chiunque sulla subnet PXE può leggere l'API key:

```bash
curl http://192.168.20.110:9091/api/autounattend/QUALSIASI-PC
# → XML con api_key in chiaro nei FirstLogonCommands
```

Con questa chiave si ha accesso completo a tutti gli endpoint autenticati del server.

La versione CR (`get_autounattend`, riga 662-669) usa invece un **enrollment token monouso** — approccio corretto.

```python
# riga 2279 — codice attuale
api_key = _get_setting("api_key", "")
# → poi inserita in: -Headers @{{'X-Api-Key'='{api_key}'}}
```

**Fix — usare enrollment token monouso come fa la versione CR:**

In `serve_autounattend_pxe()`, prima di generare il XML:
```python
with get_db_ctx() as conn:
    enroll_token = _generate_deploy_token(conn, pc_name, None)
    conn.commit()
```

Nel template XML, sostituire il download diretto con il pattern enrollment:
```xml
<!-- Invece di passare X-Api-Key, salvare un token monouso nel registry -->
<SynchronousCommand wcm:action="add">
  <Order>1</Order>
  <CommandLine>reg add "HKLM\SOFTWARE\NovaSCM" /v EnrollToken /d "{enroll_token}" /f</CommandLine>
</SynchronousCommand>
<SynchronousCommand wcm:action="add">
  <Order>2</Order>
  <CommandLine>reg add "HKLM\SOFTWARE\NovaSCM" /v EnrollServer /d "{server_url}" /f</CommandLine>
</SynchronousCommand>
```

Poi il `postinstall.ps1` (che già supporta enrollment token) si occupa del resto.

---

## 🟠 H-1 · Timestamp misti UTC/locale nello stesso DB — server/api.py

**Impatto:** `_cleanup_stale_workflows()` (riga 449) confronta timestamp UTC con workflow che hanno timestamp locali → timeout calcolati male. Ordinamento `pxe_boot_log` per `ts` inconsistente.

**Pattern 1 (locale) — da correggere:**
`datetime.datetime.now().isoformat()` — righe 524, 580, 798, 851, 920, 953, 1091, 1127, 1142, 1188, 1286, 1334, 1433, 1504, 1549

**Pattern 2 (UTC) — già corretto:**
`datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None).isoformat()` — righe 227, 430, 449, 1002, 1026, 1636, 1670, 1729, 1954, 2077, 2143, 2470

**Fix — sostituire ogni occorrenza del Pattern 1 con il Pattern 2:**

```python
# PRIMA (15 righe da trovare e sostituire):
now = datetime.datetime.now().isoformat()

# DOPO:
now = datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None).isoformat()
```

**Nota:** il `replace(tzinfo=None)` serve per compatibilità con record esistenti nel DB (senza suffisso `+00:00`).

---

## 🟠 H-2 · Nessuna validazione `boot_action` — server/api.py

**Impatto:** un valore errato (es. `"deploj"`) viene accettato e salvato nel DB. Il boot endpoint lo tratta silenziosamente come `"local"`.

**Righe:** 2148 (`create_pxe_host`), 2176 (`update_pxe_host`)

**Fix — definire costante e validare in entrambi gli endpoint:**

Definire vicino a `_PXE_SETTINGS_DEFAULTS`:
```python
_BOOT_ACTION_VALID = ("auto", "deploy", "local", "block")
```

In `create_pxe_host()`, dopo la validazione MAC (~riga 2143):
```python
    boot_action = data.get("boot_action", "auto")
    if boot_action not in _BOOT_ACTION_VALID:
        return jsonify({"error": f"boot_action non valido: {boot_action}. Valori ammessi: {', '.join(_BOOT_ACTION_VALID)}"}), 400
```

In `update_pxe_host()`, dentro il blocco di validazione (~riga 2176):
```python
    if "boot_action" in updates and updates["boot_action"] not in _BOOT_ACTION_VALID:
        return jsonify({"error": f"boot_action non valido. Valori ammessi: {', '.join(_BOOT_ACTION_VALID)}"}), 400
```

---

## 🟠 H-3 · Zero test per endpoint PXE — server/tests/test_api.py

**Impatto:** 12 route PXE attive senza copertura test. Bug come C-1, C-2, C-3 non sarebbero stati scoperti in CI.

**File:** `server/tests/test_api.py` (1038 righe, 112 test attuali)

**Fix — aggiungere i seguenti test.** Adattare `AUTH` allo stile dei test esistenti (es. `AUTH = {"X-Api-Key": "test"}`):

```python
# ── PXE Tests ────────────────────────────────────────────────────────────────

class TestPxeBoot:
    """Test per /api/boot/<mac> — endpoint senza auth, subnet allow-list."""

    def test_boot_unknown_mac_auto_provision(self, client):
        """MAC sconosciuto con auto_provision=1 → crea CR + pxe_host, restituisce script iPXE."""
        client.put("/api/pxe/settings",
                   json={"pxe_auto_provision": "1", "pxe_pc_prefix": "TEST"},
                   headers=AUTH)
        resp = client.get("/api/boot/AA:BB:CC:DD:EE:FF")
        assert resp.status_code == 200
        assert b"#!ipxe" in resp.data
        # Verifica host creato
        hosts = client.get("/api/pxe/hosts", headers=AUTH).get_json()
        assert any(h["mac"] == "AA:BB:CC:DD:EE:FF" for h in hosts)

    def test_boot_invalid_mac(self, client):
        """MAC non valido → script iPXE boot locale."""
        resp = client.get("/api/boot/INVALID")
        assert resp.status_code == 200
        assert b"#!ipxe" in resp.data
        assert b"sanboot" in resp.data

    def test_boot_known_mac_with_workflow(self, client):
        """MAC con workflow assegnato → script iPXE deploy con wimboot."""
        wf = client.post("/api/workflows",
                         json={"nome": "PXE-WF", "descrizione": "test"},
                         headers=AUTH).get_json()
        client.post("/api/pxe/hosts",
                    json={"mac": "11:22:33:44:55:66", "pc_name": "PXE-TEST",
                          "workflow_id": wf["id"], "boot_action": "deploy"},
                    headers=AUTH)
        resp = client.get("/api/boot/11:22:33:44:55:66")
        assert resp.status_code == 200
        assert b"wimboot" in resp.data

    def test_boot_blocked_host(self, client):
        """Host con boot_action=block → script iPXE poweroff."""
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:00:00:01", "boot_action": "block"},
                    headers=AUTH)
        resp = client.get("/api/boot/AA:BB:CC:00:00:01")
        assert resp.status_code == 200
        assert b"poweroff" in resp.data

    def test_boot_local_no_workflow(self, client):
        """MAC senza workflow → boot da disco locale."""
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:00:00:02", "boot_action": "auto"},
                    headers=AUTH)
        resp = client.get("/api/boot/AA:BB:CC:00:00:02")
        assert resp.status_code == 200
        assert b"sanboot" in resp.data


class TestPxeHostsCrud:
    """Test CRUD per /api/pxe/hosts."""

    def test_create_host(self, client):
        resp = client.post("/api/pxe/hosts",
                           json={"mac": "AA:BB:CC:DD:EE:01", "pc_name": "TEST-PC",
                                 "boot_action": "auto"},
                           headers=AUTH)
        assert resp.status_code == 201
        assert resp.get_json()["mac"] == "AA:BB:CC:DD:EE:01"

    def test_create_duplicate_mac(self, client):
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:DD:EE:02"}, headers=AUTH)
        resp = client.post("/api/pxe/hosts",
                           json={"mac": "AA:BB:CC:DD:EE:02"}, headers=AUTH)
        assert resp.status_code == 409

    def test_create_invalid_mac(self, client):
        resp = client.post("/api/pxe/hosts",
                           json={"mac": "not-a-mac"}, headers=AUTH)
        assert resp.status_code == 400

    def test_create_invalid_boot_action(self, client):
        resp = client.post("/api/pxe/hosts",
                           json={"mac": "AA:BB:CC:DD:EE:09", "boot_action": "deploj"},
                           headers=AUTH)
        assert resp.status_code == 400

    def test_get_host(self, client):
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:DD:EE:03"}, headers=AUTH)
        resp = client.get("/api/pxe/hosts/AA:BB:CC:DD:EE:03", headers=AUTH)
        assert resp.status_code == 200
        assert resp.get_json()["mac"] == "AA:BB:CC:DD:EE:03"

    def test_get_nonexistent_host(self, client):
        resp = client.get("/api/pxe/hosts/FF:FF:FF:FF:FF:FF", headers=AUTH)
        assert resp.status_code == 404

    def test_update_host(self, client):
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:DD:EE:04", "boot_action": "auto"},
                    headers=AUTH)
        resp = client.put("/api/pxe/hosts/AA:BB:CC:DD:EE:04",
                          json={"boot_action": "block", "notes": "manutenzione"},
                          headers=AUTH)
        assert resp.status_code == 200
        assert resp.get_json()["boot_action"] == "block"

    def test_update_invalid_boot_action(self, client):
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:DD:EE:0A"}, headers=AUTH)
        resp = client.put("/api/pxe/hosts/AA:BB:CC:DD:EE:0A",
                          json={"boot_action": "invalid"},
                          headers=AUTH)
        assert resp.status_code == 400

    def test_delete_host(self, client):
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:DD:EE:05"}, headers=AUTH)
        resp = client.delete("/api/pxe/hosts/AA:BB:CC:DD:EE:05", headers=AUTH)
        assert resp.status_code == 200
        resp2 = client.get("/api/pxe/hosts/AA:BB:CC:DD:EE:05", headers=AUTH)
        assert resp2.status_code == 404

    def test_delete_nonexistent(self, client):
        resp = client.delete("/api/pxe/hosts/FF:FF:FF:FF:FF:FF", headers=AUTH)
        assert resp.status_code == 404

    def test_list_hosts(self, client):
        client.post("/api/pxe/hosts",
                    json={"mac": "AA:BB:CC:DD:EE:06"}, headers=AUTH)
        resp = client.get("/api/pxe/hosts", headers=AUTH)
        assert resp.status_code == 200
        assert isinstance(resp.get_json(), list)
        assert len(resp.get_json()) >= 1


class TestPxeSettings:
    """Test per /api/pxe/settings."""

    def test_get_defaults(self, client):
        resp = client.get("/api/pxe/settings", headers=AUTH)
        assert resp.status_code == 200
        data = resp.get_json()
        assert "pxe_enabled" in data
        assert "pxe_pc_prefix" in data

    def test_update_and_read(self, client):
        client.put("/api/pxe/settings",
                   json={"pxe_pc_prefix": "LAB", "pxe_auto_provision": "0"},
                   headers=AUTH)
        resp = client.get("/api/pxe/settings", headers=AUTH)
        data = resp.get_json()
        assert data["pxe_pc_prefix"] == "LAB"
        assert data["pxe_auto_provision"] == "0"

    def test_password_masked(self, client):
        client.put("/api/pxe/settings",
                   json={"pxe_default_join_pass": "S3cret!"},
                   headers=AUTH)
        resp = client.get("/api/pxe/settings", headers=AUTH)
        assert resp.get_json()["pxe_default_join_pass"] == "••••••••"

    def test_password_not_overwritten_by_mask(self, client):
        """PUT con placeholder non deve sovrascrivere la password reale."""
        client.put("/api/pxe/settings",
                   json={"pxe_default_join_pass": "RealPass123"},
                   headers=AUTH)
        client.put("/api/pxe/settings",
                   json={"pxe_default_join_pass": "••••••••"},
                   headers=AUTH)
        resp = client.get("/api/pxe/settings", headers=AUTH)
        # Mascherata = ancora presente nel DB
        assert resp.get_json()["pxe_default_join_pass"] == "••••••••"

    def test_reject_unknown_keys(self, client):
        """Chiavi non in _PXE_SETTINGS_DEFAULTS vengono ignorate."""
        resp = client.put("/api/pxe/settings",
                          json={"pxe_evil_key": "hacked", "pxe_pc_prefix": "OK"},
                          headers=AUTH)
        assert resp.status_code == 200
        data = client.get("/api/pxe/settings", headers=AUTH).get_json()
        assert "pxe_evil_key" not in data


class TestPxeStatus:
    """Test per /api/pxe/status."""

    def test_status_returns_structure(self, client):
        resp = client.get("/api/pxe/status", headers=AUTH)
        assert resp.status_code == 200
        data = resp.get_json()
        assert "pxe_enabled" in data
        assert "tftp_alive" in data
        assert "winpe_files" in data
        assert "winpe_ready" in data
        assert "host_count" in data
        assert "boot_today" in data


class TestPxeBootLog:
    """Test per /api/pxe/boot-log."""

    def test_boot_log_populated_after_boot(self, client):
        """Dopo un boot PXE, il log deve contenere l'entry."""
        client.get("/api/boot/AA:BB:CC:11:22:33")
        resp = client.get("/api/pxe/boot-log", headers=AUTH)
        assert resp.status_code == 200
        logs = resp.get_json()
        assert any(l["mac"] == "AA:BB:CC:11:22:33" for l in logs)


class TestPxeFileServing:
    """Test per /api/pxe/file/<name> — whitelist file."""

    def test_path_traversal_blocked(self, client):
        resp = client.get("/api/pxe/file/../../etc/passwd")
        assert resp.status_code in (403, 404)

    def test_unknown_file_returns_404(self, client):
        resp = client.get("/api/pxe/file/evil.exe")
        assert resp.status_code == 404

    def test_allowed_file_missing_returns_404(self, client):
        """File in whitelist ma non su disco → 404 con messaggio utile."""
        resp = client.get("/api/pxe/file/wimboot")
        assert resp.status_code == 404
        assert b"non trovato" in resp.data or b"not found" in resp.data.lower()


class TestAutounattendPxe:
    """Test per /api/autounattend/<pc_name>."""

    def test_unknown_pc_returns_404(self, client):
        resp = client.get("/api/autounattend/NONEXISTENT-PC")
        assert resp.status_code == 404

    def test_known_pc_returns_xml(self, client):
        """CR esistente → autounattend.xml valido con ComputerName."""
        client.post("/api/cr",
                    json={"pc_name": "PXE-XML-TEST", "domain": "test.local",
                          "admin_pass": "Pass123"},
                    headers=AUTH)
        resp = client.get("/api/autounattend/PXE-XML-TEST")
        assert resp.status_code == 200
        assert b"<?xml" in resp.data
        assert b"PXE-XML-TEST" in resp.data

    def test_xml_has_install_to(self, client):
        """L'XML deve contenere <InstallTo> per l'installazione automatica."""
        client.post("/api/cr",
                    json={"pc_name": "PXE-INST-TEST", "domain": "test.local",
                          "admin_pass": "Pass123"},
                    headers=AUTH)
        resp = client.get("/api/autounattend/PXE-INST-TEST")
        assert b"InstallTo" in resp.data

    def test_xml_no_api_key_exposed(self, client):
        """L'XML NON deve contenere l'API key globale del server."""
        client.post("/api/cr",
                    json={"pc_name": "PXE-KEY-TEST", "domain": "test.local",
                          "admin_pass": "Pass123"},
                    headers=AUTH)
        resp = client.get("/api/autounattend/PXE-KEY-TEST")
        assert b"X-Api-Key" not in resp.data
```

**Nota:** adattare `AUTH` alla costante/fixture usata nei test esistenti. Totale: ~25 test che coprono tutti gli endpoint PXE.

---

## 🟡 M-1 · `_get_pxe_settings()` non usa i default centralizzati — server/api.py

**Impatto:** se una setting PXE non è nel DB, `_get_pxe_settings()` restituisce un dict senza quel valore. Il codice del boot endpoint (riga 1967) fa `.get("auto_provision", "1")` con default inline, ma questi default sono duplicati e possono divergere da `_PXE_SETTINGS_DEFAULTS`.

`_get_pxe_settings()` (riga 1813) rimuove il prefisso `pxe_`:
```python
return {r["key"][4:]: r["value"] for r in rows}  # "pxe_auto_provision" → "auto_provision"
```

Ma `_PXE_SETTINGS_DEFAULTS` (riga 2436) ha chiavi con prefisso. I default non vengono mai usati come base.

**Fix — far sì che `_get_pxe_settings()` parta dai default e poi sovrascriva con i valori DB:**

```python
def _get_pxe_settings() -> dict:
    """Legge tutte le settings PXE con fallback ai default."""
    defaults = {k[4:]: v for k, v in _PXE_SETTINGS_DEFAULTS.items()}  # strip pxe_
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT key, value FROM settings WHERE key LIKE 'pxe_%'"
        ).fetchall()
    defaults.update({r["key"][4:]: r["value"] for r in rows})
    return defaults
```

Poi rimuovere tutti i default inline nei `.get()` dentro `pxe_boot_script()`:
```python
# PRIMA:
pxe_cfg.get("auto_provision", "1")
pxe_cfg.get("pc_prefix", "PC")

# DOPO:
pxe_cfg["auto_provision"]  # il default è garantito dalla funzione
pxe_cfg["pc_prefix"]
```

---

## 🟡 M-2 · `send_file` importato 4 volte localmente — server/api.py

**Righe:** 1582, 1595, 1626, 2230

L'import top-level (riga 6) include `send_from_directory` ma non `send_file`. Quattro funzioni lo importano localmente.

**Fix — aggiungere all'import top-level (riga 6):**

```python
from flask import Flask, jsonify, request, send_from_directory, send_file
```

Poi rimuovere le 4 righe di import locale:
- Riga 1582: `from flask import send_file` → eliminare
- Riga 1595: `from flask import send_file` → eliminare
- Riga 1626: `from flask import send_file` → eliminare
- Riga 2230: `from flask import send_file as _send_file` → eliminare, usare `send_file`

---

## 🟡 M-3 · `editPxeHost()` e `openPxeHostModal()` sono stub — server/web/index.html

**Righe:** 1621-1628

```javascript
function editPxeHost(mac) { alert('TODO: edit host ' + mac); }
function openPxeHostModal() { alert('TODO: modal nuovo host PXE'); }
```

I button "✎" e "+ Aggiungi Host" nella UI non funzionano.

**Fix — implementare con `prompt()` minimal, coerente con lo stile della UI:**

```javascript
async function editPxeHost(mac) {
  const action = prompt(
    `Azione boot per ${mac}:\nauto | deploy | local | block`,
    'auto'
  );
  if (!action) return;
  const valid = ['auto', 'deploy', 'local', 'block'];
  if (!valid.includes(action)) { notify('Azione non valida', 'error'); return; }
  await api(`/api/pxe/hosts/${encodeURIComponent(mac)}`, 'PUT', { boot_action: action });
  notify(`Host ${mac} aggiornato a "${action}"`);
  loadPxeHosts();
}

async function openPxeHostModal() {
  const mac = prompt('Indirizzo MAC (es. AA:BB:CC:DD:EE:FF):');
  if (!mac) return;
  const name = prompt('Nome PC (opzionale):') || '';
  const resp = await api('/api/pxe/hosts', 'POST', {
    mac: mac.trim(),
    pc_name: name.trim(),
    boot_action: 'auto'
  });
  if (resp) {
    notify('Host PXE aggiunto');
    loadPxeHosts();
  }
}
```

**Nota:** la UI usa `api(url, method, body)` come singola funzione HTTP e `notify(msg, type)` per i toast — non `apiGet/apiPut/showToast`.

---

## 🟡 M-4 · `deploy/pxe.conf` — configurazione iVentoy obsoleta

**File:** `deploy/pxe.conf`

```
# Riga 1: # ProxyDHCP — iVentoy @ 192.168.10.122
# Riga 7: pxe-service=X86-64_EFI,"Network Boot (UEFI)",snponly.efi,192.168.10.122
# Riga 8: pxe-service=BC_EFI,"Network Boot (EFI-BC)",snponly.efi,192.168.10.122
```

Fa riferimento a iVentoy e `snponly.efi`. Con il setup v2.2.0, il PXE usa DHCP option 66/67 del gateway e `ipxe.efi`.

**Fix — opzione A (aggiornare):**
```
# ProxyDHCP — NovaSCM iPXE (backup se DHCP option 66/67 non configurabile)
port=0
dhcp-range=192.168.10.0,proxy
log-dhcp
pxe-service=X86-64_EFI,"NovaSCM PXE Boot (UEFI)",ipxe.efi,192.168.20.110
pxe-service=BC_EFI,"NovaSCM PXE Boot (EFI-BC)",ipxe.efi,192.168.20.110
```

**Fix — opzione B (eliminare):** se il proxyDHCP non serve più, rimuovere il file.

---

## 🟡 M-5 · Credenziali hardcoded in `deploy/autounattend.xml`

**Righe:** 25-26 (password WDS in chiaro), 144-158 (password base64), 165 (IP `192.168.10.122` obsoleto)

```xml
<Password>Admin2026!</Password>     <!-- riga 26 — plaintext -->
```

Riga 165 punta a `192.168.10.122` (vecchio CT 110 iVentoy).

**Fix — due opzioni:**
1. **Eliminare il file** — usare solo l'endpoint dinamico `/api/cr/by-name/<pc>/autounattend.xml` o `/api/autounattend/<pc>`
2. **Sostituire con placeholder** e aggiungere commento:
```xml
<!-- TEMPLATE: sostituire le credenziali prima dell'uso.
     Per generare automaticamente: GET /api/cr/by-name/{pc}/autounattend.xml -->
<Password>CHANGE_ME</Password>
```

---

## 🔵 I-1 · Cleanup boot log dentro transazione boot — server/api.py

**Righe:** ~2038-2042 (dentro `pxe_boot_script()`)

```python
conn.execute("""
    DELETE FROM pxe_boot_log
    WHERE id NOT IN (SELECT id FROM pxe_boot_log ORDER BY id DESC LIMIT 10000)
""")
```

Questa DELETE è nella stessa transazione che serve lo script iPXE. Con molti log, può rallentare il boot PXE.

**Fix — cleanup probabilistico:**

```python
import random

# Sostituire il blocco cleanup con:
if random.random() < 0.01:  # ~1% delle richieste
    try:
        conn.execute("""
            DELETE FROM pxe_boot_log
            WHERE id NOT IN (SELECT id FROM pxe_boot_log ORDER BY id DESC LIMIT 10000)
        """)
    except Exception:
        pass  # non bloccare il boot per un cleanup fallito
```

---

## 🔵 I-2 · `import ipaddress` a metà file — server/api.py riga 1910

```python
import ipaddress as _ipaddress
```

Tutti gli altri import sono in cima (righe 6-10). Questo è isolato a riga 1910.

**Fix:** spostare in cima con gli altri import:
```python
import ipaddress
```

E rimuovere l'alias `_ipaddress` — usare `ipaddress` diretto nel codice PXE.

---

## 🔵 I-3 · Due funzioni autounattend divergenti — server/api.py

Esistono due generatori completamente separati:
- `get_autounattend()` (riga 608) — CR-based, completo: enrollment token, ODJ, winget software, DiskConfiguration corretta
- `_build_autounattend_xml_pxe()` (riga 2269) — PXE-based: meno funzionalità, bug C-3, espone API key (C-5)

**Suggerimento:** dopo aver fixato C-3 e C-5, valutare di far convergere le due funzioni. Estrarre un helper comune `_build_autounattend_core(cr_dict, *, pxe_mode=False)` che entrambe le route chiamano. Questo previene future divergenze.

---

## 🔵 I-4 · CLAUDE.md IP network obsoleto — riga 126

```
Network: 192.168.20.110:9091
```

Se l'IP di CT 103 è cambiato a `192.168.20.103`, aggiornare. Se è ancora `.110`, il documento PXE_INTEGRATION_CLAUDECODE.md va corretto (usa `.103` in 15+ punti).

**Fix:** determinare l'IP corretto e allineare:
- `CLAUDE.md` riga 126
- `docs/PXE_INTEGRATION_CLAUDECODE.md` (15+ occorrenze)
- MEMORY.md (sezione CT 103)

---

## 🔵 I-5 · Template nel doc PXE è minimale vs codice completo

**File:** `docs/PXE_INTEGRATION_CLAUDECODE.md`, PARTE 3, funzione `_build_autounattend_xml()`

Il documento propone un template XML di 50 righe che manca di `DiskConfiguration`, `ImageInstall`, e rete. Il codice reale (riga 2269-2431) è completo.

**Nessun fix nel codice.** Aggiungere nota al documento:

```markdown
> **NOTA:** il template XML sotto è un riferimento semplificato.
> La versione completa è implementata in `api.py` come `_build_autounattend_xml_pxe()`.
> NON sovrascrivere la funzione esistente con questa versione minimale.
```

---

## 🔵 I-6 · `datetime.utcnow()` deprecato nel documento PXE

**File:** `docs/PXE_INTEGRATION_CLAUDECODE.md`, righe 358 e 824 del doc

Il documento propone codice con `datetime.datetime.utcnow()` che è deprecato in Python 3.12+ (il Dockerfile usa 3.12-slim). Se qualcuno copia dal doc, introduce `DeprecationWarning`.

**Fix nel doc — sostituire ogni occorrenza di:**
```python
now = datetime.datetime.utcnow().isoformat()
```
con:
```python
now = datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None).isoformat()
```

---

## ORDINE DI ESECUZIONE

```
STEP  ID    DESCRIZIONE                                           FILE
────  ────  ────────────────────────────────────────────────────  ──────────────────
 1    C-1   Definire DIST_DIR e _WINPE_DIR                        api.py
 2    C-2   Fix route <n> → <name>                                api.py
 3    C-4   Aggiungere pxe_server.py al Dockerfile                Dockerfile
 4    I-2   Spostare import ipaddress in cima                     api.py
 5    M-2   Consolidare import send_file                          api.py
 6    C-3   Aggiungere <ImageInstall> + <InstallTo>               api.py
 7    C-5   Sostituire API key con enrollment token               api.py
 8    M-1   Fix _get_pxe_settings() con default centralizzati     api.py
 9    H-2   Validazione boot_action                               api.py
10    H-1   Uniformare timestamp UTC (15 sostituzioni)            api.py
11    M-3   Implementare editPxeHost / openPxeHostModal           index.html
12    H-3   Aggiungere test PXE (~25 test)                        test_api.py
13    M-4   Aggiornare o eliminare deploy/pxe.conf                pxe.conf
14    M-5   Pulire deploy/autounattend.xml                        autounattend.xml
15    I-1   Cleanup probabilistico boot log                       api.py
16    I-4   Aggiornare IP in CLAUDE.md                            CLAUDE.md
17    I-5   Aggiungere nota al doc PXE                            PXE_INTEGRATION
18    I-6   Fix utcnow nel doc PXE                                PXE_INTEGRATION
 ─    ───   ────────────────────────────────────────────────────  ──────────────────
 ✓    ALL   cd server && NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v
```

---

*Report combinato generato il 2026-03-12 — NovaSCM v2.2.1-review — PolarisCore Homelab*
