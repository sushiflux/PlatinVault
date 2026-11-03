#!/usr/bin/env bash
# Starts the PlatinVault server of the Linux package (the systemd user service and ctl.sh both run this script).
# It sets the server's configuration from the settings file and hands over to the program (exec - the program becomes the process,
# so systemd's stop signal reaches it directly and it can make its last backup).
#
# Settings (environment or ~/.config/platinvault/platinvault.env, KEY=VALUE per line):
#   PLATINVAULT_PORT      port to listen on (default 5233)
#   PLATINVAULT_BIND      address to listen on (default 127.0.0.1 = this computer only)
#   PLATINVAULT_DATA_DIR  data folder: database, backups, API keys (default ~/.local/share/platinvault/data)
# Any arguments are passed on to the program: "run.sh --backup-now" and "run.sh --restore <file>" run a database command instead.
set -eu

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/linux/lib.sh
source "$here/lib.sh"
pv_init_paths
pv_load_config
pv_resolve_settings

if [[ ! -x "$here/platinvault" ]]; then
    pv_err "Das Programm fehlt oder ist nicht ausführbar: $here/platinvault" "The program is missing or not executable: $here/platinvault"
    exit 1
fi

# The data folder holds the API keys: private to this user from the start
if [[ ! -d "$PV_DATA_DIR" ]]; then
    (umask 077 && mkdir -p "$PV_DATA_DIR")
fi

channel=release
if [[ -r "$here/release-channel.txt" ]]; then
    channel="$(tr -d '[:space:]' < "$here/release-channel.txt")"
fi

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="http://$PV_BIND:$PV_PORT"
export Companion__DataDir="$PV_DATA_DIR"
export Companion__ReleaseChannel="$channel"
# Tells the update check that this is the Linux package (instructions: download the new package and run install.sh)
export Companion__InstallKind=linux-package
export DOTNET_CLI_TELEMETRY_OPTOUT=1

case "$PV_BIND" in
    127.0.0.1 | localhost | ::1 | "[::1]")
        # Only this computer: refuse requests with a foreign Host header (DNS rebinding protection)
        export AllowedHosts='127.0.0.1;localhost;[::1]'
        ;;
    *)
        pv_say "Hinweis: PlatinVault lauscht auf $PV_BIND und hat keine Anmeldung - jeder in diesem Netz kann es öffnen." \
            "Note: PlatinVault listens on $PV_BIND and has no login - anyone on that network can open it." >&2
        ;;
esac

# Without ICU .NET cannot start; fall back to invariant culture data (dates and numbers then use a neutral format)
if [[ -z "${DOTNET_SYSTEM_GLOBALIZATION_INVARIANT:-}" ]] && ! pv_has_icu; then
    pv_say "Hinweis: Die Bibliothek libicu fehlt - PlatinVault läuft mit neutralen Datums- und Zahlenformaten." \
        "Note: the libicu library is missing - PlatinVault runs with neutral date and number formats." >&2
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
    export DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=0
fi

cd "$here"
exec "$here/platinvault" "$@"
