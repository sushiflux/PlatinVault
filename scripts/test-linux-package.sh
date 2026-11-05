#!/usr/bin/env bash
# Smoke test of a built Linux package (scripts/build-linux.sh). Runs in CI (job "linux") and in a clean container:
# shellcheck disable=SC2015,SC2329,SC1091
#
#   scripts/test-linux-package.sh artifacts/linux/PlatinVault-linux-x64-<tag>.tar.gz [port]
#
# 1. extracts the package, starts the program directly (temporary data folder, no systemd), checks /health, /api/version, the web UI,
#    the handbook and the licence documents, stops it with SIGTERM and checks for a clean exit and the backup on stop;
# 2. runs install.sh --no-systemd in a temporary home folder: install, start, update over it (data kept), port taken (must fail and
#    leave everything as it was), uninstall (data kept / deleted).
# Nothing outside the temporary folder is touched; the port (default 5290) is only used on 127.0.0.1.
set -uo pipefail

package="${1:?usage: test-linux-package.sh <package.tar.gz> [port]}"
port="${2:-5290}"
port_busy=$((port + 1))
[[ -f "$package" ]] || { echo "Paket nicht gefunden: $package" >&2; exit 2; }

tmp="$(mktemp -d)"
failures=0
server_pid=""
occupier_pid=""
cleanup() {
    [[ -n "$server_pid" ]] && kill -TERM "$server_pid" 2>/dev/null
    [[ -n "$occupier_pid" ]] && kill -TERM "$occupier_pid" 2>/dev/null
    if [[ -x "$tmp/home/.local/share/platinvault/app/ctl.sh" ]]; then
        HOME="$tmp/home" "$tmp/home/.local/share/platinvault/app/ctl.sh" stop >/dev/null 2>&1
    fi
    rm -rf "$tmp"
}
trap cleanup EXIT

ok() { printf '  ok    %s\n' "$*"; }
bad() { printf '  FAIL  %s\n' "$*"; failures=$((failures + 1)); }
check() { # check "<description>" <command...>
    local what="$1"
    shift
    if "$@" >/dev/null 2>&1; then ok "$what"; else bad "$what"; fi
}
http_code() { curl -s -o /dev/null -w '%{http_code}' -m 10 "$1"; }
wait_ready() { # wait_ready <port> <seconds>
    local i=0
    while ((i < $2)); do
        [[ "$(http_code "http://127.0.0.1:$1/health")" == 200 ]] && return 0
        sleep 1
        i=$((i + 1))
    done
    return 1
}

echo "== Paket entpacken"
mkdir -p "$tmp/pkg"
tar -xzf "$package" -C "$tmp/pkg" || { echo "Entpacken fehlgeschlagen"; exit 1; }
root="$(find "$tmp/pkg" -mindepth 1 -maxdepth 1 -type d | head -n1)"
[[ -x "$root/install.sh" && -x "$root/app/platinvault" ]] && ok "install.sh und Programm vorhanden" || { bad "Paketaufbau"; exit 1; }
for f in app/version.txt app/release-channel.txt app/wwwroot/index.html app/docs/LICENSE app/docs/THIRD-PARTY-NOTICES.md app/docs/CHANGELOG.md \
    app/docs/handbuch/de app/docs/handbuch/en app/lib.sh app/run.sh app/ctl.sh uninstall.sh README.txt; do
    check "Datei $f" test -e "$root/$f"
done
check "version.txt hat zwei Zeilen" test "$(grep -c . "$root/app/version.txt")" = 2
check "keine appsettings.Development.json" test ! -e "$root/app/appsettings.Development.json"

# Without ICU .NET needs invariant culture data (run.sh does the same for the installed program)
source "$root/app/lib.sh"
icu_env=()
if ! pv_has_icu; then icu_env=(DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=0); fi

echo "== Programm direkt starten (ohne systemd)"
(
    cd "$root/app" || exit 1
    exec env "${icu_env[@]}" ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://127.0.0.1:$port" Companion__DataDir="$tmp/data1" \
        Companion__ReleaseChannel="$(cat release-channel.txt)" Companion__InstallKind=linux-package ./platinvault > "$tmp/direct.log" 2>&1
) &
server_pid=$!
if wait_ready "$port" 90; then ok "/health antwortet 200"; else bad "/health antwortet nicht"; cat "$tmp/direct.log"; fi
version_json="$(curl -s -m 10 "http://127.0.0.1:$port/api/version")"
echo "     /api/version -> $version_json"
check "/api/version nennt die Version" grep -q "\"version\":\"$(sed -n '1p' "$root/app/version.txt")" <<< "$version_json"
echo "     /api/system/status -> HTTP $(http_code "http://127.0.0.1:$port/api/system/status")"
[[ "$(http_code "http://127.0.0.1:$port/api/system/status")" == 200 ]] && ok "/api/system/status 200" || bad "/api/system/status"
check "Weboberflaeche (/)" bash -c "curl -s -m 10 http://127.0.0.1:$port/ | grep -q '<div id=\"root\"'"
[[ "$(http_code "http://127.0.0.1:$port/api/docs/handbook?lang=de")" == 200 ]] && ok "Handbuch (de) wird ausgeliefert" || bad "Handbuch (de)"
[[ "$(http_code "http://127.0.0.1:$port/api/docs/handbook?lang=en")" == 200 ]] && ok "Handbuch (en) wird ausgeliefert" || bad "Handbuch (en)"
[[ "$(http_code "http://127.0.0.1:$port/api/docs/licenses?file=license")" == 200 ]] && ok "Lizenz wird ausgeliefert" || bad "Lizenz"
[[ "$(http_code "http://127.0.0.1:$port/api/docs/licenses?file=notices")" == 200 ]] && ok "Drittanbieter-Hinweise werden ausgeliefert" || bad "Drittanbieter-Hinweise"
status_json="$(curl -s -m 15 "http://127.0.0.1:$port/api/updates/status")"
check "Update-Status meldet Host linuxPackage" grep -q '"host":"linuxPackage"' <<< "$status_json"
check "Datenbank in der Datenmappe" test -f "$tmp/data1/platinvault.db"
check "Schluesselordner nur fuer den Benutzer (0700)" test "$(stat -c %a "$tmp/data1/dp-keys")" = 700

# Wait for the backup at start (+5 s) so that the stop-time backup has something to compare with
sleep 8
lines_before="$(wc -l < "$tmp/direct.log")"
kill -TERM "$server_pid"
for _ in $(seq 1 60); do kill -0 "$server_pid" 2>/dev/null || break; sleep 1; done
if kill -0 "$server_pid" 2>/dev/null; then bad "Programm beendet sich nicht nach SIGTERM"; kill -KILL "$server_pid"; else ok "Programm beendet sich nach SIGTERM"; fi
wait "$server_pid" 2>/dev/null
exit_code=$?
server_pid=""
[[ "$exit_code" == 0 ]] && ok "Exit-Code 0" || bad "Exit-Code $exit_code"
tail -n +"$((lines_before + 1))" "$tmp/direct.log" | grep -q 'Sicherung (' && ok "Sicherung beim Beenden im Protokoll" || { bad "keine Sicherungszeile nach SIGTERM"; tail -n 8 "$tmp/direct.log"; }

echo "== install.sh --no-systemd (temporaerer Home-Ordner)"
export HOME="$tmp/home"
mkdir -p "$HOME"
unset XDG_DATA_HOME XDG_CONFIG_HOME PLATINVAULT_PORT PLATINVAULT_BIND PLATINVAULT_DATA_DIR
app_dir="$HOME/.local/share/platinvault/app"
if "$root/install.sh" --no-systemd --port "$port" > "$tmp/install1.log" 2>&1; then ok "Installation"; else bad "Installation"; cat "$tmp/install1.log"; fi
check "Programm installiert" test -x "$app_dir/platinvault"
check "uninstall.sh installiert" test -x "$app_dir/uninstall.sh"
check "Menueeintrag" test -f "$HOME/.local/share/applications/platinvault.desktop"
check "Menueeintrag zeigt auf ctl.sh open" grep -q "ctl.sh\" open" "$HOME/.local/share/applications/platinvault.desktop"
check "Einstellungsdatei mit Port" grep -q "^PLATINVAULT_PORT=$port" "$HOME/.config/platinvault/platinvault.env"
check "kein systemd-Dienst bei --no-systemd" test ! -e "$HOME/.config/systemd/user/platinvault.service"
check "PlatinVault laeuft nach der Installation" "$app_dir/ctl.sh" status
[[ "$(http_code "http://127.0.0.1:$port/health")" == 200 ]] && ok "/health auf dem installierten Port" || bad "/health nach der Installation"
check "Daten im Datenordner des Benutzers" test -f "$HOME/.local/share/platinvault/data/platinvault.db"
printf 'marker' > "$HOME/.local/share/platinvault/data/keep-me.txt"

echo "== Update ueber die Installation (Daten bleiben)"
if "$root/install.sh" --no-systemd > "$tmp/install2.log" 2>&1; then ok "Update"; else bad "Update"; cat "$tmp/install2.log"; fi
check "Port aus der Einstellungsdatei uebernommen" grep -q ":$port/" "$tmp/install2.log"
check "Daten nach dem Update vorhanden" test -f "$HOME/.local/share/platinvault/data/keep-me.txt"
check "app.old aufgeraeumt" test ! -e "$HOME/.local/share/platinvault/app.old"
check "laeuft nach dem Update" "$app_dir/ctl.sh" status

echo "== Port belegt: Installation muss abbrechen"
"$app_dir/ctl.sh" stop >/dev/null 2>&1
(
    cd "$root/app" || exit 1
    exec env "${icu_env[@]}" ASPNETCORE_URLS="http://127.0.0.1:$port_busy" Companion__DataDir="$tmp/occupier" ./platinvault > "$tmp/occupier.log" 2>&1
) &
occupier_pid=$!
wait_ready "$port_busy" 90 && ok "Port $port_busy ist belegt (Testprogramm)" || bad "Testprogramm startet nicht"
if "$root/install.sh" --no-systemd --no-start --port "$port_busy" > "$tmp/install3.log" 2>&1; then bad "Installation trotz belegtem Port"; else ok "Installation bricht bei belegtem Port ab"; fi
check "Fehlermeldung nennt den Port" grep -q "$port_busy" "$tmp/install3.log"
check "vorhandene Installation unveraendert" test -x "$app_dir/platinvault"
kill -TERM "$occupier_pid" 2>/dev/null
wait "$occupier_pid" 2>/dev/null
occupier_pid=""

echo "== uninstall.sh"
if "$app_dir/uninstall.sh" --keep-data > "$tmp/uninstall1.log" 2>&1; then ok "Deinstallation (Daten behalten)"; else bad "Deinstallation"; cat "$tmp/uninstall1.log"; fi
check "Programm entfernt" test ! -e "$app_dir"
check "Menueeintrag entfernt" test ! -e "$HOME/.local/share/applications/platinvault.desktop"
check "Daten behalten" test -f "$HOME/.local/share/platinvault/data/keep-me.txt"
# Install again (finds the data), then remove everything
"$root/install.sh" --no-systemd --no-start --port "$port" > "$tmp/install4.log" 2>&1 && ok "Neuinstallation findet die Daten" || bad "Neuinstallation"
check "Daten nach Neuinstallation noch da" test -f "$HOME/.local/share/platinvault/data/keep-me.txt"
if "$app_dir/uninstall.sh" --delete-data --yes > "$tmp/uninstall2.log" 2>&1; then ok "Deinstallation (Daten loeschen)"; else bad "Deinstallation mit Datenloeschen"; cat "$tmp/uninstall2.log"; fi
check "PlatinVaults Daten geloescht (Datenbank)" test ! -e "$HOME/.local/share/platinvault/data/platinvault.db"
check "die eigene Marker-Datei im Datenordner bleibt (nicht von PlatinVault)" test -f "$HOME/.local/share/platinvault/data/keep-me.txt"
rm -rf "$HOME/.local/share/platinvault"

echo "== Datenordner mit fremden Dateien: nur PlatinVaults eigene Eintraege werden geloescht"
foreign="$tmp/foreign-games"
mkdir -p "$foreign/sub" "$HOME/.config/platinvault"
printf 'mine' > "$foreign/foreign.txt"
printf 'mine' > "$foreign/sub/keep.txt"
printf 'PLATINVAULT_DATA_DIR=%s\nPLATINVAULT_PORT=%s\n' "$foreign" "$port" > "$HOME/.config/platinvault/platinvault.env"
if "$root/install.sh" --no-systemd > "$tmp/install5.log" 2>&1; then ok "Installation mit Datenordner in einem fremden Ordner"; else bad "Installation (fremder Ordner)"; cat "$tmp/install5.log"; fi
check "Datenbank im gewaehlten Ordner" test -f "$foreign/platinvault.db"
# A stale PID file pointing at a foreign process must neither count as "running" nor be killed
sleep 300 &
decoy_pid=$!
"$app_dir/ctl.sh" stop >/dev/null 2>&1
printf '%s\n' "$decoy_pid" > "$HOME/.local/share/platinvault/platinvault.pid"
"$app_dir/ctl.sh" status >/dev/null 2>&1 && bad "fremder Prozess gilt als PlatinVault" || ok "fremder Prozess in der PID-Datei gilt nicht als PlatinVault"
"$app_dir/ctl.sh" stop >/dev/null 2>&1
kill -0 "$decoy_pid" 2>/dev/null && ok "ctl.sh stop beendet den fremden Prozess nicht" || bad "ctl.sh stop hat einen fremden Prozess beendet"
kill "$decoy_pid" 2>/dev/null
wait "$decoy_pid" 2>/dev/null
"$app_dir/ctl.sh" start >/dev/null 2>&1
wait_ready "$port" 90 && ok "Neustart mit ctl.sh start" || bad "ctl.sh start"
"$app_dir/ctl.sh" stop >/dev/null 2>&1
# Refusal first: the home folder (with a trailing slash) as data folder must change nothing
printf 'PLATINVAULT_DATA_DIR=%s/\n' "$HOME" > "$HOME/.config/platinvault/platinvault.env"
if "$app_dir/uninstall.sh" --delete-data --yes > "$tmp/uninstall3.log" 2>&1; then bad "Loeschen des Home-Ordners wurde nicht abgelehnt"; else ok "Home-Ordner als Datenordner wird abgelehnt"; fi
check "nach der Ablehnung ist nichts entfernt" test -x "$app_dir/platinvault"
printf 'PLATINVAULT_DATA_DIR=%s\n' "$foreign" > "$HOME/.config/platinvault/platinvault.env"
if "$app_dir/uninstall.sh" --delete-data --yes > "$tmp/uninstall4.log" 2>&1; then ok "Deinstallation mit Daten loeschen (Ordner mit fremden Dateien)"; else bad "Deinstallation (fremder Ordner)"; cat "$tmp/uninstall4.log"; fi
check "die Ausgabe nennt die zu loeschenden Eintraege" grep -q "platinvault.db" "$tmp/uninstall4.log"
check "fremde Datei bleibt" test -f "$foreign/foreign.txt"
check "fremder Unterordner bleibt" test -f "$foreign/sub/keep.txt"
check "platinvault.db ist geloescht" test ! -e "$foreign/platinvault.db"
check "Schluesselordner ist geloescht" test ! -e "$foreign/dp-keys"
check "Ordner mit fremden Dateien bleibt bestehen" test -d "$foreign"
check "Programm entfernt" test ! -e "$app_dir"

echo "== install.sh: Fehler bei einem Schritt stellt die alte Installation wieder her"
rm -f "$HOME/.config/platinvault/platinvault.env"
"$root/install.sh" --no-systemd --no-start --port "$port" > "$tmp/install6.log" 2>&1 && ok "Grundinstallation" || bad "Grundinstallation"
printf 'old-marker' > "$app_dir/old-marker.txt"
# A program folder that cannot be written to: copying fails, the old installation must stay exactly as it was
chmod a-w "$HOME/.local/share/platinvault"
if "$root/install.sh" --no-systemd --no-start > "$tmp/install7.log" 2>&1; then bad "Update trotz nicht beschreibbarem Zielordner"; else ok "Update bricht ab, wenn der Zielordner nicht beschreibbar ist"; fi
chmod u+w "$HOME/.local/share/platinvault"
check "alte Installation unveraendert" test -f "$app_dir/old-marker.txt"
check "kein app.new zurueckgeblieben" test ! -e "$HOME/.local/share/platinvault/app/app.new"
"$app_dir/uninstall.sh" --keep-data > /dev/null 2>&1

echo
if ((failures == 0)); then echo "Linux-Paket: alle Pruefungen ok"; else echo "Linux-Paket: $failures Pruefung(en) fehlgeschlagen"; fi
exit $((failures > 0))
