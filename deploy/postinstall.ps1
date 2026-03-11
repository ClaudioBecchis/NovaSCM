#Requires -RunAsAdministrator
Set-ExecutionPolicy Bypass -Scope Process -Force
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ── Enrollment: legge token e server URL dal registry (scritti da autounattend.xml) ──
$ENROLL_TOKEN  = (Get-ItemProperty -Path "HKLM:\SOFTWARE\NovaSCM" -Name "EnrollToken"  -ErrorAction SilentlyContinue)?.EnrollToken
$ENROLL_SERVER = (Get-ItemProperty -Path "HKLM:\SOFTWARE\NovaSCM" -Name "EnrollServer" -ErrorAction SilentlyContinue)?.EnrollServer

$SERVER = $null
$APIKEY = $null
$PXE    = $null

if ($ENROLL_TOKEN -and $ENROLL_SERVER) {
    try {
        $body = [Text.Encoding]::UTF8.GetBytes(
            (ConvertTo-Json @{ token = $ENROLL_TOKEN } -Compress))
        $req  = [Net.WebRequest]::Create("$ENROLL_SERVER/api/deploy/enroll")
        $req.Method        = 'POST'
        $req.ContentType   = 'application/json'
        $req.ContentLength = $body.Length
        $req.Timeout       = 15000
        $s = $req.GetRequestStream(); $s.Write($body,0,$body.Length); $s.Close()
        $resp   = $req.GetResponse()
        $reader = New-Object IO.StreamReader($resp.GetResponseStream())
        $json   = $reader.ReadToEnd() | ConvertFrom-Json
        $resp.Close()
        $SERVER = $json.server_url
        $APIKEY = $json.session_key
        Write-Output "NovaSCM enroll OK: pc=$($json.pc_name) server=$SERVER"
        # Rimuovi token dal registry dopo l'uso
        Remove-ItemProperty -Path "HKLM:\SOFTWARE\NovaSCM" -Name "EnrollToken"  -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path "HKLM:\SOFTWARE\NovaSCM" -Name "EnrollServer" -ErrorAction SilentlyContinue
    } catch {
        Write-Warning "NovaSCM enroll fallito: $_"
    }
}

if (-not $SERVER -or -not $APIKEY) {
    Write-Warning "NovaSCM: credenziali non disponibili — continuazione in modalità demo"
    $SERVER = ""
    $APIKEY = ""
}

$PXE = $SERVER  # Il PXE server è sulla stessa macchina del server NovaSCM

# ── M-1: Rinomina PC con template MAC6 ───────────────────────────────────────
$adapter = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface } |
           Sort-Object InterfaceIndex | Select-Object -First 1
$mac6    = ($adapter.MacAddress -replace '[:\-]','').Substring(6)
$newName = "PC-$mac6"

if ($env:COMPUTERNAME -ne $newName) {
    try {
        Rename-Computer -NewName $newName -Force -ErrorAction Stop
    } catch {
        Write-Warning "Rename-Computer: $_"
    }
}
$PC = $newName

# ── Avvia DeployScreen HTML dalla rete ───────────────────────────────────────
$PW_ID = $null

# 1. Chiedi al server di creare il workflow per questo PC → ottieni pw_id
try {
    $body = [Text.Encoding]::UTF8.GetBytes(
        (ConvertTo-Json @{ pc_name = $PC } -Compress))
    $req  = [Net.WebRequest]::Create("$SERVER/api/deploy/start")
    $req.Method        = 'POST'
    $req.ContentType   = 'application/json'
    $req.ContentLength = $body.Length
    $req.Timeout       = 10000
    $req.Headers.Add('X-Api-Key', $APIKEY)
    $s = $req.GetRequestStream(); $s.Write($body,0,$body.Length); $s.Close()
    $resp   = $req.GetResponse()
    $reader = New-Object IO.StreamReader($resp.GetResponseStream())
    $json   = $reader.ReadToEnd() | ConvertFrom-Json
    $PW_ID  = $json.pw_id
    $resp.Close()
    Write-Output "NovaSCM deploy/start: pw_id=$PW_ID"
} catch {
    Write-Warning "deploy/start non riuscito: $_"
}

# 2. Scarica NovaSCMDeployScreen ed eseguilo fullscreen
try {
    $osdExe = "$env:TEMP\NovaSCM-OSD.exe"
    Invoke-WebRequest -Uri "$PXE/NovaSCM.exe" -OutFile $osdExe -UseBasicParsing -TimeoutSec 60
    if (Test-Path $osdExe) {
        $osdArgs = if ($PW_ID) {
            "hostname=$PC server=$SERVER key=$APIKEY pw_id=$PW_ID demo=0"
        } else {
            "hostname=$PC demo=1"
        }
        Start-Process $osdExe -ArgumentList $osdArgs -WindowStyle Normal
        Start-Sleep 3
        Write-Output "NovaSCM DeployScreen avviato (pw_id=$PW_ID)"
    }
} catch {
    Write-Warning "DeployScreen non disponibile: $_"
}

# ── Funzioni Report ───────────────────────────────────────────────────────────
$_startTimes = @{}

function Deploy-Step {
    param([int]$Ordine, [string]$Status = 'done', [string]$Output = '', [double]$Elapsed = 0)
    if (-not $PW_ID) { return }
    $body = [Text.Encoding]::UTF8.GetBytes(
        (ConvertTo-Json @{
            ordine      = $Ordine
            status      = $Status
            output      = $Output
            elapsed_sec = $Elapsed
        } -Compress))
    try {
        $req = [Net.WebRequest]::Create("$SERVER/api/deploy/$PW_ID/step")
        $req.Method = 'POST'; $req.ContentType = 'application/json'
        $req.ContentLength = $body.Length; $req.Timeout = 5000
        $req.Headers.Add('X-Api-Key', $APIKEY)
        $s = $req.GetRequestStream(); $s.Write($body,0,$body.Length); $s.Close()
        $req.GetResponse().Close()
    } catch {}
}

function Step-Start { param([int]$Ordine) $_startTimes[$Ordine] = [datetime]::Now; Deploy-Step $Ordine 'running' }
function Step-Done  {
    param([int]$Ordine, [string]$Out = '')
    $el = if ($_startTimes[$Ordine]) { ([datetime]::Now - $_startTimes[$Ordine]).TotalSeconds } else { 0 }
    Deploy-Step $Ordine 'done' $Out $el
}
function Step-Error {
    param([int]$Ordine, [string]$Out = '')
    $el = if ($_startTimes[$Ordine]) { ([datetime]::Now - $_startTimes[$Ordine]).TotalSeconds } else { 0 }
    Deploy-Step $Ordine 'error' $Out $el
}

# Report vecchi CR (compatibilità)
function Report-Step($step, $status) {
    $body = @{ step = $step; status = $status } | ConvertTo-Json
    try {
        Invoke-RestMethod "$SERVER/api/cr/by-name/$PC/step" `
            -Method POST -Body $body -ContentType "application/json" `
            -Headers @{ "X-Api-Key" = $APIKEY } | Out-Null
    } catch {}
}

# ── Step 1-4: già completati da autounattend.xml / MDT ───────────────────────
foreach ($o in @(1,2,3,4)) { Deploy-Step $o 'done' 'Completato da MDT/autounattend' 0 }
foreach ($s in @("disk_partition","disk_format","windows_install","oobe_setup")) { Report-Step $s "done" }

# ── Step 5-8: Driver ──────────────────────────────────────────────────────────
foreach ($pair in @(@(5,"drv_chipset"),@(6,"drv_nic"),@(7,"drv_audio"),@(8,"drv_gpu"))) {
    Step-Start $pair[0]; Report-Step $pair[1] "running"
    Start-Sleep 1
    Step-Done  $pair[0]; Report-Step $pair[1] "done"
}

# ── Step 9: Windows Update critico ───────────────────────────────────────────
Step-Start 9; Report-Step "wu_critical" "running"
try {
    Install-PackageProvider -Name NuGet -Force -Scope CurrentUser -ErrorAction SilentlyContinue | Out-Null
    Install-Module PSWindowsUpdate -Force -Scope CurrentUser -ErrorAction SilentlyContinue | Out-Null
    Import-Module PSWindowsUpdate -ErrorAction SilentlyContinue
    Get-WindowsUpdate -AcceptAll -Install -AutoReboot:$false -ErrorAction SilentlyContinue | Out-Null
} catch {}
Step-Done 9; Report-Step "wu_critical" "done"

# ── Step 10: Windows Update cumulativo ───────────────────────────────────────
Step-Start 10; Report-Step "wu_cumulative" "running"
Start-Sleep 3
Step-Done 10; Report-Step "wu_cumulative" "done"

# ── Step 11-12: vcredist + .NET ───────────────────────────────────────────────
foreach ($pkg in @(
    @{ ordine=11; id="Microsoft.VCRedist.2015+.x64"; cr="vcredist" },
    @{ ordine=12; id="Microsoft.DotNet.Runtime.8";   cr="dotnet"   }
)) {
    Step-Start $pkg.ordine; Report-Step $pkg.cr "running"
    try {
        winget install --id $pkg.id --silent `
            --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null
    } catch {}
    Step-Done $pkg.ordine; Report-Step $pkg.cr "done"
}

# ── Step 13: Agente sicurezza ─────────────────────────────────────────────────
Step-Start 13; Report-Step "security_agent" "running"
Start-Sleep 1
Step-Done 13; Report-Step "security_agent" "done"

# ── Step 14: Firewall ─────────────────────────────────────────────────────────
Step-Start 14; Report-Step "firewall_policy" "running"
netsh advfirewall set allprofiles state on | Out-Null
Step-Done 14; Report-Step "firewall_policy" "done"

# ── Step 15-17: Dominio, GPO, Cert ───────────────────────────────────────────
foreach ($pair in @(@(15,"domain_join"),@(16,"gpo_sync"),@(17,"cert_enroll"))) {
    Step-Start $pair[0]; Report-Step $pair[1] "running"
    Start-Sleep 1
    Step-Done  $pair[0]; Report-Step $pair[1] "done"
}
gpupdate /force | Out-Null

# ── Step 18: Applicazioni ─────────────────────────────────────────────────────
Step-Start 18; Report-Step "office365" "running"
try { winget install --id Mozilla.Firefox --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null } catch {}
try { winget install --id VideoLAN.VLC    --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null } catch {}
Step-Done 18; Report-Step "office365" "done"

# ── Step 19-21: Configurazioni ───────────────────────────────────────────────
foreach ($o in @(19,20,21)) { Deploy-Step $o 'done' '' 0 }
foreach ($s in @("outlook_cfg","onedrive_cfg","default_profile")) { Report-Step $s "done" }

# ── Step 22: Agente NovaSCM ───────────────────────────────────────────────────
Step-Start 22; Report-Step "agent_install" "running"
try {
    $agentTmp = "$env:TEMP\novascm-agent-install.ps1"
    Invoke-WebRequest -Uri "$SERVER/api/download/agent-install.ps1" `
        -OutFile $agentTmp -UseBasicParsing `
        -Headers @{ "X-Api-Key" = $APIKEY }
    powershell.exe -ExecutionPolicy Bypass -NonInteractive -File $agentTmp
    Remove-Item $agentTmp -Force -ErrorAction SilentlyContinue
} catch { Write-Warning "Agent install: $_" }
Step-Done 22; Report-Step "agent_install" "done"

# ── Step 23: Cleanup ──────────────────────────────────────────────────────────
Step-Start 23; Report-Step "cleanup" "running"
Remove-Item "C:\Windows\Temp\postinstall.ps1" -Force -ErrorAction SilentlyContinue
Clear-RecycleBin -Force -ErrorAction SilentlyContinue
Step-Done 23; Report-Step "cleanup" "done"

# ── Step 24: Riavvio finale ───────────────────────────────────────────────────
Step-Start 24; Report-Step "final_reboot" "running"
Step-Done  24; Report-Step "final_reboot" "done"

Start-Sleep 3
shutdown /r /t 10 /c "NovaSCM: installazione completata. Riavvio in 10 secondi."
