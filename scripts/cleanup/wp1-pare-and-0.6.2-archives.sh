#!/usr/bin/env bash
# Cleanup after building the 0.6.2 archives (2026-10-30). The scratch clone and bundle made on wp1
# for pack-cli.ps1 are replaced by the checkout at C:\home\code\extrode.com\pare, and the Mac
# scratch from the archive build goes too. tmp/dist (the archives) and the earlier cleanup scripts
# are left alone.
#
#   scripts/cleanup/wp1-pare-and-0.6.2-archives.sh                          # dry run
#   scripts/cleanup/wp1-pare-and-0.6.2-archives.sh --execute --delete-scratch
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

printf '== 1. Mac scratch\n'
for rel in tmp/wp1-bundle tmp/dist-win-0.6.2 tmp/verify-0.6.2 tmp/msdotnet tmp/msdotnet-installer; do
    [ -e "$repo/$rel" ] || continue
    if git -C "$repo" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
        printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        continue
    fi
    if [ "$act" = 1 ]; then rm -rf -- "${repo:?}/$rel"; printf 'removed %s\n' "$rel"
    else printf 'would remove %s\n' "$rel"; fi
done

printf '== 2. wp1 scratch clone and bundle\n'
# The new checkout must be in place and the scratch clone must be the bundle clone (no origin).
ok=$(ssh wp1 '
    if (-not (Test-Path C:\home\code\extrode.com\pare\.git)) { "no-checkout"; return }
    if (-not (Test-Path C:\Users\Sy\tmp\pare-0.6.2\.git)) { "gone"; return }
    $remote = git -C C:\Users\Sy\tmp\pare-0.6.2 remote get-url origin
    if ($remote -notlike "*pare-0.6.2.bundle") { "unexpected-origin"; return }
    "ok"' 2>/dev/null | tail -1 | tr -d '\r')
case "$ok" in
    ok)
        if [ "$act" = 1 ]; then
            ssh wp1 'Remove-Item -Recurse -Force C:\Users\Sy\tmp\pare-0.6.2, C:\Users\Sy\tmp\pare-0.6.2.bundle' 2>/dev/null
            printf 'removed wp1 C:\\Users\\Sy\\tmp\\pare-0.6.2 and pare-0.6.2.bundle\n'
        else printf 'would remove wp1 C:\\Users\\Sy\\tmp\\pare-0.6.2 and pare-0.6.2.bundle\n'; fi ;;
    gone) printf 'wp1 scratch clone already gone\n' ;;
    no-checkout) printf 'refuse: C:\\home\\code\\extrode.com\\pare is missing on wp1. Left alone.\n' ;;
    *) printf 'refuse: wp1 scratch clone is not the bundle clone (%s). Left alone.\n' "$ok" ;;
esac

printf '== remaining\n'
ls "$repo/tmp" | head -40
git -C "$repo" status --short
