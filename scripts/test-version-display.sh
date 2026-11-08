#!/bin/sh
# Pins scripts/version-display.sh to the same table as VersionFormatTests.cs. Run: sh scripts/test-version-display.sh
cd "$(dirname "$0")" || exit 1
fail=0
beta=$(printf '\316\262')
alpha=$(printf '\316\261')
check() {
    actual=$(sh ./version-display.sh "$1" "$2" "$3" "$4")
    if [ "$actual" != "$5" ]; then
        echo "FAIL: $1 '$2' '$3' $4 -> '$actual' (expected '$5')"
        fail=1
    fi
}
check display v2026.11.12 v2026.11.12.13.557 release "v2026.11.12.13.557"
check display v2026.11.12 v2026.11.12.13.557 beta "v2026.11.12.13.557 ($beta)"
check display v2026.11.12 v2026.11.12.13.557 nightly "v2026.11.12.13.557 ($alpha)"
# Without counters: the plain version plus the symbol only if there is one, never empty parentheses
check display v2026.11.12 "" release "v2026.11.12"
check display v2026.11.12 "" beta "v2026.11.12 ($beta)"
check display v2026.11.12 "" nightly "v2026.11.12 ($alpha)"
# A build id that is not "<short>.<prompts>.<lines>" carries no counters
check display v2026.11.12 v2026.11.12.13 release "v2026.11.12"
check display v2026.11.12 v2026.11.11.13.557 beta "v2026.11.12 ($beta)"
check tag v2026.11.12 v2026.11.12.13.557 release "v2026.11.12"
check tag v2026.11.12 v2026.11.12.13.557 beta "v2026.11.12.13.557-beta"
check tag v2026.11.12 "" beta "v2026.11.12-beta"
check tag v2026.11.12 v2026.11.12.13.557 nightly "v2026.11.12.13.557-nightly"
check tag v2026.11.12 "" nightly "v2026.11.12-nightly"
# Dev has no tag and no package: it is not a kind of this script
rejects() {
    if sh ./version-display.sh "$1" v2026.11.12 v2026.11.12.13.557 "$2" >/dev/null 2>&1; then
        echo "FAIL: $1 with kind '$2' must be rejected"
        fail=1
    fi
}
rejects display dev
rejects tag dev
rejects tag bogus
[ "$fail" = 0 ] && echo "version-display.sh: alle Faelle ok"
exit "$fail"
