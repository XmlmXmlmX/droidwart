#!/bin/sh
# Startet den ADB-Server im Vordergrund (lauscht auf allen Interfaces, Port 5037)
# und verbindet optional die Geräte aus ADB_CONNECT regelmäßig neu,
# z. B. nach einem Neustart eines Phones.
#
# Umgebungsvariablen:
#   ADB_CONNECT             Kommagetrennte Liste, z. B. "192.168.178.164:5555,192.168.178.160:5555"
#   ADB_RECONNECT_INTERVAL  Sekunden zwischen den Verbindungsversuchen (Standard: 60)
set -eu

PORT=5037

adb -a -P "$PORT" server nodaemon &
SERVER_PID=$!

# Kurz warten, bis der Server Verbindungen annimmt
sleep 2

if [ -n "${ADB_CONNECT:-}" ]; then
  (
    while kill -0 "$SERVER_PID" 2>/dev/null; do
      for dev in $(echo "$ADB_CONNECT" | tr ',' ' '); do
        # "already connected" ist kein Fehler; Ausgabe nur bei Statuswechseln interessant
        adb -P "$PORT" connect "$dev" 2>&1 | grep -v "already connected" || true
      done
      sleep "${ADB_RECONNECT_INTERVAL:-60}"
    done
  ) &
fi

# Endet der Server, endet der Container -> Docker startet ihn per restart-Policy neu
wait "$SERVER_PID"
