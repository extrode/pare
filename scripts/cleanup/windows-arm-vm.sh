#!/usr/bin/env bash
# Produced 2026-10-15 by the win-arm64 check: a Windows 11 ARM64 VM under QEMU in tmp/winvm, after
# UTM 4.7.5 crashed on start and was uninstalled.
# Usage: scripts/cleanup/windows-arm-vm.sh                  dry run, changes nothing
#        scripts/cleanup/windows-arm-vm.sh -e [flags]       act; each removal needs its own flag:
#          --delete-vm-files         tmp/winvm (VM disk, Windows ISO, VM-only ssh key)
#          --delete-utm-containers   ~/Library/Containers/com.utmapp.* left by the UTM uninstall
#          --uninstall-qemu          brew uninstall qemu
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
execute=0 vm=0 utm=0 qemu=0 failed=0
for a in "$@"; do
    case $a in
        -e|--execute) execute=1 ;;
        --delete-vm-files) vm=1 ;;
        --delete-utm-containers) utm=1 ;;
        --uninstall-qemu) qemu=1 ;;
        *) printf 'unknown argument: %s\n' "$a"; exit 2 ;;
    esac
done
act() { if [ $execute -eq 1 ]; then printf 'run:   %s\n' "$*"; "$@" || { printf '  failed\n'; failed=1; }; else printf 'would: %s\n' "$*"; fi; }

branch=$(git -C "$repo" branch --show-current)
if [ "$branch" != dev ]; then printf 'refuse: on %s, expected dev\n' "$branch"; exit $execute; fi
if pgrep -f qemu-system-aarch64 >/dev/null; then printf 'refuse: the VM is running; shut it down first\n'; exit $execute; fi

# 1. tmp/winvm
rel=tmp/winvm
if [ ! -e "$repo/$rel" ]; then
    printf 'skip:  %s is gone\n' "$rel"
elif git -C "$repo" ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
    failed=1
elif [ $vm -eq 1 ]; then
    act rm -rf "${repo:?}/$rel"
else
    printf 'keep:  %s (%s); pass --delete-vm-files to remove\n' "$rel" "$(du -sh "$repo/$rel" | cut -f1 | tr -d " ")"
fi

# 2. UTM containers
for d in "$HOME"/Library/Containers/com.utmapp.*; do
    if [ ! -e "$d" ]; then printf 'skip:  no com.utmapp containers\n'; break; fi
    if [ -e /Applications/UTM.app ]; then printf 'keep:  %s (UTM is installed)\n' "$d"; continue; fi
    if [ $utm -eq 1 ]; then act rm -rf "$d"; else printf 'keep:  %s; pass --delete-utm-containers to remove\n' "$d"; fi
done

# 3. QEMU
if ! brew list --formula qemu >/dev/null 2>&1; then
    printf 'skip:  qemu is not installed\n'
elif [ $qemu -eq 1 ]; then
    act brew uninstall qemu
else
    printf 'keep:  qemu %s; pass --uninstall-qemu to remove\n' "$(brew list --versions qemu | cut -d' ' -f2)"
fi

printf '\nremaining:\n'
[ -e "$repo/$rel" ] && printf '  %s\n' "$rel"
ls -d "$HOME"/Library/Containers/com.utmapp.* 2>/dev/null | sed 's/^/  /' || true
brew list --versions qemu 2>/dev/null | sed 's/^/  /' || true
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
