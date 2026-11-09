# ADB-Server für den android-updater-Stack
# Basis: Debian 13 (trixie), adb aus den offiziellen Debian-Paketen
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

# Hier liegt das Schlüsselpaar (adbkey/adbkey.pub), das die Phones autorisieren.
# Als Volume mounten, sonst ist nach jedem Container-Neubau eine neue Freigabe nötig.
VOLUME ["/home/adb/.android"]

EXPOSE 5037

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/entrypoint.sh"]
