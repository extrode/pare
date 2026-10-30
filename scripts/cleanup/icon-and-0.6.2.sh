#!/usr/bin/env bash
# Cleanup after the 2026-10-30 icon work and the 0.6.2 release prep. Removes the untracked icon
# drafts under tmp/ and deletes branches already merged into dev (git branch -d refuses an
# unmerged one). The 0.6.0 and 0.6.1 files in tmp/dist, tmp/feed and the cleanup scripts of earlier
# work are left alone.
#
#   scripts/cleanup/icon-and-0.6.2.sh                          # dry run
#   scripts/cleanup/icon-and-0.6.2.sh --execute --delete-scratch
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

act=0
[ "$execute" = 1 ] && [ "$delete_scratch" = 1 ] && act=1

printf '== 1. icon drafts\n'
for rel in tmp/icon tmp/icon2 tmp/icon3 tmp/icon4 tmp/run-step.py; do
    [ -e "$repo/$rel" ] || continue
    if git -C "$repo" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
        printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        continue
    fi
    if [ "$act" = 1 ]; then rm -rf -- "${repo:?}/$rel"; printf 'removed %s\n' "$rel"
    else printf 'would remove %s\n' "$rel"; fi
done

printf '== 2. branches merged into dev\n'
for b in feat/pare-icon feat/pare-icon-p fix/readme-logo-png fix/release-existing-release \
    chore/cleanup-script-docs-site chore/pages docs/contributor-cla chore/cleanup-v060-worktree \
    fix/review-2026-10-30-3 chore/audit-2026-10-30 docs/site-and-readme chore/release-0.6.1 \
    docs/handoff-husk-release fix/pare200-same-name-output chore/release-0.6.2 chore/cleanup-script-0.6.2; do
    git -C "$repo" show-ref --verify --quiet "refs/heads/$b" || continue
    if [ "$act" = 1 ]; then git -C "$repo" branch -d "$b"
    else printf 'would delete branch %s (git branch -d, refuses if unmerged)\n' "$b"; fi
done

printf '== remaining\n'
git -C "$repo" branch
git -C "$repo" status --short
