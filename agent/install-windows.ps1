# NovaSCM Agent Installer — Windows
# Scarica NovaSCMAgent.exe e lo installa come Windows Service
# Uso: install-windows.ps1 -ApiUrl "http://192.168.20.110:9091" -PcName "NOME-PC"

param(
    [string]$ApiUrl  = "http://YOUR-SERVER-IP:9091",
    [string]$PcName  = $env:COMPUTERNAME,
    [int]$PollSec    = 60
)

$ErrorActionPreference = "Stop"
$AgentDir   = "C:\ProgramData\NovaSCM"
$AgentExe   = "$AgentDir\NovaSCMAgent.exe"
$ConfigFile = "$AgentDir\agent.json"
$SvcName    = "NovaSCMAgent"

function Log($msg) { Write-Host "[NovaSCM] $msg" }

# ── 1. Crea directory ─────────────────────────────────────────────────────────
Log "Creo directory $AgentDir"
New-Item -ItemType Directory -Force -Path $AgentDir | Out-Null
New-Item -ItemType Directory -Force -Path "$AgentDir\logs" | Out-Null

# ── 2. Scarica NovaSCMAgent.exe ───────────────────────────────────────────────
Log "Scarico NovaSCMAgent.exe da $ApiUrl..."
try {
    Invoke-WebRequest -Uri "$ApiUrl/api/download/agent" -OutFile $AgentExe -UseBasicParsing
    Log "Agent scaricato: $AgentExe ($([math]::Round((Get-Item $AgentExe).Length/1MB,1)) MB)"
} catch {
    Log "ERRORE download agent: $_"
    throw
}

# ── 3. Verifica SHA256 ────────────────────────────────────────────────────────
Log "Verifico integrità SHA256..."
try {
    $Expected = (Invoke-WebRequest -Uri "$ApiUrl/api/download/agent.sha256" `
                 -UseBasicParsing).Content.Trim()
    $Actual   = (Get-FileHash $AgentExe -Algorithm SHA256).Hash
    if (-not ($Actual -ieq $Expected)) {
        Log "ERRORE: hash mismatch. Atteso: $Expected  Ottenuto: $Actual"
        Remove-Item -Force $AgentExe -ErrorAction SilentlyContinue
        throw "Verifica integrità fallita"
    }
    Log "SHA256 verificato: OK"
} catch {
    Log "ATTENZIONE: verifica SHA256 fallita: $_"
    throw
}

# ── 4. Crea config ────────────────────────────────────────────────────────────
Log "Scrivo config: $ConfigFile"
@{
    api_url  = $ApiUrl
    pc_name  = $PcName.ToUpper()
    poll_sec = [int]$PollSec
} | ConvertTo-Json | Set-Content -Path $ConfigFile -Encoding UTF8

# ── 5. Scarica NSSM (Non-Sucking Service Manager) ────────────────────────────
$NssmExe = "$AgentDir\nssm.exe"
if (-not (Test-Path $NssmExe)) {
    Log "Scarico NSSM..."
    try {
        $nssmZip = "$env:TEMP\nssm.zip"
        Invoke-WebRequest -Uri "https://nssm.cc/release/nssm-2.24.zip" -OutFile $nssmZip -UseBasicParsing
        Expand-Archive -Path $nssmZip -DestinationPath "$env:TEMP\nssm" -Force
        $nssmBin = Get-Item "$env:TEMP\nssm\nssm-2.24\win64\nssm.exe"
        Copy-Item $nssmBin.FullName $NssmExe -Force
        Remove-Item $nssmZip -Force
        Log "NSSM installato: $NssmExe"
    } catch {
        Log "NSSM non scaricabile — uso sc.exe come fallback"
        $NssmExe = $null
    }
}

# ── 6. Installa servizio Windows ──────────────────────────────────────────────
Log "Installo servizio Windows '$SvcName'..."

# Rimuovi servizio esistente se presente
$existing = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
if ($existing) {
    Log "Rimuovo servizio esistente..."
    if ($NssmExe) { & $NssmExe remove $SvcName confirm }
    else { sc.exe delete $SvcName }
    Start-Sleep 2
}

if ($NssmExe) {
    & $NssmExe install $SvcName $AgentExe
    & $NssmExe set $SvcName AppDirectory $AgentDir
    & $NssmExe set $SvcName DisplayName "NovaSCM Agent"
    & $NssmExe set $SvcName Description "NovaSCM Workflow Agent — polling e esecuzione workflow"
    & $NssmExe set $SvcName Start SERVICE_AUTO_START
    & $NssmExe set $SvcName AppStdout "$AgentDir\logs\agent.log"
    & $NssmExe set $SvcName AppStderr "$AgentDir\logs\agent.log"
    & $NssmExe set $SvcName AppRotateFiles 1
    & $NssmExe set $SvcName AppRotateBytes 5242880
} else {
    # Fallback: sc.exe — l'exe .NET Worker supporta natively Windows Service
    sc.exe create $SvcName binPath= "`"$AgentExe`"" start= auto DisplayName= "NovaSCM Agent"
    sc.exe description $SvcName "NovaSCM Workflow Agent — polling e esecuzione workflow"
}

# ── 7. Avvia servizio ─────────────────────────────────────────────────────────
Log "Avvio servizio $SvcName..."
Start-Service -Name $SvcName
$svc = Get-Service -Name $SvcName
Log "Stato servizio: $($svc.Status)"

if ($svc.Status -eq "Running") {
    Log "NovaSCM Agent installato e avviato correttamente!"
    Log "Log: $AgentDir\logs\"
    Log "Config: $ConfigFile"
} else {
    Log "ATTENZIONE: servizio non in stato Running. Controlla i log."
}
