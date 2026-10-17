#!/usr/bin/env bash
# Cleanup after the perf pass (branch perf/hot-path, merged into dev), written 2026-10-17.
# Removes tmp/perf: the golden snapshot, profiling drivers and their NativeAOT builds, the
# baseline source export (git archive of 4618174), CLI builds and output trees (~320 MB).
#
#   scripts/cleanup/perf-hot-path.sh                             # dry run, changes nothing
#   scripts/cleanup/perf-hot-path.sh --execute --delete-scratch  # delete
set -euo pipefail

execute=0
delete_scratch=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        --delete-scratch) delete_scratch=1 ;;
        *) printf 'unknown argument: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

repo="$(cd "$(dirname "$0")/../.." && pwd)"
if [ ! -f "$repo/Pare.slnx" ]; then
    printf 'refuse: %s is not the Pare repository\n' "$repo"
    exit 1
fi
failed=0

# 1. Scratch in tmp/ (gitignored)
for rel in tmp/perf; do
    if [ ! -e "$repo/$rel" ]; then
        printf 'skip:  %s already gone\n' "$rel"
        continue
    fi
    if [ -n "$(git -C "$repo" ls-files -- "$rel")" ]; then
        printf 'refuse: %s holds files tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1
        continue
    fi
    if [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
        printf 'delete: %s\n' "$rel"
        rm -rf -- "${repo:?}/$rel"
    else
        printf 'would delete: %s (%s)\n' "$rel" "$(du -sh "$repo/$rel" | cut -f1)"
    fi
done

# 2. wp1 (Windows test machine, over ssh): the bundle and logs in %TEMP%, outside any repo.
#    %TEMP%\pare is the long-lived test clone and stays; it is switched back to dev. Its
#    perf/hot-path branch stays too: wp1's dev lacks those commits, so `git branch -d` refuses,
#    and the next test run fetches a fresh bundle anyway.
if ! ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 exit 2>/dev/null; then
    printf 'skip:  wp1 unreachable, its leftovers left alone\n'
else
    for name in pare-perf-build.log pare-perf-test.log pare.bundle; do
        if ! ssh wp1 "if (Test-Path \$env:TEMP\\$name) { exit 0 } else { exit 1 }" 2>/dev/null; then
            printf 'skip:  wp1 %%TEMP%%\\%s already gone\n' "$name"
        elif [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
            printf 'delete: wp1 %%TEMP%%\\%s\n' "$name"
            ssh wp1 "Remove-Item -LiteralPath \$env:TEMP\\$name" 2>/dev/null || failed=1
        else
            printf 'would delete: wp1 %%TEMP%%\\%s\n' "$name"
        fi
    done
    current="$(ssh wp1 'git -C $env:TEMP\pare branch --show-current' 2>/dev/null | tr -d '\r')"
    if [ "$current" != "perf/hot-path" ]; then
        printf 'skip:  wp1 clone is on %s, not perf/hot-path\n' "${current:-?}"
    elif [ "$execute" -eq 1 ]; then
        printf 'switch: wp1 clone to dev\n'
        ssh wp1 'git -C $env:TEMP\pare checkout -q dev' 2>/dev/null || failed=1
    else
        printf 'would switch: wp1 clone from perf/hot-path to dev\n'
    fi
fi

# 3. Remaining state
printf '\nremaining:\n'
[ -e "$repo/tmp/perf" ] && printf '  %s\n' "$repo/tmp/perf"
ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 'Get-Item $env:TEMP\pare-perf-*.log, $env:TEMP\pare.bundle -ErrorAction SilentlyContinue | ForEach-Object { "  wp1 " + $_.FullName }; "  wp1 clone on " + (git -C $env:TEMP\pare branch --show-current)' 2>/dev/null | tr -d '\r' || true
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
