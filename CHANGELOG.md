# Changelog

All notable changes to NovaSCM are documented here.

---

## [1.1.0] - 2026-03-07

### Nuove funzionalità

- **Wake-on-LAN**: accensione remota dei PC dalla tab Rete con un click
- **SSH terminal**: apre una sessione SSH nel terminale (Windows Terminal o CMD)
- **Traceroute grafico**: visualizzazione hop-per-hop con latenza e IP per ogni salto
- **Speed test integrato**: test download/upload via Cloudflare con gauge ad arco animato
- **mDNS scanner**: scoperta automatica di dispositivi sulla rete locale via multicast DNS
- **Esporta CSV**: esportazione della lista dispositivi in formato CSV con SaveFileDialog
- **Script Library**: libreria di script PowerShell con 14 script predefiniti (inventario, gestione servizi, Windows Update, ecc.), editor integrato, esecuzione locale o remota via `Invoke-Command`
- **Auto-scan**: scansione rete automatica programmabile (ogni 5/15/30/60 minuti) con pulsante toggle
- **Note e tag dispositivi**: annotazioni e etichette per ogni dispositivo, salvate nel database SQLite
- **Notifiche toast**: notifiche di sistema nella system tray per eventi di rete (dispositivo online/offline)
- **Heatmap subnet**: visualizzazione densità IP per subnet con colori graduati
- **Matrix rain**: effetto visivo animato nella schermata di scansione
- **Gauge ad arco**: indicatori grafici per CPU, RAM, disco nel pannello dispositivi
- **Easter Egg Konami**: prova ↑↑↓↓←→←→BA nella finestra principale
- **Tab Wiki**: documentazione integrata nell'app con navigazione articoli
- **Pulsanti donazione**: supporto al progetto via Ko-fi/GitHub Sponsors

### Miglioramenti

- **Auto-update**: il controllo aggiornamenti usa ora GitHub Releases API per ottenere versione, note e link download direttamente dai release ufficiali
- **Ping sub-millisecondo**: risolto problema dei dispositivi locali che mostravano 0 ms; ora mostra "< 1 ms" con precisione tramite Stopwatch
- **UI overhaul**: pulsanti in stile light theme, status bar, etichette rivisitate per maggiore leggibilità
- **Tema chiaro unificato**: applicato a tutte le tab dell'applicazione
- **Fix tab startup**: risolto il problema della tab vuota all'avvio dell'applicazione
- **Fix link About**: corretti i link a polariscore.it e GitHub nella tab About

### Sicurezza

- Rimossi IP del server reale dagli script agente incorporati nel codice sorgente
- Aggiunto `config.json` al `.gitignore` per evitare il commit accidentale di credenziali

---

## [1.0.6] - 2026-03-01

### Rilascio iniziale

- Scansione rete con rilevamento IP, MAC, vendor, tipo dispositivo e porte aperte
- Vista tabella, mappa grafica e radar scan animato
- Gestione certificati WiFi EAP-TLS per autenticazione WPA2-Enterprise
- Generazione `autounattend.xml` + `postinstall.ps1` per Windows zero-touch deploy
- Workflow automation: sequenze di step automatizzati (cmd, PowerShell, winget, reboot)
- Fleet management: inventario hardware/software via WMI, RDP one-click
- Integrazione OPSI per distribuzione software
- Change Requests per tracciamento provisioning PC
- Database locale SQLite con sync server opzionale
- Live ping graph e monitor dispositivi in tempo reale
