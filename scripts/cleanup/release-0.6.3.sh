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
           tmp/audit-prompt.md tmp/research-prompt.md tmp/audit-out.txt tmp/research-out.txt \
           tmp/verify.bundle tmp/verify2.bundle tmp/verify3.bundle tmp/stdin-test.ps1 tmp/glob-test.ps1; do
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
for b in chore/release-0.6.3 fix/release-github-packages docs/progress-0.6.3 \
         chore/cleanup-release-0.6.3 fix/audit-0.6.3 fix/glob-drive-root chore/cleanup-wp1-0.6.3 fix/review-2026-10-31-1 fix/audit-followups-2 fix/review-2026-10-31-2; do
    if ! git -C "$repo" rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
        printf 'skip:  branch %s no longer exists\n' "$b"; continue
    fi
    if [ "$execute" = 1 ]; then
        if git -C "$repo" branch -d "$b"; then :; else printf '  refused: not merged. Left alone.\n'; failed=1; fi
    else printf 'would run: git branch -d %s\n' "$b"; fi
done

printf '== 3. wp1 verification worktree and scratch (2026-10-31)\n'
# The branches tmp-verify, tmp-verify2 and tmp-verify3 stay on wp1: `git branch -d` refuses them until
# its dev has these commits, and this script does not force. A worktree of the wp1 checkout at C:\Users\Sy\tmp\pare-verify, scratch files in C:\Users\Sy\tmp. Only with --delete-scratch.
if [ "$act" = 1 ]; then
    ssh wp1 'cd C:\home\code\extrode.com\pare; git worktree remove --force C:\Users\Sy\tmp\pare-verify; git worktree prune; Remove-Item -Recurse -Force C:\Users\Sy\tmp\glob-test, C:\Users\Sy\tmp\stdin-test, C:\Users\Sy\tmp\verify.bundle, C:\Users\Sy\tmp\verify2.bundle, C:\Users\Sy\tmp\verify3.bundle, C:\Users\Sy\tmp\glob-test.ps1, C:\Users\Sy\tmp\stdin-test.ps1 -ErrorAction SilentlyContinue; "wp1 scratch removed"' 2>&1 | grep -v 'quantum\|store now\|server may\|vulnerable\|upgraded'
else printf 'would remove the wp1 worktree pare-verify, and the scratch files in C:\\Users\\Sy\\tmp\n'; fi

printf '== Remaining\n'
git -C "$repo" branch --no-merged dev
git -C "$repo" status --short | head -5
[ "$execute" = 1 ] && [ "$failed" = 1 ] && exit 1
exit 0
