#!/bin/bash
# Deploy NovaSCM Flask server su CT LXC (Debian)
# Uso: install-flask-ct.sh <CTID> <API_KEY> <SERVER_IP>
set -euo pipefail

CTID="${1:?CTID richiesto}"
API_KEY="${2:?API_KEY richiesta}"
SERVER_IP="${3:?SERVER_IP richiesto}"
SMB_PASS="${4:-NovaSCMpxe2026!}"
APP_ROOT="/opt/novascm"
SERVICE_NAME="novascm.service"

pct exec "$CTID" -- bash -c "
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq python3 python3-venv python3-pip samba wimtools sqlite3 curl

mkdir -p ${APP_ROOT}/smb/sources ${APP_ROOT}/server/dist/winpe ${APP_ROOT}/data
ln -sfn ${APP_ROOT}/server/dist/winpe/install.wim ${APP_ROOT}/smb/sources/install.wim

# Samba user read-only per install.wim
id novascm &>/dev/null || useradd -r -M -s /usr/sbin/nologin novascm
(echo '${SMB_PASS}'; echo '${SMB_PASS}') | smbpasswd -s -a novascm

cat > /etc/samba/smb.conf.d/novascm-wininstall.conf << 'SMBEOF'
[wininstall]
   path = ${APP_ROOT}/smb
   browseable = yes
   read only = yes
   guest ok = no
   valid users = novascm
SMBEOF
mkdir -p /etc/samba/smb.conf.d
testparm -s >/dev/null
systemctl enable smbd
systemctl restart smbd

# venv
if [ ! -d ${APP_ROOT}/venv ]; then
  python3 -m venv ${APP_ROOT}/venv
fi
${APP_ROOT}/venv/bin/pip install -q -r ${APP_ROOT}/server/requirements.txt

# autoexec.ipxe
cat > ${APP_ROOT}/server/dist/autoexec.ipxe << IPEOF
#!ipxe
chain http://${SERVER_IP}:9091/api/boot/\${net0/mac} || shell
IPEOF

# systemd
cat > /etc/systemd/system/${SERVICE_NAME} << SVCEOF
[Unit]
Description=NovaSCM Flask Server (GitHub)
After=network.target smbd.service

[Service]
Type=simple
WorkingDirectory=${APP_ROOT}/server
Environment=NOVASCM_DB=${APP_ROOT}/data/novascm.db
Environment=NOVASCM_API_KEY=${API_KEY}
Environment=NOVASCM_PUBLIC_URL=http://${SERVER_IP}:9091
Environment=PORT=9091
ExecStart=${APP_ROOT}/venv/bin/python ${APP_ROOT}/server/api.py
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
SVCEOF

systemctl daemon-reload
systemctl enable ${SERVICE_NAME}
"

echo "CT${CTID}: base install OK"