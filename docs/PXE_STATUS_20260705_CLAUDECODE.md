# NovaSCM — Stato reale del PXE (5 luglio 2026)

**Contesto:** prima di questa verifica, un'analisi esterna (Grok) e la memoria del progetto puntavano su codice/CT sbagliati. Questo documento fissa lo stato verificato per evitare di ripetere l'indagine.

## Cosa NON è vero (chiarito oggi)

- **CT103 non è NovaSCM** — è `ddns-updater`. Il server NovaSCM gira su **CT112** ("novascm-server").
- Su CT112 gira un binario **.NET "NovaSCM Reborn Server"** (`NovaSCM.Server.dll`) che **non ha sorgente recuperabile da nessuna parte** (non su GitHub, nessuna cartella `.git` locale) e **non ha alcuna implementazione PXE**. Va considerato un ramo morto — non costruirci sopra.
- L'analisi PXE di Grok (`server/pxe_server.py` + `api.py`) è valida come codice, ma **non corrisponde a quello che gira in produzione** (il Reborn). I suoi fix suggeriti sono superflui: il codice Python in questo repo li ha già.

## Cosa È vero (verificato)

- **Questo repository** (`ClaudioBecchis/NovaSCM`, branch `main`, v2.5.0) è la base corretta su cui continuare.
- **149/149 test passano** (`pytest tests/`) — i 5 bug critici elencati in `BUGFIX_PXE_V22_CLAUDECODE.md` (DIST_DIR non definito, route errata, autounattend incompleto, ecc.) **sono già stati corretti** in sviluppi successivi a marzo 2026.
- Il **TFTP è integrato nel processo del server stesso** (`pxe_server.py`, thread interno) — **non serve un servizio/container separato**. Verificato: `python api.py` avvia da solo TFTP su `:69` insieme a Flask su `:9091`, senza errori di permessi (anche su Windows).
- L'infrastruttura TFTP separata che esisteva a marzo 2026 (systemd su Proxmox host) **è andata persa** con la migrazione del vecchio Node1 a TrueNAS (28/04/2026) — ma non serve ricrearla: basta deployare questo server aggiornato.

## Asset binari (`server/dist/`, MAI in git — vedi `.gitignore`)

Recuperati il 5/7/2026 senza bisogno di Windows ADK:

| File | Fonte | Note |
|---|---|---|
| `ipxe.efi` | `https://boot.ipxe.org/x86_64-efi/snponly.efi` | **Usare la variante `snponly`, non `ipxe.efi` pieno** — a marzo un client HP dava "memory block too small" con l'ipxe.efi completo (1.1MB), risolto con snponly (~290KB) |
| `winpe/wimboot` | `https://github.com/ipxe/wimboot/releases` | v2.9.0 |
| `winpe/BCD`, `winpe/boot.sdi`, `winpe/boot.wim`, `winpe/install.wim` | Estratti direttamente da `Z:\isos\Windows\Win11_25H2_Italian_x64.iso` (montata con `Mount-DiskImage`) | **Non serve Windows ADK/copype**: `postinstall.ps1` gira dopo l'installazione completa (fase specialize/OOBE), quindi il flusso usa il Windows Setup standard via `autounattend.xml` (ImageInstall/InstallTo), non una WinPE custom. Il `boot.wim` di stock dentro qualsiasi ISO Windows basta. |

Verificato con `/api/pxe/status`: `ipxe_efi: true, tftp_alive: true, winpe_ready: true`.

## Prossimi passi

1. Deploy di questo server (con `dist/` popolato) su un host reale — CT112 (sostituendo il Reborn) o un container nuovo
2. Config DHCP Option 66/67 su UniFi verso l'host scelto
3. Test di boot fisico reale (richiede presenza fisica o accesso IPMI/KVM — nessuna IA può farlo da sola)
