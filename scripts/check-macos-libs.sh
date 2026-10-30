#!/bin/sh
# Fails when a macOS binary loads any library outside /usr/lib and /System. A NativeAOT binary
# built with Homebrew's dotnet links Homebrew's openssl@3 and brotli by absolute path, and then
# stops at launch on a Mac without them ("Library not loaded"). Build release archives with
# Microsoft's SDK (https://dot.net/v1/dotnet-install.sh), which links system libraries only.
# Usage: scripts/check-macos-libs.sh <binary>...
set -eu
status=0
for f in "$@"; do
  bad=$(otool -L "$f" | tail -n +2 | awk '{print $1}' | grep -v -e '^/usr/lib/' -e '^/System/' -e '^@rpath/libpare.dylib$' || true)
  if [ -n "$bad" ]; then
    echo "$f loads libraries that are not part of macOS:" >&2
    echo "$bad" | sed 's/^/  /' >&2
    echo "  Build with Microsoft's .NET SDK, not Homebrew's (see this script's header)." >&2
    status=1
  fi
done
exit $status
