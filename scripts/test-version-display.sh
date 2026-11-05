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
check display v2026.11.12 v2026.11.12.13.557 release "v2026.11.12 (13.557)"
check display v2026.11.12 v2026.11.12.13.557 beta "v2026.11.12 (13.557 $beta)"
check display v2026.11.12 v2026.11.12.13.557 nightly "v2026.11.12 (13.557 $alpha)"
check display v2026.11.12 v2026.11.12.13.557 dev "v2026.11.12 (13.557-dev)"
check display v2026.11.12 "" release "v2026.11.12"
check display v2026.11.12 "" beta "v2026.11.12"
check display v2026.11.12 "" nightly "v2026.11.12"
check display v2026.11.12 "" dev "v2026.11.12-dev"
check display v2026.11.12 v2026.11.11.13.557 dev "v2026.11.12-dev"
check display v2026.11.12 v2026.11.12.13 release "v2026.11.12"
check tag v2026.11.12 v2026.11.12.13.557 release "v2026.11.12"
check tag v2026.11.12 v2026.11.12.13.557 beta "v2026.11.12.13.557-beta"
check tag v2026.11.12 "" beta "v2026.11.12-beta"
check tag v2026.11.12 v2026.11.12.13.557 nightly "v2026.11.12.13.557-nightly"
check tag v2026.11.12 "" nightly "v2026.11.12-nightly"
check tag v2026.11.12 v2026.11.12.13.557 dev "v2026.11.12.13.557-dev"
check tag v2026.11.12 "" dev "v2026.11.12-dev"
[ "$fail" = 0 ] && echo "version-display.sh: alle Faelle ok"
exit "$fail"
