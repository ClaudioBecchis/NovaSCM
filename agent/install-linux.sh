#!/bin/bash
# NovaSCM Agent Installer — Linux (Debian/Ubuntu)
# Uso: sudo bash install-linux.sh --api-url http://YOUR-SERVER-IP:9091

set -e

API_URL="http://YOUR-SERVER-IP:9091"
PC_NAME=$(hostname | tr '[:lower:]' '[:upper:]')
POLL_SEC=60
AGENT_DIR="/opt/novascm-agent"
CONFIG_DIR="/etc/novascm"
STATE_DIR="/var/lib/novascm"
LOG_DIR="/var/log/novascm"
SVC_NAME="novascm-agent"

# Parse args
while [[ $# -gt 0 ]]; do
    case $1 in
        --api-url)  API_URL="$2";  shift 2 ;;
        --pc-name)  PC_NAME="$2";  shift 2 ;;
        --poll-sec) POLL_SEC="$2"; shift 2 ;;
        *) shift ;;
    esac
done

log() { echo "[NovaSCM] $1"; }

# ── 1. Controlla root ─────────────────────────────────────────────────────────
if [ "$EUID" -ne 0 ]; then
    log "Errore: eseguire come root (sudo)"
    exit 1
fi

# ── 2. Installa Python se mancante ────────────────────────────────────────────
log "Controllo Python3..."
if ! command -v python3 &>/dev/null; then
    log "Installo python3..."
    apt-get update -qq
    apt-get install -y -qq python3
fi
PYTHON=$(command -v python3)
log "Python: $PYTHON"

# ── 3. Crea directory ─────────────────────────────────────────────────────────
log "Creo directory..."
mkdir -p "$AGENT_DIR" "$CONFIG_DIR" "$STATE_DIR" "$LOG_DIR"

# ── 4. Scarica Agent ──────────────────────────────────────────────────────────
log "Scarico novascm-agent.py da $API_URL..."
curl -fsSL "$API_URL/api/download/agent" -o "$AGENT_DIR/novascm-agent.py"
chmod +x "$AGENT_DIR/novascm-agent.py"
log "Agent scaricato: $AGENT_DIR/novascm-agent.py"

# ── 5. Verifica SHA256 ───────────────────────────────────────────────────────
log "Verifico integrità SHA256..."
EXPECTED=$(curl -fsSL "$API_URL/api/download/agent.sha256?os=linux")
ACTUAL=$(sha256sum "$AGENT_DIR/novascm-agent.py" | awk '{print $1}')
if [ "$ACTUAL" != "$EXPECTED" ]; then
    log "ERRORE: hash mismatch. Atteso: $EXPECTED  Ottenuto: $ACTUAL"
    rm -f "$AGENT_DIR/novascm-agent.py"
    exit 1
fi
log "SHA256 verificato: OK"

# ── 6. Crea config ────────────────────────────────────────────────────────────
log "Scrivo config: $CONFIG_DIR/agent.json"
cat > "$CONFIG_DIR/agent.json" << EOF
{
  "api_url":  "$API_URL",
  "pc_name":  "$PC_NAME",
  "poll_sec": $POLL_SEC
}
EOF

# ── 7. Crea systemd service ───────────────────────────────────────────────────
# Crea utente dedicato se non esiste
if ! id "novascm" &>/dev/null; then
    log "Creo utente novascm..."
    useradd --system --no-create-home --shell /usr/sbin/nologin novascm
fi
chown -R novascm:novascm "$AGENT_DIR" "$STATE_DIR" "$LOG_DIR"

log "Creo systemd service $SVC_NAME..."
cat > "/etc/systemd/system/$SVC_NAME.service" << EOF
[Unit]
Description=NovaSCM Workflow Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=novascm
Group=novascm
ExecStart=$PYTHON $AGENT_DIR/novascm-agent.py
WorkingDirectory=$AGENT_DIR
Restart=always
RestartSec=30
StandardOutput=append:$LOG_DIR/agent.log
StandardError=append:$LOG_DIR/agent.log
Environment=PYTHONUNBUFFERED=1
ProtectSystem=strict
ProtectHome=true
NoNewPrivileges=true
ReadWritePaths=$STATE_DIR $LOG_DIR

[Install]
WantedBy=multi-user.target
EOF

# ── 8. Abilita e avvia servizio ───────────────────────────────────────────────
log "Abilito e avvio $SVC_NAME..."
systemctl daemon-reload
systemctl enable "$SVC_NAME"
systemctl restart "$SVC_NAME"
sleep 2
STATUS=$(systemctl is-active "$SVC_NAME")
log "Stato servizio: $STATUS"

if [ "$STATUS" = "active" ]; then
    log "NovaSCM Agent installato e avviato correttamente!"
    log "Log:    $LOG_DIR/agent.log"
    log "Config: $CONFIG_DIR/agent.json"
else
    log "ATTENZIONE: servizio non attivo. Controlla: journalctl -u $SVC_NAME"
fi
