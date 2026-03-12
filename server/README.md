# NovaSCM — Server API

Server REST per la gestione delle Change Request (CR) di NovaSCM.

## Avvio rapido con Docker

```bash
docker compose up -d
```

L'API sarà disponibile su `http://localhost:9091`.

## Variabili d'ambiente

| Variabile | Default | Descrizione |
|-----------|---------|-------------|
| `NOVASCM_DB` | `/data/novascm.db` | Percorso del database SQLite |
| `PORT` | `9091` | Porta di ascolto |
| `NOVASCM_API_KEY` | _(auto-generata)_ | API key — se vuota viene generata al primo avvio e salvata in `/data/.api_key` |
| `NOVASCM_PUBLIC_URL` | _(auto)_ | URL pubblico del server (per proxy/reverse proxy) |
| `NOVASCM_PXE_ENABLED` | `0` | Imposta `1` per abilitare il server TFTP per PXE boot |
| `NOVASCM_PXE_ALLOWED_NETS` | `192.168.0.0/16,10.0.0.0/8,172.16.0.0/12` | Subnet autorizzate per PXE |

## Avvio con PXE (opzionale)

Il server include un server TFTP per il boot PXE via iPXE.

1. Scarica `ipxe.efi` da https://boot.ipxe.org/ipxe.efi e copialo in `dist/ipxe.efi`
2. Nel `docker-compose.yml`, decommenta le righe per la porta `69/udp` e il volume `./dist`
3. Imposta `NOVASCM_PXE_ENABLED=1`
4. Riavvia: `docker compose up -d`

> **Nota:** la porta 69/udp richiede privilegi root sul Docker host. Su Linux potrebbe servire `sudo`.

## Avvio senza Docker (Python)

```bash
pip install flask gunicorn flask-limiter python-json-logger tftpy
NOVASCM_DB=/percorso/novascm.db python api.py
```

## Endpoint principali

| Metodo | Path | Descrizione |
|--------|------|-------------|
| GET    | `/api/cr` | Lista tutte le CR |
| POST   | `/api/cr` | Crea nuova CR |
| GET    | `/api/cr/<id>` | Dettaglio CR |
| PUT    | `/api/cr/<id>/status` | Aggiorna stato |
| DELETE | `/api/cr/<id>` | Elimina CR |
| GET    | `/api/cr/by-name/<pc>/autounattend.xml` | Genera XML |
| POST   | `/api/cr/by-name/<pc>/checkin` | Check-in dispositivo |
| POST   | `/api/cr/by-name/<pc>/step` | Report step installazione |
| GET    | `/api/cr/<id>/steps` | Lista step CR |
| GET    | `/health` | Health check |

## Configurazione NovaSCM (client WPF)

In **Impostazioni → URL API NovaSCM** inserire:
```
http://<IP-SERVER>:9091/api/cr
```
