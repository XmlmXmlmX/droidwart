#!/bin/sh
# Starts the ADB server in the foreground (listening on all interfaces, port 5037)
# and optionally reconnects the devices listed in ADB_CONNECT, e.g. after a phone reboots.
#
# Environment variables:
#   ADB_CONNECT             Comma-separated list, e.g. "192.168.178.164:5555,192.168.178.160:5555"
#   ADB_RECONNECT_INTERVAL  Seconds between connection attempts (default: 60)
set -eu

PORT=5037

adb -a -P "$PORT" server nodaemon &
SERVER_PID=$!

# Give the server a moment to accept connections
sleep 2

if [ -n "${ADB_CONNECT:-}" ]; then
  (
    while kill -0 "$SERVER_PID" 2>/dev/null; do
      for dev in $(echo "$ADB_CONNECT" | tr ',' ' '); do
        # "already connected" is not an error; only state changes are worth logging
        adb -P "$PORT" connect "$dev" 2>&1 | grep -v "already connected" || true
      done
      sleep "${ADB_RECONNECT_INTERVAL:-60}"
    done
  ) &
fi

# If the server exits, the container exits -> Docker restarts it via the restart policy
wait "$SERVER_PID"
