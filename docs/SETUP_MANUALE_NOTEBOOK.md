# NovaSCM — Setup Manuale su Notebook Windows
## Guida completa: Agente + Schermata Grafica Deploy

**Versione:** 1.8.1  
**Data:** 2026-03-10  
**Prerequisiti:** Windows 10/11, accesso amministratore, server NovaSCM raggiungibile su rete

---

## INDICE

1. [Prerequisiti e verifica rete](#1-prerequisiti-e-verifica-rete)
2. [Struttura cartelle](#2-struttura-cartelle)
3. [Scaricare i file eseguibili](#3-scaricare-i-file-eseguibili)
4. [Creare il file di configurazione agent.json](#4-creare-il-file-di-configurazione-agentjson)
5. [Test lancio manuale agente](#5-test-lancio-manuale-agente)
6. [Test schermata grafica DeployScreen](#6-test-schermata-grafica-deployscreen)
7. [Installare l'agente come servizio Windows](#7-installare-lagente-come-servizio-windows)
8. [Verificare il servizio](#8-verificare-il-servizio)
9. [Creare un workflow di test sul server](#9-creare-un-workflow-di-test-sul-server)
10. [Test deploy completo end-to-end](#10-test-deploy-completo-end-to-end)
11. [Troubleshooting](#11-troubleshooting)
12. [Comandi utili quick reference](#12-comandi-utili-quick-reference)

---

## 1. Prerequisiti e verifica rete

### 1.1 — Requisiti software

| Componente | Requisito |
|---|---|
| OS | Windows 10 21H2+ oppure Windows 11 |
| .NET Runtime | .NET 6.0+ (incluso nell'exe self-contained) |
| Accesso | Account amministratore locale |
| Rete | Connessione a VLAN 20 (192.168.20.0/24) o tunnel al server |

### 1.2 — Verifica raggiungibilità server

Aprire **PowerShell come amministratore** e testare:

```powershell
# Sostituisci con il tuo IP server
$server = "http://192.168.20.110:9091"

# Test 1: ping base
Test-NetConnection -ComputerName "192.168.20.110" -Port 9091

# Test 2: versione API (non richiede autenticazione)
Invoke-RestMethod -Uri "$server/api/version"
```

**Output atteso Test 2:**
```json
{
  "version": "1.8.1",
  "db": "ok"
}
```

Se il server non risponde, verificare:
- Il notebook è sulla rete corretta (VLAN 10 Trusted o VLAN 20 Servers)
- Il container Docker NovaSCM è in esecuzione: `docker ps | grep novascm`
- Il firewall del server permette connessioni sulla porta 9091

### 1.3 — Recuperare la API Key

La API Key si trova nel server in uno di questi posti:

```bash
# Sul server Linux, nel container Docker:
docker exec novascm-server cat /data/.api_key

# Oppure leggendo la variabile d'ambiente nel docker-compose:
cat /path/to/docker-compose.yml | grep NOVASCM_API_KEY
```

Annotare la chiave — servirà nei passi successivi.

---

## 2. Struttura cartelle

Creare la struttura su **C:\ProgramData\NovaSCM**:

```powershell
# Eseguire in PowerShell come amministratore
New-Item -ItemType Directory -Force -Path "C:\ProgramData\NovaSCM"
New-Item -ItemType Directory -Force -Path "C:\ProgramData\NovaSCM\logs"
```

La struttura finale sarà:

```
C:\ProgramData\NovaSCM\
├── NovaSCMAgent.exe          ← agente principale
├── NovaSCMDeployScreen.exe   ← schermata grafica
├── agent.json                ← configurazione
├── state.json                ← stato runtime (creato automaticamente)
└── logs\
    └── agent.log             ← log del servizio
```

---

## 3. Scaricare i file eseguibili

### Opzione A — Download dal server NovaSCM (consigliato)

```powershell
$server = "http://192.168.20.110:9091"
$apiKey = "LA_TUA_API_KEY_QUI"
$dir    = "C:\ProgramData\NovaSCM"

$headers = @{ "X-Api-Key" = $apiKey }

# Scarica NovaSCMAgent.exe
Write-Host "Scarico NovaSCMAgent.exe..."
Invoke-WebRequest -Uri "$server/api/download/agent" `
    -Headers $headers `
    -OutFile "$dir\NovaSCMAgent.exe" `
    -UseBasicParsing
Write-Host "OK: $((Get-Item "$dir\NovaSCMAgent.exe").Length / 1MB -as [int]) MB"

# Scarica NovaSCMDeployScreen.exe
Write-Host "Scarico NovaSCMDeployScreen.exe..."
Invoke-WebRequest -Uri "$server/api/download/deploy-screen" `
    -Headers $headers `
    -OutFile "$dir\NovaSCMDeployScreen.exe" `
    -UseBasicParsing
Write-Host "OK: $((Get-Item "$dir\NovaSCMDeployScreen.exe").Length / 1MB -as [int]) MB"
```

### Opzione B — Copia manuale da condivisione di rete

Se non hai ancora l'endpoint `/api/download/deploy-screen` (richiede il task Claude Code del report), copia manualmente i file compilati:

```powershell
# Da una condivisione di rete o USB
$src = "\\192.168.10.XX\share\NovaSCM\bin"
Copy-Item "$src\NovaSCMAgent.exe"        "C:\ProgramData\NovaSCM\" -Force
Copy-Item "$src\NovaSCMDeployScreen.exe" "C:\ProgramData\NovaSCM\" -Force
```

### Opzione C — Build locale da sorgenti

Se hai Visual Studio o .NET SDK installato:

```powershell
# Clona il repo
git clone https://github.com/ClaudioBecchis/NovaSCM.git C:\dev\NovaSCM
cd C:\dev\NovaSCM

# Build agente
cd NovaSCMAgent
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
Copy-Item "bin\Release\net6.0\win-x64\publish\NovaSCMAgent.exe" "C:\ProgramData\NovaSCM\"

# Build DeployScreen
cd ..\DeployScreen
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
Copy-Item "bin\Release\net6.0\win-x64\publish\NovaSCMDeployScreen.exe" "C:\ProgramData\NovaSCM\"
```

### Verifica file scaricati

```powershell
Get-Item "C:\ProgramData\NovaSCM\*.exe" | Select-Object Name, Length, LastWriteTime
```

**Output atteso:**
```
Name                          Length  LastWriteTime
----                          ------  -------------
NovaSCMAgent.exe           45000000  10/03/2026 ...
NovaSCMDeployScreen.exe    38000000  10/03/2026 ...
```

---

## 4. Creare il file di configurazione agent.json

Creare il file `C:\ProgramData\NovaSCM\agent.json`:

```powershell
$config = @{
    api_url  = "http://192.168.20.110:9091"
    api_key  = "LA_TUA_API_KEY_QUI"
    pc_name  = $env:COMPUTERNAME.ToUpper()
    poll_sec = 30
    domain   = "polariscore.local"
} | ConvertTo-Json

$config | Set-Content -Path "C:\ProgramData\NovaSCM\agent.json" -Encoding UTF8
```

Verificare il contenuto:

```powershell
Get-Content "C:\ProgramData\NovaSCM\agent.json"
```

**Output atteso:**
```json
{
  "api_url":  "http://192.168.20.110:9091",
  "api_key":  "abc123...",
  "pc_name":  "NOTEBOOK-CLAUDIO",
  "poll_sec": 30,
  "domain":   "polariscore.local"
}
```

> **Importante:** `pc_name` deve corrispondere esattamente al nome registrato sul server NovaSCM per questo PC. Se il PC non è ancora registrato, verrà creato automaticamente al primo checkin.

---

## 5. Test lancio manuale agente

Prima di installare come servizio, testare che l'agente funzioni correttamente lanciandolo a mano.

### 5.1 — Primo lancio in foreground

Aprire **PowerShell come amministratore**:

```powershell
cd "C:\ProgramData\NovaSCM"
.\NovaSCMAgent.exe
```

**Output atteso (log in console):**
```
[INF] NovaSCM Agent v1.8.1 avviato — OS: Microsoft Windows NT 10.0.22621.0
[INF] Polling — PC=NOTEBOOK-CLAUDIO API=http://192.168.20.110:9091
[INF] Nessun workflow assegnato per NOTEBOOK-CLAUDIO
[INF] Polling — PC=NOTEBOOK-CLAUDIO API=...
```

Se vedi errori di connessione, tornare al punto 1.2.

### 5.2 — Verificare il checkin sul server

Mentre l'agente è in esecuzione, dal server verificare che il PC sia visibile:

```bash
# Dal server o da qualsiasi PC sulla rete
curl http://192.168.20.110:9091/api/cr \
  -H "X-Api-Key: LA_TUA_API_KEY"
```

Il notebook deve apparire nella lista dei PC.

### 5.3 — Fermare il test

Premere `Ctrl+C` nella PowerShell per fermare l'agente.

---

## 6. Test schermata grafica DeployScreen

### 6.1 — Test in modalità DEMO (senza server)

La modalità demo simula un deploy completo senza bisogno di un workflow reale sul server. Perfetta per verificare la grafica.

```powershell
cd "C:\ProgramData\NovaSCM"

.\NovaSCMDeployScreen.exe `
    hostname=$env:COMPUTERNAME `
    domain=polariscore.local `
    wf="Deploy Base Win 11" `
    server=http://192.168.20.110:9091 `
    key=LA_TUA_API_KEY `
    pw_id=1 `
    demo=1
```

**Cosa vedi:**
- Schermata fullscreen nera con griglia blu
- Header con nome PC e dominio
- HW Strip (CPU, RAM, Disco, Rete) — si popola dopo 2 secondi
- Pipeline degli step sulla destra (25 step demo)
- Box step corrente con spinner animato
- Log in tempo reale in fondo
- Stima tempo rimanente (ETA)
- Al termine: overlay verde con screenshot placeholder + chip HW

> **Nota:** Il cursore del mouse è nascosto (Cursor="None") — comportamento normale per la modalità deploy kiosk. Premere `Alt+F4` per uscire dalla demo.

### 6.2 — Test in modalità REALE (con workflow sul server)

Per testare con un workflow reale, prima assegnare un workflow al PC dal server (vedi sezione 9), poi:

```powershell
.\NovaSCMDeployScreen.exe `
    hostname=$env:COMPUTERNAME `
    domain=polariscore.local `
    wf="Deploy Base Win 11" `
    server=http://192.168.20.110:9091 `
    key=LA_TUA_API_KEY `
    pw_id=ID_DEL_WORKFLOW
```

Sostituire `ID_DEL_WORKFLOW` con il `pw_id` restituito dal server alla creazione del workflow.

### 6.3 — Parametri CLI disponibili

| Parametro | Descrizione | Default |
|---|---|---|
| `hostname=` | Nome PC da mostrare | Nome macchina |
| `domain=` | Dominio da mostrare | polariscore.local |
| `wf=` | Nome workflow da mostrare | Deploy Base Win 11 |
| `server=` | URL server NovaSCM | http://192.168.20.110:9091 |
| `key=` | API Key | (vuoto) |
| `pw_id=` | ID del pc_workflow | 1 |
| `demo=1` | Modalità demo simulata | 0 |
| `ver=` | Versione da mostrare nel footer | 1.8.1 |

---

## 7. Installare l'agente come servizio Windows

L'agente deve girare come servizio Windows per avviarsi automaticamente al boot, anche prima del login utente. Useremo **NSSM** (Non-Sucking Service Manager).

### 7.1 — Scaricare e installare NSSM

```powershell
$nssmDir = "C:\ProgramData\NovaSCM"

# Scarica NSSM
$nssmZip = "$env:TEMP\nssm.zip"
Invoke-WebRequest -Uri "https://nssm.cc/release/nssm-2.24.zip" `
    -OutFile $nssmZip -UseBasicParsing

# Estrai
Expand-Archive -Path $nssmZip -DestinationPath "$env:TEMP\nssm" -Force

# Copia nssm.exe (versione 64-bit)
Copy-Item "$env:TEMP\nssm\nssm-2.24\win64\nssm.exe" "$nssmDir\nssm.exe" -Force

# Pulizia
Remove-Item $nssmZip -Force
Write-Host "NSSM installato: $nssmDir\nssm.exe"
```

### 7.2 — Creare il servizio con NSSM

```powershell
$dir     = "C:\ProgramData\NovaSCM"
$nssm    = "$dir\nssm.exe"
$svcName = "NovaSCMAgent"

# Rimuovi servizio precedente se esiste
$existing = Get-Service -Name $svcName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Rimuovo servizio esistente..."
    & $nssm stop $svcName
    Start-Sleep 2
    & $nssm remove $svcName confirm
    Start-Sleep 2
}

# Installa il servizio
& $nssm install $svcName "$dir\NovaSCMAgent.exe"

# Configura il servizio
& $nssm set $svcName AppDirectory       $dir
& $nssm set $svcName DisplayName        "NovaSCM Agent"
& $nssm set $svcName Description        "NovaSCM Workflow Agent — polling e esecuzione workflow deploy"
& $nssm set $svcName Start              SERVICE_AUTO_START
& $nssm set $svcName AppStdout          "$dir\logs\agent.log"
& $nssm set $svcName AppStderr          "$dir\logs\agent.log"
& $nssm set $svcName AppRotateFiles     1
& $nssm set $svcName AppRotateBytes     5242880   # 5 MB max per file log
& $nssm set $svcName AppRotateOnline    1

Write-Host "Servizio '$svcName' creato."
```

### 7.3 — Avviare il servizio

```powershell
$svcName = "NovaSCMAgent"

Start-Service -Name $svcName
Start-Sleep 3

# Verifica stato
Get-Service -Name $svcName | Select-Object Name, Status, StartType
```

**Output atteso:**
```
Name           Status  StartType
----           ------  ---------
NovaSCMAgent  Running  Automatic
```

---

## 8. Verificare il servizio

### 8.1 — Controllare i log in tempo reale

```powershell
# Segui il log in tempo reale (come tail -f su Linux)
Get-Content "C:\ProgramData\NovaSCM\logs\agent.log" -Wait -Tail 30
```

**Output atteso:**
```
2026-03-10 10:23:01 [INF] NovaSCM Agent v1.8.1 avviato — OS: Microsoft Windows NT 10.0...
2026-03-10 10:23:01 [INF] Polling — PC=NOTEBOOK-CLAUDIO API=http://192.168.20.110:9091
2026-03-10 10:23:02 [INF] Nessun workflow assegnato per NOTEBOOK-CLAUDIO
2026-03-10 10:23:32 [INF] Polling — PC=NOTEBOOK-CLAUDIO ...
```

### 8.2 — Verificare che si riavvii dopo reboot

```powershell
# Simula reboot (o riavvia fisicamente il notebook)
Restart-Computer -Force

# Dopo il riavvio, verificare lo stato del servizio
Get-Service -Name "NovaSCMAgent"
Get-Content "C:\ProgramData\NovaSCM\logs\agent.log" -Tail 10
```

### 8.3 — Controllare in Services.msc

Aprire **Gestione servizi**:
```powershell
services.msc
```
Cercare `NovaSCM Agent` → deve risultare **In esecuzione** con Tipo avvio **Automatico**.

---

## 9. Creare un workflow di test sul server

Per testare il deploy completo, creare un workflow di test assegnato al notebook.

### 9.1 — Verificare che il PC esista sul server

```powershell
$server = "http://192.168.20.110:9091"
$apiKey = "LA_TUA_API_KEY"
$pcName = $env:COMPUTERNAME.ToUpper()
$headers = @{ "X-Api-Key" = $apiKey }

# Cerca il PC
Invoke-RestMethod -Uri "$server/api/cr/by-name/$pcName" -Headers $headers
```

Se il PC non esiste, crearlo:

```powershell
$body = @{
    pc_name     = $pcName
    domain      = "polariscore.local"
    ou          = "OU=Workstations,OU=PolarisCore,DC=polariscore,DC=local"
    dc_ip       = "192.168.20.12"
    join_user   = "svc-join"
    join_pass   = "PASSWORD"
    admin_user  = "Administrator"
    admin_pass  = "PASSWORD"
} | ConvertTo-Json

Invoke-RestMethod -Uri "$server/api/cr" `
    -Method POST `
    -Headers $headers `
    -Body $body `
    -ContentType "application/json"
```

### 9.2 — Verificare i workflow disponibili

```powershell
Invoke-RestMethod -Uri "$server/api/workflows" -Headers $headers |
    Select-Object -ExpandProperty workflows |
    Format-Table id, nome, steps_count
```

### 9.3 — Assegnare un workflow al notebook

```powershell
# Sostituisci workflow_id con l'ID del workflow desiderato (es. 1)
$body = @{
    pc_name     = $pcName
    workflow_id = 1
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "$server/api/pc-workflows" `
    -Method POST `
    -Headers $headers `
    -Body $body `
    -ContentType "application/json"

Write-Host "Workflow assegnato. pw_id = $($result.id)"
# Annotare questo pw_id!
```

---

## 10. Test deploy completo end-to-end

Con il servizio in esecuzione e un workflow assegnato, il deploy parte automaticamente al prossimo ciclo di polling (entro `poll_sec` secondi).

### 10.1 — Forzare il polling immediato

Riavviare il servizio per forzare un poll immediato:

```powershell
Restart-Service -Name "NovaSCMAgent"
```

### 10.2 — Cosa deve succedere (se il report Claude Code è stato applicato)

1. L'agente rileva il workflow assegnato
2. Lancia automaticamente `NovaSCMDeployScreen.exe` in fullscreen
3. Raccoglie HW info via WMI e le invia al server
4. Esegue gli step del workflow uno per uno
5. Per ogni step invia output/log al server (visibili nella schermata)
6. Al termine cattura uno screenshot e lo invia al server
7. La schermata mostra l'overlay verde di completamento

### 10.3 — Monitorare il progresso dai log

```powershell
# Finestra 1: log agente
Get-Content "C:\ProgramData\NovaSCM\logs\agent.log" -Wait -Tail 50

# Finestra 2: stato workflow dal server
while ($true) {
    $pw = Invoke-RestMethod -Uri "$server/api/pc-workflows/ID_PW" -Headers $headers
    Write-Host "$(Get-Date -Format 'HH:mm:ss') Status: $($pw.status) | Step: $($pw.steps | Where {$_.status -eq 'running'} | Select -ExpandProperty nome)"
    Start-Sleep 5
}
```

---

## 11. Troubleshooting

### Il servizio non si avvia

```powershell
# Controlla errori NSSM
& "C:\ProgramData\NovaSCM\nssm.exe" status NovaSCMAgent

# Prova a lanciarlo manualmente per vedere l'errore
& "C:\ProgramData\NovaSCM\NovaSCMAgent.exe"

# Controlla log di sistema
Get-EventLog -LogName System -Source "NovaSCMAgent" -Newest 10 -ErrorAction SilentlyContinue
```

### L'agente si avvia ma non trova il workflow

```powershell
# Verifica che il pc_name in agent.json corrisponda al nome sul server
$cfg = Get-Content "C:\ProgramData\NovaSCM\agent.json" | ConvertFrom-Json
Write-Host "PC Name in config: $($cfg.pc_name)"
Write-Host "PC Name Windows:   $env:COMPUTERNAME"

# Devono coincidere (maiuscolo)
```

### La schermata grafica non si apre

```powershell
# Verifica che l'exe esista
Test-Path "C:\ProgramData\NovaSCM\NovaSCMDeployScreen.exe"

# Prova a lanciarla manualmente in demo per isolare il problema
& "C:\ProgramData\NovaSCM\NovaSCMDeployScreen.exe" demo=1

# Se non si apre, verifica .NET
dotnet --version
# oppure
[System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
```

> **Nota:** Se il report Claude Code per la Parte 3 (lancio automatico DeployScreen da Worker.cs) **non è ancora stato applicato**, la schermata non si aprirà automaticamente dall'agente. In quel caso lanciarla manualmente come descritto al punto 6.

### Errore 401 Unauthorized

```powershell
# Verifica API key
$cfg = Get-Content "C:\ProgramData\NovaSCM\agent.json" | ConvertFrom-Json
Write-Host "API Key: $($cfg.api_key)"

# Testa la chiave
Invoke-RestMethod -Uri "http://192.168.20.110:9091/api/version" `
    -Headers @{ "X-Api-Key" = $cfg.api_key }
```

### Il log della schermata non si aggiorna (BUG M-1)

Questo è il bug M-1 già identificato — il fix è nel report Claude Code. Come workaround temporaneo, la schermata mostra comunque i log, ma lo scroll automatico potrebbe non funzionare.

### I cerchi degli step restano grigi (BUG M-2)

Bug M-2 noto — il cerchio del primo step rimane grigio invece di diventare blu. Il fix è nel report Claude Code. Non compromette la funzionalità.

### Crash in demo mode (BUG C-1 — CRITICO)

Se la demo va in crash dopo qualche step con `InvalidOperationException`, è il bug C-1. Richiede il fix nel report Claude Code. **Senza il fix, la demo mode non è utilizzabile.**

---

## 12. Comandi utili quick reference

```powershell
# ── SERVIZIO ───────────────────────────────────────────────────
# Stato
Get-Service NovaSCMAgent

# Avvia / Ferma / Riavvia
Start-Service   NovaSCMAgent
Stop-Service    NovaSCMAgent
Restart-Service NovaSCMAgent

# Log live
Get-Content "C:\ProgramData\NovaSCM\logs\agent.log" -Wait -Tail 50

# Rimuovi servizio
& "C:\ProgramData\NovaSCM\nssm.exe" remove NovaSCMAgent confirm

# ── DEPLOYSCREEN ───────────────────────────────────────────────
# Demo mode
& "C:\ProgramData\NovaSCM\NovaSCMDeployScreen.exe" demo=1

# Modalità reale
& "C:\ProgramData\NovaSCM\NovaSCMDeployScreen.exe" hostname=$env:COMPUTERNAME domain=polariscore.local wf="Deploy Base" server=http://192.168.20.110:9091 key=API_KEY pw_id=1

# ── SERVER (da qualsiasi PC sulla rete) ────────────────────────
$s = "http://192.168.20.110:9091"
$h = @{ "X-Api-Key" = "API_KEY" }

# Lista PC
Invoke-RestMethod "$s/api/cr" -Headers $h

# Lista workflow
Invoke-RestMethod "$s/api/workflows" -Headers $h

# Assegna workflow a PC
Invoke-RestMethod "$s/api/pc-workflows" -Method POST -Headers $h `
    -Body (@{ pc_name = "NOTEBOOK"; workflow_id = 1 } | ConvertTo-Json) `
    -ContentType "application/json"

# Stato deploy in corso
Invoke-RestMethod "$s/api/pc-workflows/1" -Headers $h

# ── DIAGNOSTICA RETE ───────────────────────────────────────────
Test-NetConnection 192.168.20.110 -Port 9091
ipconfig /all | Select-String "IPv4|MAC|Adapter"
```

---

*Guida generata il 2026-03-10 — NovaSCM v1.8.1 — PolarisCore Homelab*
