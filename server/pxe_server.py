"""
NovaSCM — TFTP Server minimale
Serve ipxe.efi via TFTP (porta 69/udp).
Avviato come thread daemon da api.py all'avvio del server Flask.

Dipendenza: pip install tftpy
"""
import logging
import os
import threading
import time

log = logging.getLogger("novascm-tftp")

_tftp_thread: threading.Thread | None = None
_tftp_healthy: bool = False
_dist_dir: str = ""
_host: str = "0.0.0.0"
_port: int = 69


def start_tftp_server(dist_dir: str, host: str = "0.0.0.0", port: int = 69) -> None:
    """
    Avvia il server TFTP in background.
    dist_dir: cartella contenente ipxe.efi (e altri file da servire via TFTP).
    """
    global _dist_dir, _host, _port

    _dist_dir = dist_dir
    _host = host
    _port = port

    ipxe_path = os.path.join(dist_dir, "ipxe.efi")
    if not os.path.isfile(ipxe_path):
        log.warning(
            "ipxe.efi non trovato in %s — server TFTP non avviato. "
            "Scaricare da https://boot.ipxe.org/ipxe.efi e copiare in server/dist/",
            dist_dir,
        )
        return

    try:
        import tftpy  # type: ignore
    except ImportError:
        log.error(
            "tftpy non installato — server TFTP non avviato. "
            "Eseguire: pip install tftpy"
        )
        return

    # SEC: a differenza degli endpoint HTTP equivalenti (_is_pxe_allowed in
    # api.py), tftpy non espone un hook per filtrare le richieste UDP/69 per
    # IP sorgente prima di servire un file statico esistente (dyn_file_func
    # scatta solo per file assenti). Chiunque raggiunga la porta 69/udp può
    # scaricare tutto il contenuto di dist_dir. Mitigare a livello di
    # firewall/VLAN (restringere UDP/69 alla subnet PXE), non solo con
    # NOVASCM_PXE_ALLOWED_SUBNETS che qui non si applica.
    log.warning(
        "TFTP (UDP/%d) non applica l'allow-list di subnet PXE — a differenza "
        "degli endpoint HTTP equivalenti, chiunque raggiunga questa porta può "
        "scaricare i file in %s. Restringere l'accesso a livello di firewall/VLAN.",
        port, dist_dir,
    )

    _launch_tftp_thread()


def _launch_tftp_thread() -> None:
    """Crea e avvia il thread TFTP."""
    global _tftp_thread, _tftp_healthy
    import tftpy  # type: ignore

    def _run() -> None:
        global _tftp_healthy
        server = tftpy.TftpServer(_dist_dir)
        try:
            log.info("TFTP server in ascolto su %s:%d — dist_dir=%s", _host, _port, _dist_dir)
            _tftp_healthy = True
            server.listen(_host, _port)  # bloccante — esce solo su errore o stop
        except PermissionError:
            log.error(
                "Permesso negato per porta %d. "
                "Su Linux la porta 69 richiede root o CAP_NET_BIND_SERVICE. "
                "Nel container Docker usare cap_add: NET_BIND_SERVICE e "
                "assicurarsi che la porta sia esposta come 69:69/udp.",
                _port,
            )
        except OSError as exc:
            log.error("TFTP server errore: %s", exc)
        finally:
            # Garantisce stato coerente anche su eccezioni impreviste
            # (non solo PermissionError/OSError) o su uscita normale del loop.
            _tftp_healthy = False

    _tftp_thread = threading.Thread(target=_run, name="tftp-server", daemon=True)
    _tftp_thread.start()
    log.info("Thread TFTP avviato (daemon)")


def is_tftp_alive() -> bool:
    """Verifica se il thread TFTP è attivo. Usato dal health check endpoint."""
    return _tftp_healthy and _tftp_thread is not None and _tftp_thread.is_alive()


def restart_tftp_if_dead() -> bool:
    """Riavvia il TFTP se il thread è morto. Ritorna True solo se il riavvio è confermato riuscito."""
    if _tftp_thread is not None and not _tftp_thread.is_alive() and _dist_dir:
        log.warning("TFTP thread morto — tentativo di restart...")
        _launch_tftp_thread()
        time.sleep(0.4)  # tempo minimo perché il thread tenti il bind
        return is_tftp_alive()
    return False
