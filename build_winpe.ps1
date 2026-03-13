# Build WinPE per NovaSCM — richiede privilegi admin
# Eseguire: powershell -ExecutionPolicy Bypass -File build_winpe.ps1

$ErrorActionPreference = "Stop"
$adkRoot = "C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit"
$winpeWim = "$adkRoot\Windows Preinstallation Environment\amd64\en-us\winpe.wim"
$pkgDir = "$adkRoot\Windows Preinstallation Environment\amd64\WinPE_OCs"
$workDir = "C:\WinPE_NovaSCM"
$mountDir = "$workDir\mount"
$destDir = "C:\Users\Black\source\PolarisManager\server\dist\winpe"
$logFile = "$workDir\build.log"

# Pulisci lavoro precedente
if (Test-Path $mountDir) {
    Write-Host "Smonto WIM precedente..."
    dism /Unmount-Wim /MountDir:"$mountDir" /Discard 2>$null
    Start-Sleep 2
}
if (Test-Path $workDir) { Remove-Item -Recurse -Force $workDir }

# Crea struttura
Write-Host "Creo struttura WinPE..."
New-Item -ItemType Directory -Force -Path $mountDir | Out-Null
Copy-Item $winpeWim "$workDir\boot.wim" -Force
Write-Host "boot.wim base copiato: $([math]::Round((Get-Item "$workDir\boot.wim").Length / 1MB, 1)) MB"

# Monta il WIM
Write-Host "Monto boot.wim..."
dism /Mount-Wim /WimFile:"$workDir\boot.wim" /Index:1 /MountDir:"$mountDir"
if ($LASTEXITCODE -ne 0) { throw "Mount WIM fallito (exit $LASTEXITCODE)" }

# Aggiungi pacchetti opzionali — ORDINE CRITICO per dipendenze:
# 1. WMI, Scripting, NetFx (base)
# 2. HTA, StorageWMI (indipendenti)
# 3. PowerShell (dipende da WMI + Scripting + NetFx)
# 4. DismCmdlets (dipende da PowerShell)
$packages = @(
    "WinPE-WMI",
    "WinPE-Scripting",
    "WinPE-NetFx",
    "WinPE-HTA",
    "WinPE-StorageWMI",
    "WinPE-PowerShell",
    "WinPE-DismCmdlets"
)

foreach ($pkg in $packages) {
    $cab = "$pkgDir\$pkg.cab"
    $cabLang = "$pkgDir\it-it\${pkg}_it-it.cab"
    if (Test-Path $cab) {
        Write-Host "Aggiungo $pkg..." -ForegroundColor Cyan
        dism /Image:"$mountDir" /Add-Package /PackagePath:"$cab" /LogPath:"$logFile"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ERRORE aggiungendo $pkg (exit $LASTEXITCODE)" -ForegroundColor Red
        } else {
            Write-Host "  OK" -ForegroundColor Green
        }
        if (Test-Path $cabLang) {
            Write-Host "  + lingua it-IT"
            dism /Image:"$mountDir" /Add-Package /PackagePath:"$cabLang" /LogPath:"$logFile" | Out-Null
        }
    } else {
        Write-Warning "MANCANTE: $cab"
    }
}

# Inietta curl.exe (non è un pacchetto WinPE, va copiato dal sistema host)
Write-Host "`n=== Iniezione curl.exe ===" -ForegroundColor Yellow
$curlSrc = "$env:SystemRoot\System32\curl.exe"
$curlDest = "$mountDir\Windows\System32\curl.exe"
if (Test-Path $curlSrc) {
    Copy-Item $curlSrc $curlDest -Force
    Write-Host "curl.exe copiato dal sistema host" -ForegroundColor Green
} else {
    Write-Host "curl.exe non trovato nel sistema host!" -ForegroundColor Red
}

# Verifica file critici
Write-Host "`n=== Verifica file critici ===" -ForegroundColor Yellow
$check = @{
    "mshta.exe"      = "Windows\System32\mshta.exe"
    "powershell.exe" = "Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    "curl.exe"       = "Windows\System32\curl.exe"
}
$allOk = $true
foreach ($kv in $check.GetEnumerator()) {
    $path = Join-Path $mountDir $kv.Value
    if (Test-Path $path) {
        $size = [math]::Round((Get-Item $path).Length / 1KB, 0)
        Write-Host "[OK] $($kv.Key) ($size KB)" -ForegroundColor Green
    } else {
        Write-Host "[MANCANTE] $($kv.Key)" -ForegroundColor Red
        $allOk = $false
    }
}

# Lista pacchetti installati per verifica
Write-Host "`n=== Pacchetti installati ===" -ForegroundColor Yellow
dism /Image:"$mountDir" /Get-Packages | Select-String "WinPE-"

# Smonta e salva
Write-Host "`nSmonto e salvo boot.wim..." -ForegroundColor Yellow
dism /Unmount-Wim /MountDir:"$mountDir" /Commit
if ($LASTEXITCODE -ne 0) { throw "Unmount WIM fallito (exit $LASTEXITCODE)" }

$finalSize = [math]::Round((Get-Item "$workDir\boot.wim").Length / 1MB, 1)
Write-Host "`nboot.wim finale: $finalSize MB" -ForegroundColor Green

# Copia in destinazione
Write-Host "Copio boot.wim in $destDir..."
if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
Copy-Item "$workDir\boot.wim" "$destDir\boot.wim" -Force
Write-Host "[DONE] boot.wim copiato" -ForegroundColor Green

# BCD e boot.sdi
$adkMedia = "$adkRoot\Windows Preinstallation Environment\amd64\Media\Boot"
if (Test-Path "$adkMedia\BCD") {
    Copy-Item "$adkMedia\BCD" "$destDir\BCD" -Force
    Write-Host "[DONE] BCD copiato" -ForegroundColor Green
}
if (Test-Path "$adkMedia\boot.sdi") {
    Copy-Item "$adkMedia\boot.sdi" "$destDir\boot.sdi" -Force
    Write-Host "[DONE] boot.sdi copiato" -ForegroundColor Green
}

Write-Host "`n=== Build WinPE completato ===" -ForegroundColor Green
Write-Host "File in $destDir :"
Get-ChildItem $destDir -File | Format-Table Name, @{L="Size (MB)"; E={[math]::Round($_.Length/1MB,1)}} -AutoSize

Write-Host "`nPremi un tasto per chiudere..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
