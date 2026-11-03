#!/usr/bin/env bash
# Controls the installed PlatinVault of the Linux package.
#
#   ctl.sh start     start the server (systemd user service when installed with it, otherwise a background process)
#   ctl.sh stop      stop the server; it makes its last database backup first
#   ctl.sh restart
#   ctl.sh status    is it running, and where (exit code 0 = running)
#   ctl.sh url       print the address to open in the browser
#   ctl.sh open      start the server if needed, wait until it is ready and open the browser (the desktop entry runs this)
set -u

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/linux/lib.sh
source "$here/lib.sh"
pv_init_paths || exit 1
pv_load_config
pv_resolve_settings || exit 1

# Stopping may take a while: the server makes its last backup
STOP_TIMEOUT=45

uses_systemd() { [[ -f "$PV_UNIT_FILE" ]] && pv_systemd_usable; }

# True while the process exists and is not a zombie (a finished process that nobody has collected yet counts as gone)
pid_alive() {
    local state
    [[ -r "/proc/$1/stat" ]] || return 1
    state="$(sed 's/.*) //' "/proc/$1/stat" 2>/dev/null | cut -c1)"
    [[ -n "$state" && "$state" != Z && "$state" != X ]]
}

# True when the process is exactly this installation's program (the executable path of the process, not a name that merely contains
# "platinvault" - the bridge or another copy must not match, and a reused PID of any other program neither)
pid_is_our_program() {
    local pid="$1" expected exe
    expected="$(readlink -f "$here/platinvault" 2>/dev/null)" || return 1
    [[ -n "$expected" ]] || return 1
    exe="$(readlink "/proc/$pid/exe" 2>/dev/null)" || return 1
    exe="${exe% (deleted)}"
    [[ "$exe" == "$expected" ]]
}

# PID of the background process started without systemd, empty when none is running.
background_pid() {
    local pid
    [[ -f "$PV_PID_FILE" ]] || return 0
    pid="$(tr -d '[:space:]' < "$PV_PID_FILE")"
    [[ "$pid" =~ ^[0-9]+$ ]] || return 0
    if pid_alive "$pid" && pid_is_our_program "$pid"; then
        printf '%s' "$pid"
    fi
}

is_running() {
    if uses_systemd; then
        systemctl --user is-active --quiet platinvault
    else
        [[ -n "$(background_pid)" ]]
    fi
}

do_start() {
    if is_running; then
        pv_say "PlatinVault läuft bereits." "PlatinVault is already running."
        return 0
    fi
    if uses_systemd; then
        systemctl --user start platinvault
        return
    fi
    if [[ ! -x "$here/run.sh" ]]; then
        pv_err "run.sh fehlt in $here" "run.sh is missing in $here"
        return 1
    fi
    mkdir -p "$PV_ROOT"
    # Keep the log small: it is only written without systemd
    if [[ -f "$PV_LOG_FILE" ]] && (($(wc -c < "$PV_LOG_FILE") > 5242880)); then : > "$PV_LOG_FILE"; fi
    nohup "$here/run.sh" >> "$PV_LOG_FILE" 2>&1 &
    printf '%s\n' "$!" > "$PV_PID_FILE"
    disown 2>/dev/null || true
}

do_stop() {
    if uses_systemd; then
        systemctl --user stop platinvault
        return
    fi
    local pid i=0
    pid="$(background_pid)"
    if [[ -z "$pid" ]]; then
        rm -f "$PV_PID_FILE"
        return 0
    fi
    kill -TERM "$pid" 2>/dev/null || true
    while pid_alive "$pid"; do
        if ((i >= STOP_TIMEOUT)); then
            pv_err "PlatinVault (Prozess $pid) hat sich nicht innerhalb von ${STOP_TIMEOUT} s beendet." \
                "PlatinVault (process $pid) did not exit within ${STOP_TIMEOUT} s."
            return 1
        fi
        sleep 1
        i=$((i + 1))
    done
    rm -f "$PV_PID_FILE"
}

open_browser() {
    local url="$1"
    if command -v xdg-open >/dev/null 2>&1; then
        xdg-open "$url" >/dev/null 2>&1 && return 0
    fi
    if command -v gio >/dev/null 2>&1; then
        gio open "$url" >/dev/null 2>&1 && return 0
    fi
    return 1
}

case "${1:-}" in
    start)
        do_start
        ;;
    stop)
        do_stop
        ;;
    restart)
        do_stop && do_start
        ;;
    status)
        if is_running; then
            pv_say "PlatinVault läuft: $(pv_url)" "PlatinVault is running: $(pv_url)"
        else
            pv_say "PlatinVault läuft nicht." "PlatinVault is not running."
            exit 3
        fi
        ;;
    url)
        pv_url
        printf '\n'
        ;;
    open)
        do_start || exit 1
        if ! pv_wait_ready 60; then
            pv_err "PlatinVault ist nach 60 s nicht bereit. Details: journalctl --user -u platinvault (oder die Datei $PV_LOG_FILE)." \
                "PlatinVault is not ready after 60 s. Details: journalctl --user -u platinvault (or the file $PV_LOG_FILE)."
            exit 1
        fi
        if ! open_browser "$(pv_url)"; then
            pv_say "Kein Browser gefunden. Öffne diese Adresse selbst: $(pv_url)" "No browser found. Open this address yourself: $(pv_url)"
        fi
        ;;
    *)
        echo "usage: ctl.sh start|stop|restart|status|url|open" >&2
        exit 2
        ;;
esac
