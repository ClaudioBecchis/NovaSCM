# NovaSCM Task Sequence GUI — Technical Specification

## 1. Purpose

Display real-time deployment progress on client PCs during Windows 11 OSD via NovaSCM.
The UI must look identical to Microsoft SCCM's Task Sequence Progress dialog.

## 2. Technical Requirements

| Requirement         | Value                                      |
|---------------------|--------------------------------------------|
| Language            | C++ (Win32 API)                            |
| Target OS           | Windows 10 / 11 (x64)                     |
| Dependencies        | None (Windows SDK only)                    |
| Output              | Single .exe, ~50KB                         |
| Runtime             | None required on target                    |
| Source files         | 1 (ProgressUI.cpp)                         |

## 3. Window Specification

| Property            | Value                                      |
|---------------------|--------------------------------------------|
| Class name          | `NovaSCMProgressClass`                     |
| Title               | `Installation Progress`                    |
| Size                | 480 × 280 px (client area)                |
| Position            | Centered on screen                         |
| Style               | `WS_POPUP \| WS_CAPTION`                  |
| Extended style      | `WS_EX_TOPMOST`                            |
| Background          | `RGB(240, 240, 240)` — #F0F0F0            |
| Resizable           | No                                         |
| Minimizable         | No (no minimize box)                       |
| Close button        | Yes (standard caption close)               |

## 4. UI Elements

### 4.1 Organization Title
- **Type:** STATIC (SS_LEFT)
- **Position:** (20, 20, 440, 25)
- **Font:** Segoe UI, 20px height, FW_BOLD
- **Default text:** "IT Department Deployment"
- **Purpose:** Identifies the deploying organization

### 4.2 Action Section
- **Label "Running action:"** — STATIC, (20, 60, 440, 20), Segoe UI 16px
- **Action text** — STATIC, (35, 80, 410, 20), Segoe UI 16px, indented 15px
- **Action progress bar** — PROGRESS_CLASS, (20, 105, 420, 20), PBS_SMOOTH

### 4.3 Overall Section
- **Label "Overall progress:"** — STATIC, (20, 145, 440, 20), Segoe UI 16px
- **Overall progress bar** — PROGRESS_CLASS, (20, 165, 420, 20), PBS_SMOOTH

### 4.4 Details
- **Details text** — STATIC, (20, 205, 440, 40), Segoe UI 16px, multiline area

## 5. Data Input

### 5.1 INI File
- **Path:** `C:\Temp\DeployStatus.ini` (hardcoded as `INI_PATH`)
- **Encoding:** ANSI or UTF-8 (GetPrivateProfileString handles both)
- **Read method:** Win32 `GetPrivateProfileString` / `GetPrivateProfileInt`

### 5.2 Fields

```ini
[Status]
Action=<string>          ; current step name
ActionPercent=<0-100>    ; current step progress
TotalPercent=<0-100>     ; overall progress (100 triggers exit)
Details=<string>         ; free text detail line
```

### 5.3 Polling
- **Mechanism:** `WM_TIMER` with `SetTimer(hWnd, IDT_TIMER1, 500, NULL)`
- **Interval:** 500ms
- **Behavior:** reads INI, updates labels and progress bars, checks for completion

## 6. Lifecycle

```
1. WinMain → RegisterClass → CreateWindowEx → ShowWindow
2. WM_CREATE → create fonts, labels, progress bars → SetTimer(500ms)
3. WM_TIMER (loop) → read INI → update UI → if TotalPercent >= 100 → DestroyWindow
4. WM_DESTROY → KillTimer → PostQuitMessage(0)
5. GetMessage loop exits → process terminates
```

## 7. Future Enhancements (planned, not implemented)

| Feature                        | Priority | Notes                                    |
|--------------------------------|----------|------------------------------------------|
| Command-line INI path          | High     | `--ini "D:\path\status.ini"`             |
| Step list panel                | Medium   | Scrollable list of all steps with status |
| Error state (red bar)          | Medium   | Change bar color on `Status=Error`       |
| Logo/icon embedding            | Low      | .ico resource for taskbar                |
| Timeout auto-close             | Low      | Close after N minutes with no update     |
| Log file output                | Low      | Write UI events to a log file            |

## 8. Build Matrix

| Compiler | Command | Libs | Output |
|----------|---------|------|--------|
| MSVC (cl.exe) | `cl.exe ProgressUI.cpp /link /SUBSYSTEM:WINDOWS` | user32 gdi32 comctl32 | ProgressUI.exe |
| MinGW-w64 | `x86_64-w64-mingw32-g++ ProgressUI.cpp -mwindows` | -lcomctl32 -static | ProgressUI.exe |
