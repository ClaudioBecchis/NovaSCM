# NovaSCM Native Deploy Engine

Motore di deploy PXE nativo per WinPE, ispirato all'architettura di Microsoft
ConfigMgr (SCCM): il server distribuisce una **sequenza di step come dati**
(Task Sequence), il client la scarica ed esegue localmente ogni step
chiamando un tool dedicato, mostrando l'avanzamento in una GUI separata via
**COM reale** (IDispatch), non via file o pipe.

## Componenti

- `NovaTsManager.c` — motore/orchestratore (equivalente a `tsmanager.exe`).
  Scarica la sequenza di step dal server (`GET /api/pc/<pc>/workflow`),
  la esegue chiamando i tool `NovaOsd*.exe`, riporta lo stato
  (`POST /api/pc/<pc>/workflow/step`), persiste il progresso su disco
  (`C:\NovaSCM\ts-state.ini`) per il resume dopo reboot.
- `NovaTsProgressUI.c` — GUI di progresso (equivalente a `tsprogressui.exe`).
  Server COM locale (`CLSCTX_LOCAL_SERVER`) che espone un oggetto
  `IDispatch` con 3 metodi (`ShowStep`, `ShowError`, `ShowDone`). **Si
  auto-registra a runtime** le chiavi di marshaling COM
  (`HKCR\Interface\{IID_IDispatch}\ProxyStubClsid32` →
  `HKCR\CLSID\{PSOAInterface}\InprocServer32` → `oleaut32.dll`) perché
  WinPE non le ha precaricate come una Windows normale — questo è lo stesso
  pattern usato dal vero `tsprogressui.exe` di ConfigMgr (confermato dalle
  stringhe `RegCreateKeyExW`/`RegSetValueExW` nel binario originale).
- `NovaOsd*.c` — tool mono-funzione (network, partition, download, apply,
  bcdboot, components, register), ciascuno un processo separato lanciato dal
  manager con `<server_url> <pc_name> <api_key> <parametri_json>`.
- `novaosd_core.c/h` — libreria condivisa (`NovaOsdCore.dll`), compilata
  come DLL e linkata da tutti i tool: parsing JSON minimale, HTTP
  (download/POST) via WinHTTP, wrapper comandi.
- `novaprogressui_iface.h` / `novaprogressui_guid.c` — GUID/DISPID condivisi
  tra manager e GUI.

## Compilazione (cross da Linux, mingw-w64)

```sh
x86_64-w64-mingw32-gcc -O2 -municode -shared -o NovaOsdCore.dll novaosd_core.c \
    -lwinhttp -Wl,--out-implib,libnovaosdcore.a

x86_64-w64-mingw32-gcc -O2 -municode -o NovaTsManager.exe NovaTsManager.c novaprogressui_guid.c \
    -lwinhttp -lole32 -loleaut32 -luuid -L. -lnovaosdcore

x86_64-w64-mingw32-gcc -O2 -municode -mwindows -o NovaTsProgressUI.exe NovaTsProgressUI.c novaprogressui_guid.c \
    -lcomctl32 -lole32 -loleaut32 -luuid -ladvapi32 -lgdi32 -luser32
```

I singoli `NovaOsd*.exe` si compilano allo stesso modo linkando solo
`novaosd_core`/`-lnovaosdcore`.

## Stato

Funzionante end-to-end in test su VM (PXE → WinPE → COM GUI live → apply OS
→ reboot). Vedi `CHANGELOG.md` per i dettagli della sessione in cui è nato.

## Problemi noti / da fare

- Nessun tipo library (`.tlb`) registrato: il marshaling IDispatch funziona
  perché e' generico (oleaut32/PSOAInterface), ma non e' stato testato con
  interfacce derivate custom — solo `IDispatch` puro.
- Nessuna gestione strutturata delle eccezioni nel manager (SEH `__try` non
  disponibile con mingw-w64 GCC): è stato aggiunto un
  `SetUnhandledExceptionFilter` best-effort che logga e riporta l'errore al
  server prima della terminazione, ma non è garantito coprire ogni crash.
- Il file di stato `C:\NovaSCM\ts-state.ini` non viene mai ripulito
  automaticamente tra un test e l'altro: uno stato residuo con
  `NextOrdine` superiore al numero di step nella sequenza corrente fa
  saltare l'intero ciclo (nessuno step eseguito, reboot immediato) — va
  pulito manualmente prima di ogni nuovo test su un disco già usato.
- Testato solo su VM QEMU/Proxmox con risorse limitate (3GB RAM): lo step
  di `apply` (scrittura del WIM su disco) è lento e non ancora validato su
  hardware reale.
