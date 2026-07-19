# Ambiente NovaSCM — host live verificati

**Verificato il:** 2026-07-19, via `pct list` + `systemctl show` diretti sui container.
**Scopo:** unica fonte di verità su IP/CT/servizi, per evitare di confondere ambiente di lab e produzione (vedi Fase A1 di `PIANO_DISM_ENROLLMENT_E2E.md`).

---

## Host Proxmox

Unico nodo live: `pve-minipc` (`192.168.10.202`), raggiungibile via `ssh root@192.168.10.202`.

## Container NovaSCM

| CT | Nome | Stato | IP | Ruolo | Servizio systemd | Porta |
|----|------|-------|-----|-------|-------------------|-------|
| **104** | `novascm-pxe-test` | running | `192.168.10.104` | **Lab/test PXE** — ambiente dove è avvenuto il breakthrough DISM del 16/07 | `novascm-pxe.service` | 9091 |
| **112** | `novascm-server` | running | `192.168.10.112` | **Produzione** — server "ufficiale" per uso quotidiano non-PXE | `novascm.service` | 9091 |

**Non sono la stessa istanza**: database, API key e stato completamente separati. Un PC/CR creato su uno non esiste sull'altro.

### CT104 — dettagli

```
Environment=NOVASCM_DB=/opt/novascm-pxe/server/novascm.db NOVASCM_API_KEY=pxetest2026
```

- Asset WinPE in `/opt/novascm-pxe/server/dist/winpe/`: `wimboot`, `BCD`, `boot.wim` (655MB, modificato 16/07 18:04 — contiene lo script DISM **iniettato a mano**, non versionato in repo)
- DHCP Option 66 del gateway UniFi punta qui (`192.168.10.104`) per il test PXE

### CT112 — dettagli

```
Environment=NOVASCM_DB=/opt/novascm/data/novascm.db NOVASCM_API_KEY=2cbdc4847e8b13129ac68fdd031929108b48255ba8b194272a768971bdf5c35b NOVASCM_PUBLIC_URL=http://192.168.10.112:9091 PORT=9091
```

- Non ha asset WinPE per il deploy PXE — solo API standard (CR, workflow, rete)
- **Non toccare per test PXE**: usare sempre CT104

## VM/PC di test

| VM | Nome | MAC | Ruolo |
|----|------|-----|-------|
| 105 | `pxe-test-vm` | `BC:24:11:A5:35:55` | VM di test PXE su Proxmox, disco SATA (no virtio-scsi — richiesto per compatibilità WinPE), `cpu: host`, Secure Boot disabilitato (`pre-enrolled-keys=0`) |

## Documentazione storica da NON usare come riferimento IP

`CLAUDE.md` e alcuni doc più vecchi citano `CT103` / `192.168.1.100` come server NovaSCM — **obsoleto**, non corrisponde a nessun host live verificato oggi. Riferimento corrente sempre e solo questo file.

## Regole operative (da `PIANO_DISM_ENROLLMENT_E2E.md`)

- Test PXE: sempre CT104 + VM105, mai CT112
- Nessuna modifica a DHCP UniFi o a CT112 senza conferma esplicita dell'utente
- Verificare sempre con `pct list` + `systemctl show` prima di assumere quale host sia "quello giusto" — questo file va aggiornato se qualcosa cambia, non tenuto a memoria
