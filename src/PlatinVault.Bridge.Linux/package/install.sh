#!/bin/sh
# Installs the PlatinVault Bridge for Linux as a systemd USER service. No root needed, nothing outside your home folder is touched
# (works on read-only-root systems such as SteamOS and Bazzite).
#
#   ./install.sh            install or update (keeps your configuration)
#   ./install.sh --no-start install without starting the service
#
# Installs to:  ${XDG_DATA_HOME:-~/.local/share}/platinvault-bridge/      (binary, handbook, licences)
# Unit file:    ${XDG_CONFIG_HOME:-~/.config}/systemd/user/platinvault-bridge.service
# Config:       ${XDG_CONFIG_HOME:-~/.config}/platinvault-bridge/bridge.json   (mode 600; written from the bridge.json of this package)
# Log:          ${XDG_STATE_HOME:-~/.local/state}/platinvault-bridge/bridge.log   and   journalctl --user -u platinvault-bridge
set -eu

START=1
for arg in "$@"; do
    case "$arg" in
        --no-start) START=0 ;;
        -h | --help) sed -n '2,12p' "$0"; exit 0 ;;
        *) echo "Unknown option / Unbekannte Option: $arg" >&2; exit 2 ;;
    esac
done

HERE=$(cd "$(dirname "$0")" && pwd)
BINARY="$HERE/platinvault-bridge"
UNIT=platinvault-bridge.service

if [ "$(id -u)" = 0 ]; then
    echo "Do not run this as root - the bridge belongs to your own user (it reads your Steam files)." >&2
    echo "Nicht als root ausfuehren - die Bridge gehoert zu deinem eigenen Benutzer (sie liest deine Steam-Dateien)." >&2
    exit 1
fi
if [ -z "${HOME:-}" ] || [ ! -d "$HOME" ]; then
    echo "HOME is not set / HOME ist nicht gesetzt." >&2
    exit 1
fi
if [ ! -f "$BINARY" ]; then
    echo "platinvault-bridge not found next to this script / platinvault-bridge liegt nicht neben diesem Skript: $HERE" >&2
    exit 1
fi

DATA="${XDG_DATA_HOME:-$HOME/.local/share}/platinvault-bridge"
UNITDIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}/platinvault-bridge/bridge.json"
DEST="$DATA/platinvault-bridge"

mkdir -p "$DATA" "$UNITDIR"

# Replace the binary through a rename, so an update works while the old version is still running
cp "$BINARY" "$DEST.new"
chmod 755 "$DEST.new"
mv -f "$DEST.new" "$DEST"

# Handbook and licences travel with the program (PlatinVault ships its manual with every download)
for f in "$HERE"/*.pdf "$HERE"/LICENSE "$HERE"/THIRD-PARTY-NOTICES.md "$HERE"/README.txt "$HERE"/LIESMICH.txt; do
    [ -f "$f" ] && cp -f "$f" "$DATA/" || true
done
[ -f "$HERE/uninstall.sh" ] && cp -f "$HERE/uninstall.sh" "$DATA/uninstall.sh" && chmod 755 "$DATA/uninstall.sh"

# Configuration: the package from PlatinVault carries a ready bridge.json (server, token, language). It is read from the file, so the
# token never appears on a command line (the process list is visible to every user). Once it is saved in the private config folder, the
# copy in the download folder is deleted - a token should not lie around in Downloads.
if [ -f "$HERE/bridge.json" ]; then
    if "$DEST" setup --from "$HERE/bridge.json"; then
        if [ -f "$CONFIG" ]; then
            rm -f "$HERE/bridge.json"
            echo "Deleted the copy of bridge.json (it holds your token) from this folder."
            echo "bridge.json (enthaelt deinen Token) wurde aus diesem Ordner geloescht."
        fi
    else
        echo "(Setup reported a problem - see above; bridge.json stays in this folder.)" >&2
        echo "(Einrichtung meldet ein Problem - siehe oben; bridge.json bleibt in diesem Ordner.)" >&2
    fi
elif [ -f "$CONFIG" ]; then
    echo "Keeping the existing configuration / Vorhandene Konfiguration bleibt erhalten: $CONFIG"
else
    echo "No bridge.json in this package. Set the bridge up yourself; the token comes from standard input so it stays out of the process list:"
    echo "Keine bridge.json im Paket. Richte die Bridge selbst ein; den Token ueber die Standardeingabe, damit er nicht in der Prozessliste steht:"
    echo "    echo '<token>' | $DEST setup --server http://<server>:<port> --token-stdin"
fi

# Values that go into the unit file: systemd expands % specifiers and $ variables in ExecStart= and Environment=, and inside quotes a
# backslash and a quote need a backslash. (Backslash first, so the ones added afterwards are not doubled.)
unit_value() {
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/%/%%/g' -e 's/\$/$$/g'
}

# The user service does not see the XDG variables of this shell. Pass the ones that are set, so it reads the same config the setup above
# wrote (and the same state folder the status command looks in).
ENVIRONMENT_LINES=""
add_environment() {
    if [ -n "$2" ]; then
        ENVIRONMENT_LINES="$ENVIRONMENT_LINES
Environment=\"$1=$(unit_value "$2")\""
    fi
}
add_environment XDG_CONFIG_HOME "${XDG_CONFIG_HOME:-}"
add_environment XDG_STATE_HOME "${XDG_STATE_HOME:-}"
add_environment XDG_DATA_HOME "${XDG_DATA_HOME:-}"

# The service. Restart=always also restarts it after a self-update; exit status 78 (configuration missing) is not retried.
# KillMode=process: stopping or restarting the service ends the bridge only. Steam and a game that the bridge started on request must
# survive an update of the bridge (the bridge also starts them in a scope of their own - this is the second line of defence).
cat > "$UNITDIR/$UNIT" <<UNITFILE
[Unit]
Description=PlatinVault Bridge (live Steam achievements)
Documentation=https://github.com/sushiflux/PlatinVault

[Service]
Type=simple
ExecStart="$(unit_value "$DEST")" run$ENVIRONMENT_LINES
KillMode=process
Restart=always
RestartSec=5
RestartPreventExitStatus=78

[Install]
WantedBy=default.target
UNITFILE

if [ "$START" = 1 ] && command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
    systemctl --user daemon-reload
    systemctl --user enable "$UNIT"
    # restart (not just start): an update replaces a running service
    systemctl --user restart "$UNIT"
    echo
    echo "Installed and started / Installiert und gestartet."
    echo "  Status / Status:   $DEST status"
    echo "  Log / Protokoll:   journalctl --user -u platinvault-bridge -f"
    echo "  Remove / Entfernen: $DATA/uninstall.sh"
else
    echo
    echo "Installed, but the service was not started (no systemd user session reachable, or --no-start)."
    echo "Installiert, aber der Dienst wurde nicht gestartet (keine systemd-Benutzersitzung erreichbar, oder --no-start)."
    echo "  Try it in the foreground / Zum Ausprobieren im Vordergrund:  $DEST run"
    echo "  Start the service later / Dienst spaeter starten:  systemctl --user daemon-reload && systemctl --user enable --now $UNIT"
fi

cat <<'NOTE'

Steam Deck: this has been prepared for the Steam Deck and Bazzite but NOT verified on real devices. In particular it is not
verified whether the systemd user service keeps running in Game Mode (it surely runs in Desktop Mode).
Steam Deck: fuer Steam Deck und Bazzite vorbereitet, aber NICHT an echten Geraeten geprueft. Insbesondere ist nicht geprueft, ob
der systemd-Benutzerdienst im Spielmodus (Game Mode) weiterlaeuft (im Desktop-Modus laeuft er).
NOTE
