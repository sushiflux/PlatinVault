#!/bin/sh
# How a PlatinVault version is written for people and for tags (user decision 2026-11-02). Used by the pre-commit hook (changelog
# heading) and the release workflow (GitHub release title); the C# twin is src/PlatinVault.Versioning/VersionFormat.cs - keep both in step
# (tests/Companion.Api.Tests/VersionFormatTests.cs pins the C# side, scripts/test-version-display.sh pins this one).
#
#   sh scripts/version-display.sh display <short> <buildId> <release|beta|nightly>
#   sh scripts/version-display.sh tag     <short> <buildId> <release|beta|nightly>
#
# short = line 1 of version.txt (v2026.11.12), buildId = line 2 (v2026.11.12.13.557, may be empty).
#   display: all five segments, then the channel symbol in parentheses; a release has none:
#            release "v2026.11.12.13.557", beta "v2026.11.12.13.557 (β)", nightly "v2026.11.12.13.557 (α)".
#            Without counters the plain short version plus the symbol, if there is one ("v2026.11.12 (β)") - never empty parentheses.
#            (Until 2026-11-08 the form was "v2026.11.12 (13.557)" / "v2026.11.12 (13.557 β)"; scripts/release-notes.sh still finds
#            changelog headings stamped in that form, because stamped headings are never rewritten.)
#   tag:     release "v2026.11.12", beta "v2026.11.12.13.557-beta", nightly "v2026.11.12.13.557-nightly" (no spaces, parentheses or Greek sign: usable as git/Docker tag and file name).
#
# Dev is not a kind here: it only runs locally on the developer machine and has no tag, no image and no package. Its readable form
# ("v2026.11.12.13.557 (</>)") is computed by the programs themselves from their environment (src/PlatinVault.Versioning/VersionFormat.cs).

set -e

mode="${1:-}"
short="${2:-}"
build="${3:-}"
kind="${4:-release}"

if [ -z "$short" ] || { [ "$mode" != "display" ] && [ "$mode" != "tag" ]; }; then
    echo "usage: version-display.sh display|tag <short> <buildId> <release|beta|nightly>" >&2
    exit 2
fi
case "$kind" in
    release | beta | nightly | alpha) ;;
    *)
        echo "version-display.sh: unknown kind '$kind' (release, beta or nightly; dev has no tag and no package)" >&2
        exit 2
        ;;
esac

# U+03B2 (β) and U+03B1 (α) written as their UTF-8 bytes, so the result never depends on the encoding of this file or on the locale
beta_mark=$(printf '\316\262')
nightly_mark=$(printf '\316\261')

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
        nightly|alpha) printf '%s-nightly\n' "$full" ;;
        *) printf '%s\n' "$short" ;;
    esac
    exit 0
fi

# All five segments, or only line 1 when the build id carries no counters; then the symbol in parentheses (a release has none)
full="$short"
[ -z "$counters" ] || full="$short.$counters"
case "$kind" in
    beta) printf '%s (%s)\n' "$full" "$beta_mark" ;;
    nightly|alpha) printf '%s (%s)\n' "$full" "$nightly_mark" ;;
    *) printf '%s\n' "$full" ;;
esac
