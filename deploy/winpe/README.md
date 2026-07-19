# Deploy PXE — WinPE + DISM

Contenuto di questa cartella:

| File | Scopo |
|------|-------|
| `startnet_dism.cmd` | Script canonico eseguito da WinPE all'avvio: partiziona il disco, applica l'immagine Windows via DISM, configura il boot, copia unattend e postinstall |
| `diskpart_gpt.txt` | Riferimento statico dello schema partizioni (lo script lo genera inline, questo file è solo documentazione) |
| `novascm-pe.ini.example` | Esempio dei parametri che lo script si aspetta in `X:\novascm-pe.ini` — copiare e adattare, mai committare con valori reali |

## Perché DISM e non `setup.exe`

`setup.exe` con unattend completo (`ImageInstall`) si è rivelato fragile sulla fase `offlineServicing`, con fallimenti intermittenti privi di diagnostica utile. SCCM/MDT non usano quel percorso — usano DISM Apply-Image (estrazione diretta dei file) + `bcdboot`, più un unattend minimo solo per i pass `specialize`/`oobeSystem`. Stesso approccio qui.

## Injection: `inject_startnet.ps1` (consigliato)

```powershell
# 1. Copia il template e compilalo con i valori reali del tuo ambiente
Copy-Item novascm-pe.ini.example novascm-pe.local.ini
notepad novascm-pe.local.ini

# 2. Verifica senza modificare nulla
.\inject_startnet.ps1 -WimPath D:\dist\winpe\boot.wim -ConfigIni .\novascm-pe.local.ini -DryRun

# 3. Esegui davvero (richiede PowerShell come amministratore, usa dism.exe)
.\inject_startnet.ps1 -WimPath D:\dist\winpe\boot.wim -ConfigIni .\novascm-pe.local.ini
```

Fa da solo: valida che WIM/ini esistano e che i parametri obbligatori siano popolati, crea un backup timestampato del WIM (`boot.wim.bak-YYYYMMDD-HHMMSS`) prima di toccarlo, monta/copia/smonta con commit via `dism.exe`, e in caso di errore a metà indica esplicitamente come smontare a mano senza corrompere l'immagine.

`novascm-pe.local.ini` (o qualsiasi nome con dati reali) **non va mai committato** — resta locale a chi esegue l'injection.

## Procedura manuale (fallback, o su Linux/WSL senza accesso a `dism.exe`)

Se non si ha accesso a Windows con DISM, la stessa cosa si può fare con `wimlib-imagex`:

```bash
# 1. Monta l'immagine WinPE (indice 1 tipicamente) in scrittura
mkdir /tmp/pemount
wimlib-imagex mountrw boot.wim 1 /tmp/pemount

# 2. Copia lo script e il file di configurazione
cp startnet_dism.cmd /tmp/pemount/Windows/System32/startnet.cmd
cp novascm-pe.ini.example /tmp/pemount/novascm-pe.ini   # poi editare con i valori reali

# 3. Smonta e commit
wimlib-imagex unmount /tmp/pemount --commit
```

**Prima di farlo su un ambiente reale**: fare sempre un backup del `boot.wim` esistente (`cp boot.wim boot.wim.bak-$(date +%Y%m%d)`), coerente con la cronologia di backup già presente nell'ambiente di lab (vedi `docs/AMBIENTE_NOVASCM.md`).

## Requisiti hardware/VM per il boot

- Disco **SATA**, non `virtio-scsi` (driver mancanti in WinPE)
- `cpu: host` (non `kvm64` — causa crash noti in wimboot)
- Secure Boot disabilitato per il test (`pre-enrolled-keys=0` su Proxmox OVMF), a meno che l'immagine non abbia i certificati corretti
- Boot order: prima il disco (`sata0`/`scsi0`), poi la rete (`net0`) — il fallback a PXE avviene automaticamente se il disco non è avviabile

## Parametri in `novascm-pe.ini`

| Parametro | Obbligatorio | Descrizione |
|-----------|--------------|-------------|
| `SERVER_IP` | sì | IP del server NovaSCM (per il ping di verifica rete) |
| `SMB_SHARE` | sì | UNC della condivisione con `install.wim` |
| `SMB_USER` / `SMB_PASS` | sì | Credenziali della condivisione SMB |
| `IMAGE_INDEX` | sì | Indice dell'immagine nel WIM — verificare con `Dism /Get-WimInfo /WimFile:install.wim` |
| `LABCONFIG_BYPASS` | no (default `0`) | `1` per bypassare i controlli TPM/Secure Boot/RAM/CPU — solo per hardware/VM di test che non li soddisfa |
| `BRAND_NAME` | no (default `NovaSCM`) | Nome mostrato nel banner e nel titolo della finestra durante il deploy — per chi vuole white-label il proprio deployment |

Se un parametro obbligatorio manca, lo script si ferma con un errore esplicito in `X:\pxe-startnet.log`, non prosegue con un valore indovinato.

## Progresso a schermo

Lo script mostra un banner all'avvio e un indicatore `[N/8] descrizione fase` (a schermo e nel titolo della finestra) per ogni fase — prima la console restava vuota per minuti durante l'apply immagine (tutto l'output andava solo nel file di log), dando l'impressione di essere bloccata. Ispirato a come Microsoft ConfigMgr sostituisce la shell WinPE con una UI grafica (`tsbootshell.exe`) invece del cmd nudo, ma restando su testo semplice: nessuna dipendenza aggiuntiva richiesta all'immagine WinPE.
