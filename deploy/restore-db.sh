#!/bin/sh
# Restores the PlatinVault database from one of its automatic backups.
#
#   ./restore-db.sh                                   newest backup from ./data/backups/recent
#   ./restore-db.sh data/backups/daily/platinvault-20261101.db
#   ./restore-db.sh --yes [file]                      no question
#
# Run it in the folder with docker-compose.yml. It stops PlatinVault, then PlatinVault itself does the restore: it copies the chosen
# backup aside and checks it, saves the current state first (a restore must never become the next data loss) and only then replaces
# the database. Afterwards PlatinVault starts again and brings an older backup up to date by itself. Your API keys are not touched.
set -eu

cd "$(dirname "$0")"

yes=0
file=""
for arg in "$@"; do
  case "$arg" in
    -y|--yes) yes=1 ;;
    -h|--help) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) file="$arg" ;;
  esac
done

say() { printf '\n== %s\n' "$*"; }
die() { echo "ERROR: $*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || die "docker not found."
[ -f docker-compose.yml ] || die "docker-compose.yml not found - run this script in the PlatinVault folder."
[ -d data ] || die "The folder ./data does not exist - is this the right folder?"

if [ -z "$file" ]; then
  file="$(ls -1t data/backups/recent/platinvault-*.db 2>/dev/null | head -n 1 || true)"
  [ -n "$file" ] || die "No backup found in data/backups/recent - pass a file: ./restore-db.sh <file.db>"
fi
[ -f "$file" ] || die "File not found: $file"

# The container sees ./data as /data. A backup from somewhere else is first copied into ./data/backups/import.
case "$file" in
  data/*) inside="/$file" ;;
  ./data/*) inside="/${file#./}" ;;
  *)
    mkdir -p data/backups/import
    cp "$file" "data/backups/import/$(basename "$file")"
    inside="/data/backups/import/$(basename "$file")"
    ;;
esac

echo "Restore from: $file ($(wc -c < "$file") bytes, $(date -r "$file" '+%Y-%m-%d %H:%M:%S' 2>/dev/null || echo '?'))"
echo "Target: data/platinvault.db - it is replaced completely (the current state is backed up first)."
if [ "$yes" != "1" ]; then
  printf 'Continue? [y/N] '
  read -r answer
  case "$answer" in y|Y|yes|YES|j|J|ja|JA) ;; *) echo "Cancelled."; exit 1 ;; esac
fi

say "Stopping PlatinVault (it makes a last backup)"
docker compose stop platinvault

say "Restoring"
status=0
docker compose run --rm --no-deps platinvault --restore "$inside" || status=$?

say "Starting PlatinVault"
docker compose up -d
[ "$status" = "0" ] || die "The restore did not happen (see the lines above). The database is unchanged."
echo
echo "Done. The state from before the restore is the newest backup in data/backups/recent."
