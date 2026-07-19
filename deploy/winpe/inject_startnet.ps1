<#
.SYNOPSIS
    Inietta startnet_dism.cmd (e la configurazione novascm-pe.ini) in un boot.wim
    WinPE, in modo ripetibile — sostituisce la procedura manuale con
    wimlib-imagex documentata in README.md.

.DESCRIPTION
    Fa esattamente due cose, in un comando solo, con verifica automatica:
    1. Monta l'indice WinPE del WIM in scrittura
    2. Copia startnet.cmd (rinominandolo da startnet_dism.cmd) e il file .ini
       di configurazione, poi smonta con commit

    Prima di ogni injection crea un backup del WIM esistente
    (boot.wim.bak-YYYYMMDD-HHMMSS) — mai sovrascrive senza rete di sicurezza.

.PARAMETER WimPath
    Percorso del boot.wim da modificare.

.PARAMETER ConfigIni
    Percorso del file novascm-pe.ini con i valori reali (SERVER_IP, SMB_SHARE,
    ecc.) — NON il file .example nel repo, una copia con i dati veri
    dell'ambiente target.

.PARAMETER Index
    Indice dell'immagine WinPE nel WIM (default 1).

.PARAMETER DryRun
    Se presente, verifica solo che WimPath/ConfigIni esistano e che i parametri
    obbligatori dell'ini siano popolati, senza montare/modificare nulla.

.EXAMPLE
    .\inject_startnet.ps1 -WimPath D:\dist\winpe\boot.wim -ConfigIni .\novascm-pe.local.ini

.EXAMPLE
    .\inject_startnet.ps1 -WimPath D:\dist\winpe\boot.wim -ConfigIni .\novascm-pe.local.ini -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WimPath,

    [Parameter(Mandatory = $true)]
    [string]$ConfigIni,

    [int]$Index = 1,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startnetSrc = Join-Path $scriptDir 'startnet_dism.cmd'

function Assert-RequiredIniKeys {
    param([string]$IniPath)

    $required = @('SERVER_IP', 'SMB_SHARE', 'SMB_USER', 'SMB_PASS', 'IMAGE_INDEX')
    $content = Get-Content $IniPath -Raw
    $missing = @()
    foreach ($key in $required) {
        if ($content -notmatch "(?m)^$key=.+$") {
            $missing += $key
        }
    }
    if ($missing.Count -gt 0) {
        throw "Parametri obbligatori mancanti o vuoti in ${IniPath}: $($missing -join ', ')"
    }
}

# ── Validazioni pre-volo (girano sempre, anche in DryRun) ──
if (-not (Test-Path $WimPath)) {
    throw "WIM non trovato: $WimPath"
}
if (-not (Test-Path $ConfigIni)) {
    throw "File di configurazione non trovato: $ConfigIni — copiare novascm-pe.ini.example e compilarlo con i valori reali"
}
if (-not (Test-Path $startnetSrc)) {
    throw "startnet_dism.cmd non trovato accanto a questo script ($scriptDir) — repo incompleto?"
}
if ($ConfigIni -like '*novascm-pe.ini.example') {
    throw "Non usare il file .example direttamente — copialo, rinominalo e inserisci i valori reali del tuo ambiente"
}
Assert-RequiredIniKeys -IniPath $ConfigIni

Write-Host "✓ WIM trovato: $WimPath" -ForegroundColor Green
Write-Host "✓ Configurazione valida: $ConfigIni" -ForegroundColor Green
Write-Host "✓ Script sorgente: $startnetSrc" -ForegroundColor Green

if ($DryRun) {
    Write-Host "`nDry-run OK — nessuna modifica effettuata. Rimuovere -DryRun per eseguire davvero." -ForegroundColor Cyan
    exit 0
}

# L'elevazione serve solo da qui in poi (dism /Mount-Wim la richiede) — il
# DryRun sopra deve restare eseguibile da chiunque, senza prompt admin.
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Serve PowerShell come amministratore per montare/modificare il WIM (dism /Mount-Wim). Il controllo -DryRun invece non richiede elevazione."
}

# ── Backup ──
$backupPath = "$WimPath.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Copy-Item $WimPath $backupPath
Write-Host "✓ Backup creato: $backupPath" -ForegroundColor Green

# ── Mount, copia, unmount+commit ──
$mountDir = Join-Path $env:TEMP "novascm-pemount-$(Get-Random)"
New-Item -ItemType Directory -Path $mountDir -Force | Out-Null

try {
    Write-Host "`nMonto l'immagine $Index di $WimPath in scrittura..." -ForegroundColor Yellow
    & dism /Mount-Wim /WimFile:$WimPath /Index:$Index /MountDir:$mountDir
    if ($LASTEXITCODE -ne 0) { throw "DISM Mount-Wim fallito (exit $LASTEXITCODE)" }

    $destStartnet = Join-Path $mountDir 'Windows\System32\startnet.cmd'
    $destIni = Join-Path $mountDir 'novascm-pe.ini'

    Copy-Item $startnetSrc $destStartnet -Force
    Copy-Item $ConfigIni $destIni -Force
    Write-Host "✓ Copiati startnet.cmd e novascm-pe.ini nell'immagine montata" -ForegroundColor Green

    Write-Host "`nSmonto con commit..." -ForegroundColor Yellow
    & dism /Unmount-Wim /MountDir:$mountDir /Commit
    if ($LASTEXITCODE -ne 0) { throw "DISM Unmount-Wim fallito (exit $LASTEXITCODE) — l'immagine montata NON è stata modificata, verificare manualmente e smontare con /Discard se necessario" }

    Write-Host "`n✓ Injection completata. Backup del WIM precedente: $backupPath" -ForegroundColor Green
}
catch {
    Write-Host "`n✗ ERRORE: $_" -ForegroundColor Red
    Write-Host "Se l'immagine è rimasta montata, smontarla con: dism /Unmount-Wim /MountDir:$mountDir /Discard" -ForegroundColor Yellow
    throw
}
finally {
    if (Test-Path $mountDir) {
        Remove-Item $mountDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
