# Deploy Windows zero-touch

## Cos'è il deploy zero-touch

NovaSCM genera due file che permettono di installare Windows **completamente in automatico**, senza premere un solo tasto:

| File | Scopo |
|---|---|
| `autounattend.xml` | Risponde a tutte le domande del setup Windows (partizioni, edizione, lingua, account) |
| `postinstall.ps1` | Eseguito al primo avvio — installa software, agente NovaSCM, certificato WiFi |

Basta mettere questi file nella radice di una chiavetta USB con l'ISO Windows estratta. Il PC si installa da solo.

---

## Configurazione nel tab 💿 Deploy

### 🪟 Windows
| Campo | Descrizione | Esempio |
|---|---|---|
| Edizione | Versione Windows | Pro / Home / Enterprise / Win10 |
| Nome PC template | Pattern per il nome automatico | `PC-{MAC6}` → `PC-B3CEEB` |
| Lingua | Locale di installazione | `it-IT`, `en-US` |

**Template nome PC:**
- `{MAC6}` — ultimi 6 caratteri del MAC address fisico
- `{nn}` — contatore incrementale (01, 02, 03...)
- Nome fisso — stesso nome per tutti i PC

### 👤 Account
| Campo | Descrizione |
|---|---|
| Password amministratore | Obbligatoria — usata dall'account Administrator locale |
| Utente standard | Opzionale — crea un account utente aggiuntivo |
| Password utente | Password dell'utente standard |

### 📦 Software winget
Seleziona i pacchetti da installare automaticamente dopo Windows:

- ☑️ Mozilla Firefox
- ☑️ VLC Media Player
- ☑️ 7-Zip
- ☑️ Notepad++
- ☑️ Visual Studio Code
- ☑️ Google Chrome
- ☑️ OnlyOffice
- + ID winget personalizzati (campo testo libero)

### 🔒 NovaSCM Agent
Attiva per installare automaticamente l'agente di enrollment WiFi.

---

## Generare i file

1. Compila tutti i campi
2. Clicca **⚙️ Genera file**
3. Il pannello destro mostra l'anteprima dell'`autounattend.xml`
4. Si abilitano i pulsanti di distribuzione

---

## Distribuzione USB

```
1. Scarica ISO Windows 11 da microsoft.com (strumento Creazione Media)
2. Estrai l'ISO su chiavetta USB FAT32 (usa 7-Zip o Rufus)
3. Clicca "💾 Salva cartella" → scegli la radice della USB
4. Avvia il PC dalla USB
5. Vai a prendere il caffè ☕
```

> ⚠️ L'autounattend.xml **cancella tutti i dati** del disco! Usare su PC nuovo o da riformattare.

---

## Distribuzione PXE

Se hai un server PXE configurato (es. CT 110 con netboot.xyz):

1. Configura **IP server PXE** e **percorso** nelle impostazioni
2. Aggiungi la chiave SSH (`~/.ssh/id_ed25519`) per l'accesso SCP
3. Clicca **🌐 Deploy PXE** — i file vengono copiati automaticamente via SCP

---

## Struttura autounattend.xml generato

```xml
<!-- Pass windowsPE: partizioni EFI + MSR + Windows -->
<!-- Pass specialize: nome PC, timezone -->
<!-- Pass oobeSystem: skip EULA, autologon, esegui postinstall.ps1 -->
```

**Partizioni create:**
- EFI: 100 MB (FAT32)
- MSR: 16 MB
- Windows: spazio rimanente (NTFS)

---

## Struttura postinstall.ps1 generato

```powershell
# 1. Rinomina PC (se template MAC)
# 2. Installa/aggiorna winget
# 3. Installa pacchetti software
# 4. Scarica e installa agente NovaSCM
# 5. Riavvio finale (10 secondi di countdown)
```
