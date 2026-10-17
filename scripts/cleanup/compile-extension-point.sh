#!/usr/bin/env bash
# Cleanup after the compile extension point (branch feat/compile-extension-point, merged into dev),
# written 2026-10-17. Removes the review's scratch in tmp/: the reviewers' and auditors' notes, the
# diff they read, the HTML report and its payload, the timer reproduction app and the Pare 0.5
# old-reader probe. Then deletes the merged branch. tmp/index.html, the report hub, is left alone.
#
#   scripts/cleanup/compile-extension-point.sh                             # dry run, changes nothing
#   scripts/cleanup/compile-extension-point.sh --execute --delete-scratch  # delete
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
if [ "$(git -C "$repo" branch --show-current)" != "dev" ]; then
    printf 'refuse: run this on dev, not %s\n' "$(git -C "$repo" branch --show-current)"
    exit 1
fi
failed=0
branch=feat/compile-extension-point
targets=(tmp/timercheck tmp/audit-svelte tmp/audit-concurrency.txt tmp/audit-oldreader.txt
    tmp/review-consequence.txt tmp/review-correctness.txt tmp/review-diff.patch
    tmp/review-feat-compile-extension-point.html tmp/review-feat-compile-extension-point.json
    tmp/full-test.log tmp/it.log)

# 1. Scratch in tmp/ (gitignored)
for rel in "${targets[@]}"; do
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

# 2. The merged branch. git branch -d refuses one that is not merged.
if ! git -C "$repo" rev-parse --verify --quiet "refs/heads/$branch" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$branch"
elif [ "$execute" -eq 1 ]; then
    printf 'git branch -d %s\n' "$branch"
    git -C "$repo" branch -d "$branch" || { printf '  refused: not merged. Left alone.\n'; failed=1; }
else
    printf 'would run: git branch -d %s\n' "$branch"
fi

# 3. Remaining state
printf '\nremaining:\n'
for rel in "${targets[@]}"; do
    [ -e "$repo/$rel" ] && printf '  %s\n' "$rel"
done
git -C "$repo" rev-parse --verify --quiet "refs/heads/$branch" >/dev/null && printf '  branch %s\n' "$branch"
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
