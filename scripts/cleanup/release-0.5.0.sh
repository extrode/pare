#!/usr/bin/env bash
# Cleanup after cutting 0.5.0 (tag v0.5.0, branch chore/release-0.5.0), written 2026-10-17.
# Removes scratch: the version check, the wp1 bundle, test, pack and benchmark logs; on wp1, the
# bundle and logs, and the clone goes back to dev. The release assets in tmp/dist, tmp/dist-lib
# and tmp/release-notes-0.5.0.md are kept until --delete-release-assets: upload them first.
#
#   scripts/cleanup/release-0.5.0.sh                                    # dry run, changes nothing
#   scripts/cleanup/release-0.5.0.sh --execute --delete-scratch         # delete scratch
#   scripts/cleanup/release-0.5.0.sh --execute --delete-release-assets  # also the 0.5.0 assets
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
failed=0
scratch=(tmp/rel-check tmp/pare-0.5.0.bundle tmp/test-0.5.0.log tmp/pack-0.5.0.log
         tmp/bench-compare.log tmp/bench-bdn.log)
assets=(tmp/release-notes-0.5.0.md)
for f in "$repo"/tmp/dist/*0.5.0* "$repo"/tmp/dist-lib/*0.5.0*; do
    [ -e "$f" ] && assets+=("${f#"$repo"/}")
done

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

# 2. Release assets, only with --delete-release-assets
for rel in "${assets[@]}"; do remove "$rel" "$delete_assets"; done

# 3. wp1 (Windows test machine, over ssh). %TEMP%\pare is the long-lived test clone and stays;
#    it is switched from v0.5.0 back to dev. Its tmp\dist is the next pack run's to overwrite.
if ! ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 exit 2>/dev/null; then
    printf 'skip:  wp1 unreachable, its leftovers left alone\n'
else
    for name in pare.bundle pare-0.5.0-pack.log pare-0.5.0-test.log; do
        if ! ssh -n wp1 "if (Test-Path \$env:TEMP\\$name) { exit 0 } else { exit 1 }" 2>/dev/null; then
            printf 'skip:  wp1 %%TEMP%%\\%s already gone\n' "$name"
        elif [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
            printf 'delete: wp1 %%TEMP%%\\%s\n' "$name"
            ssh -n wp1 "Remove-Item -LiteralPath \$env:TEMP\\$name" 2>/dev/null || failed=1
        else
            printf 'would delete: wp1 %%TEMP%%\\%s\n' "$name"
        fi
    done
    current="$(ssh -n wp1 'git -C $env:TEMP\pare describe --tags --exact-match HEAD' 2>/dev/null | tr -d '\r' || true)"
    branch="$(ssh -n wp1 'git -C $env:TEMP\pare branch --show-current' 2>/dev/null | tr -d '\r')"
    if [ -n "$branch" ] || [ "$current" != "v0.5.0" ]; then
        printf 'skip:  wp1 clone is on %s, not a detached v0.5.0\n' "${branch:-${current:-?}}"
    elif [ "$execute" -eq 1 ]; then
        printf 'switch: wp1 clone to dev\n'
        ssh -n wp1 'git -C $env:TEMP\pare checkout -q dev' 2>/dev/null || failed=1
    else
        printf 'would switch: wp1 clone from v0.5.0 to dev\n'
    fi
fi

# 4. Remaining state
printf '\nremaining:\n'
for rel in "${scratch[@]}" "${assets[@]}"; do
    [ -e "$repo/$rel" ] && printf '  %s\n' "$rel"
done
ssh -o BatchMode=yes -o ConnectTimeout=5 -n wp1 'Get-Item $env:TEMP\pare.bundle, $env:TEMP\pare-0.5.0-*.log -ErrorAction SilentlyContinue | ForEach-Object { "  wp1 " + $_.FullName }; "  wp1 clone on " + (git -C $env:TEMP\pare branch --show-current)' 2>/dev/null | tr -d '\r' || true
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
