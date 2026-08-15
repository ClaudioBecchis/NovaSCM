<#
.SYNOPSIS
    Helper function per aggiornare il file INI letto da ProgressUI.exe

.DESCRIPTION
    Scrive C:\Temp\DeployStatus.ini nel formato atteso dalla GUI.
    Chiamare questa funzione da ogni step del task sequence NovaSCM.

.EXAMPLE
    # All'inizio del deploy
    Update-DeployStatus -Action "Partizionamento disco" -ActionPercent 0 -TotalPercent 5 `
        -Details "Creazione tabella GPT su disco 0..."

    # A meta' di uno step
    Update-DeployStatus -Action "Applicazione immagine OS" -ActionPercent 60 -TotalPercent 25 `
        -Details "install.wim -> C:\ (60% completato)"

    # Al completamento (chiude la finestra)
    Update-DeployStatus -Action "Completato" -ActionPercent 100 -TotalPercent 100 `
        -Details "Deploy terminato con successo."
#>
function Update-DeployStatus {
    param(
        [Parameter(Mandatory)]
        [string]$Action,

        [Parameter(Mandatory)]
        [ValidateRange(0, 100)]
        [int]$ActionPercent,

        [Parameter(Mandatory)]
        [ValidateRange(0, 100)]
        [int]$TotalPercent,

        [string]$Details = "",

        [string]$IniPath = "C:\Temp\DeployStatus.ini"
    )

    # Crea la directory se non esiste
    $dir = Split-Path -Path $IniPath -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    # Scrivi il file INI
    @"
[Status]
Action=$Action
ActionPercent=$ActionPercent
TotalPercent=$TotalPercent
Details=$Details
"@ | Set-Content -Path $IniPath -Encoding UTF8 -Force
}


# ============================================================
#  ESEMPIO: Simulazione di un task sequence completo
# ============================================================

<#
    Decommentare il blocco sotto per testare la GUI.
    Lanciare prima ProgressUI.exe, poi eseguire questo script.

$steps = @(
    @{ Action="Inizializzazione";           Details="Caricamento configurazione NovaSCM..." },
    @{ Action="Partizionamento disco";      Details="GPT, partizioni EFI + NTFS" },
    @{ Action="Applicazione immagine OS";   Details="Windows 11 Enterprise 24H2 x64" },
    @{ Action="Iniezione driver";           Details="Driver pack OEM Dell Latitude" },
    @{ Action="Configurazione Windows";     Details="Applicazione unattend.xml" },
    @{ Action="Installazione aggiornamenti"; Details="Cumulative Update + Security patches" },
    @{ Action="Installazione applicazioni"; Details="Microsoft 365, Teams, LOB apps" },
    @{ Action="Registrazione agente";       Details="NovaSCM agent enrollment" },
    @{ Action="Pulizia finale";             Details="Sysprep, log collection, cleanup" }
)

for ($i = 0; $i -lt $steps.Count; $i++) {
    $totalPct = [math]::Floor(($i / $steps.Count) * 100)

    # Inizio step
    Update-DeployStatus -Action $steps[$i].Action -ActionPercent 0 `
        -TotalPercent $totalPct -Details $steps[$i].Details
    Start-Sleep -Seconds 1

    # Meta' step
    Update-DeployStatus -Action $steps[$i].Action -ActionPercent 50 `
        -TotalPercent ($totalPct + [math]::Floor(50 / $steps.Count)) -Details $steps[$i].Details
    Start-Sleep -Seconds 1

    # Fine step
    Update-DeployStatus -Action $steps[$i].Action -ActionPercent 100 `
        -TotalPercent ($totalPct + [math]::Floor(100 / $steps.Count)) -Details $steps[$i].Details
    Start-Sleep -Seconds 1
}

# Completamento
Update-DeployStatus -Action "Completato" -ActionPercent 100 -TotalPercent 100 `
    -Details "Deploy terminato con successo. La finestra si chiudera'."
#>
