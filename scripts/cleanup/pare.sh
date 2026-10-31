#!/usr/bin/env bash
# The one cleanup script for this repository, written 2026-10-31. It replaces the fifteen
# per-work scripts (agent worktrees, audits, branch backlog, compile extension point, icon, native
# lib, perf, Rein embed, releases 0.5.0 to 0.7.0, Svelte spike, Windows ARM VM, wp1 clone), which
# are in git history. Each section below is one of their jobs, or what this session left behind.
# Every target is checked when it is handled: absent is a skip, tracked by git is a refusal.
#
#   scripts/cleanup/pare.sh                      # dry run, changes nothing
#   scripts/cleanup/pare.sh -e                   # act: delete merged branches (git branch -d)
#   scripts/cleanup/pare.sh -e --delete-scratch  # also remove the scratch under tmp/ and on wp1
#   scripts/cleanup/pare.sh -e --delete-vm-files --delete-utm-containers --uninstall-qemu
#                                                # the Windows ARM VM leftovers, one flag each
#
# Branches: every local branch merged into dev except dev, main and the current one. git branch -d
# decides; a branch it refuses is reported and left alone. Re-run it whenever branches pile up.
set -euo pipefail

execute=0 scratch=0 vm=0 utm=0 qemu=0 failed=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        --delete-scratch) scratch=1 ;;
        --delete-vm-files) vm=1 ;;
        --delete-utm-containers) utm=1 ;;
        --uninstall-qemu) qemu=1 ;;
        *) printf 'unknown argument: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

repo="$(cd "$(dirname "$0")/../.." && pwd)"
[ -f "$repo/Pare.slnx" ] || { printf 'refuse: %s is not the Pare repository\n' "$repo"; exit 1; }
[ "$(git -C "$repo" branch --show-current)" = dev ] || { printf 'refuse: run this on dev\n'; exit 1; }

# remove_path <repo-relative path>: only with -e --delete-scratch
remove_path() {
    local rel="$1"
    if [ ! -e "$repo/$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; return 0; fi
    if [ -n "$(git -C "$repo" ls-files -- "$rel")" ]; then
        printf 'refuse: %s holds files tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1; return 0
    fi
    if [ "$execute" = 1 ] && [ "$scratch" = 1 ]; then
        rm -rf -- "${repo:?}/$rel"; printf 'removed %s\n' "$rel"
    else
        printf 'would remove %s (%s)\n' "$rel" "$(du -sh "$repo/$rel" | cut -f1 | tr -d ' ')"
    fi
}

printf '== 1. Scratch under tmp/ (gitignored)\n'
# tmp/issues (the musl issue draft) and tmp/dist's real archives stay.
for rel in tmp/docs-src tmp/full-test.log tmp/native-stage \
           tmp/assets-0.6.3 tmp/dist-win-0.6.3 tmp/tool-check tmp/msdotnet tmp/msdotnet-installer \
           tmp/pack-cli.log tmp/review-range.diff tmp/release.yml.good tmp/README.bak \
           tmp/audit-prompt.md tmp/research-prompt.md tmp/audit-out.txt tmp/research-out.txt \
           tmp/verify.bundle tmp/verify2.bundle tmp/verify3.bundle tmp/verify4.bundle \
           tmp/stdin-test.ps1 tmp/glob-test.ps1 tmp/aot-070 tmp/e2e \
           tmp/bench-0.7.0.log tmp/bench-0.7.0b.log tmp/bench-nofix.log tmp/pack-cli-0.7.0.log \
           tmp/ci-mac-fail.log tmp/ci-124408059288.log tmp/ci-124408059321.log \
           tmp/dev.bundle tmp/dev2.bundle tmp/fix.bundle tmp/hook tmp/fakebin tmp/trx tmp/compare \
           tmp/packtest tmp/want.sh tmp/osx-job.log tmp/docs-build.log tmp/arts tmp/ci tmp/cl.out tmp/utmtest tmp/rel071 \
           tmp/hr tmp/hr-hotreload.log tmp/hr-dev.log tmp/hr-project.log tmp/hr-run.sh tmp/hr-run-root.sh \
           tmp/dedupe.proj tmp/wwwroot tmp/bump-test.log tmp/dev-test.log \
           tmp/rerun3.log tmp/unit.log tmp/break.log tmp/nosrc.log tmp/review-dev-diff.patch tmp/review-low.md; do
    remove_path "$rel"
done
for f in "$repo"/tmp/dist/*0.7.1-test*; do
    [ -e "$f" ] && remove_path "${f#"$repo"/}"
done

printf '== 2. wp1 leftovers (over ssh)\n'
if ! ssh -o BatchMode=yes -o ConnectTimeout=5 wp1 exit 2>/dev/null; then
    printf 'skip:  wp1 unreachable, its leftovers left alone\n'
else
    wp1_scratch='C:\Users\Sy\AppData\Local\Temp\pare\tmp\native-stage, C:\home\code\extrode.com\dev.bundle, C:\home\code\extrode.com\fix.bundle, C:\Users\Sy\fix.bundle, C:\Users\Sy\dedupe.proj, C:\Users\Sy\wwwroot, C:\home\code\extrode.com\pare\tmp\devpath'
    if [ "$execute" = 1 ] && [ "$scratch" = 1 ]; then
        ssh wp1 "Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $wp1_scratch; Get-ChildItem \$env:TEMP -Directory -Filter pare-native-* | Remove-Item -Recurse -Force; cd C:\\home\\code\\extrode.com\\pare; git worktree remove C:\\Users\\Sy\\tmp\\pare-verify 2>\$null; git worktree prune; git switch -q dev; git branch -D fix/duplicate-source-metadata 2>\$null; 'wp1 scratch removed'" 2>&1 | grep -v 'quantum\|store now\|server may\|vulnerable\|upgraded' || true
    else
        printf 'would remove on wp1: %s, %%TEMP%%\\pare-native-*, the pare-verify worktree, and the wp1 checkout back on dev without fix/duplicate-source-metadata\n' "$wp1_scratch"
    fi
fi

printf '== 3. Merged branches (git branch -d)\n'
checked_out="$(git -C "$repo" worktree list --porcelain | sed -n 's:^branch refs/heads/::p')"
merged="$(git -C "$repo" for-each-ref --merged dev --format='%(refname:short)' refs/heads/ | grep -vxE 'dev|main' || true)"
if [ -z "$merged" ]; then printf 'skip:  no merged branches\n'; fi
for b in $merged; do
    if printf '%s\n' "$checked_out" | grep -qxF -- "$b"; then
        printf 'refuse: %s is checked out in a worktree. Left alone.\n' "$b"; failed=1; continue
    fi
    if [ "$execute" = 1 ]; then
        git -C "$repo" branch -d "$b" || { printf '  refused by git. Left alone.\n'; failed=1; }
    else
        printf 'would run: git branch -d %s\n' "$b"
    fi
done

printf '== 4. Windows ARM VM leftovers (each removal needs its own flag)\n'
rel=tmp/winvm
if [ ! -e "$repo/$rel" ]; then printf 'skip:  %s is gone\n' "$rel"
elif [ "$vm" = 1 ]; then
    if pgrep -f qemu-system-aarch64 >/dev/null; then printf 'refuse: the VM is running\n'; failed=1
    else [ "$execute" = 1 ] && { rm -rf -- "${repo:?}/$rel"; printf 'removed %s\n' "$rel"; } || printf 'would remove %s\n' "$rel"; fi
else printf 'keep:  %s; pass --delete-vm-files to remove\n' "$rel"; fi
# UTM was tried for the VM and replaced by plain QEMU; these are its empty sandbox folders.
# macOS lets only Finder, or a terminal with Full Disk Access, remove another app's container.
utm_denied=0
for d in "$HOME"/Library/Containers/com.utmapp.* "$HOME"/Library/Group\ Containers/*.com.utmapp.*; do
    [ -e "$d" ] || continue
    if [ -e /Applications/UTM.app ]; then printf 'keep:  %s (UTM is installed)\n' "$d"; continue; fi
    if [ "$utm" = 1 ]; then
        if [ "$execute" = 0 ]; then printf 'would remove %s\n' "$d"
        elif rm -rf -- "$d" 2>/dev/null; then printf 'removed %s\n' "$d"
        else printf 'denied: %s\n' "$d"; utm_denied=1; failed=1; fi
    else printf 'keep:  %s; pass --delete-utm-containers to remove\n' "$d"; fi
done
if [ "$utm_denied" = 1 ]; then
    printf '        macOS protects app containers. Drag the denied folders to the Trash in Finder, or give\n'
    printf '        your terminal Full Disk Access (System Settings > Privacy & Security) and run this again.\n'
fi
if ! brew list --formula qemu >/dev/null 2>&1; then printf 'skip:  qemu is not installed\n'
elif [ "$qemu" = 1 ]; then
    if [ "$execute" = 1 ]; then brew uninstall qemu || failed=1; else printf 'would run: brew uninstall qemu\n'; fi
else printf 'keep:  qemu; pass --uninstall-qemu to remove\n'; fi

printf '\n== Remaining\n'
printf 'branches not merged into dev:\n'; git -C "$repo" branch --no-merged dev | sed 's/^/  /'
git -C "$repo" worktree list | sed 's/^/  worktree: /'
git -C "$repo" status --short | head -5
[ "$execute" = 1 ] && [ "$failed" = 1 ] && exit 1
exit 0
