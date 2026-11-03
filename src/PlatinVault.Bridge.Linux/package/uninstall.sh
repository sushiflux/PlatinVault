#!/bin/sh
# Removes the PlatinVault Bridge for Linux: stops and disables the user service, deletes the unit file, the binary and the documents.
#
#   ./uninstall.sh          keeps your configuration (server + token) and the log
#   ./uninstall.sh --purge  removes those as well
set -eu

PURGE=0
for arg in "$@"; do
    case "$arg" in
        --purge) PURGE=1 ;;
        -h | --help) sed -n '2,6p' "$0"; exit 0 ;;
        *) echo "Unknown option / Unbekannte Option: $arg" >&2; exit 2 ;;
    esac
done

UNIT=platinvault-bridge.service
DATA="${XDG_DATA_HOME:-$HOME/.local/share}/platinvault-bridge"
UNITFILE="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/$UNIT"
CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}/platinvault-bridge"
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/platinvault-bridge"

if command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
    systemctl --user disable --now "$UNIT" 2>/dev/null || true
fi

rm -f "$UNITFILE"
if command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
    systemctl --user daemon-reload || true
fi

# Only this program's own folder is removed - never anything else
if [ -d "$DATA" ]; then
    # uninstall.sh may be running from inside that folder; the shell has read it already, deleting is safe
    rm -rf "$DATA"
fi

if [ "$PURGE" = 1 ]; then
    rm -rf "$CONFIG" "$STATE"
    echo "Removed, including configuration and log / Entfernt, einschliesslich Konfiguration und Protokoll."
else
    echo "Removed / Entfernt. Kept / Behalten: $CONFIG (server + token), $STATE (log)."
    echo "Remove those as well / Auch diese entfernen: $0 --purge"
fi
