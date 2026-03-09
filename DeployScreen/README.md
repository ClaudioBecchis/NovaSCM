# NovaSCM Deploy Screen — WPF Client

Schermata fullscreen mostrata sul PC client durante il deploy in WinPE.

## Build

```powershell
# Richiede .NET 6 SDK installato sul PC di build (NON sul PC target/WinPE)
cd DeployScreen
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
# Output: bin\Release\net6.0-win-x64\publish\NovaSCMDeployScreen.exe  (~65 MB self-contained)
```

## Uso in WinPE (autounattend.xml)

### 1. Copia l'exe nel WinPE

Aggiungi `NovaSCMDeployScreen.exe` alla cartella `\Windows\System32\` dell'immagine WinPE
oppure a un percorso USB accessibile come `X:\Deploy\`.

### 2. Avvio da autounattend.xml (fase windowsPE)

```xml
<settings pass="windowsPE">
  <component name="Microsoft-Windows-Setup" ...>
    <RunSynchronous>
      <RunSynchronousCommand wcm:action="add">
        <Order>1</Order>
        <Path>X:\Deploy\NovaSCMDeployScreen.exe hostname=%COMPUTERNAME% domain=polariscore.local wf="Deploy Base Win 11" server=http://192.168.20.110:9091 key=APIKEY pw_id=14</Path>
        <Description>NovaSCM Deploy Screen</Description>
        <WillReboot>Never</WillReboot>
      </RunSynchronousCommand>
    </RunSynchronous>
  </component>
</settings>
```

> **Nota**: in WinPE `%COMPUTERNAME%` non è disponibile. Usa un valore fisso o recuperalo
> tramite script WMIC prima di lanciare l'exe:
> ```
> wmic computersystem get name /value > X:\pcname.txt
> ```

### 3. Avvio in modalità demo (test senza server)

```
NovaSCMDeployScreen.exe demo=1
```

## Parametri

| Parametro  | Default                   | Descrizione                          |
|------------|---------------------------|--------------------------------------|
| `hostname` | `WKS-MKTG-042`            | Nome del PC mostrato in header       |
| `domain`   | `polariscore.local`       | Dominio mostrato sotto l'hostname    |
| `wf`       | `Deploy Base Win 11`      | Nome del workflow                    |
| `server`   | `http://192.168.20.110:9091` | URL base del server NovaSCM       |
| `key`      | _(vuoto)_                 | API key (`X-Api-Key`)                |
| `pw_id`    | `1`                       | ID pc_workflow da monitorare         |
| `ver`      | `1.8.0`                   | Versione mostrata in footer          |
| `demo`     | `0`                       | `1` per demo simulata (no server)    |

## Requisiti WinPE

Per eseguire un'applicazione WPF .NET 6 self-contained in WinPE servono:

```powershell
# Aggiungere all'immagine WinPE con DISM:
Dism /Add-Package /Image:C:\WinPE\mount /PackagePath:"C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Windows Preinstallation Environment\amd64\WinPE_OCs\WinPE-WMI.cab"
Dism /Add-Package /Image:C:\WinPE\mount /PackagePath:"...\WinPE_OCs\WinPE-NetFx.cab"
Dism /Add-Package /Image:C:\WinPE\mount /PackagePath:"...\WinPE_OCs\WinPE-Scripting.cab"
Dism /Add-Package /Image:C:\WinPE\mount /PackagePath:"...\WinPE_OCs\WinPE-PowerShell.cab"
```

> **Alternativa senza WinPE modificato**: lancia la schermata nella fase **specialize**
> o **oobeSystem** invece che in windowsPE. In quel caso Windows è già installato
> e non servono pacchetti aggiuntivi.

## Struttura progetto

```
DeployScreen/
├── NovaSCMDeployScreen.csproj
├── App.xaml / App.xaml.cs
├── MainWindow.xaml          ← UI (layout 2 colonne)
├── MainWindow.xaml.cs       ← logica, polling, demo, animazioni
└── README.md
```

## Comportamento

- **Polling ogni 3s** su `GET /api/pc-workflows/{pw_id}`
- **Tutti gli step visibili** nella colonna destra con scroll automatico al passo attivo
- **Ring circolare** + barra lineare + contatori nella colonna sinistra
- **Spinner animato** sullo step attivo, ✓ verde sui completati
- **Overlay "Configurazione completata"** con countdown riavvio a 30s
- **Overlay errore** con nome dello step fallito
- **Fullscreen senza bordi**, cursore nascosto
