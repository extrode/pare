#!/usr/bin/env bash
# Cleanup after the 0.6.3 release (2026-10-31): the scratch from packing the archives and from
# checking the CLI install, and the three branches this release merged into dev. tmp/dist (the
# archives packed on this Mac) and the earlier cleanup scripts are left alone.
#
#   scripts/cleanup/release-0.6.3.sh                          # dry run
#   scripts/cleanup/release-0.6.3.sh --execute --delete-scratch
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
[ -f "$repo/Pare.slnx" ] || { printf 'refuse: %s is not the Pare repository\n' "$repo"; exit 1; }
[ "$(git -C "$repo" branch --show-current)" = dev ] || { printf 'refuse: not on dev\n'; exit 1; }

failed=0
act=0
[ "$execute" = 1 ] && [ "$delete_scratch" = 1 ] && act=1

printf '== 1. Scratch under tmp/\n'
for rel in tmp/assets-0.6.3 tmp/dist-win-0.6.3 tmp/tool-check tmp/msdotnet tmp/msdotnet-installer \
           tmp/pack-cli.log tmp/review-range.diff tmp/release.yml.good tmp/README.bak \
           tmp/audit-prompt.md tmp/research-prompt.md; do
    if [ ! -e "$repo/$rel" ]; then printf 'skip:  %s is already gone\n' "$rel"; continue; fi
    if git -C "$repo" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
        printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1
        continue
    fi
    if [ "$act" = 1 ]; then rm -rf -- "${repo:?}/$rel"; printf 'removed %s\n' "$rel"
    else printf 'would remove %s\n' "$rel"; fi
done

printf '== 2. Merged branches\n'
for b in chore/release-0.6.3 fix/release-github-packages docs/progress-0.6.3; do
    if ! git -C "$repo" rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
        printf 'skip:  branch %s no longer exists\n' "$b"; continue
    fi
    if [ "$execute" = 1 ]; then
        if git -C "$repo" branch -d "$b"; then :; else printf '  refused: not merged. Left alone.\n'; failed=1; fi
    else printf 'would run: git branch -d %s\n' "$b"; fi
done

printf '== Remaining\n'
git -C "$repo" branch --no-merged dev
git -C "$repo" status --short | head -5
[ "$execute" = 1 ] && [ "$failed" = 1 ] && exit 1
exit 0
