#!/bin/sh
# Prints the release notes of ONE version from the customer changelog: its entry in the English section, a rule, its entry in the
# German section. Used by .github/workflows/release.yml (GitHub release body) and scripts/test-changelog-hooks.sh.
#
#   sh scripts/release-notes.sh <CHANGELOG.md> <readable version>
#
# readable version = what scripts/version-display.sh display prints, e.g. "v2026.11.12 (13.557)" for a release or
# "v2026.11.12 (13.557 β)" for a beta. An entry is the "## <version>" heading (optionally followed by " · <platforms>") and everything
# up to the next "## " heading, the next language anchor or the end; a trailing "---" rule and blank lines are dropped.
# Exit code 1 with a message on stderr when the entry is missing in a language section.

set -e

file="${1:-}"
version="${2:-}"
if [ -z "$file" ] || [ -z "$version" ]; then
    echo "usage: release-notes.sh <CHANGELOG.md> <readable version>" >&2
    exit 2
fi
if [ ! -f "$file" ]; then
    echo "release-notes.sh: $file not found" >&2
    exit 1
fi

# " · " (U+00B7) as bytes, so neither awk nor this file depend on the locale
sep=$(printf ' \302\267 ')

extract() {
    awk -v lang="$1" -v version="$version" -v sep="$sep" '
        { sub(/\r$/, "") }
        /^<a (id|name)="(english|deutsch)"/ { section = ($0 ~ "\"" lang "\""); if (taking) taking = 0; next }
        taking && /^## / { taking = 0 }
        section && !found && /^## / {
            heading = substr($0, 4)
            if (heading == version || index(heading, version sep) == 1) { taking = 1; found = 1 }
        }
        taking { lines[++n] = $0 }
        END {
            while (n > 0 && (lines[n] == "" || lines[n] == "---")) n--
            for (i = 1; i <= n; i++) print lines[i]
            exit found ? 0 : 1
        }
    ' "$file"
}

if ! english=$(extract english); then
    echo "release-notes.sh: no entry \"## $version\" in the English section of $file" >&2
    exit 1
fi
if ! german=$(extract deutsch); then
    echo "release-notes.sh: no entry \"## $version\" in the German section of $file" >&2
    exit 1
fi

printf '%s\n\n---\n\n%s\n' "$english" "$german"
