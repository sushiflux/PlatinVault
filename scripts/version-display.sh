#!/bin/sh
# How a PlatinVault version is written for people and for tags (user decision 2026-11-02). Used by the pre-commit hook (changelog
# heading) and the release workflow (GitHub release title); the C# twin is src/PlatinVault.Versioning/VersionFormat.cs - keep both in step
# (tests/Companion.Api.Tests/VersionFormatTests.cs pins the C# side, scripts/test-version-display.sh pins this one).
#
#   sh scripts/version-display.sh display <short> <buildId> <release|beta|dev>
#   sh scripts/version-display.sh tag     <short> <buildId> <release|beta|dev>
#
# short = line 1 of version.txt (v2026.11.12), buildId = line 2 (v2026.11.12.13.557, may be empty).
#   display: release "v2026.11.12 (13.557)", beta "v2026.11.12 (13.557 β)", dev "v2026.11.12 (13.557-dev)".
#            Without counters the plain short version (plus "-dev" for dev) - never empty parentheses.
#   tag:     release "v2026.11.12", beta "v2026.11.12.13.557-beta" (no spaces, parentheses or β: usable as git/Docker tag and file name).

set -e

mode="${1:-}"
short="${2:-}"
build="${3:-}"
kind="${4:-release}"

if [ -z "$short" ] || { [ "$mode" != "display" ] && [ "$mode" != "tag" ]; }; then
    echo "usage: version-display.sh display|tag <short> <buildId> <release|beta|dev>" >&2
    exit 2
fi

# U+03B2 written as its UTF-8 bytes, so the result never depends on the encoding of this file or on the locale
beta_mark=$(printf '\316\262')

# "prompts.lines" from the build id: it must be <short>.<digits>.<digits>
counters=""
case "$build" in
    "$short".*)
        rest="${build#"$short".}"
        case "$rest" in
            "" | *[!0-9.]* | .* | *. | *..* | *.*.*) ;;
            *.*) counters="$rest" ;;
        esac
        ;;
esac

if [ "$mode" = "tag" ]; then
    full="$short"
    [ -z "$counters" ] || full="$short.$counters"
    case "$kind" in
        beta) printf '%s-beta\n' "$full" ;;
        dev) printf '%s-dev\n' "$full" ;;
        *) printf '%s\n' "$short" ;;
    esac
    exit 0
fi

if [ -z "$counters" ]; then
    if [ "$kind" = "dev" ]; then printf '%s-dev\n' "$short"; else printf '%s\n' "$short"; fi
    exit 0
fi
case "$kind" in
    beta) printf '%s (%s %s)\n' "$short" "$counters" "$beta_mark" ;;
    dev) printf '%s (%s-dev)\n' "$short" "$counters" ;;
    *) printf '%s (%s)\n' "$short" "$counters" ;;
esac
