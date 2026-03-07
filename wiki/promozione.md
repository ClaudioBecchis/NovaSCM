# Materiale promozionale NovaSCM

## AlternativeTo — Testo per registrazione

**URL:** https://alternativeto.net/software/add/

**Nome:** NovaSCM
**Sito:** https://polariscore.it/novascm
**GitHub:** https://github.com/claudiobecchis/NovaSCM
**Piattaforma:** Windows
**Licenza:** Open Source (MIT)
**Prezzo:** Gratuito
**Categoria:** Network Management, IT Management, System Administration

**Descrizione breve (160 car.):**
> Open source fleet & network manager per Windows. Scansione rete, certificati WiFi EAP-TLS, deploy Windows zero-touch, workflow automation. Alternativa gratuita a SCCM.

**Descrizione completa:**
> NovaSCM è uno strumento gratuito e open source per la gestione di reti e parchi macchine Windows, ispirato a Microsoft SCCM (System Center Configuration Manager).
>
> **Funzionalità principali:**
> - Scansione rete automatica (IP, MAC, vendor, tipo device, porte) con vista tabella, mappa grafica e heatmap subnet
> - Gestione certificati WiFi EAP-TLS per autenticazione WPA2-Enterprise
> - Deploy Windows zero-touch: genera autounattend.xml + postinstall.ps1 per USB o PXE
> - Workflow automation: sequenze di step automatizzati sui PC (cmd, PowerShell, winget, reboot)
> - Fleet management: inventario hardware/software via WMI, RDP one-click
> - Integrazione OPSI per distribuzione software
> - Change Requests per tracciamento provisioning PC
> - Database locale SQLite con sync server opzionale
> - Wiki documentazione integrata nell'app
> - Live ping graph, radar scan animato, network map
>
> Ideale per: homelab, piccole aziende, sysadmin, IT manager.

**Alternative a cui si paragona:**
- Microsoft SCCM / Endpoint Configuration Manager
- Ansible (parzialmente)
- PDQ Deploy
- Famatech Remote Administrator

---

## Awesome Lists GitHub — Submission

**Repository consigliati dove proporre NovaSCM:**

1. **awesome-homelab** — cerca su GitHub "awesome homelab"
   - Issue title: `Add NovaSCM — Open Source Fleet & Network Manager`
   - Body: `NovaSCM is a free, open source fleet and network management tool for Windows homelabs. Features: network scanning, EAP-TLS WiFi certs, Windows zero-touch deploy, workflow automation. https://github.com/claudiobecchis/NovaSCM`

2. **awesome-sysadmin** — https://github.com/awesome-foss/awesome-sysadmin
   - Categoria: `IT Asset Management` o `Network Management`
   - Testo: `[NovaSCM](https://github.com/claudiobecchis/NovaSCM) - Open source fleet & network manager. Network scanning, WiFi EAP-TLS certs, Windows zero-touch deployment, workflow automation. [MIT]`

3. **awesome-selfhosted** — https://github.com/awesome-selfhosted/awesome-selfhosted
   - Categoria: `Network Management`

---

## Reddit — Post consigliato

**Subreddit:** r/homelab, r/sysadmin, r/selfhosted

**Titolo:**
> I built NovaSCM — a free, open source SCCM-like tool for homelabs and small businesses

**Testo (inglese per reach globale):**
> Hey everyone! I wanted to share a project I've been building: **NovaSCM**, a free Windows app for managing your home or office network.
>
> **What it does:**
> - Network scanning (IP, MAC, vendor, device type, ports) with table view, visual map, and subnet heatmap
> - WiFi EAP-TLS certificate management for WPA2-Enterprise
> - Windows zero-touch deployment (generates autounattend.xml + postinstall.ps1)
> - Workflow automation (like SCCM Task Sequences but simpler)
> - Fleet PC management with hardware inventory via WMI
> - Offline-first with SQLite local database
> - Built-in documentation wiki
>
> It's basically a lightweight SCCM replacement that works without any servers (server is optional for advanced features).
>
> **Tech stack:** C# WPF .NET 9, SQLite, ModernWPF
>
> GitHub: https://github.com/claudiobecchis/NovaSCM
>
> It's MIT licensed and free forever. Happy to answer questions!

---

## Hacker News — Show HN

**Titolo:**
> Show HN: NovaSCM – Open-source fleet and network manager for Windows (MIT)

**Testo:**
> I built NovaSCM after spending too much time manually managing my homelab. It's a WPF desktop app that combines network scanning, WiFi certificate management (EAP-TLS), zero-touch Windows deployment, and workflow automation in one tool.
>
> The goal was to replicate the useful parts of Microsoft SCCM without the enterprise complexity and licensing cost.
>
> Key features: subnet scanning with visual heatmap, EAP-TLS cert generation/distribution, autounattend.xml builder for unattended Windows installs, SCCM-style task sequence workflows, SQLite-based offline-first storage.
>
> https://github.com/claudiobecchis/NovaSCM
