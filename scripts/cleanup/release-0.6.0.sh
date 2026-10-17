#!/usr/bin/env bash
# Cleanup after cutting 0.6.0 (tag v0.6.0), written 2026-10-17. Removes scratch: the wp1 bundle,
# the quickstart check, the release asset listing and the v0.6.0 worktree used for the Linux
# assets; deletes the merged release branches; on wp1, the clone goes back to dev (its origin,
# C:\Users\Sy\pare.bundle, stays). The release assets in tmp/dist, tmp/dist-lib and
# tmp/release-notes-0.6.0.md are kept until --delete-release-assets: upload them first.
#
#   scripts/cleanup/release-0.6.0.sh                                    # dry run, changes nothing
#   scripts/cleanup/release-0.6.0.sh --execute --delete-scratch         # delete scratch
#   scripts/cleanup/release-0.6.0.sh --execute --delete-release-assets  # also the 0.6.0 assets
set -euo pipefail

execute=0
delete_scratch=0
delete_assets=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        --delete-scratch) delete_scratch=1 ;;
        --delete-release-assets) delete_assets=1 ;;
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
worktree=.worktrees/release-0.6.0
scratch=(tmp/pare.bundle tmp/qs-0.6.0 tmp/rel-assets.txt)
assets=(tmp/release-notes-0.6.0.md)
for f in "$repo"/tmp/dist/*0.6.0* "$repo"/tmp/dist-lib/*0.6.0*; do
    [ -e "$f" ] && assets+=("${f#"$repo"/}")
done
branches=(chore/release-0.6.0 docs/release-0.6.0 chore/release-0.6.0-worktree chore/release-0.6.0-branches)

remove() { # rel allowed
    local rel="$1" allowed="$2"
    if [ ! -e "$repo/$rel" ]; then
        printf 'skip:  %s already gone\n' "$rel"
        return
    fi
    if [ -n "$(git -C "$repo" ls-files -- "$rel")" ]; then
        printf 'refuse: %s holds files tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1
        return
    fi
    if [ "$execute" -eq 1 ] && [ "$allowed" -eq 1 ]; then
        printf 'delete: %s\n' "$rel"
        rm -rf -- "${repo:?}/$rel"
    else
        printf 'would delete: %s (%s)\n' "$rel" "$(du -sh "$repo/$rel" | cut -f1)"
    fi
}

# 1. Scratch in tmp/ (gitignored)
for rel in "${scratch[@]}"; do remove "$rel" "$delete_scratch"; done

# 2. The worktree at v0.6.0. git worktree remove refuses one with changes.
if ! git -C "$repo" worktree list --porcelain | grep -qx "worktree $repo/$worktree"; then
    printf 'skip:  worktree %s already gone\n' "$worktree"
elif [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
    printf 'git worktree remove %s\n' "$worktree"
    git -C "$repo" worktree remove "$repo/$worktree" || { printf '  refused: it has changes. Left alone.\n'; failed=1; }
else
    printf 'would run: git worktree remove %s\n' "$worktree"
fi

# 3. Release assets, only with --delete-release-assets
for rel in "${assets[@]}"; do remove "$rel" "$delete_assets"; done

# 4. Merged branches. git branch -d refuses one that is not merged.
for branch in "${branches[@]}"; do
    if ! git -C "$repo" rev-parse --verify --quiet "refs/heads/$branch" >/dev/null; then
        printf 'skip:  branch %s no longer exists\n' "$branch"
    elif [ "$execute" -eq 1 ]; then
        printf 'git branch -d %s\n' "$branch"
        git -C "$repo" branch -d "$branch" || { printf '  refused: not merged. Left alone.\n'; failed=1; }
    else
        printf 'would run: git branch -d %s\n' "$branch"
    fi
done

# 5. wp1 (Windows test machine, over ssh). %TEMP%\pare is the long-lived test clone and stays;
#    it is switched from v0.6.0 back to dev. Its tmp\dist is the next pack run's to overwrite.
if ! ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 exit 2>/dev/null; then
    printf 'skip:  wp1 unreachable, its clone left alone\n'
else
    current="$(ssh -n wp1 'git -C $env:TEMP\pare describe --tags --exact-match HEAD' 2>/dev/null | tr -d '\r' || true)"
    branch="$(ssh -n wp1 'git -C $env:TEMP\pare branch --show-current' 2>/dev/null | tr -d '\r')"
    if [ -n "$branch" ] || [ "$current" != "v0.6.0" ]; then
        printf 'skip:  wp1 clone is on %s, not a detached v0.6.0\n' "${branch:-${current:-?}}"
    elif [ "$execute" -eq 1 ]; then
        printf 'switch: wp1 clone to dev\n'
        ssh -n wp1 'git -C $env:TEMP\pare checkout -q dev' 2>/dev/null || failed=1
    else
        printf 'would switch: wp1 clone from v0.6.0 to dev\n'
    fi
fi

# 6. Remaining state
printf '\nremaining:\n'
for rel in "${scratch[@]}" "${assets[@]}"; do
    [ -e "$repo/$rel" ] && printf '  %s\n' "$rel"
done
git -C "$repo" worktree list --porcelain | grep -qx "worktree $repo/$worktree" && printf '  worktree %s\n' "$worktree"
for branch in "${branches[@]}"; do
    git -C "$repo" rev-parse --verify --quiet "refs/heads/$branch" >/dev/null && printf '  branch %s\n' "$branch"
done
ssh -o BatchMode=yes -o ConnectTimeout=5 -n wp1 '"  wp1 clone on " + (git -C $env:TEMP\pare branch --show-current)' 2>/dev/null | tr -d '\r' || true
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
