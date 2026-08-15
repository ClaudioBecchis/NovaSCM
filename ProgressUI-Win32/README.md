# NovaSCM Task Sequence GUI

Native Win32 deployment progress window for NovaSCM, inspired by Microsoft SCCM's Task Sequence Progress dialog.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Language](https://img.shields.io/badge/language-C++-00599C)
![Dependencies](https://img.shields.io/badge/dependencies-none-green)
![Size](https://img.shields.io/badge/exe_size-~50KB-lightgrey)

## Overview

`ProgressUI.exe` displays a progress window on client PCs during Windows 11 deployment. NovaSCM's deployment scripts write status updates to a simple INI file, and the GUI reads them in real time.

```
NovaSCM scripts ──writes──► C:\Temp\DeployStatus.ini ──reads──► ProgressUI.exe (this)
```

### Features

- **SCCM-identical look:** Segoe UI, standard Windows progress bars, system colors
- **Zero dependencies:** pure Win32 API, no runtime required on target
- **Tiny footprint:** ~50KB executable
- **Real-time updates:** polls INI file every 500ms
- **Always-on-top:** `WS_EX_TOPMOST` ensures visibility during OSD
- **Auto-close:** exits automatically when deployment reaches 100%

## Screenshot

```
┌──────────────────────────────────────────────────┐
│ Installation Progress                        — × │
├──────────────────────────────────────────────────┤
│                                                  │
│  IT Department Deployment                        │
│                                                  │
│  Running action:                                 │
│    Estrazione immagine OS                        │
│  ████████████████████░░░░░░░░░░░░░░░░░░░░  45%  │
│                                                  │
│  Overall progress:                               │
│  ██████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  15%  │
│                                                  │
│  Estrazione del file install.wim sul disco C:... │
│                                                  │
└──────────────────────────────────────────────────┘
```

## Build

### Prerequisites

One of:
- **Visual Studio** (any edition) with C++ Desktop workload — use Developer Command Prompt
- **MinGW-w64** (x86_64-w64-mingw32-g++)

### Compile

```cmd
:: MSVC
build\Compila_MSVC.bat

:: MinGW
build\Compila_MinGW.bat
```

Or manually:

```cmd
:: MSVC
cd src
cl.exe ProgressUI.cpp /Fe:..\build\ProgressUI.exe /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib comctl32.lib

:: MinGW
cd src
x86_64-w64-mingw32-g++ ProgressUI.cpp -o ..\build\ProgressUI.exe -mwindows -lcomctl32 -static
```

## Usage

### 1. Deploy ProgressUI.exe to the client

Copy `ProgressUI.exe` to the target machine (e.g., `C:\NovaSCM\ProgressUI.exe`).

### 2. Launch it before the deployment starts

```powershell
Start-Process -FilePath "C:\NovaSCM\ProgressUI.exe" -WindowStyle Normal
```

### 3. Update the INI file from your deployment scripts

```powershell
function Update-DeployStatus {
    param(
        [string]$Action,
        [int]$ActionPercent,
        [int]$TotalPercent,
        [string]$Details
    )
    $iniPath = "C:\Temp\DeployStatus.ini"

    # Ensure directory exists
    if (-not (Test-Path (Split-Path $iniPath))) {
        New-Item -ItemType Directory -Path (Split-Path $iniPath) -Force | Out-Null
    }

    @"
[Status]
Action=$Action
ActionPercent=$ActionPercent
TotalPercent=$TotalPercent
Details=$Details
"@ | Set-Content -Path $iniPath -Encoding UTF8 -Force
}

# Examples
Update-DeployStatus -Action "Partizionamento disco" -ActionPercent 0 -TotalPercent 5 -Details "Creazione tabella GPT..."
# ...deployment steps...
Update-DeployStatus -Action "Completato" -ActionPercent 100 -TotalPercent 100 -Details "Deploy terminato con successo."
```

### 4. The window closes automatically at TotalPercent=100

## INI File Reference

Path: `C:\Temp\DeployStatus.ini`

```ini
[Status]
Action=Nome dello step corrente
ActionPercent=45
TotalPercent=15
Details=Testo descrittivo libero mostrato in basso
```

| Key             | Type   | Range  | Description                              |
|-----------------|--------|--------|------------------------------------------|
| `Action`        | string | —      | Current step name                        |
| `ActionPercent` | int    | 0–100  | Current step progress                    |
| `TotalPercent`  | int    | 0–100  | Overall deployment progress (100 = exit) |
| `Details`       | string | —      | Detail text at bottom of window          |

## Customization

Edit `src/ProgressUI.cpp`:

| What                | Where (line ~)     | Default                       |
|---------------------|--------------------|-------------------------------|
| Organization name   | `CreateWindow` L31 | `"IT Department Deployment"`  |
| INI file path       | `INI_PATH` L18     | `C:\Temp\DeployStatus.ini`    |
| Window title        | `CreateWindowEx` L103 | `"Installation Progress"`  |
| Window size         | `winW` / `winH` L99-100 | 480 × 280               |
| Poll interval       | `SetTimer` L50     | 500ms                         |

## Integration with NovaSCM

This GUI is part of the [NovaSCM](https://github.com/ClaudioBecchis/NovaSCM) deployment management system. During a Windows 11 OSD task sequence:

1. NovaSCM agent copies `ProgressUI.exe` to the client
2. Launches it as the first step
3. Each deployment step updates the INI file
4. GUI provides visual feedback to the technician
5. Window auto-closes on completion

## Project Structure

```
NovaSCM-TaskSequenceUI/
├── CLAUDE.md                      # Claude Code project instructions
├── README.md                      # This file
├── src/
│   └── ProgressUI.cpp             # Main (and only) source file
├── build/
│   ├── Compila_MSVC.bat           # Build script for Visual Studio
│   └── Compila_MinGW.bat          # Build script for MinGW
├── examples/
│   ├── DeployStatus.ini           # Example INI for testing
│   └── Update-DeployStatus.ps1   # PowerShell helper function
└── docs/
    └── SPEC.md                    # Detailed technical specification
```

## License

Part of NovaSCM — © Claudio Becchis / PolarisCore
