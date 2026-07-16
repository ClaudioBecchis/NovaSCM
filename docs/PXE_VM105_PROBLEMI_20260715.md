# PXE VM105 — Cosa funziona e cosa NO (15 luglio 2026)

Documento di stato verificato su infrastruttura test reale.  
Aggiornato dopo sessione debug con log CT104, console VM105 e fix applicati.

---

## Infrastruttura test

| Componente | Dettaglio |
|------------|-----------|
| **CT104** `novascm-pxe-test` | `192.168.10.104` — Flask `:9091`, TFTP `:69`, nginx `:80`, Samba `[wininstall]` |
| **VM 105** `pxe-test-vm` | Proxmox `192.168.10.202`, MAC `BC:24:11:A5:35:55`, hostname `PC-A53555`, pw_id=88 |
| **DHCP UniFi** | VLAN Management — option 66=`192.168.10.104`, option 67=`ipxe.efi` |
| **Codice** | `/opt/novascm-pxe/server/api.py` su CT104 (deploy da `Y:\NovaSCM\server\api.py`) |

---

## FUNZIONA (verificato con log)

1. **DHCP → TFTP `ipxe.efi`** — client `.237` scarica snponly (~295 KB)
2. **iPXE `autoexec.ipxe`** — chain HTTP verso `/api/boot/bc:24:11:a5:35:55`
3. **API deploy** — risponde script wimboot per `PC-A53555`, action=deploy, pw_id=88
4. **nginx statici** — `wimboot`, `BCD`, `boot.sdi`, `boot.wim` serviti da `http://192.168.10.104/winpe/`
5. **autounattend** — `GET /api/autounattend/PC-A53555` → XML 200
6. **Portatile fisico** (`E8:D8:D1:ED:BF:8F`) — catena PXE completa fino a `boot.wim` (bloccato con `action=block`, corretto)
7. **VM105 network boot OVMF** — dopo fix `rng0` + Secure Boot disabilitato: compare `UEFI PXEv4`, `autoexec.ipxe ... ok`

La catena di rete PXE **non è il problema aperto**. Era rotta solo per configurazione VM/firmware, non per CT104/DHCP.

---

## NON FUNZIONA (problema aperto)

### Sintomo principale

Dopo download di `boot.wim` e consegna di `autounattend.xml`, la **VM 105 si riavvia in loop** (~13–30 secondi).  
Non si vede installazione Windows. **Nessun accesso Samba** da `.237` a `[wininstall]` nei log.

**Aggiornamento 15/07 sera:** Samba era rotta (1326 + share vuota per symlink/cache). **Risolto** — vedi Causa 7. Prossimo step: ritest VM105 PXE.

### Cosa implica

Windows Setup **non arriva** (o fallisce subito) alla fase `ImageInstall` che legge `install.wim` da:

```
\\192.168.10.104\wininstall\sources\install.wim
```

---

## Cause identificate (sessione 15/07)

### 1. OVMF senza VirtIO RNG — RISOLTO

- **Sintomo:** `BdsDxe: No bootable option or device was found`
- **Causa:** `pve-edk2-firmware 4.2025+` disabilita network boot senza fonte entropia
- **Fix:** `qm set 105 --rng0 source=/dev/urandom`
- **Fonte:** [Proxmox forum 8.3.5 / 8.4.0](https://forum.proxmox.com/threads/uefi-pxe-boot-issues-after-upgrading-from-proxmox-ve-8-3-4-to-8-3-5.164468/)

### 2. Secure Boot blocca iPXE — RISOLTO

- **Sintomo:** `BdsDxe: failed to load Boot0003 ... Access Denied` dopo download NBP
- **Causa:** `ipxe.efi` / snponly non firmato Microsoft
- **Fix:** `efidisk0` con `pre-enrolled-keys=0` (Secure Boot off)

### 3. Indice sbagliato nel `boot.wim` — parzialmente risolto (evoluzione 15/07)

- **Sintomo:** loop reboot immediato post-WinPE, zero Samba
- **Causa:** `boot.wim` ISO ha **2 indici**:
  - **Indice 1:** `Microsoft Windows PE` — WinPE base
  - **Indice 2:** `Microsoft Windows Setup` — ambiente completo con `setup.exe`
- **Tentativo 1 (abbandonato):** export Setup-only (1 indice) + patch BCD 2→1 — instabile con wimboot PXE
- **Stato attuale (sessione notte):** ripristinato **`boot.wim` ISO v2 completo** (2 indici, Boot Index 2):
  - File: `627704165` → `629520229` byte dopo inject `startnet.cmd`
  - Backup setup-only: `boot.wim.bak-setup-only` (598 MB)
  - ISO sorgente: `Z:\isos\Windows\Win11_25H2_Italian_x64_v2.iso` montata su Proxmox `/mnt/iso2`
- **BCD:** originale v2 ISO (index 2 nativo), backup patch: `BCD.bak-patched-index1`

### 4. CT104 con 256 MB RAM — RISOLTO (15/07 sera)

- **Sintomo:** `wimlib-imagex` **Killed** (OOM) durante operazioni sul `boot.wim`
- **Causa:** `pct config 104` → `memory: 256`
- **Fix:** `pct set 104 -memory 1024`
- **Workaround operativo:** `wimtools` su host Proxmox + mount WIM in `/var/lib/lxc/104/rootfs/...` (FUSE non disponibile nel CT)

### 5. BCD ramdisk index — patch abbandonata con boot.wim v2 completo

- Patch 9× DWORD `2→1` applicata per WIM Setup-only esportato
- Con **`boot.wim` v2 a 2 indici** si usa **BCD originale v2** (index 2 nativo) — nessuna patch necessaria
- Script iPXE attuale serve BCD + boot.sdi + boot.wim **esterni** (non `rawwim`)

### 6. `winpeshl.ini` — **ERRORE DI FIX, RIMUOVERE** (15/07 sera → corretto in sessione notte)

- **Sintomo:** `ping -n 120` in `startnet.cmd` non rallentava il reboot (ciclo restava ~20s)
- **Primo fix (SBAGLIATO):** iniettato `winpeshl.ini` con `[LaunchApps]` che puntava a `startnet.cmd`
- **Causa reale del loop ~20s:** `winpeshl.exe` **non può eseguire file `.cmd`/`.bat`** in `[LaunchApps]` → fallisce in silenzio → WinPE reboot immediato (comportamento hardcoded)
- **Fix corretto:** **eliminare** `winpeshl.ini` — senza di esso WinPE usa il default di fabbrica: `cmd.exe` + `startnet.cmd` automatico
- **Stato attuale `boot.wim` v2:** `winpeshl.ini` **assente** ✓
- **`startnet.cmd` iniettato** (345 byte, via `wimlib-imagex update` su copia in `/tmp`):
  ```
  wpeinit → ping CT104 → ipconfig /all → net use \\192.168.10.104\wininstall
  → setup.exe /unattend:X:\autounattend.xml → ping -n 120 → cmd.exe
  ```
- **Se ciclo resta ~20s:** non si arriva a eseguire `startnet.cmd` → problema **pre-WinPE** (wimboot/BCD/boot.wim), non Samba né autounattend

### 7. Samba `[wininstall]` — **RISOLTO** (15/07 sera)

Tre problemi distinti, tutti corretti:

#### 7a. Autenticazione (errore 1326)

- **Sintomo:** `net use /user:novascm` → errore 1326 da Claudio-PC
- **Causa:** utente `novascm` presente in `pdbedit -L` ma **password Samba mai impostata** (non allineata a `pxe_smb_pass` nel DB)
- **Fix:**
  ```bash
  printf '%s\n%s\n' 'NovaSCMpxe2026!' 'NovaSCMpxe2026!' | smbpasswd -s novascm
  systemctl restart smbd nmbd
  ```
- **DB:** `pxe_smb_pass=NovaSCMpxe2026!`, `pxe_smb_user=novascm`, `map to guest = Bad User` già presente in `[global]`

#### 7b. Share apparentemente vuota (`install.wim` invisibile)

- **Sintomo:** `net use` OK ma `dir \\192.168.10.104\wininstall\sources` → 0 file
- **Causa:** `install.wim` era un **symlink** verso `/opt/novascm-pxe/server/dist/winpe/install.wim` (fuori dalla share). Samba non espone symlink che escono dal path della condivisione
- **Fix:** sostituito symlink con **hardlink** (stesso filesystem ZFS):
  ```bash
  rm /opt/novascm-pxe/smb/sources/install.wim
  ln /opt/novascm-pxe/server/dist/winpe/install.wim /opt/novascm-pxe/smb/sources/install.wim
  chown -R novascm:novascm /opt/novascm-pxe/smb
  chmod 755 smb smb/sources && chmod 644 smb/sources/install.wim
  ```

#### 7c. Cache directory SMB su Windows (share “vuota” dopo il fix)

- **Sintomo:** dopo hardlink, share ancora vuota da Explorer con sessione già aperta
- **Causa:** lease/cache SMB3 su sessione Windows aperta **prima** del fix (directory `sources` cached come vuota)
- **Fix lato client:**
  ```
  net use \\192.168.10.104\wininstall /delete /y
  net use X: \\192.168.10.104\wininstall /user:novascm NovaSCMpxe2026!
  dir X:\sources\install.wim
  ```
- **Verifica riuscita:** `install.wim` 6.760.658.043 byte visibile da Claudio-PC (`.80`)

### 8. `install.wim` v2 — **DEPLOYATO** (sessione notte)

- Sorgente staging: `Y:\_staging\win11-v2\install.wim`
- CT104: **7.464.366.837 byte**, WIM valido, **indice 5 = Windows 11 Pro**
- Hardlink in `smb/sources/install.wim`, visibile via `\\192.168.10.104\wininstall\sources\install.wim`
- DB: `pxe_wim_index=5`, `pxe_install_wim_mode=smb`

### 9. autounattend iPXE — fix nome file (sessione notte)

- **Problema:** wimboot iniettava autounattend come `PC-A53555` invece di `autounattend.xml`
- **Fix script iPXE:** `imgfetch --name autounattend.xml .../api/autounattend/{pc_name}`
- Console wimboot mostra `Using autounattend.xml` ✓

### 10. Script iPXE wimboot — evoluzione (sessione notte)

| Versione | Sintassi | Esito |
|----------|----------|-------|
| v1 | `index=2 rawwim gui` + solo boot.wim | wimboot OK, reboot ~20s |
| v2 | `index=2` + BCD + boot.sdi + boot.wim + autounattend | **attuale**, ancora reboot ~20s |

Script live (`api.py` `_ipxe_deploy`):
```ipxe
kernel http://192.168.10.104/winpe/wimboot index=2
imgfetch .../BCD BCD
imgfetch .../boot.sdi boot.sdi
imgfetch .../boot.wim boot.wim
imgfetch --name autounattend.xml .../api/autounattend/PC-A53555
boot
```

### 11. Test isolamento autounattend — reboot anche senza XML

- Boot senza autounattend → ancora `boot.EFI` + reboot
- **Conclusione:** non è l'XML il colpevole del loop pre-WinPE

---

## Config VM105 (stato sessione notte 15/07)

```
bios: ovmf
machine: pc-q35-11.0
rng0: source=/dev/urandom          ← OBBLIGATORIO per PXE OVMF
efidisk0: local-zfs:vm-105-disk-6, pre-enrolled-keys=0  ← RICREATO (reset NVRAM)
net0: e1000=BC:24:11:A5:35:55
sata0: 64G
boot: order=net0                  ← solo rete (no fallback SATA)
memory: 4096                      ← aumentato da 3072
tpmstate0: presente (Win11)
```

Reset EFI eseguito con `qm stop 105 --skiplock` → `--delete efidisk0` → ricreazione con `pre-enrolled-keys=0`.

---

## Cosa NON era il problema (scartato)

| Ipotesi | Esito |
|---------|-------|
| Driver ethernet mancanti per PXE | ❌ e1000 ha ROM PXE + driver in WinPE stock |
| DHCP / TFTP / iPXE / nginx | ❌ catena OK, log `.237` |
| `autoexec.ipxe` mancante | ❌ presente, embedded in snponly |
| Disco EFI locale al posto di network boot | ❌ workaround scartato; PXE vero funziona con rng0 |

---

## Prossimi passi concreti (priorità sessione notte 15/07)

### Priorità 1 — Capire se WinPE parte (ciclo ~20s = NO)

| Durata ciclo | Significato |
|--------------|-------------|
| **~20s** | WinPE **non** esegue `startnet.cmd` — problema wimboot/BCD/boot.wim/firmware |
| **≥120s** | WinPE OK, pausa debug attiva — leggere console (`ipconfig`, `net use`) |

### Priorità 2 — Tentativi wimboot rimanenti

1. `wimboot index=2` **senza** `gui` / `rawwim` (già fatto) + BCD/boot.sdi esterni
2. Provare `wimboot` **senza** `index=2` (BCD decide)
3. Provare `initrd` invece di `imgfetch` per BCD/boot.sdi (carica in RAM)
4. Verificare integrità `boot.wim` v2: `wimlib-imagex verify boot.wim`
5. Montare WIM su Proxmox (FUSE disponibile host) e ispezionare `setup.exe` in indice 2
6. Disabilitare temporaneamente `tpmstate0` su VM105 (test)
7. Cambiare NIC da `e1000` a `virtio` (solo se WinPE parte — driver diversi)

### Priorità 3 — Samba — **COMPLETATO** ✓

`install.wim` v2 7.46 GB accessibile. Zero connessioni da `.237` = Setup non arriva a `ImageInstall`.

### Priorità 4 — Test end-to-end (dopo WinPE OK)

1. Monitorare `smbstatus` CT104 durante boot VM105
2. Confermare partizionamento disco e avanzamento Setup

---

## File e path utili

| Cosa | Path |
|------|------|
| boot.wim live | CT104 `.../winpe/boot.wim` — **v2 ISO completo**, 2 indici, Boot Index 2, ~600 MB + startnet.cmd |
| boot.wim backup | `boot.wim.bak-setup-only`, `boot.wim.bak-pre-startnet-*` |
| BCD live | CT104 `.../winpe/BCD` — originale v2 (16 KB) |
| install.wim | CT104 `.../smb/sources/install.wim` — hardlink v2, 7.46 GB |
| DB settings | CT104 `/opt/novascm-pxe/server/novascm.db` — tabella `settings` |
| Codice live | CT104 `/opt/novascm-pxe/server/api.py` ← sync da `Y:\NovaSCM\server\api.py` |
| ISO sorgente | `Z:\isos\Windows\Win11_25H2_Italian_x64_v2.iso` → Proxmox `/mnt/iso2` |
| Staging install.wim | `Y:\_staging\win11-v2\install.wim` |
| Doc sessione | `Y:\NovaSCM\docs\PXE_VM105_PROBLEMI_20260715.md` |
| Console deploy | `http://192.168.10.104:9091/deploy-client?key=pxetest2026&pw_id=88&hostname=PC-A53555` |

---

## Cronologia rapida

| Data | Evento |
|------|--------|
| 2026-07-05 | Prima catena PXE end-to-end OK; loop post-autounattend, SMB path obsoleto corretto |
| 2026-07-14 | VM105 e1000, loop ~13s, nessun install.wim da Samba |
| 2026-07-15 mattina | Debug OVMF: rng0 + Secure Boot; `setup.exe` mancante trovato |
| 2026-07-15 sera | Export `boot.wim` indice 2 Setup-only; BCD patch 9× index 2→1; `winpeshl.ini` + `startnet.cmd` debug; CT104 RAM 1024 MB |
| 2026-07-15 sera | Test Samba da Claudio-PC: errore 1326 → fix `smbpasswd`, hardlink `install.wim`, permessi `novascm` |
| 2026-07-15 sera | Samba verificato: `install.wim` 6.76 GB visibile da `.80` dopo riconnessione pulita |
| 2026-07-15 notte | `install.wim` v2 deployato (7.46 GB, indice 5 Pro); fix `imgfetch --name autounattend.xml` |
| 2026-07-15 notte | `boot.wim` ripristinato ISO v2 completo; abbandonato Setup-only export + BCD patch |
| 2026-07-15 notte | Identificato `winpeshl.ini` con `.cmd` in LaunchApps come causa crash WinPE — rimosso/non presente |
| 2026-07-15 notte | `startnet.cmd` debug iniettato (345 B): wpeinit → net use → setup → ping 120 → cmd.exe |
| 2026-07-15 notte | VM105: reset EFI NVRAM, boot=net0 only, RAM 4 GB |
| 2026-07-15 notte | iPXE: `wimboot index=2` + BCD + boot.sdi + boot.wim (no rawwim/gui) |
| 2026-07-15 notte | **Ritest VM105: ancora reboot ~20s** — WinPE non parte, startnet.cmd non eseguito |

---

*Solo fatti verificati in log/console/test manuali.*

## Stato sessione salvata — 15/07/2026 notte

| Area | Stato |
|------|-------|
| Catena PXE (DHCP→iPXE→HTTP) | **OK** |
| wimboot download file | **OK** (`Using autounattend.xml`) |
| Samba `[wininstall]` + install.wim v2 | **OK** |
| WinPE boot / startnet.cmd | **FALLISCE** — loop ~20s |
| Windows Setup / ImageInstall | **Mai raggiunto** — zero SMB da `.237` |

**Ipotesi attiva:** il reboot avviene **prima** di WinPE (bootmgfw.efi / BCD / WIM index / firmware), non per autounattend né Samba.

**Credenziali test:** Samba `novascm` / `NovaSCMpxe2026!` — Proxmox `root@192.168.10.202` — deploy key `pxetest2026`

**Riprendere da:** Priorità 2 (tentativi wimboot) — verificare durata ciclo (20s vs 120s) in console Proxmox VM105.