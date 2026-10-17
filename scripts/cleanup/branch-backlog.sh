#!/usr/bin/env bash
# Cleanup of the local branches that `git branch -d` refuses on dev, written 2026-10-17. The ones
# merged into dev (85 that day) are the global script's:
#
#   pwsh -NoProfile ~/.claude/scripts/cleanup/merged-branch-backlog.ps1 -Repos <this repo>      # dry run
#   pwsh -NoProfile ~/.claude/scripts/cleanup/merged-branch-backlog.ps1 -Repos <this repo> -e   # delete
#
# This one takes what that leaves. Each branch is deleted only if a run-time check finds its
# commits elsewhere:
#   worktree-agent-*     placeholders the agent tool made; deleted if the tip is in main
#   docs/vs-msbuild-plan deleted if `git cherry dev` finds every commit already applied to dev
# A branch checked out in a worktree is left alone. git branch -d would refuse these, since they
# are not merged into HEAD, so the delete is `git update-ref -d` against the tip that was checked.
#
#   scripts/cleanup/branch-backlog.sh                                       # dry run, changes nothing
#   scripts/cleanup/branch-backlog.sh --execute --delete-unmerged-branches  # delete
set -euo pipefail

execute=0
delete_unmerged=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        --delete-unmerged-branches) delete_unmerged=1 ;;
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
checked_out="$(git -C "$repo" worktree list --porcelain | sed -n 's:^branch refs/heads/::p')"

delete() { # branch reason
    local b="$1" reason="$2" tip
    tip="$(git -C "$repo" rev-parse "refs/heads/$b")"
    if printf '%s\n' "$checked_out" | grep -qxF -- "$b"; then
        printf 'refuse: %s is checked out in a worktree. Left alone.\n' "$b"
        failed=1
    elif [ "$execute" -eq 1 ] && [ "$delete_unmerged" -eq 1 ]; then
        printf 'delete: %s (%s, was %s)\n' "$b" "$reason" "${tip:0:7}"
        git -C "$repo" update-ref -d "refs/heads/$b" "$tip" || { printf '  refused by git. Left alone.\n'; failed=1; }
    else
        printf 'would delete: %s (%s, %s)\n' "$b" "$reason" "${tip:0:7}"
    fi
}

# 1. worktree-agent-* whose tip is in main
agents="$(git -C "$repo" for-each-ref --format='%(refname:short)' 'refs/heads/worktree-agent-*')"
[ -z "$agents" ] && printf 'skip:  no worktree-agent-* branches\n'
for b in $agents; do
    if git -C "$repo" merge-base --is-ancestor "refs/heads/$b" refs/heads/main; then
        delete "$b" "tip in main"
    else
        printf 'refuse: %s has commits that are not in main. Left alone.\n' "$b"
        failed=1
    fi
done

# 2. Branches whose commits dev has as equivalent patches
for b in docs/vs-msbuild-plan; do
    if ! git -C "$repo" rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
        printf 'skip:  branch %s no longer exists\n' "$b"
    elif git -C "$repo" cherry dev "refs/heads/$b" | grep -q '^+'; then
        printf 'refuse: %s has a commit dev does not have. Left alone.\n' "$b"
        failed=1
    else
        delete "$b" "every commit applied to dev"
    fi
done

# 3. Remaining state
printf '\nremaining:\n'
git -C "$repo" for-each-ref --format='  %(refname:short)' 'refs/heads/worktree-agent-*'
git -C "$repo" rev-parse --verify --quiet refs/heads/docs/vs-msbuild-plan >/dev/null && printf '  docs/vs-msbuild-plan\n'
printf '  %s branches merged into dev (merged-branch-backlog.ps1)\n' \
    "$(git -C "$repo" branch --merged dev --format='%(refname:short)' | grep -cvxE 'dev|main' || true)"
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
