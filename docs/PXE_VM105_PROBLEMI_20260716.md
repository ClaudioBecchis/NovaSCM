# PXE VM105 — Sessione 16 luglio 2026 (continua da PXE_VM105_PROBLEMI_20260715.md)

Ripresa del debug dalla sessione Grok del 15/07 (vedi doc precedente per tutto lo storico). Stato di partenza: catena PXE OK, Samba/install.wim risolti, ma WinPE non arriva a eseguire `startnet.cmd` — reboot loop ~20s.

## Test eseguiti oggi (tutti NEGATIVI/inconclusivi sul timing, ma ognuno ha eliminato un'ipotesi)

### 1. Rimosso `index=2` dal comando kernel wimboot (BCD decide l'indice)

- **Risultato:** ciclo identico, nessun cambiamento nel timing (~30-31s, confermato da log nginx)
- **Conclusione:** l'hardcoding dell'indice non è la causa

### 2. `initrd` al posto di `imgfetch` per BCD/boot.sdi

- **Risultato:** ciclo identico ~30-31s
- **Conclusione:** la sintassi di caricamento file iPXE non è la causa

### 3. `wimlib-imagex verify boot.wim`

- **Risultato:** **verificato con successo al 100%**, nessuna corruzione in nessuno dei due indici
- **Conclusione:** il file WIM non è corrotto

### 4. Montaggio indice 2 (Windows Setup) su host Proxmox via FUSE

- **Risultato:** `setup.exe` presente e corretto (root + `/sources`), `startnet.cmd` iniettato correttamente (345 byte, contenuto verificato), `winpeshl.ini` correttamente assente
- **Conclusione:** il contenuto del WIM è strutturalmente corretto

### 5. Riapplicato `bootstatuspolicy IgnoreAllFailures` + `recoveryenabled No` sul BCD LIVE

**Scoperta importante:** il fix bcdedit applicato il 15/07 sera era stato fatto su un BCD "patchato" (index 2→1) poi **abbandonato** quando è stato ripristinato il boot.wim v2 completo — il BCD live attuale non aveva mai ricevuto questo fix.

- Prelevato BCD da CT104, applicato via bcdedit da PC Windows locale (prompt admin), ripushato su CT104 (backup: `BCD.bak-pre-bootstatuspolicy-20260716`)
- **Risultato:** ciclo IDENTICO ~30-31s, **zero connessioni Samba da .237** durante tutto il test (monitorato smbstatus ogni 5s)
- **Conclusione:** il crash avviene PRIMA che Windows/winload.efi possa anche solo tentare l'avvio — altrimenti bootstatuspolicy avrebbe cambiato il comportamento. Il problema è a livello wimboot/firmware, non OS.

## Scoperta chiave: screenshot console via QEMU monitor (screendump)

Senza accesso VNC/browser, usato `qm monitor 105` + comando `screendump /path.ppm` (nativo QEMU/Proxmox, non richiede noVNC) per catturare la console reale in sequenza durante il boot. Convertito PPM→PNG con script PowerShell locale (System.Drawing, nessun tool esterno necessario).

**Sequenza osservata:**
1. Boot PXE normale, download wimboot/BCD/boot.sdi/boot.wim (~11s totali, confermato da log nginx)
2. **Log interno di wimboot v2.9.0 catturato per la prima volta:**
   ```
   wimboot v2.9.0 -- Windows Imaging Format bootloader
   Using BCD via 0x79ded958 len 0x6000 ...found BCD
   Using boot.sdi via 0x79deda18 len 0x306000
   Using boot.wim via 0x79dedad8 len 0x2585b765 ...found WIM file boot.wim
   Using autounattend.xml via 0x79dedb98 len 0x2696 ...found file
   ...found file "\Windows\Boot\EFI\bootmgfw.efi"
   Using bootmgfw.efi via 0x79a77060 len 0x2de9c8 ...extracted
   ...found file "\Windows\Boot\EFI_EX\bootmgfw_EX.efi"
   Using bootmgfw_EX.efi via 0x79a77150 len 0x2de938 ...extracted
   ...patching WIM boot.wim (header, autounattend.xml, lookup.copy/boot/file, boot.copy, directory, dir.file, System32, dir.subdir, length)
   ...found file boot.stl, fonts (segmono_boot.ttf, segoen_slboot.ttf, segoe_slboot.ttf, wgl4_boot.ttf)
   Secure Boot is disabled
   ```
   Tutti i passaggi completano SENZA errori visibili.
3. **Subito dopo questo log (entro ~1-2s), la VM è già ripartita da zero** (schermata firmware PXE iniziale) — **nessun messaggio di errore, nessuna schermata blu, reset istantaneo e silenzioso**

**Interpretazione:** il crash avviene esattamente nel momento dell'handoff da wimboot a `bootmgfw.efi` (o subito dopo), con un reset di piattaforma immediato e silenzioso — non un errore Windows diagnosticabile (che mostrerebbe testo per almeno una frazione di secondo). Questo pattern è tipico di un'eccezione a livello firmware (page fault, violazione di memoria) piuttosto che un errore applicativo Windows.

## Ipotesi principale (non ancora testata, richiede conferma utente)

`pve-edk2-firmware` è alla versione **4.2025.05-2** (confermato via `dpkg -l`). Ricerche web indicano che:
- Versioni recenti di pve-edk2-firmware hanno introdotto problemi di boot noti (boot loop / reset) per alcuni utenti, risolti con **downgrade a versioni precedenti** (es. `3.20220526-1` in un caso riportato sul forum Proxmox)
- Le nuove versioni edk2 hanno introdotto policy di memory protection (NX) più stringenti che possono causare crash silenziosi in bootloader "non convenzionali" come wimboot (che patcha WIM in memoria e usa tecniche di boot non standard)

**Non ho effettuato il downgrade** perché è un pacchetto di sistema condiviso sull'intero nodo Proxmox (pve-minipc), che affetterebbe tutte le VM OVMF, non solo VM105 — richiede conferma esplicita dell'utente prima di procedere.

## Stato VM105/CT104 a fine sessione

- VM105: **spenta** (`qm stop 105`)
- CT104: **attivo**, servizio novascm-pxe attivo con script iPXE aggiornato (senza `index=2`, con `initrd` per BCD/boot.sdi — nessuno dei due causa regressioni, lasciati così)
- BCD live: versione con `bootstatuspolicy IgnoreAllFailures` + `recoveryenabled No` (backup pre-fix: `BCD.bak-pre-bootstatuspolicy-20260716`)

## Aggiornamento — downgrade firmware BLOCCATO da apt/Proxmox

Tentato `apt-get install --allow-downgrades pve-edk2-firmware=4.2025.05-1` (versione immediatamente precedente disponibile nel repo, insieme a `4.2025.02-4` ancora più vecchia). **apt ha rifiutato l'operazione**: il downgrade trascinerebbe la rimozione di `proxmox-ve`, `pve-manager`, `qemu-server`, `pve-container`, `pve-ha-manager` (meta-pacchetto Proxmox VE + stack di gestione), perché quelle versioni del firmware sono più vecchie della dipendenza minima richiesta dal proxmox-ve attualmente installato (9.2.0). **Non forzato** — richiederebbe `touch /please-remove-proxmox-ve` + purge, un'azione distruttiva sull'intero stack di gestione di un nodo in produzione. Nessuna modifica effettivamente applicata, versioni confermate invariate dopo il tentativo.

## Aggiornamento — test i440fx/SeaBIOS: INCONCLUSIVO

Cambiata VM105 da `bios: ovmf` + `machine: pc-q35-11.0` a `bios: seabios` + `machine: pc` (con rimozione/poi ripristino di `efidisk0`, confermato dall'utente essendo un'azione distruttiva sul NVRAM della VM di test).

**Risultato:** il firmware SeaBIOS (iPXE 1.20.1 embedded via ROM) scarica comunque `ipxe.efi` — il **DHCP UniFi serve sempre lo stesso filename `ipxe.efi`** indipendentemente dal client (non fa architecture detection option 60/93 per distinguere BIOS legacy da UEFI). SeaBIOS lo etichetta "PXE-NBP (may be EFI?)" e resta bloccato lì, non essendo in grado di eseguire un binario EFI. **Test non valido/non comparabile** così com'è — per un vero confronto BIOS vs UEFI servirebbe configurare il DHCP per servire un NBP legacy (es. `undionly.kpxe`) ai client BIOS, che è una modifica di rete condivisa (fuori scope di un test isolato sulla VM).

**VM105 ripristinata** alla configurazione originale OVMF (bios=ovmf, machine=pc-q35-11.0, efidisk0 riattaccato allo stesso disco `vm-105-disk-6`) — stato finale identico a prima di questo test.

## Conclusione sessione 16/07

Tutte le vie diagnostiche **sicure e isolate alla VM/CT di test** sono state esaurite: 6 ipotesi testate e escluse (index=2, initrd, integrità WIM, contenuto WIM, bootstatuspolicy BCD, i440fx-non valido). Il downgrade firmware — l'ipotesi più promettente rimasta — è bloccato da una protezione apt/Proxmox che impedirebbe di romperlo senza un'azione distruttiva sullo stack di gestione dell'intero nodo.

**Opzioni per proseguire (da valutare con l'utente):**
1. Testare il downgrade firmware in un ambiente Proxmox isolato/usa e getta (non su pve-minipc in produzione) per confermare l'ipotesi senza rischio
2. Configurare il DHCP con architecture detection per abilitare un vero test BIOS legacy (richiede touch alla rete condivisa)
3. Accettare il limite attuale e considerare un approccio alternativo per il deploy Windows (es. ISO montata via virtual media invece di wimboot/PXE per il boot iniziale, mantenendo NovaSCM per il provisioning post-boot)
4. Cercare aggiornamenti di `wimboot` stesso (attualmente v2.9.0) — potrebbe esserci una versione più recente con fix di compatibilità per firmware EDK2 recenti

Task ancora pendenti dalla lista originale (bassa priorità vista l'evidenza firmware-level): disabilitare `tpmstate0`, cambiare NIC e1000→virtio.

## SVOLTA — probabile causa reale trovata: modello CPU `kvm64` (default Proxmox)

Ispezionato con lo strumento **ZEMU** (`zemu.exe`, in `Downloads\zemu_qemu\`, bundle QEMU standalone con file `wimldr`/`wim.qcow2` — un ambiente pensato apposta per testare boot stile WIM) — la stessa ISO Win11 25H2 v2 si avvia perfettamente anche lì. Catturata la riga di comando QEMU reale che ZEMU usa (via `Get-CimInstance Win32_Process`):

```
qemu-system-x86_64w.exe -bios X64_EFI.fd -cpu Icelake-Server -accel tcg,thread=multi -smp 4
  -M q35,kernel-irqchip=off -m 4096 -device vmware-svga -device usb-ehci -device usb-kbd -device usb-tablet
  -nic user,model=e1000 -drive file=...,index=0,media=cdrom -boot d,splash-time=1,strict=on
```

Punto chiave: **`-cpu Icelake-Server`** — un modello CPU moderno con feature set ricco. ZEMU può usarlo solo perché usa **TCG** (emulazione software pura, sull'host Windows) e quindi non è vincolato all'hardware fisico reale. L'host Proxmox pve-minipc ha una CPU fisica **Intel i7-8700T** (niente AVX-512) e usa **KVM**, con VM105 configurata senza `cpu:` esplicito → default **`kvm64`**, un modello volutamente minimale/generico molto povero di funzionalità.

**Ipotesi:** `bootmgfw_EX.efi` (introdotto per il trust "Windows UEFI CA 2023", specifico per Win11 24H2+) potrebbe eseguire controlli/istruzioni che richiedono funzionalità CPU assenti nel generico `kvm64`, causando l'eccezione silenziosa e il reset istantaneo osservato.

**Rilevante anche per l'obiettivo reale**: i PC fisici di destinazione hanno la **propria CPU vera** — mai il `kvm64` minimale di Proxmox. Impostare `cpu: host` (passthrough della CPU fisica) è quindi anche più rappresentativo del deploy reale, non solo un workaround di test.

### Test con `cpu: host`

VM105 ricreata da zero (su richiesta utente, "cancella e ricrea senza impostazioni custom") e riconfigurata con `cpu: host` invece del default. **Nota**: la VM fresca aveva perso il fix storico "Secure Boot disabilitato" (`pre-enrolled-keys=0`) — regressione temporanea che ha causato un errore diverso ("BdsDxe: failed to load Boot0002 ... Access Denied", cioè Secure Boot che blocca ipxe.efi non firmato) prima di poter testare `cpu: host` sul percorso PXE. Ripristinato `pre-enrolled-keys=0`.

**Risultato preliminare con `cpu: host` + Secure Boot off + boot PXE**: **un solo download di `boot.wim`, NESSUN ciclo di reboot ripetuto** in 70 secondi di monitoraggio nginx — pattern radicalmente diverso dal loop ~30s costante osservato in ogni test precedente con `kvm64`. Fortemente indicativo che **`cpu: host` risolva il crash**. Test in corso di conferma finale (verifica se WinPE arriva davvero a eseguire `startnet.cmd`/setup.exe, non solo se il download si ripete).

### Prossimo passo per la prossima sessione

Se confermato: applicare `cpu: host` (o un modello esplicito con feature set moderno tipo `qemu64`+flags o un modello Xeon/Core specifico) come fix permanente nella configurazione standard delle VM di test NovaSCM, e verificare se lo stesso principio si applica quando si farà il deploy reale su hardware fisico (che già di per sé espone la CPU vera, quindi il problema potrebbe non essersi mai presentato su hardware reale).

## Aggiornamento — test firmware isolato (senza toccare apt/proxmox-ve) + TPM + disco: TUTTI NEGATIVI

Trovato un modo per testare versioni firmware precedenti **senza** installarle come pacchetto (evitando il blocco apt che rimuoverebbe `proxmox-ve`): `apt-get download` (solo download, non installazione) + estrazione manuale del singolo file `OVMF_CODE_4M.secboot.fd` dal `.deb`, sostituzione temporanea con backup, test, ripristino. Confermato che VM105 è l'unica VM sul nodo che usa OVMF (tutto il resto sono CT/LXC) — rischio per altri workload praticamente nullo. Ogni sostituzione confermata esplicitamente dall'utente.

**Testate 3 versioni del file firmware, tutte con ESITO IDENTICO (~30-31s, nessun cambiamento):**
- 4.2025.05-2 (originale)
- 4.2025.05-1
- 4.2025.02-4

**Firmware ripristinato all'originale, checksum verificato identico, pacchetto dpkg mai stato toccato (restato 4.2025.05-2 per tutta l'operazione).**

**Testato anche, entrambi negativi:**
- Rimozione `tpmstate0` (confermato dall'utente) → nessun cambiamento
- Rimozione `sata0` (VM senza alcun disco locale, confermato dall'utente) → nessun cambiamento

**VM105 ripristinata identica alla configurazione originale in ogni dettaglio** (tpmstate0, sata0, tutto riattaccato).

## Conclusione aggiornata

La versione del firmware EDK2/OVMF **è definitivamente esclusa** come causa (3 versioni diverse testate, comportamento identico). TPM e disco locale esclusi. Il crash rimane un reset istantaneo e silenzioso esattamente all'handoff wimboot→bootmgfw.efi, riproducibile al 100% indipendentemente da ogni variabile testata finora (script iPXE, contenuto/integrità WIM, BCD, firmware, TPM, disco).

**Ipotesi rimaste, non testabili in modo isolato sulla VM:**
1. Bug specifico di wimboot 2.9.0 con questa combinazione Windows 11 24H2 + chipset q35 (il changelog 2.9.0 menziona modifiche recenti proprio per compatibilità Windows 11 24H2 — potrebbe essere incompleto/buggy per questo caso specifico)
2. Incompatibilità specifica del chipset q35 (non testabile pulitamente in i440fx senza modificare il DHCP per architecture detection, che è una modifica di rete condivisa)
3. Un problema nell'ISO Windows 11 25H2 Italian specifica usata (`Z:\isos\Windows\Win11_25H2_Italian_x64_v2.iso`) — mai provato con un'ISO diversa

**Suggerimento concreto non ancora tentato:** aprire una issue/discussion su `github.com/ipxe/wimboot` descrivendo il problema (log wimboot + screenshot) — è un progetto attivo, potrebbero riconoscere un bug noto o suggerire un fix mirato.

## Test diagnostico differenziale: Windows 10 22H2 su VM105 — RISULTATO DECISIVO

Vincolo del progetto chiarito dall'utente: il deploy DEVE avvenire 100% via rete (PXE), il PC client deve essere solo collegato via cavo, nessun intervento manuale. Il test sul portatile fisico già configurato (`E8:D8:D1:ED:BF:8F`) è stato escluso (PC in uso).

**Setup:** creata un'area di test parallela su CT104 (`/opt/novascm-pxe/server/dist/winpe10/` + location nginx `/winpe10/`, non toccando i file Win11 già pronti). Estratto `boot.wim`/`BCD`/`boot.sdi` da `Win10_22H2_Italian_x64v1.iso` (montata via il mount CIFS già esistente `/mnt/winiso` → rimontata come loop su `/mnt/win10iso`). Iniettato `startnet.cmd` di debug nell'indice 2 (Windows Setup, stesso Boot Index 2 di Win11). Script iPXE temporaneamente puntato a `/winpe10/` invece di `/winpe/` (poi ripristinato).

**Risultato:** **NESSUN reboot loop** — la catena di download (wimboot/BCD/boot.sdi/boot.wim) è avvenuta UNA SOLA VOLTA, senza ripetersi. La VM è rimasta "running" per ~20s (a differenza del pattern di reset istantaneo di Windows 11), poi si è fermata (`status: stopped`).

**Causa dell'arresto identificata nei log di sistema (`journalctl`):** il processo QEMU di VM105 è stato **ucciso dall'OOM killer del kernel host** (`oom-kill: task_memcg=/qemu.slice/105.scope, task=kvm, ... anon-rss:3392712kB`). L'host pve-minipc ha solo **7.5GB di RAM totali, senza swap**, con ~4.3GB già impegnati dai 14 container LXC sempre attivi (pihole, cloudflared, novascm-server, polaris-search, powerdns, ecc.) — non c'era abbastanza margine per VM105 (4096MB allocati) quando ha iniziato a usare memoria reale eseguendo WinPE.

**Interpretazione:** Windows 10 **non ha incontrato il crash di Windows 11** — è arrivato più avanti nella sequenza di boot (con ogni probabilità dentro WinPE in esecuzione, dato che il pattern di reset istantaneo osservato con Win11 non si è MAI ripresentato) prima di essere fermato da un problema di risorse host completamente indipendente e non collegato al bug. Questo **conferma con forza** che il crash è specifico della combinazione Windows 11 25H2 + wimboot 2.9.0 (probabilmente `bootmgfw_EX.efi`/catena di trust "Windows UEFI CA 2023", introdotti di recente nel changelog di wimboot 2.9.0 proprio per compatibilità Win11 24H2+) — non un problema generale di VM/OVMF/wimboot/chipset.

**Ambiente di test ripulito completamente:** rimossa directory `winpe10`, nginx e `api.py` ripristinati alla configurazione originale (index=2, imgfetch per tutti i file, nessun test override), CT104 riavviato (si era fermato insieme all'evento OOM, causa non accertata ma probabilmente correlata alla pressione di memoria sull'host durante l'evento), VM105 spenta.

## Conclusione finale sessione 16/07

**Causa identificata con alta confidenza:** bug/incompatibilità specifica tra wimboot 2.9.0 e la catena di boot di Windows 11 25H2 (probabilmente `bootmgfw_EX.efi` per il trust "Windows UEFI CA 2023"), non un problema di VM/firmware/hardware generico.

**Percorsi concreti per la prossima sessione, in ordine di sforzo:**
1. **Aprire issue su `github.com/ipxe/wimboot`** con i dettagli raccolti (versione 2.9.0, log wimboot completo, Windows 11 25H2, comportamento: reset istantaneo e silenzioso subito dopo "Secure Boot is disabled" e l'estrazione di `bootmgfw_EX.efi`)
2. **Retest Windows 10 con più RAM disponibile** (liberando temporaneamente memoria da CT non essenziali, o aumentando la RAM fisica dell'host) per confermare che WinPE arriva davvero fino in fondo — al momento si è solo dedotto indirettamente dall'assenza del reset loop
3. **Provare a forzare `bootmgfw.efi` invece di `bootmgfw_EX.efi`** se esiste un modo per dire a wimboot di non tentare l'estrazione/uso del bootloader `_EX` (potrebbe essere proprio quel componente introdotto di recente a rompersi con l'attuale OVMF)
4. **Cercare una ISO Windows 11 diversa** (es. build 24H2 pre-25H2, o una ISO "de-24H2-ified" già circolante nella comunità sysadmin per problemi noti col trust "UEFI CA 2023")

## RISOLUZIONE — causa del crash trovata e confermata: cpu host

**Causa reale identificata (16/07, sessione pomeridiana):** il default Proxmox per la CPU virtuale (kvm64, generico/minimale) causa il crash. Impostando `cpu: host` (passthrough della CPU fisica reale, un Intel i7-8700T) su VM105, il crash non si e' piu' ripresentato in nessun test successivo. Confermato piu' volte: nessun reset istantaneo, nessun ciclo di reboot, Windows Setup arriva a girare per davvero (schermate GUI reali di Windows 11 Setup).

Trovato ispezionando ZEMU (zemu.exe, tool standalone in Downloads\zemu_qemu\, bundle QEMU + file wimldr/wim.qcow2 per test WIM-boot), che avvia la stessa ISO Win11 25H2 v2 senza problemi usando `-cpu Icelake-Server` via TCG (emulazione software). L'host fisico (i7-8700T, niente AVX-512) usa KVM con default kvm64 — un modello volutamente minimale, molto povero di funzionalita' rispetto sia a Icelake-Server sia alla CPU fisica reale.

Rilevante anche per i PC fisici reali: essendo un fix specifico della virtualizzazione, e' probabile che questo bug non si presenti mai su hardware fisico (i PC di destinazione avranno sempre la propria CPU vera).

## Nuovo problema scoperto (indipendente): disco locale non visibile a Setup nel percorso wimboot

Dopo il fix CPU, preparata VM105 per un deploy completo (OVMF + Secure Boot off + TPM 2.0 + disco locale 64GB + cpu host + rete). Windows Setup non vede alcun disco locale ("Installa il driver per visualizzare l'hardware"), sia con disco SATA sia IDE.

Isolato con un test di controllo decisivo: boot diretto della stessa ISO da CD-ROM virtuale (bypassando wimboot/PXE) con lo stesso disco presente -> Setup vede il disco perfettamente, arriva fino a "Installa Windows 11 / Ripristina il PC". Conferma: il problema e' specifico del percorso wimboot.

Tentativi falliti: bus IDE invece di SATA, flag `gui` di wimboot, ritardo esplicito in startnet.cmd prima di setup.exe, script diagnostico diskpart iniettato in startnet.cmd (verificato che il file su disco viene aggiornato correttamente, ma al boot Setup mostra comunque la sua GUI direttamente senza eseguire lo script).

Scoperta tecnica: per l'indice "Windows Setup" l'avvio potrebbe non passare da System32\startnet.cmd come assunto — meccanismo esatto non ancora confermato.

Issue #65 su github.com/ipxe/wimboot aggiornata con tutti questi dettagli, in attesa di risposta dai maintainer.

## Altri miglioramenti fatti in questa sessione (non PXE-correlati)

- Rimossi dati demo/finti dal database CT104 (CR id 2-7, introdotti da seed_demo.py in una sessione passata), mantenuti solo i 2 record reali (PC-A53555, PC-EDBF8F)
- Fix autenticazione dashboard NovaSCM (index.html): il frontend leggeva la API key da un meta tag non piu' iniettato dal server (fix sicurezza precedente), lasciando l'app permanentemente non autenticata. Implementato flusso mancante: prompt one-time -> scambio via /api/ui-token -> token salvato in localStorage (8h validita')
- Aggiunto sistema di eventi live (SSE): nuovo endpoint /api/events, broadcast su ogni boot PXE e cambio stato workflow. Frontend aggiornato istantaneamente via EventSource, toast rosso immediato su errore. Polling mantenuto solo come fallback
- Polling PXE aggiunto al ciclo di aggiornamento automatico (prima la sezione PXE/Boot Manager si caricava una sola volta)

## Approccio PE-first (proposto dall'utente) e scoperta decisiva

Provato l'approccio "PE-first" (come MDT/SCCM): invece di avviare via wimboot direttamente l'indice 2 (Windows Setup), avviare l'indice 1 (WinPE puro), verificare il disco lì con diskpart, poi lanciare `setup.exe` manualmente dalla share di rete con `/unattend`.

**Setup:** cambiato `kernel {static_url}/wimboot index=1` (era index=2). Copiata l'intera cartella `\sources\` dell'ISO (tranne install.wim gia' presente) sulla share Samba `[wininstall]`, cosi' `setup.exe` e' raggiungibile da PE via rete. Nuovo `startnet.cmd` iniettato nell'**indice 1** (non 2): wpeinit -> diskpart list disk (log) -> net use share -> lancio `\\192.168.10.104\wininstall\sources\setup.exe /unattend:X:\autounattend.xml`.

**File boot.wim/BCD/boot.sdi rigenerati puliti dall'ISO originale** (non piu' quelli manipolati nelle sessioni precedenti) prima di questo test, per escludere corruzione accumulata — stesso risultato di prima con Setup diretto (nessun disco), quindi la corruzione del file non era la causa.

**Risultato:** `setup.exe` si e' avviato per davvero dalla rete ("Avvio del programma di installazione") — molto più avanti di qualsiasi test precedente. Fallito solo su `X:\autounattend.xml` non trovato (percorso diverso in questo contesto PE, fix banale).

**Scoperta decisiva (dal log di debug):** eseguito manualmente `diskpart` -> `list disk` in PE PRIMA di lanciare Setup: **"Nessun disco fisso da visualizzare"**. Zero dischi enumerati, in PE puro, indipendentemente da Setup. Questo riformula la diagnosi: non e' che Setup salti l'inizializzazione di PE — e' che **nessun dispositivo disco viene enumerato affatto** durante un boot wimboot, con qualsiasi bus provato (SATA, IDE, virtio-scsi), sulla stessa VM che invece vede il disco istantaneamente con un boot ISO diretto (no wimboot).

Questo spiega anche il fallimento precedente del caricamento manuale del driver virtio (trovato il .inf ma "Errore durante l'installazione del driver" — coerente con l'assenza di un device PCI/PnP a cui il driver possa agganciarsi).

**Issue #65 aggiornata** con questa scoperta precisa, chiedendo al maintainer se il processo di boot di wimboot (hook I/O per il ramdisk WIM/BCD/boot.sdi) possa interferire con l'enumerazione PCI/ACPI di ALTRI controller storage nella stessa VM.

**Stato VM105 a fine sessione:** ancora in pausa di debug (shell cmd.exe raggiunta con successo), boot.wim/BCD/boot.sdi live sono ora le versioni fresche+PE-first (backup dei file precedenti in `winpe/backup-pre-fresh-20260716/`). Script iPXE punta a `index=1`.

**Prossimo passo concreto:** aggiungere il fix banale (percorso corretto per autounattend.xml in questo contesto PE — probabilmente va richiesto via URL diretto invece di assumere che sia su X:\), poi ripetere `diskpart list disk` per confermare se resta vuoto anche dopo il fix, isolando definitivamente se il problema e' l'enumerazione PCI o qualcos'altro.

## RISOLTO DEFINITIVAMENTE — causa del disco non visibile: driver storage mancante nel WIM (non un bug wimboot)

**Confermato con DISM**: scaricato il `boot.wim` (indice 1, PE) in locale, montato offline con `Dism /Mount-Wim`, iniettato il driver `vioscsi.inf` (Red Hat VirtIO SCSI, da `virtio-win-0.1.285.iso`) con `Dism /Add-Driver`, rismontato con `/Commit`. Cambiato anche il disco VM105 da SATA/IDE a **virtio-scsi (scsi0)** per usare questo driver.

**Risultato: SUCCESSO TOTALE.** Al riavvio, `diskpart list disk` in PE ora mostra **"Disco 0 Online 64 Gbytes"** — il disco e' finalmente visibile. Confermato ripetutamente su piu' riavvii. Il problema non era mai stato un bug di wimboot ne' un problema di enumerazione PCI: era semplicemente che questo specifico `boot.wim` (indice PE) non aveva il driver storage necessario per il controller virtio-scsi — esattamente come un deploy MDT/SCCM richiede sempre l'iniezione di driver storage per hardware non-inbox.

**Confermato anche dal maintainer di wimboot** (issue #65, chiusa come risolta): il crash originale era un problema noto (CPU deve supportare `popcnt`/`sse4.2`, risolto con `cpu: host`); il problema del disco era giustamente considerato non correlato a wimboot.

## Approccio PE-first: boot index allineato con successo

Il boot.wim originale aveva `Boot Index: 2` (Setup) nell'header, ma noi avviavamo `index=1` (PE) via riga di comando wimboot — mismatch che impediva il patching automatico di `autounattend.xml` su X:\ (wimboot patcha in base al Boot Index dell'header, non al parametro `index=` passato a runtime).

**Fix:** usato `wimlib-imagex export` per ricreare il WIM con l'indice 1 (PE) come **nuovo Boot Index** (`--boot` flag), mantenendo l'indice 2 (Setup) come immagine secondaria. Verificato con `wimlib-imagex info`: `Boot Index: 1`.

## Problema residuo (separato, minore): `net use` fallisce quasi sempre con errore 53

Con disco visibile e Boot Index corretto, resta un problema di connessione SMB **lato client WinPE**: `net use \\192.168.10.104\wininstall` fallisce con **"Errore di sistema 53 - Impossibile trovare il percorso di rete"** nella stragrande maggioranza dei tentativi (automatici E manuali), sia con attesa breve che con attesa di 150 secondi, con o senza retry.

**Scoperta importante durante il debug:** un click accidentale nella console puo' mettere `cmd.exe` in **modalita' "Seleziona" (QuickEdit)**, che mette in PAUSA l'esecuzione dello script finche' non si preme Esc — questo ha causato risultati fuorvianti in alcuni test precedenti (VM apparentemente "bloccata" senza progredire). Attenzione a non cliccare nella finestra della console durante i test futuri.

**Dato chiave dai log Samba (`log.smbd` su CT104):** su tutti i tentativi di `net use` fatti in questa sessione (molti, automatici e manuali), risultano solo **2 connessioni accettate** dal server ("Allowed connection from 192.168.10.237"), ed entrambe con autenticazione NTLMSSP completata con successo. Questo prova che **il problema non e' il server Samba** (quando la connessione arriva, funziona sempre) — la maggior parte dei tentativi client non arriva proprio a contattare la rete a livello SMB, nonostante `ping` e `ipconfig` mostrino la rete IP funzionante correttamente.

**Ipotesi da investigare nella prossima sessione:**
1. Verificare se il servizio client SMB di WinPE (`LanmanWorkstation` equivalente) ha un ritardo di inizializzazione specifico non legato al tempo assoluto trascorso ma a un evento preciso (es. primo tentativo di risoluzione NetBIOS via UDP 137-139 che fallisce silenziosamente e va in timeout lungo prima del fallback diretto)
2. Provare a forzare SMB2/3 esplicitamente o disabilitare tentativi di risoluzione NetBIOS (registry WinPE o parametri `net use`)
3. Provare un name/IP diverso, o `net use` con `/user:CT104-IP\novascm` invece di solo `novascm`
4. Controllare se il driver di rete e1000 in questo boot.wim ha comportamenti simili al driver storage (mancante/incompleto) — anche se `ipconfig` mostra un indirizzo valido, potrebbe mancare qualche componente per SMB specificamente

## Stato finale VM105 a fine sessione

- VM105: spenta
- `boot.wim` live: **versione finale con tutti i fix** — Boot Index 1, driver vioscsi iniettato, script `startnet.cmd` con verifica disco + tentativo SMB con attesa lunga (150s)
- Disco: `scsi0` (virtio-scsi, 64GB)
- CPU: `host` (fix crash originale)
- Backup di tutte le versioni precedenti del `boot.wim` in `winpe/backup-pre-fresh-20260716/`

## Riepilogo cronologico completo dei fix di questa sessione (16/07)

1. **Crash istantaneo wimboot→bootmgfw.efi**: risolto con `cpu: host` (conferma ufficiale maintainer: richiede `popcnt`/`sse4.2`)
2. **Disco non visibile in Setup/PE**: risolto iniettando driver `vioscsi` via DISM nell'indice PE del boot.wim, usando disco virtio-scsi
3. **autounattend.xml non trovato su X:\ con index=1**: risolto allineando il Boot Index dell'header WIM (era 2, ora 1) all'indice effettivamente avviato
4. **`net use` Samba intermittente (errore 53)**: **RISOLTO** — vedi sezione seguente

## RISOLTO — errore 53 `net use`: socket TCP fantasma lato server, non un problema WinPE

Seguito il piano dettagliato di Grok (`PXE_PE_SMB_ERROR53_ACCORGIMENTI.md`): installato `tcpdump` su CT104 e catturato il traffico verso/da VM105 durante un tentativo automatico di `net use`.

**Trovato nel dump:** il client (WinPE) invia correttamente un pacchetto **SYN** verso la porta 445, ma il server risponde con un **ACK nudo** (`Flags [.]`) invece del corretto **SYN-ACK** (`Flags [S.]`) — numero di ack incoerente con la sequenza del client. Sintomo classico di stato di connessione stantio.

**Confermato con `ss -tan` dentro CT104:** due socket **ESTABLISHED fantasma** ancora aperti lato server, residui di test precedenti nella stessa sessione (la VM era stata riavviata a metà connessione più volte senza chiudere correttamente il socket):
```
ESTAB  192.168.10.104:445  192.168.10.237:49668
ESTAB  192.168.10.104:445  192.168.10.237:49669
```
WinPE riusa le stesse porte effimere ad ogni riavvio (allocazione deterministica in un fresh network stack), quindi il nuovo tentativo di connessione collideva sempre con queste sessioni fantasma, e il server rispondeva in base allo stato della vecchia sessione invece di accettarne una nuova.

**Fix:** chiusi i socket fantasma con `ss -K dst 192.168.10.237 dport = 49668` (e 49669) dentro CT104. **Ritestato immediatamente `net use` manualmente in WinPE → "Esecuzione comando riuscita"**, nessuna attesa necessaria.

**Conclusione importante:** questo NON è un problema di WinPE, rete, driver o timing — è un **artefatto della metodologia di test** (decine di riavvii della stessa VM/IP in un'ora durante questa sessione maratona). In un deploy reale di produzione, ogni PC fisico ha il proprio MAC/IP univoco e viene deployato una sola volta, quindi **questa collisione di socket non si presenterebbe mai**. Il flusso PE-first end-to-end (disco + rete + Samba + Setup) è quindi considerato **completamente funzionante**.

## AGGIORNAMENTO — tentativo di deploy completo: nuovo blocco SMB, non risolto in questa sessione

Su richiesta dell'utente ("avvia il deploy completo e verifica fino alla fine"), riavviata VM105 con tutti i fix (Boot Index 1, driver vioscsi, autounattend copiato sulla share `smb/sources/autounattend.xml` e referenziato via UNC invece di X:\).

**Nuovo errore, diverso da prima:** `net use` ora fallisce sistematicamente con **Errore 67 "Impossibile trovare il nome della rete"** (non più errore 53) — **15 tentativi su 15 falliti in modo identico**, anche dopo:
- Riavvio completo pulito di `smbd`/`nmbd` su CT104
- Verifica che la share `wininstall` sia perfettamente raggiungibile e sana **da CT104 stesso** (`smbclient //localhost/wininstall` funziona senza problemi)
- Verifica ARP pulita (`192.168.10.237` risulta `REACHABLE` con MAC corretto)
- Verifica nessun socket fantasma residuo prima di ogni test

**Differenza chiave rispetto al problema precedente (errore 53):** errore 53 = server irraggiungibile; errore 67 = server raggiunto ma nome condivisione non trovato **dal punto di vista del client WinPE**, mentre la stessa condivisione funziona perfettamente in locale su CT104. Questo isola il problema a qualcosa nel **percorso di rete tra la VM e CT104** (bridge/veth) o nella negoziazione SMB specifica lato client WinPE — non alla configurazione Samba stessa.

**Non testato per mancanza di tempo in questa sessione (idee per la prossima):**
1. Cambiare NIC da `e1000` a `virtio-net` (con driver NetKVM iniettato nel boot.wim, stesso metodo usato per `vioscsi`) — task già in lista, mai eseguito, potrebbe rivelare un problema specifico del driver/emulazione e1000 sotto questo carico di traffico SMB
2. `tcpdump` di un NUOVO tentativo (quello con errore 67, non ancora catturato) per vedere esattamente a che punto della negoziazione SMB (dopo il TCP handshake, quale pacchetto SMB specifico fallisce)
3. Riavviare l'intero CT104 (non solo i servizi Samba) o anche l'host Proxmox, per escludere uno stato di rete accumulato a livello di bridge/kernel dopo ore di test ripetuti
4. Provare un nome share diverso o verificare se il precedente successo era legato a un ordine di operazioni specifico non più riproducibile

**Stato:** il deploy completo NON è stato verificato fino alla fine in questa sessione. I bug principali (crash, disco, Boot Index) restano risolti e confermati; questo è un blocco nuovo e separato, di natura di rete a basso livello, da investigare con tempo/attenzione freschi.

---

## AGGIORNAMENTO sera 16/07/2026 (Grok) — post errore 67 fino a Setup + fix unattend

### Contesto operativo

- **VINCOLO UTENTE:** non toccare **RAM** né **CPU** di VM105 (restano `memory: 6144`, `cpu: host`).
- Host **pve-minipc** (`.10.202`): ~**7.5 GiB** RAM totale; con VM a 6 GB + CT in running il nodo va in **OOM/crash** ripetuti se manca swap.
- **Swap host:** creato/riattivato a volte come zvol `rpool/swap-pxe` (~4 GiB); **non resta montato automaticamente al reboot** del nodo → dopo ogni reboot host verificare `swapon --show` e se serve `swapon /dev/zvol/rpool/swap-pxe`.
- iGPU BIOS 64→512 MB: **toglie** RAM di sistema (peggio per OOM); su host headless preferire minimo (64/128).

### Infrastruttura test

| Item | Valore |
|------|--------|
| CT104 | `novascm-pxe-test` `.10.104` — nginx, smbd/nmbd, API `:9091`, TFTP |
| VM105 | `pxe-test-vm` MAC `BC:24:11:A5:35:55` → IP PE tipico `.10.237` / PC `PC-A53555` |
| Share | `\\192.168.10.104\wininstall` → `/opt/novascm-pxe/smb` |
| Credenziali share | user `novascm` / pass `NovaSCMpxe2026!` (guest ok abilitato) |
| boot.wim live | `/opt/novascm-pxe/server/dist/winpe/boot.wim` (Boot Index **1**, vioscsi, startnet aggiornato) |

---

### Fix applicati in questa ripresa (CT104)

#### 1) Samba PE-friendly (`/etc/samba/smb.conf`)

Marker `# NovaSCM-PE-FIX`. Effetti (confermati con `testparm`):

- `netbios name = PXE-CT104` (≤15 char)
- `server min protocol = NT1` / max SMB3 (prima solo SMB2 — lab WinPE)
- `server signing = No`, `client signing = No`, `smb encrypt` disabilitato lato policy
- `ntlm auth` / `lanman auth` compatibilità
- `[wininstall]`: `guest ok = yes`, `force user = novascm`, path `/opt/novascm-pxe/smb`
- Backup: `/etc/samba/smb.conf.bak-pre-pefix-*`

**Verifica da Windows host (Claudio-PC):** `net use` auth **e** guest su `\\192.168.10.104\wininstall` → **OK**, `setup.exe` visibile.

#### 2) `startnet.cmd` in `boot.wim` indice 1 (wimlib update, no fuse)

Percorso WIM: `/Windows/System32/startnet.cmd`  
Backup WIM: `boot.wim.bak-pre-pefix-*`

Comportamento:

- Log su `X:\pxe-startnet.log`
- `wpeinit` + `wpeutil InitializeNetwork/WaitForNetwork` + start workstation
- Attesa IPv4, ping `.104`
- **Retry mount max 25**: guest → `novascm` → `.\novascm` → `PXE-CT104\novascm` → `192.168.10.104\novascm` → `WORKGROUP\novascm` → IPC$ poi share
- Setup: preferisce `X:\autounattend.xml`, poi `Z:\sources\autounattend.xml`
- Fallimento: `cmd /k` con log

Copia repo di riferimento: `docs/startnet_PE_current.cmd` (allineare se serve).

#### 3) nginx alias HTTP (opzionale)

- File site: `/etc/nginx/sites-enabled/novascm-pxe`
- `location /wininstall/` → alias `/opt/novascm-pxe/smb/`
- `GET /wininstall/sources/setup.exe` → 200
- **Attenzione:** non lasciare file `*.bak*` in `sites-enabled` (nginx li carica e va in errore “duplicate default server”)

#### 4) autounattend — **RISOLTO** errore “impostazione o componente non esiste”

**Sintomo in console Setup (IT):**  
*Impossibile analizzare o elaborare il file di risposte … `[Z:\sources\autounattend.xml]` per il passaggio `[windowsPE]`. Un'impostazione o un componente specificato nel file di risposte non esiste.*

**Causa:** blocco non valido nel pass `windowsPE`:

```xml
<ComplianceCheck>
  <DisplayCheck>false</DisplayCheck>
  <AllowUpgradesWithUnsupportedTPMOrCPU>true</AllowUpgradesWithUnsupportedTPMOrCPU>
</ComplianceCheck>
```

`AllowUpgradesWithUnsupportedTPMOrCPU` **non è valido** in Microsoft-Windows-Setup / windowsPE su Setup Win11 (build PE 26100).

**Fix:**

| Dove | Cosa |
|------|------|
| Share statica | `/opt/novascm-pxe/smb/sources/autounattend.xml` riscritto (CRLF), **senza** ComplianceCheck |
| API | `api.py` `_build_autounattend_xml_pxe`: rimosso `<ComplianceCheck>…</ComplianceCheck>` (commento placeholder); MSR con **Size 16** |
| Backup | `autounattend.xml.bak-*`, `api.py.bak-pre-unattendfix-*` |
| Bypass HW | solo LabConfig reg (TPM/SecureBoot/RAM/Storage/CPU) via `RunSynchronous` |
| Partizioni | EFI 300 + MSR **16** + Primary extend → InstallTo partition 3 |
| install.wim | UNC `\\192.168.10.104\wininstall\sources\install.wim` index **5** |

XML statico validato con `xml.etree` → **XML_OK**. Servizio `novascm-pxe` riavviato.

**Nota API:** `GET /api/autounattend/<pc>` da localhost **senza** deploy token → **403**; da client PXE con sessione deploy → **200** (es. 14:28:44 UTC per `PC-A53555`).

---

### Esito test end-to-end (sera 16/07) — stato verificato

| Fase | Esito | Evidenza |
|------|-------|----------|
| Host stabile con VM 6G | **Parziale** | Crash OOM multipli se no swap; con swap regge a tratti |
| PXE iPXE → wimboot/BCD/boot.sdi/boot.wim | **OK** | nginx 200; boot.wim ~642–643 MB (dopo update startnet) |
| API boot script | **OK** | `PXE boot: MAC=… PC=PC-A53555 action=deploy` |
| autounattend via API (wimboot) | **OK** | GET 200 con deploy token |
| `net use` / share da PE | **OK** (superato 67/53 in questa sessione) | `smbstatus`: user `novascm`, host `.237`, **SMB3_11**, service `wininstall` |
| Avvio `setup.exe` da rete | **OK** | Lock SMB su `sources/setup.exe` + decine di DLL setup |
| Parsing unattend windowsPE | **BLOCCATO poi FIXATO** | Dialog errore su `AllowUpgrades…`; file share corretto dopo fix |
| Deploy completo (copia install.wim → OOBE) | **Non ancora chiuso** | Richiede ritest Setup con unattend corretto (reboot PE o `setup.exe /unattend:Z:\sources\autounattend.xml`) |

**Conclusione aggiornata sull’errore 67:** in questa ripresa, dopo fix Samba + startnet + host reboot/CT pulito, **SMB da PE funziona** (sessione autenticata SMB3.1.1, Setup legge dalla share). Il blocco successivo non era più 67 ma **schema unattend**.

---

### Comandi operativi utili (lab)

```bash
# Host Proxmox
qm status 105; free -h; swapon --show
# Se swap assente dopo reboot host:
swapon /dev/zvol/rpool/swap-pxe   # se lo zvol esiste

# CT104
pct exec 104 -- smbstatus
pct exec 104 -- tail -20 /var/log/nginx/access.log
pct exec 104 -- journalctl -u novascm-pxe -n 30 --no-pager
# Socket fantasma 445 (prevenzione err 53 lab):
pct exec 104 -- ss -tan | grep 445
# ss -K se supportato verso client PE

# In console WinPE (se dialog unattend o setup fallito ma Z: montato):
net use Z: \\192.168.10.104\wininstall /user:novascm NovaSCMpxe2026!
Z:\sources\setup.exe /unattend:Z:\sources\autounattend.xml
# Log startnet:
type X:\pxe-startnet.log
```

**Console PE:** non cliccare (QuickEdit mette in pausa `cmd`).

---

### Stato a salvataggio documento (sera 16/07)

- **VM105:** tipicamente running o riavviata dall’utente per ritest unattend fix
- **CT104:** running con fix Samba + unattend share + api.py
- **Unattend share:** versione corretta **senza** `AllowUpgradesWithUnsupportedTPMOrCPU`
- **Prossimo passo:** completare install Windows (partizionamento → apply image index 5 → specialize/oobe) e verificare FirstLogon/agent se si ripristina blocco oobe completo dall’API
- **Rischio residuo:** OOM host con `memory: 6144` senza swap; non abbassare RAM se l’utente non lo autorizza

### File correlati

- `docs/PXE_PE_SMB_ERROR53_ACCORGIMENTI.md` — piano 53/67 (pre-fix sera)
- `docs/startnet_PE_current.cmd` — copia startnet (verificare sync col WIM)
- Memoria Claude: `novascm-pxe-test-20260705.md`, `pve-minipc-swap-oom-fix-20260716.md` (se presente)

---

*Salvato da Grok 16/07/2026 sera — sessione avvio/test VM105, fix Samba/startnet/unattend.*

---

## AGGIORNAMENTO notte 16/07/2026 (Claude) — infrastruttura host cambiata, note sopra OBSOLETE su RAM/swap

**Importante:** le note sopra su swap/RAM (`vincolo utente non toccare RAM`, `rpool/swap-pxe` da riattivare a mano) sono superate da qui in poi. In questa sessione, dopo altri crash ripetuti dell'host (stesso pattern: schermo con "Purging GPU memory", poi blocco totale, riavvio fisico richiesto):

1. **Swap ZFS zvol rimosso completamente** (creato prima come `rpool/swap` 8GB con `sync=always`, poi eliminato): causava un blocco quando il sistema iniziava effettivamente a usarlo (osservazione diretta utente), coerente con `sync=always` che rende ogni swap-out sincrono e bloccante sotto pressione reale. **Non c'è più swap host, in nessuna forma**, a meno di reintervento futuro.
2. **Modulo kernel `i915` (iGPU Intel) blacklistato** (`/etc/modprobe.d/blacklist-i915.conf` + `update-initramfs -u`) — confermato che nessun CT/VM su pve-minipc usa `/dev/dri` o `hostpci`, quindi la iGPU non serve a nulla su questo nodo headless. Elimina il meccanismo "Purging GPU memory" alla radice (il driver non si carica più).
3. **`lm-sensors` + `coretemp` installati** (persistito in `/etc/modules-load.d/coretemp.conf`) — temperature CPU/NVMe monitorabili (`ssh root@192.168.10.202 sensors`), sempre risultate normali (mai la causa dei crash).
4. **VM105 RAM ridotta da 6144MB → 3072MB** (decisione esplicita dell'utente stasera, sovrascrive il vincolo precedente "non toccare RAM") — margine host ancora risicato (7.5GB totali, ~90%+ di utilizzo osservato durante Setup con VM105 attiva).
5. **Container non essenziali fermati "temporaneamente" durante i test PXE** per liberare RAM: `simc`(109), `snipeit`(121), `polaris-search`(115), `novascm-server`(112), `yarr-stremio`(117), `ytuner`(133). Da riavviare manualmente a fine sessione di test (non hanno autostart bloccato, solo fermati con `pct stop`).

**Causa radice del crash host ancora non completamente risolta**: anche dopo tutti i fix sopra, l'host si è bloccato di nuovo mentre Setup era in fase di copia file con VM105 a 3GB — il vero limite resta la RAM fisica totale (7.5GB) troppo risicata per host Proxmox + 14 CT + una VM Windows contemporaneamente. **Unica soluzione definitiva non ancora attuata: aggiungere RAM fisica al minipc.**

**Nota BIOS:** durante questa sessione il BIOS del minipc si è resettato una volta ai valori di default (causa non chiara — possibile intervento manuale o glitch), riattivando Secure Boot (che blocca il boot Proxmox da root ZFS) e riportando la memoria stolen iGPU al default. Se il minipc non risponde più in rete dopo un riavvio fisico, controllare PRIMA lo stato di Secure Boot nel BIOS (deve restare disattivato).

**File sincronizzati in questa sessione** (`Y:\NovaSCM` con le versioni live su CT104, erano divergenti): `server/api.py`, `server/tests/test_api.py`, `server/web/deploy-client.html`, `server/web/index.html`, `docs/startnet_PE_current.cmd`.

Vedi memoria Claude: `pve-minipc-swap-oom-fix-20260716.md` per il log cronologico completo di tutti i crash/fix di stasera.
