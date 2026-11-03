# shellcheck shell=bash disable=SC2034
# Shared helpers of the PlatinVault Linux package. Sourced by install.sh, uninstall.sh, run.sh and ctl.sh - not meant to be run on its own.
#
# Everything lives in the home folder of the user (no root, works on read-only systems such as SteamOS and Bazzite):
#   program   $XDG_DATA_HOME/platinvault/app          (replaced on every update)
#   data      $XDG_DATA_HOME/platinvault/data         (database, backups, API keys - kept on update and by default on uninstall)
#   settings  $XDG_CONFIG_HOME/platinvault/platinvault.env
# Messages are printed in German when the system language is German, otherwise in English.

PV_DEFAULT_PORT=5233
PV_DEFAULT_BIND=127.0.0.1

case "${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}" in
    de*) PV_LANG=de ;;
    *) PV_LANG=en ;;
esac

# pv_say "<German>" "<English>"
pv_say() {
    if [[ "$PV_LANG" == de ]]; then printf '%s\n' "$1"; else printf '%s\n' "$2"; fi
}

# pv_err "<German>" "<English>" - to stderr
pv_err() {
    if [[ "$PV_LANG" == de ]]; then printf '!! %s\n' "$1" >&2; else printf '!! %s\n' "$2" >&2; fi
}

pv_init_paths() {
    if [[ -z "${HOME:-}" ]]; then
        pv_err "HOME ist nicht gesetzt - PlatinVault installiert in den Home-Ordner des Benutzers." "HOME is not set - PlatinVault installs into the user's home folder."
        return 1
    fi
    PV_DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
    PV_CONFIG_HOME="${XDG_CONFIG_HOME:-$HOME/.config}"
    PV_ROOT="$PV_DATA_HOME/platinvault"
    PV_APP_DIR="$PV_ROOT/app"
    PV_DEFAULT_DATA_DIR="$PV_ROOT/data"
    PV_ENV_FILE="$PV_CONFIG_HOME/platinvault/platinvault.env"
    PV_UNIT_FILE="$PV_CONFIG_HOME/systemd/user/platinvault.service"
    PV_DESKTOP_FILE="$PV_DATA_HOME/applications/platinvault.desktop"
    PV_ICON_FILE="$PV_DATA_HOME/icons/hicolor/512x512/apps/platinvault.png"
    # Only used when the server is started without systemd (ctl.sh start)
    PV_PID_FILE="$PV_ROOT/platinvault.pid"
    PV_LOG_FILE="$PV_ROOT/platinvault.log"
}

# Reads the three known settings from the settings file; a variable that is already set in the environment wins.
# Only plain KEY=VALUE lines are understood (optional quotes around the value, # comments); the file is never executed.
pv_load_config() {
    [[ -f "$PV_ENV_FILE" ]] || return 0
    local line key value
    while IFS= read -r line || [[ -n "$line" ]]; do
        line="${line%$'\r'}"
        [[ "$line" =~ ^[[:space:]]*(PLATINVAULT_PORT|PLATINVAULT_BIND|PLATINVAULT_DATA_DIR)[[:space:]]*=(.*)$ ]] || continue
        key="${BASH_REMATCH[1]}"
        value="${BASH_REMATCH[2]}"
        value="${value#"${value%%[![:space:]]*}"}"
        value="${value%"${value##*[![:space:]]}"}"
        if [[ "$value" =~ ^\"(.*)\"$ || "$value" =~ ^\'(.*)\'$ ]]; then value="${BASH_REMATCH[1]}"; fi
        [[ -n "$value" ]] || continue
        if [[ -z "${!key:-}" ]]; then export "$key=$value"; fi
    done < "$PV_ENV_FILE"
}

# Sets PV_PORT / PV_BIND / PV_DATA_DIR from the environment (after pv_load_config) and validates them. Returns 1 with a message when invalid.
pv_resolve_settings() {
    PV_PORT="${PLATINVAULT_PORT:-$PV_DEFAULT_PORT}"
    PV_BIND="${PLATINVAULT_BIND:-$PV_DEFAULT_BIND}"
    PV_DATA_DIR="${PLATINVAULT_DATA_DIR:-$PV_DEFAULT_DATA_DIR}"
    if ! [[ "$PV_PORT" =~ ^[0-9]+$ ]] || ((PV_PORT < 1024 || PV_PORT > 65535)); then
        pv_err "Ungültiger Port '$PV_PORT' (erlaubt: 1024-65535). Einstellung: PLATINVAULT_PORT in $PV_ENV_FILE" \
            "Invalid port '$PV_PORT' (allowed: 1024-65535). Setting: PLATINVAULT_PORT in $PV_ENV_FILE"
        return 1
    fi
    if ! [[ "$PV_BIND" =~ ^[0-9A-Za-z.:-]+$ ]]; then
        pv_err "Ungültige Adresse '$PV_BIND' (PLATINVAULT_BIND)." "Invalid address '$PV_BIND' (PLATINVAULT_BIND)."
        return 1
    fi
    if [[ "$PV_DATA_DIR" != /* ]]; then
        pv_err "PLATINVAULT_DATA_DIR muss ein absoluter Pfad sein (ist: '$PV_DATA_DIR')." "PLATINVAULT_DATA_DIR must be an absolute path (is: '$PV_DATA_DIR')."
        return 1
    fi
}

# True when something listens on the TCP port (any address)
pv_port_in_use() {
    local port="$1"
    if command -v ss >/dev/null 2>&1; then
        ss -ltn 2>/dev/null | awk -v p="$port" '$4 ~ (":" p "$") { found = 1 } END { exit !found }'
        return
    fi
    if command -v netstat >/dev/null 2>&1; then
        netstat -ltn 2>/dev/null | awk -v p="$port" '$4 ~ (":" p "$") { found = 1 } END { exit !found }'
        return
    fi
    (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null
}

# The address to open in a browser / to probe (a wildcard bind is reached through loopback)
pv_url() {
    local host="$PV_BIND"
    case "$host" in 0.0.0.0 | :: | "[::]") host=127.0.0.1 ;; esac
    [[ "$host" == *:* && "$host" != \[* ]] && host="[$host]"
    printf 'http://%s:%s/' "$host" "$PV_PORT"
}

# True when GET <url> answers 2xx (curl, wget, or plain bash as the last resort)
pv_http_ok() {
    local url="$1" hostport host port
    if command -v curl >/dev/null 2>&1; then
        curl -fsS -m 3 -o /dev/null "$url" 2>/dev/null
        return
    fi
    if command -v wget >/dev/null 2>&1; then
        wget -q -T 3 -O /dev/null "$url" 2>/dev/null
        return
    fi
    hostport="${url#http://}"
    hostport="${hostport%%/*}"
    host="${hostport%:*}"
    port="${hostport##*:}"
    host="${host#[}"
    host="${host%]}"
    (
        exec 3<>"/dev/tcp/$host/$port" || exit 1
        printf 'GET %s HTTP/1.0\r\nHost: %s\r\n\r\n' "/${url#http://*/}" "$hostport" >&3
        head -n 1 <&3 | grep -q ' 2[0-9][0-9] '
    ) 2>/dev/null
}

# Waits until the server answers /health (seconds as $1, default 60). Returns 1 on timeout.
pv_wait_ready() {
    local limit="${1:-60}" i=0
    while ((i < limit)); do
        if pv_http_ok "$(pv_url)health"; then return 0; fi
        sleep 1
        i=$((i + 1))
    done
    return 1
}

# True when the ICU library .NET needs for culture data is installed
pv_has_icu() {
    local ld
    ld="$(command -v ldconfig 2>/dev/null || true)"
    [[ -n "$ld" ]] || ld=/sbin/ldconfig
    if [[ -x "$ld" ]] && "$ld" -p 2>/dev/null | grep -q 'libicuuc\.so'; then return 0; fi
    compgen -G '/usr/lib*/libicuuc.so*' >/dev/null && return 0
    compgen -G '/usr/lib/*/libicuuc.so*' >/dev/null && return 0
    compgen -G '/lib/*/libicuuc.so*' >/dev/null && return 0
    return 1
}

# True when a user systemd manager can be reached
pv_systemd_usable() {
    command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1
}
