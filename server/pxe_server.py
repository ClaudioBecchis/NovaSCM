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


def start_tftp_server(dist_dir: str, host: str = "0.0.0.0", port: int = 69) -> None:
    """
    Avvia il server TFTP in background.
    dist_dir: cartella contenente ipxe.efi (e altri file da servire via TFTP).
    """
    global _tftp_thread

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

    def _run() -> None:
        server = tftpy.TftpServer(dist_dir)
        log.info("TFTP server avviato su %s:%d — dist_dir=%s", host, port, dist_dir)
        try:
            server.listen(host, port)
        except PermissionError:
            log.error(
                "Permesso negato per porta %d. "
                "Su Linux la porta 69 richiede root o CAP_NET_BIND_SERVICE. "
                "Nel container Docker assicurarsi che la porta sia esposta come 69:69/udp.",
                port,
            )
        except OSError as exc:
            log.error("TFTP server errore: %s", exc)

    _tftp_thread = threading.Thread(target=_run, name="tftp-server", daemon=True)
    _tftp_thread.start()
    log.info("Thread TFTP avviato (daemon)")
