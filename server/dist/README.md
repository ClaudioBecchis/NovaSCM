# server/dist — File binari PXE

Questa cartella contiene i file binari necessari per il boot PXE.
**Non versionati in git** (troppo grandi / binari di terze parti).

## Struttura richiesta

```
server/dist/
├── ipxe.efi          ← iPXE UEFI chainloader (~400 KB)
└── winpe/
    ├── wimboot       ← wimboot bootloader (no estensione, ~50 KB)
    ├── BCD           ← Windows Boot Configuration Data
    ├── boot.sdi      ← Windows Setup boot sector image
    └── boot.wim      ← WinPE boot image (~500 MB)
```

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

### Alternativa: estrarre da ISO Windows
`boot.wim` si trova in `sources\boot.wim` dentro l'ISO di Windows 11.
Puoi montare l'ISO e copiare il file direttamente.

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
