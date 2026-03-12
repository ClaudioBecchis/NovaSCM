"""
NovaSCM — TFTP Server minimale
Serve ipxe.efi via TFTP (porta 69/udp).
Avviato come thread daemon da api.py all'avvio del server Flask.

Dipendenza: pip install tftpy
"""
import logging
import os
import threading

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

    _launch_tftp_thread()


def _launch_tftp_thread() -> None:
    """Crea e avvia il thread TFTP."""
    global _tftp_thread, _tftp_healthy
    import tftpy  # type: ignore

    def _run() -> None:
        global _tftp_healthy
        server = tftpy.TftpServer(_dist_dir)
        log.info("TFTP server avviato su %s:%d — dist_dir=%s", _host, _port, _dist_dir)
        _tftp_healthy = True
        try:
            server.listen(_host, _port)
        except PermissionError:
            _tftp_healthy = False
            log.error(
                "Permesso negato per porta %d. "
                "Su Linux la porta 69 richiede root o CAP_NET_BIND_SERVICE. "
                "Nel container Docker usare cap_add: NET_BIND_SERVICE e "
                "assicurarsi che la porta sia esposta come 69:69/udp.",
                _port,
            )
        except OSError as exc:
            _tftp_healthy = False
            log.error("TFTP server errore: %s", exc)

    _tftp_thread = threading.Thread(target=_run, name="tftp-server", daemon=True)
    _tftp_thread.start()
    log.info("Thread TFTP avviato (daemon)")


def is_tftp_alive() -> bool:
    """Verifica se il thread TFTP è attivo. Usato dal health check endpoint."""
    return _tftp_healthy and _tftp_thread is not None and _tftp_thread.is_alive()


def restart_tftp_if_dead() -> bool:
    """Riavvia il TFTP se il thread è morto. Ritorna True se riavviato."""
    if _tftp_thread is not None and not _tftp_thread.is_alive() and _dist_dir:
        log.warning("TFTP thread morto — tentativo di restart...")
        _launch_tftp_thread()
        return True
    return False
