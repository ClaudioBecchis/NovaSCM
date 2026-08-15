# NovaSCM Task Sequence GUI — Project Instructions

## What This Project Is

A **native Win32 C++ executable** that displays a deployment progress window on client PCs during Windows 11 OSD (Operating System Deployment) via NovaSCM. The UI replicates the look and feel of Microsoft SCCM's Task Sequence Progress dialog.

**This is NOT a web app, NOT WPF, NOT .NET.** It is pure Win32 API + Common Controls (comctl32), compiled with MSVC or MinGW into a single small .exe (~50KB) with zero runtime dependencies.

## Architecture

```
┌─────────────────────────────────────────────────┐
│  NovaSCM deployment scripts (PowerShell/Python)  │
│  write status updates to an INI file             │
└──────────────────────┬──────────────────────────┘
                       │ writes every N seconds
                       ▼
           C:\Temp\DeployStatus.ini
                       │
                       │ reads every 500ms (WM_TIMER)
                       ▼
┌─────────────────────────────────────────────────┐
│  ProgressUI.exe  (this project)                  │
│  - Win32 window, WS_POPUP | WS_CAPTION           │
│  - WS_EX_TOPMOST (stays above all windows)       │
│  - Two PBS_SMOOTH progress bars                  │
│  - Auto-closes when TotalPercent >= 100          │
└─────────────────────────────────────────────────┘
```

## INI File Format (input)

The executable reads `C:\Temp\DeployStatus.ini` using `GetPrivateProfileString` / `GetPrivateProfileInt`. The INI path is hardcoded in `src/ProgressUI.cpp` as `INI_PATH`.

```ini
[Status]
Action=Estrazione immagine OS
ActionPercent=45
TotalPercent=15
Details=Estrazione del file install.wim sul disco C: in corso...
```

| Key             | Type   | Description                                  |
|-----------------|--------|----------------------------------------------|
| Action          | string | Current step name shown under "Running action:" |
| ActionPercent   | int    | 0-100, fills the top progress bar            |
| TotalPercent    | int    | 0-100, fills the bottom progress bar. At 100 the window closes. |
| Details         | string | Free text shown at the bottom of the window  |

## Window Layout (pixel coordinates)

Window: **480 × 280**, centered, `WS_POPUP | WS_CAPTION`, `WS_EX_TOPMOST`, background `RGB(240, 240, 240)`.

| Element              | Position (x, y, w, h) | Font              | Alignment |
|----------------------|------------------------|-------------------|-----------|
| Title (org name)     | 20, 20, 440, 25        | Segoe UI 20px Bold | SS_LEFT   |
| "Running action:"    | 20, 60, 440, 20        | Segoe UI 16px      | SS_LEFT   |
| Action label         | 35, 80, 410, 20        | Segoe UI 16px      | SS_LEFT   |
| Action progress bar  | 20, 105, 420, 20       | —                  | PBS_SMOOTH |
| "Overall progress:"  | 20, 145, 440, 20       | Segoe UI 16px      | SS_LEFT   |
| Overall progress bar | 20, 165, 420, 20       | —                  | PBS_SMOOTH |
| Details label        | 20, 205, 440, 40       | Segoe UI 16px      | SS_LEFT   |

## Build

### Option A: MSVC (Visual Studio Developer Command Prompt)
```cmd
cd src
cl.exe ProgressUI.cpp /Fe:..\build\ProgressUI.exe /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib comctl32.lib
```

### Option B: MinGW (x86_64-w64-mingw32-g++)
```cmd
cd src
x86_64-w64-mingw32-g++ ProgressUI.cpp -o ..\build\ProgressUI.exe -mwindows -lcomctl32 -static
```

Output: `build/ProgressUI.exe`

## Testing

1. Place `examples/DeployStatus.ini` at `C:\Temp\DeployStatus.ini`
2. Run `build/ProgressUI.exe`
3. Edit the INI file values while the window is open — the UI updates every 500ms
4. Set `TotalPercent=100` to trigger auto-close

## Rules for Contributing

- **No .NET, no WPF, no WinUI, no frameworks.** Pure Win32 API only.
- **No external dependencies.** Only Windows SDK headers (windows.h, commctrl.h, tchar.h).
- **Single source file** (`src/ProgressUI.cpp`). Keep it under 200 lines.
- **Keep the SCCM look.** Segoe UI font, #F0F0F0 background, standard Win32 progress bars.
- All strings must use `_T()` macro for Unicode compatibility.
- The INI path must remain configurable (currently hardcoded, future: command-line arg).
- The window must remain `WS_EX_TOPMOST` — it runs during OSD where user interaction must be blocked.
- **Audit labels:** any claim about behavior must be verifiable against the source. Use `[EXECUTED]` / `[PATTERN]` / `[UNVERIFIED]` when documenting behavior.
