# PolarisCore — Audit Completo
**Data**: 2026-03-13
**Scope**: Codice sorgente, infrastruttura Proxmox, DNS, Cloudflare, nginx reverse proxy

---

## RIEPILOGO ESECUTIVO

| Area | Problemi Critici | Warning | Info |
|------|:---:|:---:|:---:|
| Codice api.py | 2 | 3 | 4 |
| Sorgenti & Versioni | 3 | 1 | 1 |
| Infrastruttura Proxmox | 1 | 4 | 5 |
| Cloudflare Tunnel | 1 | 0 | 0 |
| nginx Reverse Proxy | 3 | 2 | 1 |
| DNS | 2 | 2 | 1 |
| **TOTALE** | **12** | **12** | **12** |

---

## SEZIONE 1 — CODICE SERVER (api.py)

### CRITICO

| ID | Riga | Problema | Impatto |
|----|------|----------|---------|
| C-01 | 1575 | `open(VERSION_FILE).read()` senza context manager | Resource leak — file handle non chiuso se eccezione durante JSON parsing |
| C-02 | 1578 | Fallback version hardcoded `"1.0.0"` | Se version.json manca, API riporta v1.0.0 invece di 2.2.x |

### WARNING

| ID | Riga | Problema |
|----|------|----------|
| W-01 | 1635, 1665, 1724 | `import uuid as _uuid` ripetuto 3 volte localmente — spostare a top-level |
| W-02 | 8 | `ipaddress` importato ma mai usato nel file |
| W-03 | 15 | `contextmanager` importato ma mai usato |

### INFO

| ID | Riga | Nota |
|----|------|------|
| I-01 | 402 | `ALTER TABLE` usa f-string — mitigato da lista hardcoded, ma anti-pattern |
| I-02 | 101 | `functools` importato 2 volte (top-level + locale nel fallback limiter) |
| I-03 | 28 righe | Pattern `datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None).isoformat()` verbose — considerare helper `now_iso()` |
| I-04 | 2569, 2610 | Nessun controllo esistenza `index.html`/`deploy-client.html` — 500 se mancano |

### POSITIVO

- `send_file` importato correttamente a top-level, nessun import locale residuo
- `DIST_DIR` e `_WINPE_DIR` definiti e usati correttamente ovunque
- `_BOOT_ACTION_VALID` validato in tutti i path create/update
- Tutti i route decorator corrispondono alle signature delle funzioni (45+ route verificate)
- Autenticazione sicura: `hmac.compare_digest()`, token monouso con scadenza, lock threading
- Nessun TODO/FIXME/HACK nel codice
- Nessun escape sequence invalido (`\s`, `\d`) trovato

---

## SEZIONE 2 — SORGENTI & VERSIONI

### CRITICO — Disallineamento versioni

| File | Versione attuale | Dovrebbe essere |
|------|:---:|:---:|
| `App.xaml.cs` | 2.2.1 | ✅ |
| `CLAUDE.md` | 2.2.1 | ✅ |
| `CHANGELOG.md` | 2.2.1 | ✅ |
| `wiki/Home.md` | 2.2.1 | ✅ |
| `deploy/postinstall.ps1` | 2.2.1 | ✅ |
| `server/version.json` | **2.2.0** | ❌ → 2.2.1 |
| `PolarisManager.csproj` | **2.2.0** | ❌ → 2.2.1 |

### CRITICO — wiki/FAQ.md porta sbagliata

| File | Riga | Problema |
|------|------|----------|
| `wiki/FAQ.md` | 43 | URL usa porta `9090` ma NovaSCM API gira su `9091` |

### WARNING

| File | Nota |
|------|------|
| `deploy/autounattend.xml` | Marcato come legacy in CLAUDE.md (M-3) — considerare rimozione o commento deprecation |

### POSITIVO

- IP `192.168.1.100` coerente in tutti i file (nessun residuo `.103`)
- Dockerfile copia correttamente `api.py` e `pxe_server.py`
- docker-compose.yml completo (porte, health check, limiti risorse, volumi)
- `.gitignore` esclude `*.db`, `.env`, `config.json`, `server/dist/` — sicuro
- CI/CD (`test.yml`) configurato correttamente (pytest + xUnit su push/PR main)
- `deploy/pxe.conf` e `deploy/postinstall.ps1` corretti

---

## SEZIONE 3 — INFRASTRUTTURA PROXMOX

### CRITICO

| ID | Problema | Dettagli |
|----|----------|----------|
| P-01 | **Swap 100% pieno** | 8.0G/8.0G usato (31Mi libero). Il sistema ha subito memory pressure. 9 CT + 2 VM running su 32GB RAM. |

### WARNING

| ID | Problema | Dettagli |
|----|----------|----------|
| P-02 | `/tmp` al 92% | 15G/16G — loop mount ISO/WinPE residui (`/tmp/winpe-work`, `/tmp/iso_mount`, `/tmp/oc_efi`) |
| P-03 | CT 103 disco al 69% | 4.2G/6G — esegue Homepage, Portainer, Certportal, NovaSCM. Monitorare o espandere |
| P-04 | CT 110 (pxe) ha 4 unita systemd fallite | `logrotate`, `systemd-logind`, `systemd-networkd`, `systemd-networkd.socket` |
| P-05 | `logrotate.service` fallito sull'host | I log potrebbero non ruotare correttamente |

### INFO

| ID | Nota |
|----|------|
| P-06 | VM 109 (homeassistant) **non presente** in `qm list` — potrebbe essere stata rimossa |
| P-07 | VM 118 (trmm) e VM 119 (win-dc) sono **ferme** |
| P-08 | Nessuna subscription Proxmox — repo community |
| P-09 | ZFS pool features aggiornabili (`zpool upgrade`) su APPS e DATA |
| P-10 | 5 pacchetti aggiornabili (linux-libc-dev, ndpi, ntopng, pfring) |

### STATO CONTAINER

| CTID | Nome | Stato | Disco | Uptime | Servizi falliti |
|------|------|:-----:|------:|-------:|:---:|
| 100 | pihole | ✅ running | 22% | 2d 21h | 0 |
| 101 | vaultwarden | ✅ running | 13% | 10d 18h | 0 |
| 102 | cloudflared | ✅ running | 22% | 10d 18h | 0 |
| 103 | homepage | ✅ running | **69%** | 10d 18h | 0 |
| 105 | radius | ✅ running | 19% | 9d 0h | 0 |
| 107 | jellyfin | ✅ running | 26% | 5d 14h | 0 |
| 108 | mediastack | ✅ running | 55% | 5d 14h | 0 |
| 110 | pxe | ✅ running | 40% | 1d 0h | **4** |
| 116 | technitium | ✅ running | 25% | 9d 20h | 0 |

### STATO VM

| VMID | Nome | Stato | RAM |
|------|------|:-----:|----:|
| 118 | trmm | ⏹ stopped | 4 GB |
| 119 | win-dc | ⏹ stopped | 12 GB |
| 120 | win-client | ✅ running | 4 GB |
| 121 | macos-sonoma | ⏹ stopped | 12 GB |
| 200 | pxe-test | ✅ running | 4 GB |

### NovaSCM su CT 103

- **Servizio**: `novascm.service` attivo, gunicorn 2w/4t, 56.9M RAM
- **API**: `/health` → `{"status":"ok"}` ✅
- **DB**: 8.5K (molto piccolo)
- **dist/**: `ipxe.efi` (174KB) presente ✅, `NovaSCMDeployScreen.exe` (134MB), cartella `winpe/` presente
- **Docker**: 3 container attivi (Homepage, Portainer, Certportal)

---

## SEZIONE 4 — CLOUDFLARE TUNNEL (CT 102)

### CRITICO

| ID | Problema | Dettagli |
|----|----------|----------|
| CF-01 | Ingress mancanti | `config.yml` ha solo `vault` e `home`. **Mancano** `polariscore.it` e `www.polariscore.it` — probabilmente serviti da Cloudflare Pages (non tunnel), ma MEMORY.md diceva diversamente. Verificare. |

### STATO INGRESS

| Hostname | Target | Backend attivo? |
|----------|--------|:---:|
| `vault.polariscore.it` | 192.168.20.101:8000 | ✅ (vaultwarden running) |
| `home.polariscore.it` | 192.168.1.100:3000 | ✅ (Homepage HTTP 200) |
| catch-all | 404 | — |

### NOTE
- Cloudflared v2026.2.0 — disponibile v2026.3.0
- Warning QUIC stream timeout intermittenti (riconnessi con successo)

---

## SEZIONE 5 — NGINX REVERSE PROXY (miniPC 192.168.10.111)

### CRITICO

| ID | File | Problema |
|----|------|----------|
| N-01 | `home.conf` | `proxy_pass http://192.168.1.100` — **manca `:3000`**. Homepage gira su porta 3000, non 80. LAN users vedono pagina sbagliata o errore. |
| N-02 | `adguard.conf` | Punta a `192.168.20.104:3000` — CT 115 AdGuard **eliminato**. Produce 502. |
| N-03 | `pdns-admin.conf` | Punta a `192.168.1.100:8080` — CT 114 PowerDNS **eliminato**. Produce 502. |

### WARNING

| ID | File | Problema |
|----|------|----------|
| N-04 | `opsi.conf` | Punta a `192.168.1.100:4447` — IP non corrisponde a nessun CT attivo. Produce 502. |
| N-05 | `dns2.conf` | Redirect malformato: `return 301 https://;` (manca `$host$request_uri`) + downgrade HTTPS→HTTP |

### INFO

| ID | File | Nota |
|----|------|------|
| N-06 | `technitium.conf` | Proxy a porta 5381 via socat relay → 5380 localhost. Funziona ma fragile. |

### CONFIGURAZIONI FUNZIONANTI

| File | Server Name | Target | Stato |
|------|-------------|--------|:---:|
| `vault.conf` | vault.polariscore.it | 192.168.20.101:8000 | ✅ |
| `sonarr.conf` | sonarr.polariscore.it | 192.168.20.116:8989 | ✅ |
| `radarr.conf` | radarr.polariscore.it | 192.168.20.116:7878 | ✅ |
| `qbit.conf` | qbit.polariscore.it | 192.168.20.116:8080 | ✅ |
| `prowlarr.conf` | prowlarr.polariscore.it | 192.168.20.116:9696 | ✅ |
| `status.conf` | status.polariscore.it | 192.168.1.100:3001 | ✅ |
| `jellyfin.conf` | jellyfin.polariscore.it | 192.168.20.115:8096 | ✅ |
| `ha.conf` | ha.polariscore.it | 192.168.20.111:8123 | ✅ |

### CERTIFICATO SSL

- **Wildcard**: `*.polariscore.it` — Let's Encrypt
- **Scadenza**: 2026-05-28 ✅

---

## SEZIONE 6 — DNS

### CRITICO

| ID | Problema | Dettagli |
|----|----------|----------|
| D-01 | `fogserver.polariscore.it` → 192.168.1.100 | CT 117 eliminato — record DNS orfano in dnsmasq |
| D-02 | `home.polariscore.it` → miniPC (192.168.10.111) → nginx → porta 80 | Catena DNS→nginx→backend rotta (vedi N-01) |

### WARNING

| ID | Problema |
|----|----------|
| D-03 | Nessun record DNS per: `cockpit.polariscore.it`, `files.polariscore.it`, `pdns.polariscore.it`, `adguard.polariscore.it`, `opsi.polariscore.it` — nginx vhost esistono ma sono irraggiungibili |
| D-04 | `dns1.conf` e `pihole.conf` in nginx sono configurazioni duplicate per lo stesso backend (192.168.1.100) |

### INFO

| ID | Nota |
|----|------|
| D-05 | Technitium DNS (CT 116) gira come processo `dotnet` senza servizio systemd — nessun auto-restart se crasha |

### MAPPA DNS → IP (Pi-hole custom.list + dnsmasq)

| Record | IP | Stato |
|--------|----|----|
| `homepage` | 192.168.1.100 | ✅ CT 103 |
| `pihole.polariscore.it` | 192.168.1.100 | ✅ CT 100 |
| `status.polariscore.it` | 192.168.10.111 | ✅ miniPC → proxy → CT 103:3001 |
| `vault.polariscore.it` | 192.168.10.111 | ✅ miniPC → proxy → CT 101:8000 |
| `monitoring.polariscore.it` | 192.168.10.111 | ✅ miniPC → Grafana |
| `certportal.polariscore.it` | 192.168.10.111 | ✅ miniPC → CT 103:9090 |
| `home.polariscore.it` | 192.168.10.111 | ❌ miniPC → porta 80 (dovrebbe essere :3000) |
| `ha.polariscore.it` | 192.168.20.111 | ✅ VM 109 HA |
| `fogserver.polariscore.it` | 192.168.1.100 | ❌ CT eliminato |
| `aquarium` | 192.168.20.104 | ❓ Non in memoria |

---

## SEZIONE 7 — FILE PXE MANCANTI

### Stato dist/ su CT 103

| File | Presente | Note |
|------|:---:|------|
| `dist/ipxe.efi` | ✅ | 174 KB |
| `dist/NovaSCMDeployScreen.exe` | ✅ | 134 MB |
| `dist/autoexec.ipxe` | ✅ | 517 B |
| `dist/winpe/wimboot` | ❓ | Da verificare (cartella esiste) |
| `dist/winpe/BCD` | ❓ | Da verificare |
| `dist/winpe/boot.sdi` | ❓ | Da verificare |
| `dist/winpe/boot.wim` | ❓ | Da verificare |

### File disponibili su `D:\NovaSCM_WinPE\media\`

| File | Path locale | Azione |
|------|-------------|--------|
| BCD | `D:\NovaSCM_WinPE\media\Boot\BCD` | Copiare in CT 103 `/opt/novascm/dist/winpe/` |
| boot.sdi | `D:\NovaSCM_WinPE\media\Boot\boot.sdi` | Copiare in CT 103 `/opt/novascm/dist/winpe/` |
| boot.wim | `D:\NovaSCM_WinPE\media\sources\boot.wim` | Copiare in CT 103 `/opt/novascm/dist/winpe/` |
| wimboot | Non presente su D: | Scaricare da github.com/ipxe/wimboot/releases |

---

## PRIORITA DI INTERVENTO

### P0 — Critico (risoluzione immediata)

1. **Swap pieno al 100%** — spegnere VM non necessarie (pxe-test 200, win-client 120) o aggiungere RAM
2. **`home.conf` porta sbagliata** — cambiare `proxy_pass` da `http://192.168.1.100` a `http://192.168.1.100:3000`
3. **File handle leak in api.py:1575** — aggiungere `with` context manager
4. **version.json e .csproj** — aggiornare da 2.2.0 a 2.2.1

### P1 — Warning (risoluzione entro settimana)

5. **`/tmp` al 92%** — smontare loop device residui (`umount /tmp/winpe-work` ecc.)
6. **CT 103 disco al 69%** — espandere rootfs o pulire file non necessari
7. **CT 110 servizi systemd falliti** — verificare networkd e logind
8. **Rimuovere nginx conf orfane** — `adguard.conf`, `pdns-admin.conf`, `opsi.conf`
9. **wiki/FAQ.md porta 9090→9091**
10. **Record DNS fogserver** — rimuovere da dnsmasq

### P2 — Miglioramenti (quando possibile)

11. Import `uuid` a top-level in api.py (rimuovere 3 import locali)
12. Rimuovere import inutilizzati (`ipaddress`, `contextmanager`)
13. Fallback version da 1.0.0 a 2.2.1
14. Creare servizio systemd per Technitium DNS (CT 116)
15. Aggiornare cloudflared da v2026.2.0 a v2026.3.0
16. Correggere `dns2.conf` redirect malformato
17. Copiare file WinPE (BCD, boot.sdi, boot.wim) da D: a CT 103
18. Scaricare wimboot e copiare su CT 103
19. `deploy/autounattend.xml` — marcare come deprecated o rimuovere

---

*Report generato automaticamente da Claude Code — audit read-only, nessuna modifica applicata.*
