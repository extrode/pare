#!/usr/bin/env bash
# Cleanup of the built-in agent worktrees under .claude/worktrees/agent-*, written 2026-10-17
# (branch chore/wp1-perf-cleanup). Their copies of the bench csproj stop BenchmarkDotNet's
# out-of-process run. Removes each worktree whose commits are in dev and which has no changes;
# branches stay (merged-branch-backlog handles those).
#
#   scripts/cleanup/stale-agent-worktrees.sh      # dry run, changes nothing
#   scripts/cleanup/stale-agent-worktrees.sh -e   # remove
set -euo pipefail

execute=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        *) printf 'unknown argument: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

repo="$(cd "$(dirname "$0")/../.." && pwd)"
if [ ! -f "$repo/Pare.slnx" ]; then
    printf 'refuse: %s is not the Pare repository\n' "$repo"
    exit 1
fi
if ! git -C "$repo" rev-parse --verify --quiet refs/heads/dev >/dev/null; then
    printf 'refuse: no dev branch to compare against\n'
    exit 1
fi
failed=0

# 1. Worktrees: merged into dev and clean, else left alone
found=0
for dir in "$repo"/.claude/worktrees/agent-*; do
    [ -d "$dir" ] || continue
    found=1
    rel="${dir#"$repo"/}"
    head="$(git -C "$dir" rev-parse HEAD)"
    # In dev as an ancestor, or every commit's patch already applied there (cherry-picked)
    if ! git -C "$repo" merge-base --is-ancestor "$head" dev &&
       git -C "$repo" cherry dev "$head" | grep -q '^+'; then
        printf 'keep:  %s (%s not in dev)\n' "$rel" "$(git -C "$dir" branch --show-current)"
        continue
    fi
    if [ -n "$(git -C "$dir" status --porcelain)" ]; then
        printf 'keep:  %s (uncommitted changes)\n' "$rel"
        continue
    fi
    if [ "$execute" -eq 1 ]; then
        printf 'remove: %s\n' "$rel"
        git -C "$repo" worktree remove "$dir" || { printf '  refused. Left alone.\n'; failed=1; }
    else
        printf 'would remove: %s\n' "$rel"
    fi
done
[ "$found" -eq 1 ] || printf 'skip:  no agent worktrees left\n'
[ "$execute" -eq 1 ] && git -C "$repo" worktree prune

# 2. Remaining state
printf '\nremaining:\n'
git -C "$repo" worktree list | grep 'claude/worktrees/agent-' | sed 's/^/  /' || true
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
