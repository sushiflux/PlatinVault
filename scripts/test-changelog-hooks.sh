#!/bin/sh
# Tests the changelog part of the git hooks and scripts/release-notes.sh in a throw-away git repository (never this one):
#   - "## vNEXT" / "## vNEXT β" in a staged CHANGELOG.md become this commit's readable version (suffix " · ..." kept)
#   - the changed-lines counter in version.txt equals the real size of the commit (stamping never changes it)
#   - intern/CHANGELOG.dev.md gets version + time in its newest heading per language, only when it changed, and intern/ (its own git
#     repository) is committed by post-commit with the version as message
#   - release-notes.sh prints exactly the entry of one version (English, rule, German) and fails for a missing one
#
#   sh scripts/test-changelog-hooks.sh

set -e

src=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
fails=0
ok() { printf 'ok    %s\n' "$1"; }
bad() { printf 'FAIL  %s\n' "$1"; fails=$((fails + 1)); }
check() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1: expected [$3], got [$2]"; fi; }

beta=$(printf '\316\262')
dot=$(printf '\302\267')

# The hooks run with this repository's git variables when the test itself is started from a hook - never touch this repository
unset GIT_DIR GIT_INDEX_FILE GIT_WORK_TREE GIT_PREFIX GIT_OBJECT_DIRECTORY GIT_COMMON_DIR
export PV_HOOK_NO_PUSH=1 GIT_TERMINAL_PROMPT=0

repo="$work/repo"
mkdir -p "$repo/scripts/git-hooks" "$repo/.run"
cp "$src/scripts/git-hooks/pre-commit" "$src/scripts/git-hooks/post-commit" "$repo/scripts/git-hooks/"
cp "$src/scripts/version-display.sh" "$src/scripts/release-notes.sh" "$repo/scripts/"
chmod +x "$repo/scripts/git-hooks/pre-commit" "$repo/scripts/git-hooks/post-commit"
cd "$repo"
git init -q
git config user.name Test
git config user.email test@example.invalid
git config commit.gpgsign false
git config core.autocrlf false
git config core.hooksPath scripts/git-hooks
printf 'intern/\n.run/\n' > .gitignore

# The commit's own counters, read back after the commit
counter_of() { sed -n '2p' version.txt | awk -F. '{ print $NF }'; }
size_of() { git show --numstat --format= HEAD -- . ':(exclude)version.txt' | awk '$1 != "-" { s += $1 + $2 } END { print s + 0 }'; }
display_of() { sh scripts/version-display.sh display "$(sed -n '1p' version.txt)" "$(sed -n '2p' version.txt)" "$1"; }

# ---- 1. release placeholder -------------------------------------------------------------------------------------------------------
cat > CHANGELOG.md <<EOF
# Changelog

---

<a name="english"></a>
## English

## vNEXT $dot Unraid, Windows

First release.

- New thing.

---

<a name="deutsch"></a>
## Deutsch

## vNEXT

Erstes Release.

- Neues Ding.
EOF
printf '{ "prompts": 4 }\n' > .run/devbuild.json
git add -A
git commit -q -m one
release1=$(display_of release)
check "release heading EN (suffix kept)" "$(sed -n '8p' CHANGELOG.md)" "## $release1 $dot Unraid, Windows"
check "release heading DE" "$(grep '^## v' CHANGELOG.md | sed -n '2p')" "## $release1"
check "no placeholder left in the commit" "$(git show HEAD:CHANGELOG.md | grep -c 'vNEXT' || true)" "0"
check "changed-lines counter = commit size (release)" "$(counter_of)" "$(size_of)"
check "prompt counter in the build id" "$(sed -n '2p' version.txt | awk -F. '{ print $(NF-1) }')" "4"
check "post-commit resets the prompt counter" "$(tr -d ' \n' < .run/devbuild.json)" '{"prompts":0}'

# ---- 2. beta placeholder: a new block on top of each language section ------------------------------------------------------------
awk -v b="$beta" -v d="$dot" '
    { print }
    $0 == "## English" { print ""; print "## vNEXT " b " " d " Unraid"; print ""; print "- Beta change."; print "- Second line." }
    $0 == "## Deutsch" { print ""; print "## vNEXT " b; print ""; print "- Beta-Änderung." }
' CHANGELOG.md > CHANGELOG.tmp && mv CHANGELOG.tmp CHANGELOG.md
git add CHANGELOG.md
git commit -q -m two
beta2=$(display_of beta)
check "beta heading EN" "$(grep '^## v' CHANGELOG.md | sed -n '1p')" "## $beta2 $dot Unraid"
check "beta heading DE" "$(grep '^## v' CHANGELOG.md | sed -n '3p')" "## $beta2"
check "older release entry untouched" "$(grep '^## v' CHANGELOG.md | sed -n '2p')" "## $release1 $dot Unraid, Windows"
check "changed-lines counter = commit size (beta)" "$(counter_of)" "$(size_of)"

# ---- 3. release notes --------------------------------------------------------------------------------------------------------------
expected=$(printf '%s\n' "## $beta2 $dot Unraid" "" "- Beta change." "- Second line." "" "---" "" "## $beta2" "" "- Beta-Änderung.")
check "release notes of the beta" "$(sh scripts/release-notes.sh CHANGELOG.md "$beta2")" "$expected"
notes=$(sh scripts/release-notes.sh CHANGELOG.md "$release1")
check "release notes of the release start with its EN heading" "$(printf '%s\n' "$notes" | sed -n '1p')" "## $release1 $dot Unraid, Windows"
check "release notes of the release end with its DE text" "$(printf '%s\n' "$notes" | tail -n 1)" "- Neues Ding."
check "release notes: one rule between the languages" "$(printf '%s\n' "$notes" | grep -c '^---$')" "1"
if sh scripts/release-notes.sh CHANGELOG.md "v1999.1.1 (1.1)" >/dev/null 2>&1; then bad "missing entry must fail"; else ok "missing entry fails"; fi
# A release heading must not pick the beta block of the same commit and vice versa
if sh scripts/release-notes.sh CHANGELOG.md "$(display_of release)" >/dev/null 2>&1; then
    bad "release notes found a release entry that only exists as beta"
else
    ok "release and beta headings are told apart"
fi

# ---- 4. private developer changelog in intern/ (its own git repository) --------------------------------------------------------
mkdir -p intern
(cd intern && git init -q && git config core.autocrlf false && git config user.name Test && git config user.email test@example.invalid && git config commit.gpgsign false)
cat > intern/CHANGELOG.dev.md <<'EOF'
# Dev changelog

<a name="english"></a>
## English

## vNEXT - draft

- Something.

## v2026.1.1 - 2026-01-01 10:00

- Old.

<a name="deutsch"></a>
## Deutsch

## vNEXT - Entwurf

- Etwas.
EOF
(cd intern && git add -A && git commit -q -m start)
printf 'x\n' > file.txt
printf '\n## v2026.2.2 - 2026-02-02 10:00\n' >> intern/CHANGELOG.dev.md
git add file.txt
git commit -q -m three
version3=$(sed -n '1p' version.txt)
first_en=$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '1p')
case "$first_en" in
    "## $version3 - "[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]" "[0-9][0-9]:[0-9][0-9]) ok "intern heading EN stamped" ;;
    *) bad "intern heading EN stamped: got [$first_en]" ;;
esac
check "intern older entry untouched" "$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '2p')" "## v2026.1.1 - 2026-01-01 10:00"
check "intern heading DE stamped" "$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '3p' | cut -d' ' -f2)" "$version3"
check "intern committed by post-commit with the version" "$(git -C intern log -1 --format=%s)" "$version3"
check "intern working tree clean afterwards" "$(git -C intern status --porcelain)" ""
check "outer repository does not contain intern/" "$(git ls-files intern | wc -l | tr -d ' ')" "0"

printf 'y\n' >> file.txt
git add file.txt
git commit -q -m four
check "unchanged intern changelog is not restamped" "$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '1p' | cut -d' ' -f2)" "$version3"
check "nothing to commit in intern" "$(git -C intern log -1 --format=%s)" "$version3"

# Without intern/ nothing happens and the commit still works
rm -rf intern
printf 'z\n' >> file.txt
git add file.txt
if git commit -q -m five; then ok "commit without intern/ works"; else bad "commit without intern/ failed"; fi

echo
if [ "$fails" -eq 0 ]; then
    echo "test-changelog-hooks: alle Faelle ok"
else
    echo "test-changelog-hooks: $fails Fehler"
    exit 1
fi
