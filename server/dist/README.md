# server/dist — File binari PXE

Questa cartella contiene i file binari necessari per il boot PXE.
**Non versionati in git** (troppo grandi / binari di terze parti).

## Struttura richiesta

```
server/dist/
├── ipxe.efi                  ← iPXE UEFI chainloader — usare la variante snponly (~290 KB)
├── autoexec.ipxe             ← script chainload verso /api/boot/{mac} (specifico per ambiente, IP hardcoded)
├── NovaSCMDeployScreen.exe   ← schermata fullscreen WPF (opzionale, scaricata da /api/download/deploy-screen)
└── winpe/
    ├── wimboot       ← wimboot bootloader (no estensione, ~50 KB)
    ├── BCD           ← Windows Boot Configuration Data
    ├── boot.sdi      ← Windows Setup boot sector image
    ├── boot.wim      ← WinPE boot image (~500 MB)
    └── install.wim   ← immagine Windows da installare (~6-7 GB)
```

**IMPORTANTE — `ipxe.efi`**: usare sempre la variante **`snponly`**, non l'`ipxe.efi` pieno. Su hardware reale (verificato marzo 2026 su client HP) l'`ipxe.efi` completo (~1.1MB) dà errore "memory block too small" per limiti di memoria della ROM di rete del client. La variante snponly (~290KB) da `https://boot.ipxe.org/x86_64-efi/snponly.efi` funziona sempre.

**IMPORTANTE — `autoexec.ipxe`**: contenuto minimo:
```
#!ipxe
chain http://<IP_SERVER>:9091/api/boot/${net0/mac} || shell
```
Senza questo file iPXE si ferma al prompt interattivo dopo il boot (non fa il chain automatico verso l'API).

**IMPORTANTE — setting `pxe_install_wim_path`**: Windows Setup richiede l'`install.wim` raggiungibile via **percorso SMB/UNC** (non HTTP) durante l'`ImageInstall`. Il default nel codice (`\\192.168.10.201\wininstall\...`) è **obsoleto** (vecchio IP, share inesistente) — va aggiornato per ogni ambiente. Il setting **non è esposto** dall'endpoint `/api/settings` (schema ristretto a `default_workflow_id, webhook_url, webhook_enabled`) — va scritto direttamente nella tabella `settings` del DB SQLite:
```sql
INSERT INTO settings (key, value) VALUES ('pxe_install_wim_path', '\\<IP_SERVER>\wininstall\sources\install.wim')
ON CONFLICT(key) DO UPDATE SET value=excluded.value;
```
Serve una condivisione Samba (read-only, guest) che esponga quella cartella con dentro `sources/install.wim`.

## Come ottenere i file

### ipxe.efi
Scarica la build ufficiale:
```
https://boot.ipxe.org/ipxe.efi
```
Oppure copia da CT110 TFTP:
```bash
scp root@192.168.1.122:/srv/tftp/ipxe.efi server/dist/ipxe.efi
```

### wimboot
Scarica dal progetto GitHub:
```
https://github.com/ipxe/wimboot/releases/latest
```
Prendi il file `wimboot` (ELF, senza estensione) e mettilo in `winpe/wimboot`.

### BCD, boot.sdi, boot.wim — via Windows ADK

1. Scarica **Windows ADK** + **WinPE add-on** da Microsoft:
   - https://learn.microsoft.com/windows-hardware/get-started/adk-install

2. Crea immagine WinPE (da prompt admin con ADK installato):
   ```powershell
   copype amd64 C:\WinPE_amd64
   ```

3. Copia i file:
   ```powershell
   $winpe = "C:\WinPE_amd64"
   $dest  = "server\dist\winpe"
   Copy-Item "$winpe\media\Boot\BCD"             "$dest\BCD"
   Copy-Item "$winpe\media\Boot\boot.sdi"         "$dest\boot.sdi"
   Copy-Item "$winpe\media\sources\boot.wim"      "$dest\boot.wim"
   ```

### Alternativa consigliata: estrarre da ISO Windows (NON serve Windows ADK)

**Non serve Windows ADK/copype.** `postinstall.ps1` gira in fase specialize/oobeSystem (Windows già installato), non dentro una WinPE custom — quindi bastano i file stock di una qualsiasi ISO Windows:

```powershell
$mount = Mount-DiskImage -ImagePath "C:\path\to\Win11.iso" -PassThru
$vol = ($mount | Get-Volume).DriveLetter
Copy-Item "${vol}:\sources\boot.wim" "server\dist\winpe\boot.wim"
Copy-Item "${vol}:\boot\boot.sdi" "server\dist\winpe\boot.sdi"
Copy-Item "${vol}:\efi\microsoft\boot\bcd" "server\dist\winpe\BCD"
Copy-Item "${vol}:\sources\install.wim" "server\dist\winpe\install.wim"
Dismount-DiskImage -ImagePath "C:\path\to\Win11.iso"
```

**TODO verifica**: confermare con `Dism /Get-WimInfo /WimFile:server\dist\winpe\install.wim` (richiede prompt admin) che l'indice immagine configurato nell'autounattend (`/IMAGE/INDEX`, default 5 = solitamente "Pro" nelle ISO multi-edizione standard) corrisponda davvero all'edizione desiderata in QUESTA iso specifica — non ancora verificato al 2026-07-05.

## Verifica

Una volta copiati tutti i file, verifica con:
```bash
ls -lh server/dist/ipxe.efi server/dist/winpe/
```

Poi testa il PXE status via API:
```
GET http://192.168.1.100:9091/api/pxe/status
```
Tutti i file devono risultare `present: true`.
