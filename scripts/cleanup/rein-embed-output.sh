#!/usr/bin/env bash
# Cleanup after the Rein handoff work (PareEmbed, PareOutput, 0.4.0), written 2026-10-17.
# Removes scratch files that session left in tmp/, and the handoff's original outside the repo
# once the tracked copy in docs/handoffs/ matches it.
#
#   scripts/cleanup/rein-embed-output.sh                             # dry run, changes nothing
#   scripts/cleanup/rein-embed-output.sh --execute --delete-scratch  # delete
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

act() {
    if [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
        printf 'delete: %s\n' "$1"
        rm -rf -- "$1"
    else
        printf 'would delete: %s\n' "$1"
    fi
}

# 1. Scratch in tmp/ (gitignored)
for rel in tmp/integration-1.log tmp/test-0.4.0.log tmp/biop tmp/rein-check; do
    if [ ! -e "$repo/$rel" ]; then
        printf 'skip:  %s already gone\n' "$rel"
        continue
    fi
    if git -C "$repo" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
        printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1
        continue
    fi
    act "$repo/$rel"
done

# 2. The handoff's original, outside the repo: only when the tracked copy is identical
original="$HOME/.claude/handoffs/pare/2026-10-17-rein-web-ui-needs.md"
copy="docs/handoffs/2026-10-17-rein-web-ui-needs.md"
if [ ! -e "$original" ]; then
    printf 'skip:  %s already gone\n' "$original"
elif ! git -C "$repo" ls-files --error-unmatch -- "$copy" >/dev/null 2>&1; then
    printf 'refuse: %s is not tracked, so the original is the only copy. Left alone.\n' "$copy"
    failed=1
elif ! cmp -s "$original" "$repo/$copy"; then
    printf 'refuse: %s differs from %s. Left alone.\n' "$original" "$copy"
    failed=1
else
    act "$original"
fi

# 3. Remaining state
printf '\nremaining:\n'
for path in "$repo/tmp/integration-1.log" "$repo/tmp/test-0.4.0.log" "$repo/tmp/biop" "$repo/tmp/rein-check" "$original"; do
    [ -e "$path" ] && printf '  %s\n' "$path"
done
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
