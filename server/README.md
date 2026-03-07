# NovaSCM — Server API

Server REST per la gestione delle Change Request (CR) di NovaSCM.

## Avvio rapido con Docker

```bash
docker compose up -d
```

L'API sarà disponibile su `http://localhost:9091`.

## Variabili d'ambiente

| Variabile     | Default               | Descrizione                     |
|---------------|-----------------------|---------------------------------|
| `NOVASCM_DB`  | `/data/novascm.db`    | Percorso del database SQLite    |
| `PORT`        | `9091`                | Porta di ascolto                |

## Avvio senza Docker (Python)

```bash
pip install flask gunicorn
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
