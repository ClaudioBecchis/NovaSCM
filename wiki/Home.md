# NovaSCM — Open Source Fleet & Network Manager

> **Gestisci la tua rete come un professionista.** NovaSCM è uno strumento gratuito e open source per la gestione di reti domestiche e aziendali, ispirato a Microsoft SCCM ma accessibile a tutti.

![NovaSCM Banner](https://polariscore.it/novascm/banner.png)

---

## ✨ Funzionalità principali

| Funzione | Descrizione |
|---|---|
| 📡 **Scansione rete** | Scopre tutti i device in automatico — IP, MAC, vendor, tipo, porte aperte |
| 🔐 **WiFi EAP-TLS** | Genera e distribuisce certificati per WiFi enterprise WPA2-Enterprise |
| 💿 **Deploy Windows** | Crea `autounattend.xml` + `postinstall.ps1` per installazione Windows zero-touch |
| ⚙️ **Workflow** | Sequenze di step automatizzati eseguiti sui PC (stile Task Sequence SCCM) |
| 🖥️ **Fleet PC** | Inventario hardware/software, RDP one-click, agent di gestione |
| 📦 **OPSI** | Integrazione con il server OPSI per distribuzione software |
| 🏢 **SCCM** | Visualizzatore Task Sequence compatibile con Microsoft Endpoint Config Manager |
| 📋 **Change Requests** | Tracciamento ciclo di vita PC dalla richiesta alla consegna |
| 📖 **Wiki integrata** | Documentazione completa direttamente nell'app |

---

## 📋 Changelog rapido

| Versione | Data | Note |
|---|---|---|
| **v1.6.2** | 2026-03-08 | 🔒 Security patch SEC-02: eliminata argument injection in UI (`ProcessStartInfo.ArgumentList`) |
| v1.6.1 | 2026-03-08 | 🔒 Security patch SEC-01: eliminata command injection in agent `run_cmd()` |
| v1.6.0 | 2026-03-08 | Refactoring BUG-05/ARCH-01: static HttpClient, Flask init_db, SSL fix, try/catch |
| v1.5.0 | 2026-03-08 | NovaSCMApiService centralizzato, cache API, workflow drag-and-drop |
| v1.4.0 | 2026-03-07 | Tab Deploy OS, autounattend.xml builder, profili deploy |

---

## 🚀 Quick Start

```powershell
# Scarica l'ultima versione
# https://github.com/claudiobecchis/NovaSCM/releases

# Oppure compila da sorgente:
git clone https://github.com/claudiobecchis/NovaSCM
cd NovaSCM
dotnet build
```

**Requisiti:** Windows 10/11 (64-bit) · .NET 9.0 Runtime

---

## 📚 Documentazione

- [[Installazione e primo avvio|Installazione]]
- [[Scansione Rete|Scansione-Rete]]
- [[Certificati WiFi EAP-TLS|Certificati-EAP-TLS]]
- [[Deploy Windows zero-touch|Deploy-Windows]]
- [[Workflow e automazione|Workflow]]
- [[Gestione Fleet PC|Fleet-PC]]
- [[OPSI — distribuzione software|OPSI]]
- [[SCCM e OSD|SCCM]]
- [[Change Requests|Change-Requests]]
- [[FAQ e Risoluzione problemi|FAQ]]

---

## ☕ Supporta il progetto

NovaSCM è gratuito e lo resterà sempre. Se ti è utile:

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Offrimi%20un%20caffè-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/polariscore)
[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-db61a2?logo=github-sponsors)](https://github.com/sponsors/claudiobecchis)
[![PayPal](https://img.shields.io/badge/PayPal-Dona-003087?logo=paypal)](https://paypal.me/CBECCHIS)

---

## 📄 Licenza

MIT License — Software libero e open source.
© 2026 [Claudio Becchis](https://polariscore.it) — PolarisCore.it
