# Installazione e primo avvio

## Requisiti di sistema

| Componente | Requisito |
|---|---|
| Sistema operativo | Windows 10 / Windows 11 (64-bit) |
| Runtime | [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) |
| RAM | 256 MB minimi, 512 MB consigliati |
| Disco | 50 MB per l'applicazione + database locale |
| Rete | Accesso alla subnet da gestire |

---

## Download e installazione

### Metodo 1 — Eseguibile (consigliato)

1. Vai alla pagina [**Releases**](https://github.com/claudiobecchis/NovaSCM/releases)
2. Scarica `NovaSCM-Setup.exe` o `NovaSCM-portable.zip`
3. Esegui il setup oppure estrai la cartella
4. Lancia `NovaSCM.exe`

### Metodo 2 — Compila da sorgente

```powershell
# Clona il repository
git clone https://github.com/claudiobecchis/NovaSCM
cd NovaSCM

# Installa dipendenze e compila
dotnet restore
dotnet build -c Release

# Avvia
dotnet run
```

**Dipendenze NuGet:**
- `ModernWpfUI` — interfaccia moderna
- `Microsoft.Data.Sqlite` — database locale

---

## Primo avvio

Al primo avvio, NovaSCM aprirà automaticamente il tab **⚙️ Impostazioni**.

### Configurazione minima

Puoi usare NovaSCM **senza alcun server** — le funzioni di rete locale funzionano subito.

Per le funzioni avanzate (workflow, change requests, certificati):

```
Certportal URL:     http://192.168.x.x:9090
UniFi Controller:   https://192.168.x.x
NovaSCM API URL:    http://192.168.x.x:9091
```

### Database locale

NovaSCM salva tutti i dati in:
```
%APPDATA%\PolarisManager\novascm.db
```
Il database viene creato automaticamente al primo avvio.

---

## Struttura dei tab

| Tab | Funzione |
|---|---|
| 📡 Rete | Scansione IP, mappa rete, heatmap, ping live |
| 🔐 Certificati | Gestione cert WiFi EAP-TLS |
| 📦 App | Coda installazione software |
| ⚙️ OPSI | Pacchetti OPSI |
| 🖥️ PC | Fleet management, inventario, gauge risorse |
| 💿 Deploy | Generazione autounattend.xml + postinstall.ps1 |
| ⚙️ Workflow | Automazione sequenze |
| 📋 Richieste | Change Requests provisioning PC |
| 🏢 SCCM | Task Sequence viewer |
| 📖 Wiki | Documentazione integrata |
| ℹ️ About | Info, supporto, Easter egg 😏 |
| ⚙️ Impostazioni | Configurazione server e rete |

---

## Aggiornamenti

NovaSCM controlla gli aggiornamenti automaticamente all'avvio (in background, silenzioso).
Puoi controllare manualmente dal tab **ℹ️ About → 🔄 Controlla aggiornamenti**.
