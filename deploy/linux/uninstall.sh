#!/usr/bin/env bash
# Removes PlatinVault for the current user: stops it, removes the service, the menu entry and the program.
# Your data (database, backups, API keys) stays by default - it can be deleted too, after a question.
#
#   uninstall.sh [--keep-data | --delete-data] [--yes]
#
#   --keep-data    keep the data folder (default; also the default when there is no terminal to ask)
#   --delete-data  delete PlatinVault's data too (the database cannot be restored afterwards!). Only the files and folders PlatinVault
#                  itself writes are deleted (listed before anything happens); everything else in the folder stays, and the folder is
#                  removed only if it is empty afterwards.
#   --yes          do not ask (needed with --delete-data when there is no terminal)
set -u

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "$here/lib.sh" ]]; then lib="$here/lib.sh"; else lib="$here/app/lib.sh"; fi
# shellcheck source=deploy/linux/lib.sh
source "$lib"

# Everything the server writes into its data folder (database with its WAL files, backups, key ring, encrypted API keys, caches)
DATA_ENTRIES=(platinvault.db platinvault.db-wal platinvault.db-shm backups dp-keys secrets cache translations)

data_choice=""
assume_yes=0
while (($# > 0)); do
    case "$1" in
        --keep-data) data_choice=keep ;;
        --delete-data) data_choice=delete ;;
        --yes | -y) assume_yes=1 ;;
        -h | --help)
            pv_say "Aufruf: uninstall.sh [--keep-data | --delete-data] [--yes]" "Usage: uninstall.sh [--keep-data | --delete-data] [--yes]"
            exit 0
            ;;
        *)
            pv_err "Unbekannte Option: $1" "Unknown option: $1"
            exit 2
            ;;
    esac
    shift
done

# Absolute path without symlinks and without ".." or a trailing slash (the folder need not exist)
normalize_path() {
    realpath -m -- "$1" 2>/dev/null || readlink -m -- "$1" 2>/dev/null
}

# The data folder must never be "/", the home folder or one of its parents (compared after normalising both)
data_dir_is_safe() {
    local dir home
    dir="$(normalize_path "$PV_DATA_DIR")"
    home="$(normalize_path "$HOME")"
    [[ -n "$dir" && -n "$home" ]] || return 1
    [[ "$dir" != / && "$dir" != "$home" && "$home" != "$dir"/* ]]
}

# The entries of DATA_ENTRIES that exist in the data folder (one per line)
existing_data_entries() {
    local entry
    for entry in "${DATA_ENTRIES[@]}"; do
        if [[ -e "$PV_DATA_DIR/$entry" || -L "$PV_DATA_DIR/$entry" ]]; then printf '%s\n' "$entry"; fi
    done
}

# Prints what --delete-data would delete, exactly
announce_deletion() {
    local entry
    pv_say "Es werden genau diese Einträge in $PV_DATA_DIR gelöscht (und nichts anderes):" "Exactly these entries in $PV_DATA_DIR will be deleted (and nothing else):"
    while IFS= read -r entry; do
        if [[ -d "$PV_DATA_DIR/$entry" && ! -L "$PV_DATA_DIR/$entry" ]]; then printf '  %s/ (%s)\n' "$entry" "$(pv_say "Ordner mit allem darin" "folder with everything in it")"; else printf '  %s\n' "$entry"; fi
    done < <(existing_data_entries)
    pv_say "Alle anderen Dateien in diesem Ordner bleiben. Der Ordner selbst wird nur entfernt, wenn er danach leer ist." \
        "All other files in that folder stay. The folder itself is removed only if it is empty afterwards."
}

delete_data() {
    local entry
    while IFS= read -r entry; do
        rm -rf -- "${PV_DATA_DIR:?}/$entry"
    done < <(existing_data_entries)
    rm -f -- "$PV_ENV_FILE"
    rmdir "$(dirname "$PV_ENV_FILE")" 2>/dev/null || true
    if rmdir "$PV_DATA_DIR" 2>/dev/null; then
        pv_say "Daten gelöscht, der leere Ordner $PV_DATA_DIR wurde entfernt." "Data deleted, the empty folder $PV_DATA_DIR was removed."
    else
        pv_say "Daten gelöscht. Der Ordner $PV_DATA_DIR bleibt, weil er noch andere Dateien enthält (nicht von PlatinVault)." \
            "Data deleted. The folder $PV_DATA_DIR stays because it still contains other files (not from PlatinVault)."
    fi
}

main() {
    pv_init_paths || return 1
    pv_load_config
    pv_resolve_settings || return 1

    # Ask what to do with the data (default: keep)
    if [[ -z "$data_choice" ]]; then
        data_choice=keep
        if [[ -t 0 && -t 1 ]]; then
            local answer
            pv_say "Deine Daten (Datenbank, Sicherungen, API-Schlüssel) liegen in $PV_DATA_DIR." "Your data (database, backups, API keys) is in $PV_DATA_DIR."
            read -r -p "$(pv_say "Daten behalten? [J/n] " "Keep the data? [Y/n] ")" answer || answer=""
            case "$answer" in
                n | N | nein | Nein | no | No) data_choice=delete ;;
            esac
        fi
    fi
    if [[ "$data_choice" == delete ]]; then
        # Checked before anything is stopped or removed: a refusal changes nothing
        if ! data_dir_is_safe; then
            pv_err "$PV_DATA_DIR ist \"/\", dein Home-Ordner oder einer seiner übergeordneten Ordner - die Daten werden nicht gelöscht. Es wurde nichts verändert." \
                "$PV_DATA_DIR is \"/\", your home folder or one of its parent folders - the data is not deleted. Nothing was changed."
            return 1
        fi
        announce_deletion
        if [[ "$assume_yes" != 1 ]]; then
            if [[ ! -t 0 ]]; then
                pv_err "Zum Löschen der Daten ohne Terminal ist --yes nötig. Es wurde nichts verändert." "Deleting the data without a terminal needs --yes. Nothing was changed."
                return 1
            fi
            local confirm
            read -r -p "$(pv_say "Diese Einträge wirklich löschen? Das lässt sich nicht rückgängig machen. [j/N] " "Really delete these entries? This cannot be undone. [y/N] ")" confirm || confirm=""
            case "$confirm" in
                j | J | ja | Ja | y | Y | yes | Yes) ;;
                *)
                    pv_say "Abgebrochen - es wurde nichts verändert." "Cancelled - nothing was changed."
                    return 1
                    ;;
            esac
        fi
    fi

    # Stop (the server makes its last backup), then remove the service
    pv_say "[1/4] Beende PlatinVault ..." "[1/4] Stopping PlatinVault ..."
    if [[ -x "$PV_APP_DIR/ctl.sh" ]]; then
        if ! "$PV_APP_DIR/ctl.sh" stop; then
            pv_err "PlatinVault ließ sich nicht sauber beenden." "PlatinVault could not be stopped cleanly."
            if [[ "$data_choice" == delete ]]; then
                pv_err "Die Daten werden nicht gelöscht, solange PlatinVault läuft. Es wurde nichts verändert." "The data is not deleted while PlatinVault is running. Nothing was changed."
                return 1
            fi
        fi
    fi
    pv_say "[2/4] Entferne den Dienst und den Menüeintrag ..." "[2/4] Removing the service and the menu entry ..."
    if [[ -f "$PV_UNIT_FILE" ]]; then
        if pv_systemd_usable; then
            systemctl --user disable --now platinvault.service >/dev/null 2>&1 || true
        fi
        rm -f "$PV_UNIT_FILE"
        if pv_systemd_usable; then systemctl --user daemon-reload >/dev/null 2>&1 || true; fi
    fi
    rm -f "$PV_DESKTOP_FILE" "$PV_ICON_FILE"
    if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$(dirname "$PV_DESKTOP_FILE")" >/dev/null 2>&1 || true; fi

    pv_say "[3/4] Entferne das Programm ..." "[3/4] Removing the program ..."
    rm -rf "$PV_APP_DIR" "$PV_ROOT/app.new" "$PV_ROOT/app.old"
    rm -f "$PV_PID_FILE" "$PV_LOG_FILE"

    pv_say "[4/4] Daten ..." "[4/4] Data ..."
    if [[ "$data_choice" == delete ]]; then
        delete_data
    else
        pv_say "Daten behalten: $PV_DATA_DIR (Einstellungen: $PV_ENV_FILE). Eine spätere Installation findet sie wieder." \
            "Data kept: $PV_DATA_DIR (settings: $PV_ENV_FILE). A later installation finds it again."
    fi
    rmdir "$PV_ROOT" 2>/dev/null || true
    pv_say "PlatinVault ist entfernt." "PlatinVault is removed."
}

# This file may delete the folder it lives in (app/uninstall.sh): bash keeps reading it through its open file handle
main
exit $?
