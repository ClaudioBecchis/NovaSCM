# Scansione Rete

## Come funziona

NovaSCM esegue una scansione in parallelo su tutti gli IP della subnet:

1. **Ping ICMP** — verifica quali IP sono online
2. **ARP lookup** — legge il MAC address dalla tabella ARP di Windows
3. **OUI lookup** — identifica il vendor dal prefisso MAC (database locale)
4. **Port scan** — scansiona le porte più comuni per identificare il tipo di device
5. **Reverse DNS** — tenta di risolvere l'hostname

Tutto avviene in parallelo con semaforo per non saturare la rete.

---

## Scansione base

1. Inserisci **IP base** (es. `192.168.10.0`) e **subnet mask** (es. `24`)
2. Clicca **▶ Scansiona**
3. I device appaiono in tempo reale durante la scansione
4. La **barra radar** verde animata mostra la progressione
5. Al termine, i risultati vengono salvati nel database locale

> 💡 Doppio click su un device per aprire il dettaglio porte.

---

## Scansione multi-VLAN

Configura le subnet in **⚙️ Impostazioni → Subnet da scansionare** (una per riga):

```
192.168.10.0/24
192.168.20.0/24
192.168.30.0/24
```

Poi usa il pulsante **🌐 Tutte le VLAN** — le subnet vengono scansionate in parallelo.

---

## Viste disponibili

### ☰ Lista (default)
Tabella con colonne: icona, IP, MAC, hostname, tipo device, connessione, vendor, certificato, stato.

### 🗺️ Mappa rete
Vista grafica hub-and-spoke. Il gateway è al centro, gli altri device disposti in cerchi concentrici.
- Animazione **pulse** per i device online
- Linee tratteggiate per i device offline
- Tooltip al passaggio del mouse

### ⬛ Heatmap subnet
Griglia 16×16 = 256 celle, una per ogni IP `.0`→`.255` della subnet.

| Colore | Significato |
|---|---|
| 🟢 Verde | Device online |
| 🔵 Blu | Ha certificato EAP-TLS |
| 🟡 Amber | Gateway (`.1` o `.254`) |
| ⬛ Scuro | Offline o non ancora scansionato |

---

## Live Ping Graph

Seleziona un device nella tabella → il pannello inferiore mostra il grafico ping in tempo reale:

- **Linea verde** = latenza normale (< 50ms)
- **Linea gialla** = latenza media (50-150ms)
- **Linea rossa** = latenza alta o timeout
- **Area colorata** = riempimento gradiente per visualizzare i picchi

---

## Identificazione automatica device

NovaSCM identifica il tipo di device dalle porte aperte:

| Porte | Tipo rilevato |
|---|---|
| 8006 | Proxmox VE |
| 8123 | Home Assistant |
| 3389 | Windows PC |
| 22 + 80/443 | Linux Server |
| 22 | Linux generico |
| 9100 / 515 / 631 | Stampante |
| 1883 | IoT / MQTT |
| Vendor Ubiquiti/TP-Link | Router / AP |
| Vendor Apple | Apple device |

---

## Azioni disponibili

- **➕ Registra** — registra il device nel sistema (via MAC) per App e OPSI
- **🔐 Genera Cert** — genera certificato WiFi EAP-TLS
- **📱 QR Code** — genera QR code per enrollment mobile
- **👁️ Monitora** — scansione continua ogni 30s con notifica per nuovi device o disconnessioni
- **📊 Inventario** — raccoglie hardware/software via WMI (richiede WinRM attivo)
