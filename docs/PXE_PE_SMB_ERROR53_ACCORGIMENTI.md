# PXE WinPE — Samba `net use` (errori 53 e 67): accorgimenti e piano

**Data:** 2026-07-16 (Grok)  
**Aggiornato:** 2026-07-16 (applicati fix Samba netbios + startnet retry; VM105 avviata per test)  
**Contesto:** `PXE_VM105_PROBLEMI_20260716.md`  
**Riferimento startnet live:** iniettato in `boot.wim` indice 1 su CT104 — copia repo: `docs/startnet_PE_current.cmd`

## Fix applicati da Grok (16/07, tentativo risoluzione 67)

| Fix | Dettaglio |
|-----|-----------|
| **Samba `netbios name`** | Hostname CT era `novascm-pxe-test` (**>15 char** NetBIOS → WARNING testparm). Impostato `netbios name = PXE-CT104`, `server min protocol = SMB2`, `client min protocol = SMB2`. Backup: `/etc/samba/smb.conf.bak-pre-netbios-*` |
| **Restart smbd/nmbd** | Dopo la modifica config |
| **startnet.cmd** | Retry max 20, user `192.168.10.104\novascm`, attesa IP, unattend su `X:\` **o** `Z:\sources\autounattend.xml`. UNC corretti `\\192.168.10.104\wininstall` |
| **boot.wim** | Aggiornato indice 1 (backup automatico wimlib / `.bak-pre-startnet-retry-*`) |
| **VM105** | `memory` portata a **4096** (era 2048), `qm start 105` per ritest |

**Verifica richiesta utente:** console VM105 — `list disk` deve mostrare Disco 0; mount deve riuscire entro i retry; Setup deve partire.

---

## Stato fix infrastruttura (non rifare)

| Problema | Stato | Fix |
|----------|--------|-----|
| Crash wimboot→bootmgfw | **Risolto** | `cpu: host` (`popcnt`/`sse4.2`) |
| Disco non visto in PE | **Risolto** | driver `vioscsi` + disco virtio-scsi |
| `X:\autounattend.xml` con index=1 | **Risolto** | Boot Index header WIM = 1 |
| Errore **53** `net use` | **Risolto** (artefatto test) | socket ESTAB fantasma su CT104 verso `.237` |
| Errore **67** `net use` | **Aperto** | nome rete / negoziazione SMB client↔share |
| Deploy end-to-end | **Non verificato** | bloccato su 67 in sessione deploy completo |

---

## Errore 53 — cos’era (chiuso)

| | |
|--|--|
| Sintomo | 53 — percorso di rete non trovato |
| Causa reale | Socket **ESTABLISHED fantasma** su CT104 (`192.168.10.104:445` ↔ `.237:porte`) dopo molti reboot VM mid-session; server rispondeva ACK stantio invece di SYN-ACK |
| Evidenza | `tcpdump` + `ss -tan` |
| Fix | `ss -K dst 192.168.10.237 dport = …` poi `net use` OK |
| Impatto prod | **Basso** — PC fisico unico MAC/IP, un deploy; collisione porte tipica del lab multi-reboot |

### Prevenzione in lab

Prima di ogni batteria di test PE:

```bash
# su CT104
ss -tan | grep ':445'
ss -K dst 192.168.10.237   # se supportato / adattare
systemctl restart smbd nmbd
# oppure reboot CT104
```

---

## Errore 67 — problema aperto (deploy completo)

| | |
|--|--|
| Sintomo | **67 — Impossibile trovare il nome della rete** (non 53) |
| Frequenza documentata | 15/15 fallimenti in un tentativo di deploy completo |
| Samba su CT104 locale | OK (`smbclient //localhost/wininstall`) |
| ARP | `.237` REACHABLE, MAC corretto |
| Socket fantasma | ripuliti prima dei test 67 |
| Interpretazione | Server Samba “sano”; fallisce il percorso **VM ↔ CT104** o la **negoziazione SMB lato client PE** (tree connect / nome share), non “smbd spento” |

### 53 vs 67

| Codice | Significato pratico |
|--------|---------------------|
| **53** | Non trovo il percorso → spesso non arrivi al server (o TCP malformato) |
| **67** | Non trovo il **nome di rete** → spesso TCP c’è, ma fallisce share name / tree connect / NetBIOS / risposta mal interpretata |

---

## Piano di risoluzione errore 67 (ordine fisso)

### 1. Reset stato rete (5 min)

1. `qm stop 105`
2. CT104: kill ESTAB fantasma verso client PE + `systemctl restart smbd nmbd` (o **reboot CT104**)
3. Un solo boot VM, **senza click** in console (QuickEdit mette in pausa `cmd`)
4. Un solo `net use` manuale in PE

Se dopo reboot CT104 il 67 sparisce → artefatto accumulato (come il 53). Documentare e aggiungere retry in `startnet`.

### 2. tcpdump su un 67 vero (obbligatorio)

Durante `net use \\192.168.10.104\wininstall /user:novascm …`:

```bash
tcpdump -i any host 192.168.10.237 and port 445 -nn -s0 -w /tmp/smb67.pcap
```

| Pattern | Azione |
|---------|--------|
| Nessun SYN | client SMB non parte → driver rete / servizio PE |
| SYN + SYN-ACK + stop | dialect / negotiate |
| Tree connect `BAD_NETWORK_NAME` | nome share / path / export |
| Session setup fail | user/pass/domain |
| Tutto OK in pcap, 67 in PE | bug interpretazione client (raro) |

**Senza pcap non cambiare NIC a caso.**

### 3. Isolare share name vs client SMB

In PE (stesso boot):

```bat
ping -n 2 192.168.10.104
net use \\192.168.10.104\IPC$ /user:novascm <pass>
net use \\192.168.10.104\wininstall /user:novascm <pass>
net use \\192.168.10.104\wininstall /user:192.168.10.104\novascm <pass>
```

| Esito | Conclusione |
|-------|-------------|
| `IPC$` OK, `wininstall` 67 | share/path/export |
| Entrambi 67 | client SMB / path rete verso 445 |
| A intermittenza | race → retry |

Su CT104:

```bash
smbclient //192.168.10.104/wininstall -U novascm
smbstatus
testparm -s | grep -A20 wininstall
```

Verificare nome share **esatto** `wininstall`, path esistente.

### 4. NIC virtio + NetKVM (se 445 flaky)

Stesso metodo di `vioscsi`:

1. Iniettare **NetKVM** nel `boot.wim` indice PE  
2. VM105: NIC **virtio** al posto di e1000  
3. Ritestare `net use`  

Se diventa stabile → emulazione e1000 + SMB.

### 5. Piano B prodotto: non dipendere da `net use`

| Opzione | Idea |
|---------|------|
| **HTTP** | `install.wim` / file via nginx `.104` (già usato per wimboot) |
| **Retry SMB** | 15× `net use` solo IP; se fail → pause lab |
| **iSCSI** | overkill ora |

Produzione NovaSCM consigliata: PE + driver storage/net + **retry SMB su IP**; se lab resta instabile → **HTTP per install.wim**.

### 6. Autounattend (dopo accesso file)

Preferire UNC dopo mount:

```bat
setup.exe /unattend:\\192.168.10.104\wininstall\sources\autounattend.xml
```

o file in `X:\Windows\Panther\` dopo download/mount.

---

## Sequenza operativa prossima sessione

1. Reboot CT104 (o smbd + `ss -K` verso client)  
2. Un boot PE, `net use` manuale ×5  
3. Se ancora 67 → **tcpdump** e classificare  
4. Pcap → share name **oppure** virtio-net **oppure** HTTP  
5. Deploy completo **senza** toccare la console  
6. Aggiornare `PXE_VM105_PROBLEMI_20260716.md` (o file data del giorno) con esito  

---

## Cosa non rifare

| Azione | Motivo |
|--------|--------|
| Downgrade OVMF via apt su pve-minipc | Rischio stack Proxmox |
| Tornare a `kvm64` | Crash noto |
| Reiniettare solo vioscsi a caso | Disco già OK |
| 40 reboot stessa VM senza ripulire socket 445 | Ricrea fantasmi (53) |
| Click in console PE durante test | QuickEdit pausa cmd |
| Biasimare Samba senza pcap del **67** | Locale smbclient OK |

---

## `startnet.cmd` live (letto 2026-07-16, CT104 boot.wim indice 1)

```bat
@echo off
wpeinit
@echo === Verifica disco (PE) ===
echo list disk > X:\dp.txt
diskpart /s X:\dp.txt
type X:\dp.txt

@echo === Attesa lunga stabilizzazione rete/SMB ===
ping -n 150 127.0.0.1 >nul

@echo === Mount share Samba ===
net use \\192.168.10.104\wininstall /user:novascm NovaSCMpxe2026!

@echo === Avvio Setup da rete (PE-first) ===
\\192.168.10.104\wininstall\sources\setup.exe /unattend:X:\autounattend.xml

@echo === Setup terminato o fallito - pausa debug ===
ping -n 180 127.0.0.1 >nul
cmd.exe
```

### Limiti dello script attuale

| Punto | Note |
|-------|------|
| IP già usato | OK |
| Un solo `net use` | Nessun retry su 53/67 |
| Sleep 150s cieco | Non verifica 445 |
| Unattend solo `X:\` | Preferire UNC dopo mount |
| Password in chiaro | OK lab, no prod |

### Migliorie minime suggerite (non applicate in questo doc)

1. Loop ping a `.104`  
2. `for /L` retry `net use` (15×)  
3. Se mount fail → `cmd /k` senza Setup  
4. Setup con `/unattend:\\192.168.10.104\wininstall\sources\autounattend.xml`  
5. Opzionale: unattend via HTTP se manca su X:\  

---

## Riferimenti

- Storico completo: `Y:\NovaSCM\docs\PXE_VM105_PROBLEMI_20260716.md`  
- Sessione 15/07: `Y:\NovaSCM\docs\PXE_VM105_PROBLEMI_20260715.md`  
- CT104: `192.168.10.104`, share `[wininstall]`, VM105, API `:9091`  
- Issue wimboot #65: chiusa (CPU + driver storage; non SMB)
