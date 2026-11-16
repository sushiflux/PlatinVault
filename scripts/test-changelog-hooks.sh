#!/bin/sh
# Tests the changelog part of the git hooks and scripts/release-notes.sh in a throw-away git repository (never this one):
#   - "## vNEXT" / "## vNEXT β" in a staged CHANGELOG.md become this commit's readable version (suffix " · ..." kept)
#   - the changed-lines counter in version.txt equals the real size of the commit (stamping never changes it)
#   - intern/CHANGELOG.dev.md (no git repository needed): only a "## vNEXT" heading per language gets version + time, older headings and
#     files without a placeholder stay untouched
#   - post-commit / post-merge call the command of the git config key intern.sync with "save <root>" / "restore <root>" (a fake command
#     here); only the clone's local key counts (global file or "git -c" = ignored); no key = nothing happens; a failing or missing command never fails the commit or the merge
#   - release-notes.sh prints exactly the entry of one version (English, rule, German) and fails for a missing one; it finds entries under
#     the notation of 2026-11-08 (v2026.11.12.13.557 (β)) and under the old one (v2026.11.12 (13.557 β)), which stamped headings keep
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
alpha=$(printf '\316\261')
dot=$(printf '\302\267')

# The hooks run with this repository's git variables when the test itself is started from a hook - never touch this repository
unset GIT_DIR GIT_INDEX_FILE GIT_WORK_TREE GIT_PREFIX GIT_OBJECT_DIRECTORY GIT_COMMON_DIR
export GIT_TERMINAL_PROMPT=0

repo="$work/repo"
mkdir -p "$repo/scripts/git-hooks" "$repo/.run"
cp "$src/scripts/git-hooks/pre-commit" "$src/scripts/git-hooks/post-commit" "$src/scripts/git-hooks/post-merge" "$repo/scripts/git-hooks/"
cp "$src/scripts/version-display.sh" "$src/scripts/release-notes.sh" "$repo/scripts/"
chmod +x "$repo/scripts/git-hooks/pre-commit" "$repo/scripts/git-hooks/post-commit" "$repo/scripts/git-hooks/post-merge"
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

# ---- 2. beta and nightly changelogs (CHANGELOG.beta.md / CHANGELOG.nightly.md) ----------------------------------------------------
cat > CHANGELOG.beta.md <<EOF
# Beta Changelog

<a name="english"></a>
## English

## vNEXT $beta $dot Unraid

- Beta change.
- Second line.

---

<a name="deutsch"></a>
## Deutsch

## vNEXT $beta

- Beta-Änderung.
EOF
git add CHANGELOG.beta.md
git commit -q -m two
beta2=$(display_of beta)
check "beta heading EN" "$(grep '^## v' CHANGELOG.beta.md | sed -n '1p')" "## $beta2 $dot Unraid"
check "beta heading DE" "$(grep '^## v' CHANGELOG.beta.md | sed -n '2p')" "## $beta2"
check "changed-lines counter = commit size (beta)" "$(counter_of)" "$(size_of)"

cat > CHANGELOG.nightly.md <<EOF
# Nightly Changelog

<a name="english"></a>
## English

## vNEXT $alpha $dot Windows

- Nightly change.

---

<a name="deutsch"></a>
## Deutsch

## vNEXT $alpha

- Nightly-Änderung.
EOF
git add CHANGELOG.nightly.md
git commit -q -m two-nightly
nightly2=$(display_of nightly)
check "nightly heading EN" "$(grep '^## v' CHANGELOG.nightly.md | sed -n '1p')" "## $nightly2 $dot Windows"
check "nightly heading DE" "$(grep '^## v' CHANGELOG.nightly.md | sed -n '2p')" "## $nightly2"
check "changed-lines counter = commit size (nightly)" "$(counter_of)" "$(size_of)"

# ---- 3. release notes --------------------------------------------------------------------------------------------------------------
expected=$(printf '%s\n' "## $beta2 $dot Unraid" "" "- Beta change." "- Second line." "" "---" "" "## $beta2" "" "- Beta-Änderung.")
check "release notes of the beta" "$(sh scripts/release-notes.sh CHANGELOG.beta.md "$beta2")" "$expected"
expected_nightly=$(printf '%s\n' "## $nightly2 $dot Windows" "" "- Nightly change." "" "---" "" "## $nightly2" "" "- Nightly-Änderung.")
check "release notes of the nightly" "$(sh scripts/release-notes.sh CHANGELOG.nightly.md "$nightly2")" "$expected_nightly"
notes=$(sh scripts/release-notes.sh CHANGELOG.md "$release1")
check "release notes of the release start with its EN heading" "$(printf '%s\n' "$notes" | sed -n '1p')" "## $release1 $dot Unraid, Windows"
check "release notes of the release end with its DE text" "$(printf '%s\n' "$notes" | tail -n 1)" "- Neues Ding."
check "release notes: one rule between the languages" "$(printf '%s\n' "$notes" | grep -c '^---$')" "1"
if sh scripts/release-notes.sh CHANGELOG.md "v1999.1.1 (1.1)" >/dev/null 2>&1; then bad "missing entry must fail"; else ok "missing entry fails"; fi
# A release heading must not pick the beta block of the same commit and vice versa
if sh scripts/release-notes.sh CHANGELOG.beta.md "$(display_of release)" >/dev/null 2>&1; then
    bad "release notes found a release entry that only exists as beta"
else
    ok "release and beta headings are told apart"
fi

# ---- 3b. headings stamped in the old notation (before 2026-11-08) are never rewritten and must still be found -------------------------
# Old: "## v2026.11.19 (2.40 β)" / "## v2026.11.18 (9.7263)"; new: "## v2026.11.20.3.150 (β)" / "## v2026.11.17.24.19071". One file holds both.
cat > CHANGELOG.beta.md <<EOF
# Beta Changelog

<a name="english"></a>
## English

## vNEXT $beta

- Newest beta.

## v2026.11.19 (2.40 $beta) $dot Unraid, Windows

- Old notation, beta.

## v2026.11.18 (9.7263)

- Old notation, release.

## v2026.11.17.24.19071

- New notation, release.

---

<a name="deutsch"></a>
## Deutsch

## vNEXT $beta

- Neueste Beta.

## v2026.11.19 (2.40 $beta) $dot Unraid, Windows

- Alte Schreibweise, Beta.

## v2026.11.18 (9.7263)

- Alte Schreibweise, Release.

## v2026.11.17.24.19071

- Neue Schreibweise, Release.
EOF
git add CHANGELOG.beta.md
git commit -q -m legacy
beta3=$(display_of beta)
check "stamping leaves old-notation headings alone" "$(grep '^## v' CHANGELOG.beta.md | sed -n '2p;3p' | tr '\n' '|')" "## v2026.11.19 (2.40 $beta) $dot Unraid, Windows|## v2026.11.18 (9.7263)|"
check "stamping leaves new-notation headings alone" "$(grep '^## v' CHANGELOG.beta.md | sed -n '4p')" "## v2026.11.17.24.19071"
check "the placeholder next to them is stamped" "$(grep '^## v' CHANGELOG.beta.md | sed -n '1p')" "## $beta3"
# The new notation finds an entry stamped in the old one (platforms kept), and the old notation still works as an argument
check "old-notation beta found by the new version" "$(sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.19.2.40 ($beta)" | sed -n '1p')" "## v2026.11.19 (2.40 $beta) $dot Unraid, Windows"
check "old-notation beta: German part found too" "$(sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.19.2.40 ($beta)" | tail -n 1)" "- Alte Schreibweise, Beta."
check "old-notation release found by the new version" "$(sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.18.9.7263" | tail -n 1)" "- Alte Schreibweise, Release."
check "old-notation release: English part stops at the next heading" "$(sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.18.9.7263" | sed -n '3p')" "- Old notation, release."
check "new-notation release found" "$(sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.17.24.19071" | tail -n 1)" "- Neue Schreibweise, Release."
check "old notation as the argument still works" "$(sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.18 (9.7263)" | tail -n 1)" "- Alte Schreibweise, Release."
# A beta heading is not the release of the same build, and a longer counter is not a shorter one
if sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.19.2.40" >/dev/null 2>&1; then bad "the release form picked a beta entry (old notation)"; else ok "old-notation beta is not found as a release"; fi
if sh scripts/release-notes.sh CHANGELOG.beta.md "v2026.11.18.9.72630" >/dev/null 2>&1; then bad "a longer counter matched a shorter one"; else ok "counters are matched exactly"; fi

# ---- 4. private developer changelog in intern/ (git-ignored, no git repository of its own needed) ----------------------------------
mkdir -p intern
cat > intern/CHANGELOG.dev.md <<'EOT'
# Dev changelog

<a name="english"></a>
## English

## vNEXT

- Something.

## v2026.1.1 - 2026-01-01 10:00

- Old.

<a name="deutsch"></a>
## Deutsch

## vNEXT

- Etwas.

## vNEXT - Entwurf

- Not the exact placeholder, stays as it is.
EOT
printf 'x\n' > file.txt
git add file.txt
git commit -q -m three
version3=$(sed -n '1p' version.txt)
check "no intern/.git exists" "$([ -e intern/.git ] && echo yes || echo no)" "no"
first_en=$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '1p')
case "$first_en" in
    "## $version3 - "[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]" "[0-9][0-9]:[0-9][0-9]) ok "intern placeholder EN stamped" ;;
    *) bad "intern placeholder EN stamped: got [$first_en]" ;;
esac
check "intern older entry untouched" "$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '2p')" "## v2026.1.1 - 2026-01-01 10:00"
check "intern placeholder DE stamped" "$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '3p' | cut -d' ' -f2)" "$version3"
check "intern non-exact placeholder untouched" "$(grep '^## v' intern/CHANGELOG.dev.md | sed -n '4p')" "## vNEXT - Entwurf"
check "outer repository does not contain intern/" "$(git ls-files intern | wc -l | tr -d ' ')" "0"

# No placeholder = nothing changes, the newest entry is not restamped by the next commit
sum_before=$(cksum < intern/CHANGELOG.dev.md)
printf 'y\n' >> file.txt
git add file.txt
git commit -q -m four
check "no placeholder: changelog unchanged" "$(cksum < intern/CHANGELOG.dev.md)" "$sum_before"

# A placeholder with CRLF line endings is stamped too (whether the CR survives depends on the awk build: msys awk strips it on input)
printf '<a name="english"></a>\r\n## English\r\n\r\n## vNEXT\r\n\r\n- Crlf.\r\n' > intern/CHANGELOG.dev.md
printf 'z\n' >> file.txt
git add file.txt
git commit -q -m four-crlf
check "CRLF placeholder carries the version" "$(grep '^## v' intern/CHANGELOG.dev.md | tr -d '\r' | cut -d' ' -f2)" "$(sed -n '1p' version.txt)"

# ---- 5. intern.sync: post-commit calls "save <root>", post-merge calls "restore <root>" (fake command, nothing real, no push) ------
root=$(git rev-parse --show-toplevel)
tooldir="$work/Some Path"
mkdir -p "$tooldir"
out="$work/sync.out"
cat > "$tooldir/fake sync.sh" <<'EOT'
#!/bin/sh
# Records its arguments; the exit code comes from FAKE_SYNC_EXIT (default 0)
printf '%s\n' "$*" >> "$FAKE_SYNC_OUT"
echo "tool output line"
exit "${FAKE_SYNC_EXIT:-0}"
EOT
export FAKE_SYNC_OUT="$out"
lines_in_out() { if [ -f "$out" ]; then wc -l < "$out" | tr -d ' '; else echo 0; fi; }
# save runs detached: wait until the expected number of lines appeared
wait_for_lines() {
    n=0
    while [ "$(lines_in_out)" -lt "$1" ] && [ $n -lt 100 ]; do sleep 0.1; n=$((n + 1)); done
}
wait_for_log() {
    n=0
    while ! grep -q "$1" .run/intern-sync.log 2>/dev/null && [ $n -lt 100 ]; do sleep 0.1; n=$((n + 1)); done
}

# no key: nothing happens
git config --unset intern.sync 2>/dev/null || true
printf '1\n' >> file.txt; git add file.txt; git commit -q -m s0
sleep 1
check "no intern.sync: tool never called" "$([ -e "$out" ] && echo yes || echo no)" "no"

# only the clone's own (local) key counts: the same key in a global config file or passed with "git -c" never runs anything
global_config="$work/global.gitconfig"
git config --file "$global_config" intern.sync "sh \"$tooldir/fake sync.sh\""
printf '1g\n' >> file.txt; git add file.txt
GIT_CONFIG_GLOBAL="$global_config" git commit -q -m s0-global
git -c intern.sync="sh \"$tooldir/fake sync.sh\"" commit -q --allow-empty -m s0-cli
git checkout -q -b side-global
printf 'g\n' > side-global.txt; git add side-global.txt; git commit -q -m side-global
git checkout -q -
GIT_CONFIG_GLOBAL="$global_config" git merge -q --no-ff -m merged-global side-global
git checkout -q -b side-cli
printf 'c\n' > side-cli.txt; git add side-cli.txt; git commit -q -m side-cli
git checkout -q -
git -c intern.sync="sh \"$tooldir/fake sync.sh\"" merge -q --no-ff -m merged-cli side-cli
sleep 1
check "intern.sync outside the local config: tool never called" "$([ -e "$out" ] && echo yes || echo no)" "no"

# key with a quoted path containing a space
git config intern.sync "sh \"$tooldir/fake sync.sh\""
printf '2\n' >> file.txt; git add file.txt; git commit -q -m s1
wait_for_lines 1
check "post-commit calls: save <root>" "$(sed -n '1p' "$out")" "save $root"
wait_for_log 'exit code'
check "post-commit log has the tool output" "$(grep -c 'tool output line' .run/intern-sync.log)" "1"
check "post-commit log has a timestamp line" "$(grep -c '^[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] [0-9:]* save$' .run/intern-sync.log)" "1"

# post-merge: restore <root>, synchronous
git checkout -q -b side
printf 'side\n' > side.txt; git add side.txt; git commit -q -m side
wait_for_log 'exit code'
sleep 1
git checkout -q -
: > "$out"
git merge -q --no-ff -m merged side
check "post-merge calls: restore <root>" "$(sed -n '1p' "$out")" "restore $root"
check "post-merge log has a restore line" "$(grep -c ' restore$' .run/intern-sync.log)" "1"

# a failing command never fails the commit or the merge
export FAKE_SYNC_EXIT=3
printf '3\n' >> file.txt; git add file.txt
if git commit -q -m s2; then ok "failing tool does not fail the commit"; else bad "failing tool failed the commit"; fi
sleep 1
git checkout -q -b side2
printf 'side2\n' > side2.txt; git add side2.txt; git commit -q -m side2
sleep 1
git checkout -q -
if git merge -q --no-ff -m merged2 side2; then ok "failing tool does not fail the merge"; else bad "failing tool failed the merge"; fi
check "the failing exit code is logged" "$(grep -c 'exit code 3' .run/intern-sync.log | awk '{ print ($1 >= 1) }')" "1"
unset FAKE_SYNC_EXIT

# a command that does not exist at all
git config intern.sync "/nonexistent/tool"
printf '4\n' >> file.txt; git add file.txt
if git commit -q -m s3; then ok "missing tool does not fail the commit"; else bad "missing tool failed the commit"; fi
sleep 1
git checkout -q -b side3
printf 'side3\n' > side3.txt; git add side3.txt; git commit -q -m side3
sleep 1
git checkout -q -
if git merge -q --no-ff -m merged3 side3; then ok "missing tool does not fail the merge"; else bad "missing tool failed the merge"; fi
git config --unset intern.sync

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
