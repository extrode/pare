#!/usr/bin/env bash
# Cleanup after the 2026-10-30 audit. Removes scratch (the official-SDK install, the stranger
# project, downloaded 0.6.0 archives, logs) and deletes the merged fix branches. The 0.6.1 release
# assets in tmp/dist are kept until --delete-release-assets: upload them first.
# On wp1 the clone C:\Users\Sy\pare-audit, pare-audit.bundle and patch.ps1 are left for you:
#   ssh wp1 "pwsh -Command Remove-Item -Recurse -Force C:\Users\Sy\pare-audit, C:\Users\Sy\pare-audit.bundle, C:\Users\Sy\patch.ps1"
#
#   scripts/cleanup/audit-2026-10-30.sh                                    # dry run
#   scripts/cleanup/audit-2026-10-30.sh --execute --delete-scratch         # scratch and branches
#   scripts/cleanup/audit-2026-10-30.sh --execute --delete-release-assets  # also tmp/dist 0.6.1 files
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
[ -f "$repo/Pare.slnx" ] || { printf 'refuse: %s is not the Pare repository\n' "$repo"; exit 1; }
[ "$(git -C "$repo" branch --show-current)" = dev ] || { printf 'refuse: not on dev\n'; exit 1; }

failed=0
remove() { # relative-path
    local rel="$1"
    [ -e "$repo/$rel" ] || return 0
    if git -C "$repo" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
        printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1
        return 0
    fi
    if [ "$execute" = 1 ] && [ "$2" = yes ]; then rm -rf -- "${repo:?}/$rel"; printf 'removed %s\n' "$rel"
    else printf 'would remove %s\n' "$rel"; fi
}

printf '== 1. scratch\n'
for rel in tmp/audit tmp/stranger tmp/guide tmp/pk tmp/docs-src tmp/shot.py tmp/bad.ts dist/docs-site; do
    remove "$rel" "$([ "$delete_scratch" = 1 ] && echo yes || echo no)"
done
for f in "$repo"/tmp/shot-*.png; do [ -e "$f" ] && remove "tmp/$(basename "$f")" "$([ "$delete_scratch" = 1 ] && echo yes || echo no)"; done

printf '== 2. merged branches\n'
for b in fix/macos-archives-system-libs fix/windows-ci-tests fix/review-2026-10-30-1 docs/community-files; do
    git -C "$repo" show-ref --verify --quiet "refs/heads/$b" || continue
    if [ "$execute" = 1 ] && [ "$delete_scratch" = 1 ]; then git -C "$repo" branch -d "$b"
    else printf 'would delete branch %s (git branch -d, refuses if unmerged)\n' "$b"; fi
done

printf '== 3. 0.6.1 release assets in tmp/dist\n'
for f in "$repo"/tmp/dist/*0.6.1*; do
    [ -e "$f" ] || continue
    rel="tmp/dist/$(basename "$f")"
    remove "$rel" "$([ "$delete_assets" = 1 ] && echo yes || echo no)"
done

printf '== state\n'; git -C "$repo" status -sb | head -3
exit $failed
