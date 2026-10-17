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

# 2. Not handled here: on wp1, %TEMP%\pare-perf-build.log, %TEMP%\pare-perf-test.log and
#    %TEMP%\pare.bundle, and the perf/hot-path branch in %TEMP%\pare.

# 3. Remaining state
printf '\nremaining:\n'
[ -e "$repo/tmp/perf" ] && printf '  %s\n' "$repo/tmp/perf"
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
