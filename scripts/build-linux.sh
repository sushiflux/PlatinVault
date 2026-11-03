#!/usr/bin/env bash
# Builds the PlatinVault Linux package (the server as a self-contained program plus the built web UI, the handbook, the licence texts
# and the install scripts of deploy/linux):
#
#   artifacts/linux/PlatinVault-linux-x64-<tag>.tar.gz          the package (one top-level folder PlatinVault-linux-x64-<tag>/)
#   artifacts/linux/PlatinVault-linux-x64-<tag>.tar.gz.sha256   its SHA-256 ("<hash> *<file name>")
#
# <tag> is the technical form of the version, the same string as the git tag: v2026.11.12 (release) or v2026.11.12.13.557-beta /
# v2026.11.12.13.557-dev (scripts/version-display.sh is the single definition, version.txt of the repository the source).
#
# Runs on Linux (CI: ubuntu-latest; locally in a container such as mcr.microsoft.com/dotnet/sdk:10.0). Needs the .NET SDK (global.json),
# bash, GNU tar, gzip, sha256sum and - unless --frontend-dist is given - Node.js with npm.
#
#   scripts/build-linux.sh [--channel release|beta|dev] [--product-version "<readable version>"] [--frontend-dist <dir>] [--skip-frontend]
#                          [--out <dir>] [--work-dir <dir>]
#
#   --channel          release (default) | beta | dev: written to release-channel.txt (the server shows "v2026.11.12 (13.557 β)" etc.)
#   --product-version  the readable version for the file properties; default: computed with scripts/version-display.sh
#   --frontend-dist    use an already built UI folder (index.html inside) instead of running npm
#   --skip-frontend    use frontend/dist as it is
#   --out              output folder (default artifacts/linux)
#   --work-dir         scratch folder for dotnet output and staging (default artifacts/linux-cache)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
channel=release
product_version=""
frontend_dist=""
skip_frontend=0
out="$root/artifacts/linux"
work="$root/artifacts/linux-cache"

while (($# > 0)); do
    case "$1" in
        --channel) channel="${2:?--channel needs a value}"; shift 2 ;;
        --product-version) product_version="${2:?--product-version needs a value}"; shift 2 ;;
        --frontend-dist) frontend_dist="${2:?--frontend-dist needs a folder}"; shift 2 ;;
        --skip-frontend) skip_frontend=1; shift ;;
        --out) out="${2:?--out needs a folder}"; shift 2 ;;
        --work-dir) work="${2:?--work-dir needs a folder}"; shift 2 ;;
        -h | --help) sed -n '2,22p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "Unbekannte Option / unknown option: $1" >&2; exit 2 ;;
    esac
done

step() { printf '\n==> %s\n' "$*"; }
die() { echo "FEHLER: $*" >&2; exit 1; }

case "$channel" in release | beta | dev) ;; *) die "--channel muss release, beta oder dev sein (ist: $channel)" ;; esac
for tool in dotnet tar gzip sha256sum; do command -v "$tool" >/dev/null 2>&1 || die "$tool wurde nicht gefunden."; done

# ---- version (version.txt line 1 = vYear.Month.Commit, line 2 = five-segment build id) ----------------------------------------------
short="$(sed -n '1p' "$root/version.txt" | tr -d '\r[:space:]')"
detail="$(sed -n '2p' "$root/version.txt" | tr -d '\r[:space:]')"
[[ -n "$short" ]] || die "version.txt ist leer."
[[ -n "$detail" ]] || detail="$short"
if [[ "$channel" != release && ! "$detail" =~ ^${short//./\\.}\.[0-9]+\.[0-9]+$ ]]; then
    die "version.txt hat keine Build-ID mit Zaehlern (Zeile 2) - ein Beta-/Dev-Paket braucht sie."
fi
tag="$(sh "$root/scripts/version-display.sh" tag "$short" "$detail" "$channel")"
if [[ -z "$product_version" ]]; then
    product_version="$(sh "$root/scripts/version-display.sh" display "$short" "$detail" "$channel")"
fi
# File version: version.txt line 1 as four numbers (each at most 65535), same rule as scripts/build-windows.ps1
if [[ ! "$short" =~ ^v?([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then die "version.txt Zeile 1 '$short' hat nicht die Form vJahr.Monat.Commit."; fi
for part in "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}"; do
    ((part <= 65535)) || die "Dateiversion aus '$short': $part ist groesser als 65535."
done
file_version="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.${BASH_REMATCH[3]}.0"

name="PlatinVault-linux-x64-$tag"
stage="$work/stage/$name"
app="$stage/app"
step "PlatinVault fuer Linux x64: $tag ($channel), Produktversion '$product_version', Dateiversion $file_version"

rm -rf "$work/stage"
mkdir -p "$app" "$out"
# A package of an earlier build must never be mistaken for this one
rm -f "$out"/PlatinVault-linux-x64-*.tar.gz "$out"/PlatinVault-linux-x64-*.tar.gz.sha256

# ---- 1. web UI ---------------------------------------------------------------------------------------------------------------------
if [[ -n "$frontend_dist" ]]; then
    dist="$frontend_dist"
else
    dist="$root/frontend/dist"
    if ((!skip_frontend)); then
        command -v npm >/dev/null 2>&1 || die "npm wurde nicht gefunden (oder --frontend-dist / --skip-frontend benutzen)."
        step "Baue die Oberflaeche (npm run build) ..."
        (
            cd "$root/frontend"
            if [[ ! -d node_modules ]]; then npm ci --no-fund --no-audit --ignore-scripts; fi
            npm run build
        )
    fi
fi
[[ -f "$dist/index.html" ]] || die "$dist/index.html fehlt (Oberflaeche nicht gebaut)."

# ---- 2. server ---------------------------------------------------------------------------------------------------------------------
step "Veroeffentliche Companion.Api (Release, linux-x64, eigenstaendig) ..."
# Self-contained (no .NET on the user's PC), a folder rather than a single file (the program finds branding.json, version.txt, docs/ and
# wwwroot/ next to itself). The restore for linux-x64 adds a runtime section to the packages.lock.json files; they are put back as they
# were when the script ends, so a local run leaves the working tree clean (CI does not restore in locked mode).
lock_backup="$work/lock-backup"
rm -rf "$lock_backup"
while IFS= read -r -d '' lock; do
    rel="${lock#"$root"/}"
    mkdir -p "$lock_backup/$(dirname "$rel")"
    cp -p "$lock" "$lock_backup/$rel"
done < <(find "$root/src" -name packages.lock.json -not -path '*/obj/*' -not -path '*/bin/*' -print0)
restore_locks() {
    local saved rel
    while IFS= read -r -d '' saved; do
        rel="${saved#"$lock_backup"/}"
        cmp -s "$saved" "$root/$rel" || cp -p "$saved" "$root/$rel"
    done < <(find "$lock_backup" -name packages.lock.json -print0)
}
trap restore_locks EXIT
dotnet publish "$root/src/Companion.Api/Companion.Api.csproj" \
    -c Release -r linux-x64 --self-contained true \
    --artifacts-path "$work/dotnet" -o "$app" \
    -p:UseAppHost=true -p:DebugType=none \
    "-p:PvProductVersion=$product_version" "-p:PvFileVersion=$file_version"
[[ -f "$app/Companion.Api" ]] || die "Der Programmstarter Companion.Api wurde nicht erzeugt."
# The program under its product name (the process shows up as "platinvault" in ps and top)
mv "$app/Companion.Api" "$app/platinvault"
chmod 755 "$app/platinvault"
# Development settings and the IIS file are not part of the package
rm -f "$app/appsettings.Development.json" "$app/web.config"

# ---- 3. assemble ---------------------------------------------------------------------------------------------------------------------
step "Stelle das Paket zusammen ..."
cp -R "$dist" "$app/wwwroot"
for lang in de en; do
    mkdir -p "$app/docs/handbuch/$lang"
    shopt -s nullglob
    pdfs=("$root/docs/handbuch/$lang/"*.pdf)
    shopt -u nullglob
    ((${#pdfs[@]} > 0)) || die "Handbuch-PDF ($lang) fehlt - das Handbuch gehoert immer zum Produkt."
    cp "${pdfs[@]}" "$app/docs/handbuch/$lang/"
done
# Licence: the official GNU AGPL v3 text, copied unchanged; the server serves both documents
cp "$root/LICENSE" "$app/docs/LICENSE"
cp "$root/THIRD-PARTY-NOTICES.md" "$app/docs/THIRD-PARTY-NOTICES.md"
cp "$root/branding/icon.png" "$app/platinvault.png"
# version.txt keeps the two plain lines (version, build id); the server turns them into the readable form with the channel
printf '%s\n%s' "$short" "$detail" > "$app/version.txt"
printf '%s' "$channel" > "$app/release-channel.txt"

for script in lib.sh run.sh ctl.sh; do
    [[ -f "$root/deploy/linux/$script" ]] || die "deploy/linux/$script fehlt."
    sed 's/\r$//' "$root/deploy/linux/$script" > "$app/$script"
done
chmod 755 "$app/run.sh" "$app/ctl.sh"
chmod 644 "$app/lib.sh"
for script in install.sh uninstall.sh; do
    [[ -f "$root/deploy/linux/$script" ]] || die "deploy/linux/$script fehlt."
    sed 's/\r$//' "$root/deploy/linux/$script" > "$stage/$script"
    chmod 755 "$stage/$script"
done
sed 's/\r$//' "$root/deploy/linux/README.txt" > "$stage/README.txt"
chmod 644 "$stage/README.txt"

# ---- 4. archive ----------------------------------------------------------------------------------------------------------------------
step "Packe das Archiv ..."
# Same file order and owner every time; the time stamp is the commit's (or now outside a git checkout)
mtime="$(git -C "$root" log -1 --format=%ct 2>/dev/null || date +%s)"
archive="$out/$name.tar.gz"
tar --sort=name --owner=0 --group=0 --numeric-owner --mtime="@$mtime" -C "$work/stage" -cf - "$name" | gzip -9n > "$archive"
(cd "$out" && sha256sum "$name.tar.gz" | sed 's/  / */' > "$name.tar.gz.sha256")

size_mb=$(($(stat -c %s "$archive") / 1048576))
app_mb=$(($(du -sk "$app" | cut -f1) / 1024))
echo "Fertig: $archive (${size_mb} MB; entpackt ${app_mb} MB)"
echo "   Produktversion: $product_version, Dateiversion: $file_version"
echo "   SHA-256: $(cut -d' ' -f1 "$archive.sha256")"
