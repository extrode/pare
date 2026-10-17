#!/usr/bin/env bash
# Cleanup after the Svelte tier 1 spike (branch docs/svelte-spike, merged into dev), written 2026-10-17.
# Removes the spike's scratch: the npm install of svelte 5.57.2, the ClearScript console and
# MSBuild task projects, the reference outputs and the archive sent to wp1; on wp1, the extracted
# copy and the archive. The spike's sources now live in ../husk/prototypes/001-engine-spike.
#
#   scripts/cleanup/svelte-spike.sh                             # dry run, changes nothing
#   scripts/cleanup/svelte-spike.sh --execute --delete-scratch  # delete
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
targets=(tmp/svelte-spike tmp/svelte-spike-win.tgz)

# 1. Scratch in tmp/ (gitignored)
for rel in "${targets[@]}"; do
    if [ ! -e "$repo/$rel" ]; then
        printf 'skip:  %s already gone\n' "$rel"
        continue
    fi
    if [ -n "$(git -C "$repo" ls-files -- "$rel")" ]; then
        printf 'refuse: %s holds files tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1
        continue
    fi
    if [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
        printf 'delete: %s\n' "$rel"
        rm -rf -- "${repo:?}/$rel"
    else
        printf 'would delete: %s (%s)\n' "$rel" "$(du -sh "$repo/$rel" | cut -f1)"
    fi
done

# 2. wp1 (Windows test machine, over ssh). The Pare clone at %TEMP%\pare is not touched.
if ! ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 exit 2>/dev/null; then
    printf 'skip:  wp1 unreachable, its leftovers left alone\n'
else
    for name in svelte-spike svelte-spike-win.tgz; do
        if ! ssh -n wp1 "if (Test-Path \$env:TEMP\\$name) { exit 0 } else { exit 1 }" 2>/dev/null; then
            printf 'skip:  wp1 %%TEMP%%\\%s already gone\n' "$name"
        elif [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
            printf 'delete: wp1 %%TEMP%%\\%s\n' "$name"
            ssh -n wp1 "Remove-Item -Recurse -Force -LiteralPath \$env:TEMP\\$name" 2>/dev/null || failed=1
        else
            printf 'would delete: wp1 %%TEMP%%\\%s\n' "$name"
        fi
    done
fi

# 3. Remaining state
printf '\nremaining:\n'
for rel in "${targets[@]}"; do
    [ -e "$repo/$rel" ] && printf '  %s\n' "$rel"
done
ssh -o BatchMode=yes -o ConnectTimeout=5 -n wp1 'Get-Item $env:TEMP\svelte-spike, $env:TEMP\svelte-spike-win.tgz -ErrorAction SilentlyContinue | ForEach-Object { "  wp1 " + $_.FullName }' 2>/dev/null | tr -d '\r' || true
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
