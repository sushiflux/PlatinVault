#!/usr/bin/env bash
# Builds the PlatinVault Bridge for Linux (src/PlatinVault.Bridge.Linux) as a self-contained single-file linux-x64 binary and packs it
# with install.sh, uninstall.sh, the readme, the handbook PDFs and the licence texts:
#
#   artifacts/linux/PlatinVault-Bridge-linux-x64.tar.gz          the package (one top-level folder PlatinVault-Bridge/)
#   artifacts/linux/PlatinVault-Bridge-linux-x64.tar.gz.sha256   its SHA-256 ("<hash> *<file name>")
#   artifacts/bridge-linux/                                      the same files unpacked, in the layout the server image uses
#                                                                (platinvault-bridge, bridge-version.txt, package/); the development server
#                                                                (Development environment) serves the Linux bridge from here
#
# The package of this script has NO bridge.json (it does not know any server): it is the generic download of the GitHub release. The
# ready-to-run package with server address and token comes from the server itself (Settings > PlatinVault Bridge > Linux).
#
# The bridge has its own version 1.0.<n>, n = number of commits that touched the Linux bridge, the shared bridge core or the Steam readers
# (the Windows bridge counts its own files the same way). Set --bridge-version / BRIDGE_LINUX_VERSION when there is no git history.
#
# Runs on Linux (CI: ubuntu-latest; locally in a container such as mcr.microsoft.com/dotnet/sdk:10.0). Needs the .NET SDK (global.json),
# bash, GNU tar, gzip and sha256sum.
#
#   scripts/build-bridge-linux.sh [--bridge-version 1.0.7] [--channel release|beta|dev] [--product-version "<readable version>"]
#                                 [--out <dir>] [--work-dir <dir>] [--print-version]
#
#   --print-version    only print the bridge version (1.0.<n>) and exit - the release workflow passes it to the Docker build
#   --channel          release (default; or $PV_CHANNEL) | beta | dev: only used for the readable product version
#   --product-version  the readable version for the file properties; default: computed with scripts/version-display.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bridge_version="${BRIDGE_LINUX_VERSION:-}"
channel="${PV_CHANNEL:-release}"
product_version="${PV_PRODUCT_VERSION:-}"
out="$root/artifacts/linux"
work="$root/artifacts/bridge-linux-cache"
unpacked="$root/artifacts/bridge-linux"
print_version=0

while (($# > 0)); do
    case "$1" in
        --bridge-version) bridge_version="${2:?--bridge-version needs a value}"; shift 2 ;;
        --channel) channel="${2:?--channel needs a value}"; shift 2 ;;
        --product-version) product_version="${2:?--product-version needs a value}"; shift 2 ;;
        --out) out="${2:?--out needs a folder}"; shift 2 ;;
        --work-dir) work="${2:?--work-dir needs a folder}"; shift 2 ;;
        --print-version) print_version=1; shift ;;
        -h | --help) sed -n '2,28p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "Unbekannte Option / unknown option: $1" >&2; exit 2 ;;
    esac
done

step() { printf '\n==> %s\n' "$*" >&2; }
die() { echo "FEHLER: $*" >&2; exit 1; }

# ---- bridge version -----------------------------------------------------------------------------------------------------------------
if [[ -z "$bridge_version" ]]; then
    count=0
    if command -v git >/dev/null 2>&1 && git -C "$root" rev-parse --git-dir >/dev/null 2>&1; then
        count="$(git -C "$root" rev-list --count HEAD -- src/PlatinVault.Bridge.Linux src/PlatinVault.Bridge.Core src/PlatinVault.SteamLocal 2>/dev/null || echo 0)"
    fi
    bridge_version="1.0.$count"
fi
[[ "$bridge_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "Ungueltige Bridge-Version '$bridge_version' (erwartet 1.0.7)."
if ((print_version)); then
    echo "$bridge_version"
    exit 0
fi

case "$channel" in release | beta | dev) ;; *) die "--channel muss release, beta oder dev sein (ist: $channel)" ;; esac
for tool in dotnet tar gzip sha256sum; do command -v "$tool" >/dev/null 2>&1 || die "$tool wurde nicht gefunden."; done

# ---- product version for the file properties (same rules as scripts/build-linux.sh) ---------------------------------------------------
short="$(sed -n '1p' "$root/version.txt" | tr -d '\r[:space:]')"
detail="$(sed -n '2p' "$root/version.txt" | tr -d '\r[:space:]')"
[[ -n "$short" ]] || die "version.txt ist leer."
[[ -n "$detail" ]] || detail="$short"
if [[ -z "$product_version" ]]; then
    product_version="$(sh "$root/scripts/version-display.sh" display "$short" "$detail" "$channel")"
fi

name=PlatinVault-Bridge-linux-x64
stage="$work/stage/PlatinVault-Bridge"
publish="$work/publish"
step "PlatinVault Bridge fuer Linux $bridge_version (Produktversion '$product_version')"

rm -rf "$work/stage" "$publish" "$unpacked"
mkdir -p "$stage" "$out" "$unpacked/package"

# ---- 1. publish -----------------------------------------------------------------------------------------------------------------------
step "Veroeffentliche PlatinVault.Bridge.Linux (Release, linux-x64, eigenstaendig, eine Datei) ..."
# The restore for linux-x64 may add a runtime section to packages.lock.json files; they are put back as they were when the script ends,
# so a local run leaves the working tree clean (CI does not restore in locked mode).
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

dotnet publish "$root/src/PlatinVault.Bridge.Linux/PlatinVault.Bridge.Linux.csproj" \
    -c Release -r linux-x64 \
    --artifacts-path "$work/dotnet" -o "$publish" \
    "-p:Version=$bridge_version" "-p:PvProductVersion=$product_version"
[[ -f "$publish/platinvault-bridge" ]] || die "platinvault-bridge wurde nicht erzeugt."

# ---- 2. assemble ----------------------------------------------------------------------------------------------------------------------
step "Stelle das Paket zusammen ..."
cp "$publish/platinvault-bridge" "$stage/platinvault-bridge"
chmod 755 "$stage/platinvault-bridge"
for file in install.sh uninstall.sh README.txt LIESMICH.txt; do
    [[ -f "$root/src/PlatinVault.Bridge.Linux/package/$file" ]] || die "src/PlatinVault.Bridge.Linux/package/$file fehlt."
    sed 's/\r$//' "$root/src/PlatinVault.Bridge.Linux/package/$file" > "$stage/$file"
done
chmod 755 "$stage/install.sh" "$stage/uninstall.sh"
chmod 644 "$stage/README.txt" "$stage/LIESMICH.txt"

# The handbook is part of the product: whoever downloads the bridge has it right away (names as in the Windows bridge package)
declare -A handbook_names=([de]="Handbuch (Deutsch).pdf" [en]="Handbook (English).pdf")
for lang in de en; do
    shopt -s nullglob
    pdfs=("$root/docs/handbuch/$lang/"*.pdf)
    shopt -u nullglob
    ((${#pdfs[@]} > 0)) || die "Handbuch-PDF ($lang) fehlt - das Handbuch gehoert immer zum Produkt."
    cp "${pdfs[0]}" "$stage/${handbook_names[$lang]}"
    chmod 644 "$stage/${handbook_names[$lang]}"
done
# Licence: the official GNU AGPL v3 text, copied unchanged, and the notices of the bundled open-source parts (the .NET runtime is inside)
cp "$root/LICENSE" "$stage/LICENSE"
cp "$root/THIRD-PARTY-NOTICES.md" "$stage/THIRD-PARTY-NOTICES.md"
chmod 644 "$stage/LICENSE" "$stage/THIRD-PARTY-NOTICES.md"
printf '%s' "$bridge_version" > "$stage/bridge-version.txt"

# The layout the server image / development server reads (BridgeBundle): binary, version file, package templates
cp "$publish/platinvault-bridge" "$unpacked/platinvault-bridge"
chmod 755 "$unpacked/platinvault-bridge"
printf '%s' "$bridge_version" > "$unpacked/bridge-version.txt"
cp -R "$root/src/PlatinVault.Bridge.Linux/package/." "$unpacked/package/"

# ---- 3. archive -----------------------------------------------------------------------------------------------------------------------
step "Packe das Archiv ..."
# Same file order and owner every time; the time stamp is the commit's (or now outside a git checkout)
mtime="$(git -C "$root" log -1 --format=%ct 2>/dev/null || date +%s)"
archive="$out/$name.tar.gz"
rm -f "$archive" "$archive.sha256"
tar --sort=name --owner=0 --group=0 --numeric-owner --mtime="@$mtime" -C "$work/stage" -cf - PlatinVault-Bridge | gzip -9n > "$archive"
(cd "$out" && sha256sum "$name.tar.gz" | sed 's/  / */' > "$name.tar.gz.sha256")

size_mb=$(($(stat -c %s "$archive") / 1048576))
echo "Fertig: $archive (${size_mb} MB)"
echo "   Bridge-Version: $bridge_version, Produktversion: $product_version"
echo "   SHA-256: $(cut -d' ' -f1 "$archive.sha256")"
echo "   entpackt (Server-Layout): $unpacked"
