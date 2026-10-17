#!/usr/bin/env bash
# Cleanup after libpare (branch feat/native-lib, merged into dev), written 2026-10-17.
# Removes the scratch of building and checking the C library: the osx-arm64 publish and smoke
# binary, the README example build, the pack scripts' libpare stage, the pack log and the bundle
# sent to wp1; on wp1, the bundle, logs, stage, unzip check and the P/Invoke fixture's publish
# directories (Windows keeps a loaded DLL locked, so the test cannot delete them).
# The libpare archives in tmp/dist stay: the next pack run overwrites them.
#
#   scripts/cleanup/native-lib.sh                             # dry run, changes nothing
#   scripts/cleanup/native-lib.sh --execute --delete-scratch  # delete
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
targets=(tmp/native tmp/native-stage tmp/native-lib.bundle tmp/pack-native.log tmp/smoke
         tmp/readme-app tmp/readme-app.c)

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

# 2. wp1 (Windows test machine, over ssh). %TEMP%\pare is the long-lived test clone and stays;
#    it is switched back to dev. Its feat/native-lib branch stays too: wp1's dev lacks those
#    commits, so `git branch -d` refuses, and the next run fetches a fresh bundle anyway.
if ! ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 exit 2>/dev/null; then
    printf 'skip:  wp1 unreachable, its leftovers left alone\n'
else
    wp1_targets="$(ssh wp1 '@(Get-Item $env:TEMP\pare-native.bundle, $env:TEMP\pare-native-pack.log, $env:TEMP\pare-native-test.log, $env:TEMP\pare\tmp\native-stage, $env:TEMP\pare\tmp\native-zip-check -ErrorAction SilentlyContinue) + @(Get-ChildItem $env:TEMP -Directory -Filter pare-native-*) | ForEach-Object FullName' 2>/dev/null | tr -d '\r')"
    if [ -z "$wp1_targets" ]; then
        printf 'skip:  wp1 leftovers already gone\n'
    fi
    while IFS= read -r path; do
        [ -z "$path" ] && continue
        if [ "$execute" -eq 1 ] && [ "$delete_scratch" -eq 1 ]; then
            printf 'delete: wp1 %s\n' "$path"
            ssh wp1 "Remove-Item -Recurse -Force -LiteralPath '$path'" 2>/dev/null || failed=1
        else
            printf 'would delete: wp1 %s\n' "$path"
        fi
    done <<< "$wp1_targets"
    current="$(ssh wp1 'git -C $env:TEMP\pare branch --show-current' 2>/dev/null | tr -d '\r')"
    if [ "$current" != "feat/native-lib" ]; then
        printf 'skip:  wp1 clone is on %s, not feat/native-lib\n' "${current:-?}"
    elif [ "$execute" -eq 1 ]; then
        printf 'switch: wp1 clone to dev\n'
        ssh wp1 'git -C $env:TEMP\pare checkout -q dev' 2>/dev/null || failed=1
    else
        printf 'would switch: wp1 clone from feat/native-lib to dev\n'
    fi
fi

# 3. Remaining state
printf '\nremaining:\n'
for rel in "${targets[@]}"; do
    [ -e "$repo/$rel" ] && printf '  %s\n' "$rel"
done
ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 '@(Get-Item $env:TEMP\pare-native.bundle, $env:TEMP\pare-native-*.log, $env:TEMP\pare\tmp\native-stage, $env:TEMP\pare\tmp\native-zip-check -ErrorAction SilentlyContinue) + @(Get-ChildItem $env:TEMP -Directory -Filter pare-native-*) | ForEach-Object { "  wp1 " + $_.FullName }; "  wp1 clone on " + (git -C $env:TEMP\pare branch --show-current)' 2>/dev/null | tr -d '\r' || true
printf 'done\n'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
