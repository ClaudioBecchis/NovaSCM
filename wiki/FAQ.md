# FAQ — Domande frequenti

## Installazione

**NovaSCM è gratuito?**
Sì, completamente gratuito e open source (MIT License). Lo sarà per sempre.

**Funziona senza server?**
Sì. Le funzioni di rete locale (scansione, ping, mappa, heatmap) funzionano immediatamente senza configurare nulla. Il server è opzionale per workflow, certificati e change requests.

**Serve un account?**
No. NovaSCM non richiede registrazione né account online.

---

## Scansione rete

**Il MAC non viene trovato**
Il MAC è letto dalla tabella ARP di Windows. Se il device è su una subnet diversa (con router in mezzo), il MAC potrebbe non essere visibile. Usa uno switch managed con VLAN access per subnet diverse.

**La scansione è lenta**
Su subnet /24 sono normali 15-30 secondi. Se usi firewall aggressivi o reti con alta latenza, il tempo aumenta. Puoi ridurre il parallelismo nelle impostazioni.

**Un device online non viene rilevato**
Alcuni device bloccano ICMP (ping). Prova la scansione "completa porte" con doppio click su quell'IP.

**Come scansiono più subnet contemporaneamente?**
Vai in ⚙️ Impostazioni → campo **Subnet multiple** → inserisci una subnet per riga (formato `192.168.x.0/24`). Poi usa **🌐 Tutte le VLAN** nel tab Rete.

---

## Certificati WiFi EAP-TLS

**Il certificato non viene generato**
Verifica che:
1. Il Certportal sia raggiungibile all'URL configurato
2. La CA sia presente sul server (`/ca/ca.crt`)
3. Il MAC del device sia noto al Certportal

**Come faccio l'enrollment automatico su un PC Windows?**
Da PowerShell (admin) sul PC da registrare:
```powershell
iwr http://192.168.20.110:9090/agent/install.ps1 | iex
```

---

## Deploy Windows

**Posso usare autounattend.xml con Windows 10?**
Sì, seleziona "Windows 10 Pro" nell'edizione. Il file generato è compatibile con Win10 e Win11.

**Il PC non avvia dall'USB**
- Verifica che il BIOS/UEFI sia configurato per avviare da USB
- Disattiva Secure Boot se la chiavetta non è certificata
- Usa Rufus in modalità **GPT + UEFI** (non legacy)

**Posso personalizzare il postinstall.ps1?**
Sì, dopo aver cliccato "Genera file", puoi modificare manualmente `postinstall.ps1` prima di copiarlo sulla USB.

---

## Workflow

**La connessione al server workflow fallisce**
Verifica l'URL in ⚙️ Impostazioni → campo **NovaSCM API URL**. In modalità offline, i workflow vengono salvati localmente e sincronizzati quando il server è disponibile.

**Posso eseguire script PowerShell nei workflow?**
Sì, aggiungi uno step di tipo `powershell` e inserisci il percorso o il contenuto dello script.

---

## Easter Egg

**Ho sentito di un Easter Egg nascosto...**
Apri NovaSCM e digita sulla tastiera: `↑ ↑ ↓ ↓ ← → ← → B A` 😏

---

## Supporto

- **Bug report:** [GitHub Issues](https://github.com/claudiobecchis/NovaSCM/issues)
- **Richieste funzionalità:** [GitHub Discussions](https://github.com/claudiobecchis/NovaSCM/discussions)
- **Sito:** [polariscore.it](https://polariscore.it)
- **Ko-fi:** [ko-fi.com/polariscore](https://ko-fi.com/polariscore)
