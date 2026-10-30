#!/bin/sh
# Fails when a macOS binary loads any library outside /usr/lib and /System. A NativeAOT binary
# built with Homebrew's dotnet links Homebrew's openssl@3 and brotli by absolute path, and then
# stops at launch on a Mac without them ("Library not loaded"). Build release archives with
# Microsoft's SDK (https://dot.net/v1/dotnet-install.sh), which links system libraries only.
# Usage: scripts/check-macos-libs.sh <binary>...
set -eu
status=0
for f in "$@"; do
  if ! file -b "$f" 2>/dev/null | grep -q 'Mach-O' || ! listing=$(otool -L "$f" 2>&1); then
    printf '%s: not a readable Mach-O file\n' "$f" >&2
    status=1
    continue
  fi
  own=$(otool -D "$f" 2>/dev/null | tail -n +2 || true)
  bad=$(printf '%s\n' "$listing" | tail -n +2 | awk '{print $1}' | grep -v -e '^/usr/lib/' -e '^/System/' || true)
  [ -z "$own" ] || bad=$(printf '%s\n' "$bad" | grep -v -x -F -e "$own" || true)
  if [ -n "$bad" ]; then
    echo "$f loads libraries that are not part of macOS:" >&2
    echo "$bad" | sed 's/^/  /' >&2
    echo "  Build with Microsoft's .NET SDK, not Homebrew's (see this script's header)." >&2
    status=1
  fi
done
exit $status
