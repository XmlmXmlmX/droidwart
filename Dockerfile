# ADB server for the Droidwart stack
# Base: Debian 13 (trixie), adb from the official Debian packages
FROM debian:trixie-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends adb tini ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && useradd --create-home --uid 1000 --shell /usr/sbin/nologin adb \
 && mkdir -p /home/adb/.android \
 && chown -R adb:adb /home/adb/.android

COPY --chmod=0755 entrypoint.sh /usr/local/bin/entrypoint.sh

USER adb
WORKDIR /home/adb

# Holds the key pair (adbkey/adbkey.pub) that the phones authorize.
# Mount it as a volume, otherwise every rebuild requires a new authorization on the devices.
VOLUME ["/home/adb/.android"]

EXPOSE 5037

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/entrypoint.sh"]
