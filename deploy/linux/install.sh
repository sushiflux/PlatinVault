#!/usr/bin/env bash
# Installs (or updates) PlatinVault for the current user - no root needed, nothing outside the home folder.
#
#   ./install.sh [--port N] [--no-systemd] [--no-start] [--help]
#
#   program   ~/.local/share/platinvault/app
#   data      ~/.local/share/platinvault/data   (kept on updates)
#   settings  ~/.config/platinvault/platinvault.env
#   service   ~/.config/systemd/user/platinvault.service  (starts PlatinVault at login)
#   menu      ~/.local/share/applications/platinvault.desktop
#
# Update: run the install.sh of the new package - the old version is stopped (it makes a last backup), the program is replaced, the data
# stays. PlatinVault listens on 127.0.0.1 (this computer only), default port 5233; --port or PLATINVAULT_PORT changes it. If the
# port is taken the installation stops with a message - it never picks another port by itself.
#   --no-systemd  install without a systemd user service (the menu entry then starts PlatinVault itself)
#   --no-start    install but do not start
set -u

pkg="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/linux/lib.sh
source "$pkg/app/lib.sh"

want_port=""
use_systemd=1
start_after=1

usage() {
    pv_say "Aufruf: ./install.sh [--port N] [--no-systemd] [--no-start]" "Usage: ./install.sh [--port N] [--no-systemd] [--no-start]"
}

while (($# > 0)); do
    case "$1" in
        --port)
            want_port="${2:-}"
            shift 2 || { usage; exit 2; }
            ;;
        --port=*)
            want_port="${1#--port=}"
            shift
            ;;
        --no-systemd)
            use_systemd=0
            shift
            ;;
        --no-start)
            start_after=0
            shift
            ;;
        -h | --help)
            usage
            exit 0
            ;;
        *)
            pv_err "Unbekannte Option: $1" "Unknown option: $1"
            usage
            exit 2
            ;;
    esac
done

fail() { pv_err "$1" "$2"; exit 1; }

# Creates the settings file with an explanation of every setting when it does not exist yet (an existing file is never touched)
ensure_config_file() {
    [[ -f "$PV_ENV_FILE" ]] && return 0
    mkdir -p "$(dirname "$PV_ENV_FILE")" || return 1
    cat > "$PV_ENV_FILE" <<'EOF'
# PlatinVault settings (one KEY=VALUE per line, applied the next time PlatinVault starts).
# After a change:  ~/.local/share/platinvault/app/ctl.sh restart
#
# Port PlatinVault listens on (default 5233). Open http://127.0.0.1:<port>/ in the browser.
#PLATINVAULT_PORT=5233
#
# Address to listen on. The default 127.0.0.1 means: only this computer. Another address (for example 0.0.0.0 to reach PlatinVault
# from a phone in the same network) is possible, but PlatinVault has no login - everyone in that network could open it.
#PLATINVAULT_BIND=127.0.0.1
#
# Folder for the database, the automatic backups and the encrypted API keys (default ~/.local/share/platinvault/data).
# It must be on a local disk of this computer (not a network share).
#PLATINVAULT_DATA_DIR=/home/you/.local/share/platinvault/data
EOF
}

# Writes KEY=VALUE into the settings file (replaces the commented or active line of the key, or appends it)
set_config() {
    local key="$1" value="$2" tmp status=0
    ensure_config_file || return 1
    if grep -Eq "^[[:space:]]*#?[[:space:]]*${key}=" "$PV_ENV_FILE"; then
        tmp="$(mktemp)" || return 1
        # Replace the first (commented or active) line of the key, keep the rest
        awk -v k="$key" -v v="$value" '
            !done && $0 ~ "^[[:space:]]*#?[[:space:]]*" k "=" { print k "=" v; done = 1; next }
            { print }
        ' "$PV_ENV_FILE" > "$tmp" && cat "$tmp" > "$PV_ENV_FILE" || status=1
        rm -f "$tmp"
        return "$status"
    fi
    printf '%s=%s\n' "$key" "$value" >> "$PV_ENV_FILE"
}

# ---- checks ------------------------------------------------------------------------------------------------------------------------
pv_init_paths || exit 1
[[ "$(uname -s)" == Linux ]] || fail "Dieses Paket ist nur für Linux." "This package is for Linux only."
case "$(uname -m)" in
    x86_64 | amd64) ;;
    *) fail "Dieses Paket ist für x86_64 (64-Bit-PC, Steam Deck). Dieser Rechner meldet: $(uname -m)." "This package is for x86_64 (64-bit PC, Steam Deck). This machine reports: $(uname -m)." ;;
esac
[[ -x "$pkg/app/platinvault" ]] || fail "Das Programm fehlt im Paket ($pkg/app/platinvault) - das Paket wurde unvollständig entpackt." "The program is missing from the package ($pkg/app/platinvault) - the package was not extracted completely."
if [[ "$(id -u)" == 0 ]]; then
    fail "Bitte nicht als root installieren: PlatinVault gehört in den Home-Ordner des Benutzers, der Steam spielt." "Please do not install as root: PlatinVault belongs in the home folder of the user who plays Steam."
fi

# The settings (port, data folder) of an earlier installation apply to the new one as well
if [[ -n "$want_port" ]]; then export PLATINVAULT_PORT="$want_port"; fi
pv_load_config
pv_resolve_settings || exit 1

new_version="$(sed -n '1p' "$pkg/app/version.txt" 2>/dev/null | tr -d '[:space:]')"
old_version=""
[[ -f "$PV_APP_DIR/version.txt" ]] && old_version="$(sed -n '1p' "$PV_APP_DIR/version.txt" | tr -d '[:space:]')"

if [[ -n "$old_version" ]]; then
    pv_say "PlatinVault $old_version ist installiert - wird auf ${new_version:-diese Version} aktualisiert. Deine Daten bleiben erhalten." \
        "PlatinVault $old_version is installed - updating to ${new_version:-this version}. Your data is kept."
else
    pv_say "Installiere PlatinVault ${new_version:-} für $(id -un) ..." "Installing PlatinVault ${new_version:-} for $(id -un) ..."
fi

# ---- 1. stop the old version (it makes a last backup) --------------------------------------------------------------------------------
was_running=0
if [[ -x "$PV_APP_DIR/ctl.sh" ]]; then
    if "$PV_APP_DIR/ctl.sh" status >/dev/null 2>&1; then was_running=1; fi
    pv_say "[1/5] Beende die laufende Version ..." "[1/5] Stopping the running version ..."
    "$PV_APP_DIR/ctl.sh" stop || fail "Die laufende Version ließ sich nicht beenden. Es wurde nichts verändert." "The running version could not be stopped. Nothing was changed."
else
    pv_say "[1/5] Keine laufende Version." "[1/5] No running version."
fi

# ---- 2. the port must be free - never switch silently ---------------------------------------------------------------------------------------
if pv_port_in_use "$PV_PORT"; then
    if ((was_running)); then "$PV_APP_DIR/ctl.sh" start >/dev/null 2>&1 || true; fi
    fail "Der Port $PV_PORT ist bereits belegt (ein anderes Programm lauscht dort). Es wurde nichts verändert. Wähle einen freien Port: ./install.sh --port <Zahl> (1024-65535) - er wird in $PV_ENV_FILE gespeichert." \
        "Port $PV_PORT is already in use (another program is listening there). Nothing was changed. Choose a free port: ./install.sh --port <number> (1024-65535) - it is saved in $PV_ENV_FILE."
fi

# ---- 3. program ---------------------------------------------------------------------------------------------------------------------------
# Every step is checked. A failure puts the previous installation back (and starts it again when it was running) and stops with a message.
swapped=0
had_old=0

restart_old_if_it_was_running() {
    if ((was_running && had_old)) && [[ -x "$PV_APP_DIR/ctl.sh" ]]; then "$PV_APP_DIR/ctl.sh" start >/dev/null 2>&1 || true; fi
}

# Undo: remove the half-installed program, restore the previous one
rollback_program() {
    rm -rf "$PV_ROOT/app.new"
    if ((swapped)); then
        if [[ -x "$PV_APP_DIR/ctl.sh" ]]; then "$PV_APP_DIR/ctl.sh" stop >/dev/null 2>&1 || true; fi
        rm -rf "$PV_APP_DIR"
    fi
    if ((had_old)) && [[ ! -e "$PV_APP_DIR" ]] && [[ -d "$PV_ROOT/app.old" ]]; then mv "$PV_ROOT/app.old" "$PV_APP_DIR"; fi
    restart_old_if_it_was_running
}

abort_install() {
    rollback_program
    pv_err "$1" "$2"
    pv_say "Die vorherige Installation wurde wiederhergestellt (falls es eine gab)." "The previous installation was restored (if there was one)."
    exit 1
}

pv_say "[2/5] Kopiere das Programm nach $PV_APP_DIR ..." "[2/5] Copying the program to $PV_APP_DIR ..."
mkdir -p "$PV_ROOT" || fail "Der Ordner $PV_ROOT lässt sich nicht anlegen." "The folder $PV_ROOT cannot be created."
rm -rf "$PV_ROOT/app.new" "$PV_ROOT/app.old"
if [[ -e "$PV_ROOT/app.new" || -e "$PV_ROOT/app.old" ]]; then
    restart_old_if_it_was_running
    fail "Reste einer früheren Installation in $PV_ROOT (app.new/app.old) ließen sich nicht entfernen. Es wurde nichts verändert." \
        "Leftovers of an earlier installation in $PV_ROOT (app.new/app.old) could not be removed. Nothing was changed."
fi
if ! cp -a "$pkg/app" "$PV_ROOT/app.new" || ! cp -p "$pkg/uninstall.sh" "$PV_ROOT/app.new/uninstall.sh"; then
    abort_install "Das Kopieren ist fehlgeschlagen (Platz auf der Platte?). Die vorhandene Installation wurde nicht verändert." \
        "Copying failed (disk space?). The existing installation was not changed."
fi
if [[ -e "$PV_APP_DIR" || -L "$PV_APP_DIR" ]]; then
    mv -T -- "$PV_APP_DIR" "$PV_ROOT/app.old" || abort_install "Die vorhandene Installation ($PV_APP_DIR) ließ sich nicht zur Seite verschieben. Es wurde nichts verändert." \
        "The existing installation ($PV_APP_DIR) could not be moved aside. Nothing was changed."
    had_old=1
fi
if [[ -e "$PV_APP_DIR" ]]; then
    abort_install "$PV_APP_DIR existiert nach dem Verschieben noch. Es wurde nichts verändert." "$PV_APP_DIR still exists after moving it aside. Nothing was changed."
fi
mv -T -- "$PV_ROOT/app.new" "$PV_APP_DIR" || abort_install "Das Programm ließ sich nicht an seinen Platz verschieben ($PV_APP_DIR)." "The program could not be moved into place ($PV_APP_DIR)."
swapped=1
[[ -x "$PV_APP_DIR/platinvault" && -x "$PV_APP_DIR/ctl.sh" ]] || abort_install "Das installierte Programm ist unvollständig ($PV_APP_DIR)." "The installed program is incomplete ($PV_APP_DIR)."

ensure_config_file || abort_install "Die Einstellungsdatei $PV_ENV_FILE ließ sich nicht anlegen." "The settings file $PV_ENV_FILE could not be created."
if [[ -n "$want_port" ]]; then
    set_config PLATINVAULT_PORT "$PV_PORT" || abort_install "Der Port ließ sich nicht in $PV_ENV_FILE speichern." "The port could not be saved in $PV_ENV_FILE."
fi

# ---- 4. start at login (systemd user service) and menu entry --------------------------------------------------------------------------------
service_installed=0
if ((use_systemd)); then
    if pv_systemd_usable; then
        pv_say "[3/5] Richte den Dienst ein (startet PlatinVault bei der Anmeldung) ..." "[3/5] Setting up the service (starts PlatinVault at login) ..."
        run_path="${PV_APP_DIR//%/%%}"
        mkdir -p "$(dirname "$PV_UNIT_FILE")" || abort_install "Der Ordner für die Dienstdatei ließ sich nicht anlegen." "The folder for the service file could not be created."
        cat > "$PV_UNIT_FILE" <<EOF
[Unit]
Description=PlatinVault - live Steam achievements and guides

[Service]
Type=simple
ExecStart="$run_path/run.sh"
Restart=on-failure
RestartSec=5
# Exit code 3: the database belongs to a newer PlatinVault or is damaged - starting again would not help
RestartPreventExitStatus=3
# The server makes its last database backup when it stops
TimeoutStopSec=60
NoNewPrivileges=true

[Install]
WantedBy=default.target
EOF
        [[ -s "$PV_UNIT_FILE" ]] || abort_install "Die Dienstdatei $PV_UNIT_FILE ließ sich nicht schreiben." "The service file $PV_UNIT_FILE could not be written."
        systemctl --user daemon-reload || abort_install "systemctl --user daemon-reload ist fehlgeschlagen." "systemctl --user daemon-reload failed."
        systemctl --user enable platinvault.service >/dev/null 2>&1 || pv_err "Der Dienst ließ sich nicht aktivieren (Autostart bei der Anmeldung fehlt)." "The service could not be enabled (no start at login)."
        service_installed=1
    else
        pv_say "[3/5] systemd (Benutzerdienst) ist hier nicht erreichbar - installiere ohne Dienst. Das Menü-Symbol startet PlatinVault selbst." \
            "[3/5] systemd (user service) is not reachable here - installing without a service. The menu entry starts PlatinVault itself."
        rm -f "$PV_UNIT_FILE"
    fi
else
    pv_say "[3/5] Ohne systemd-Dienst (--no-systemd). Das Menü-Symbol startet PlatinVault selbst." "[3/5] Without a systemd service (--no-systemd). The menu entry starts PlatinVault itself."
    if [[ -f "$PV_UNIT_FILE" ]] && pv_systemd_usable; then systemctl --user disable platinvault.service >/dev/null 2>&1 || true; fi
    rm -f "$PV_UNIT_FILE"
fi

pv_say "[4/5] Lege den Menüeintrag an ..." "[4/5] Creating the menu entry ..."
mkdir -p "$(dirname "$PV_DESKTOP_FILE")" "$(dirname "$PV_ICON_FILE")" || abort_install "Die Ordner für den Menüeintrag ließen sich nicht anlegen." "The folders for the menu entry could not be created."
icon_value="platinvault"
if [[ -f "$PV_APP_DIR/platinvault.png" ]]; then
    cp -f "$PV_APP_DIR/platinvault.png" "$PV_ICON_FILE" || abort_install "Das Symbol ließ sich nicht nach $PV_ICON_FILE kopieren." "The icon could not be copied to $PV_ICON_FILE."
    icon_value="$PV_ICON_FILE"
fi
cat > "$PV_DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=PlatinVault
Comment=Live Steam achievements and 100% guides
Comment[de]=Steam-Trophäen live und 100%-Guides
Exec="${PV_APP_DIR//%/%%}/ctl.sh" open
Icon=$icon_value
Terminal=false
Categories=Game;Utility;
StartupNotify=false
EOF
[[ -s "$PV_DESKTOP_FILE" ]] || abort_install "Der Menüeintrag $PV_DESKTOP_FILE ließ sich nicht schreiben." "The menu entry $PV_DESKTOP_FILE could not be written."
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$(dirname "$PV_DESKTOP_FILE")" >/dev/null 2>&1 || true; fi

# ---- 5. start ---------------------------------------------------------------------------------------------------------------------------
if ((start_after)); then
    pv_say "[5/5] Starte PlatinVault ..." "[5/5] Starting PlatinVault ..."
    "$PV_APP_DIR/ctl.sh" start || abort_install "PlatinVault ließ sich nicht starten. Details: journalctl --user -u platinvault" "PlatinVault could not be started. Details: journalctl --user -u platinvault"
    if pv_wait_ready 90; then
        rm -rf "$PV_ROOT/app.old"
        pv_say "PlatinVault läuft: $(pv_url)" "PlatinVault is running: $(pv_url)"
    else
        pv_err "PlatinVault antwortet nach 90 s nicht auf $(pv_url). Details: journalctl --user -u platinvault (ohne systemd: $PV_LOG_FILE). Die vorige Version liegt noch in $PV_ROOT/app.old." \
            "PlatinVault does not answer on $(pv_url) after 90 s. Details: journalctl --user -u platinvault (without systemd: $PV_LOG_FILE). The previous version is still in $PV_ROOT/app.old."
        exit 1
    fi
else
    rm -rf "$PV_ROOT/app.old"
    pv_say "[5/5] Nicht gestartet (--no-start). Starten: $PV_APP_DIR/ctl.sh start" "[5/5] Not started (--no-start). Start it with: $PV_APP_DIR/ctl.sh start"
fi

echo
pv_say "Fertig. Öffne PlatinVault im Browser über das Menü-Symbol \"PlatinVault\" oder die Adresse $(pv_url)" \
    "Done. Open PlatinVault in the browser through the \"PlatinVault\" menu entry or the address $(pv_url)"
pv_say "Daten:        $PV_DATA_DIR" "Data:         $PV_DATA_DIR"
pv_say "Einstellungen: $PV_ENV_FILE (Port, Adresse, Datenordner)" "Settings:     $PV_ENV_FILE (port, address, data folder)"
pv_say "Update:       neues Paket entpacken und dessen install.sh ausführen. Entfernen: $PV_APP_DIR/uninstall.sh" \
    "Update:       extract the new package and run its install.sh. Remove: $PV_APP_DIR/uninstall.sh"
if ((service_installed)); then
    pv_say "Hinweis: Der Dienst läuft in der Desktop-Sitzung. Ob systemd --user im Spielmodus des Steam Decks läuft, ist nicht geprüft." \
        "Note: the service runs in the desktop session. Whether systemd --user runs in the Steam Deck's Game Mode has not been verified."
fi
