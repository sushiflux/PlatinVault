#!/bin/sh
# Container entrypoint. The container starts as root only to hand the data folder to the configured user, then runs the
# app without root rights (setpriv). PUID/PGID default to Unraid's nobody:users (99:100), the owner Unraid gives a new
# appdata folder; other hosts set their own. Started with --user (not root), it simply runs the app as that user.
set -eu

data_dir="${Companion__DataDir:-/data}"
app="/app/Companion.Api.dll"

if [ "$(id -u)" != "0" ]; then
    exec dotnet "$app" "$@"
fi

puid="${PUID:-99}"
pgid="${PGID:-100}"
case "$puid" in ''|*[!0-9]*) echo "PUID muss eine Zahl sein (ist: '$puid')." >&2; exit 1 ;; esac
case "$pgid" in ''|*[!0-9]*) echo "PGID muss eine Zahl sein (ist: '$pgid')." >&2; exit 1 ;; esac
if [ "$puid" = "0" ]; then
    echo "PUID=0 (root) ist nicht erlaubt: PlatinVault laeuft nie mit Root-Rechten. Bitte eine normale Benutzer-ID setzen (Unraid: 99)." >&2
    exit 1
fi

mkdir -p "$data_dir"
# Only what does not belong to the user yet - a full chown on every start would be slow on a big folder
find "$data_dir" \( ! -user "$puid" -o ! -group "$pgid" \) -exec chown -h "$puid:$pgid" {} +

export HOME=/tmp
exec setpriv --reuid="$puid" --regid="$pgid" --clear-groups -- dotnet "$app" "$@"
