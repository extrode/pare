#!/usr/bin/env bash
# Produced 2026-10-15 by the 0.3.0 work: background agents each ran in a git worktree under
# .claude/worktrees/ on a branch that is now merged into dev.
# Usage: scripts/cleanup/agent-worktrees.sh        dry run, changes nothing
#        scripts/cleanup/agent-worktrees.sh -e     remove the worktrees, then `git branch -d` the
#                                                  branches (git refuses any that is not merged)
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
execute=0 failed=0
for a in "$@"; do
    case $a in
        -e|--execute) execute=1 ;;
        *) printf 'unknown argument: %s\n' "$a"; exit 2 ;;
    esac
done

branch=$(git -C "$repo" branch --show-current)
if [ "$branch" != dev ]; then printf 'refuse: on %s, expected dev\n' "$branch"; exit $execute; fi

# 1. Worktrees under .claude/worktrees, with the branch each one has checked out.
branches=()
while IFS= read -r line; do
    case $line in
        "worktree "*) path=${line#worktree } ;;
        "branch refs/heads/"*)
            [[ $path == "$repo/.claude/worktrees/"* ]] || continue
            b=${line#branch refs/heads/}
            branches+=("$b")
            if [ $execute -eq 0 ]; then
                printf 'would: git worktree remove %s (%s)\n' "$path" "$b"
            elif ! git -C "$repo" worktree remove "$path"; then
                printf '  refused: %s has changes. Left alone.\n' "$path"; failed=1
            fi ;;
    esac
done < <(git -C "$repo" worktree list --porcelain)
[ ${#branches[@]} -eq 0 ] && printf 'skip:  no worktrees under .claude/worktrees\n'
if [ $execute -eq 1 ]; then git -C "$repo" worktree prune; fi

# 2. Their branches, the worktree-agent-* placeholders the tool made, and the VS spike/plan
#    branches whose content reached dev through feat/vs-msbuild.
#    Unmerged ones are only listed: their commits are not in dev, and `git branch -D` is yours to run.
for b in $(printf '%s\n' "${branches[@]}" $(git -C "$repo" for-each-ref --format='%(refname:short)' 'refs/heads/worktree-agent-*') feat/vs-msbuild-spike docs/vs-msbuild-plan | awk '!seen[$0]++'); do
    if ! git -C "$repo" rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
        printf 'skip:  branch %s no longer exists\n' "$b"
    elif ! git -C "$repo" merge-base --is-ancestor "$b" dev; then
        printf 'keep:  %s has commits not in dev (%s)\n' "$b" "$(git -C "$repo" log -1 --format=%s "$b")"
    elif [ $execute -eq 0 ]; then
        printf 'would: git branch -d %s\n' "$b"
    elif ! git -C "$repo" branch -d "$b"; then
        printf '  refused: %s is not merged into dev. Left alone.\n' "$b"; failed=1
    fi
done

printf '\nremaining:\n'
git -C "$repo" worktree list | sed 's/^/  /'
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
