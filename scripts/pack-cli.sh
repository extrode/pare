#!/bin/sh
# Packs the pare CLI into tmp/dist:
#   Extrode.Pare.Cli.<v>.nupkg          .NET tool pointer package (lists the RID packages). NOT what
#                                       nuget.org holds: release.yml publishes a framework-dependent
#                                       Extrode.Pare.Cli of the same id and version. Do not upload it.
#   Extrode.Pare.Cli.<rid>.<v>.nupkg    NativeAOT binary for one RID
#   Extrode.Pare.Cli.any.<v>.nupkg      framework-dependent fallback for unlisted RIDs
#   pare-<v>-<rid>.tar.gz               the same binary as a standalone archive
#   libpare-<v>-<rid>.tar.gz            the C shared library and pare.h (src/Extrode.Pare.Native)
# NativeAOT cannot cross OS, so this packs osx-arm64 and osx-x64 on macOS, the host RID on
# Linux, and linux-{arm64,x64} and linux-musl-{arm64,x64} in Docker when it runs. win-* need a
# Windows machine: scripts/pack-cli.ps1.
# Each RID is compiled twice (CLI and library), about a minute each.
# macOS archives must come from Microsoft's SDK: Homebrew's dotnet links Homebrew libraries into
# the binary, and scripts/check-macos-libs.sh stops the pack when it sees one.
# Usage: scripts/pack-cli.sh [--version 0.6.0] [--no-docker]
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
out="$root/tmp/dist"
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
docker=yes
while [ $# -gt 0 ]; do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --no-docker) docker=no; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
mkdir -p "$out"
pack() { dotnet pack "$root/src/Extrode.Pare.Cli" -c Release -o "$out" -p:Version="$version" -v q -nologo "$@"; }

archive() { # rid publish-dir
  stage="$root/tmp/dist-stage/pare-$version-$1"
  rm -rf "$stage" && mkdir -p "$stage"
  cp "$2/pare" "$root/LICENSE.md" "$root/NOTICE.md" "$root/README.md" "$stage/"
  COPYFILE_DISABLE=1 tar --no-xattrs -czf "$out/pare-$version-$1.tar.gz" -C "$root/tmp/dist-stage" "pare-$version-$1"
  rm -rf "$root/tmp/dist-stage"
  echo "pare-$version-$1.tar.gz"
}

# The stage under tmp/native-stage is overwritten file by file and left for scripts/cleanup.
native_archive() { # rid library-file
  stage="$root/tmp/native-stage/libpare-$version-$1"
  mkdir -p "$stage"
  cp "$2" "$root/src/Extrode.Pare.Native/include/pare.h" "$root/LICENSE.md" "$root/NOTICE.md" "$stage/"
  COPYFILE_DISABLE=1 tar --no-xattrs -czf "$out/libpare-$version-$1.tar.gz" -C "$root/tmp/native-stage" "libpare-$version-$1"
  echo "libpare-$version-$1.tar.gz"
}

pack
pack -r any
case "$(uname -s)-$(uname -m)" in
  Darwin-*) rids="osx-arm64 osx-x64" ;;
  Linux-x86_64) rids="linux-x64" ;;
  Linux-aarch64 | Linux-arm64) rids="linux-arm64" ;;
  *) echo "unsupported host: $(uname -s)-$(uname -m)" >&2; exit 2 ;;
esac
for rid in $rids; do
  pack -r "$rid"
  [ "$(uname -s)" != Darwin ] || "$root/scripts/check-macos-libs.sh" "$root/src/Extrode.Pare.Cli/bin/Release/net10.0/$rid/publish/pare"
  archive "$rid" "$root/src/Extrode.Pare.Cli/bin/Release/net10.0/$rid/publish"
  dotnet publish "$root/src/Extrode.Pare.Native" -c Release -r "$rid" -p:Version="$version" -v q -nologo
  [ "$(uname -s)" != Darwin ] || "$root/scripts/check-macos-libs.sh" "$root/src/Extrode.Pare.Native/bin/Release/net10.0/$rid/publish/libpare.dylib"
  native_archive "$rid" "$root/src/Extrode.Pare.Native/bin/Release/net10.0/$rid/publish/libpare.dylib"
done

# Linux in Docker: glibc RIDs on the Debian SDK image, musl RIDs on the Alpine one. Sources
# are copied inside the container so its obj/ and bin/ never touch the host's.
in_docker() { # rid platform image setup-command [pack arguments]
  extra="${5:-}"
  docker run --rm --platform "$2" -v "$root":/repo:ro -v "$out":/out "$3" sh -c "
    set -e
    $4
    mkdir /w && cd /repo
    mkdir -p /w/docs/assets && cp -r docs/assets/logo /w/docs/assets/ && cp -r src Directory.Build.props Directory.Packages.props global.json LICENSE.md NOTICE.md README.md /w/
    cd /w
    dotnet pack src/Extrode.Pare.Cli -c Release -r $1 -o /out -p:Version=$version -v q -nologo $extra
    mkdir -p /out/.publish-$1 && cp src/Extrode.Pare.Cli/bin/Release/net10.0/$1/publish/pare /out/.publish-$1/
    dotnet publish src/Extrode.Pare.Native -c Release -r $1 -p:Version=$version -v q -nologo $extra
    cp src/Extrode.Pare.Native/bin/Release/net10.0/$1/publish/libpare.so /out/.publish-$1/"
  archive "$1" "$out/.publish-$1"
  native_archive "$1" "$out/.publish-$1/libpare.so"
  rm -rf "$out/.publish-$1"
}
if [ "$(uname -s)" = Darwin ] && [ "$docker" = yes ] && docker info >/dev/null 2>&1; then
  debian="apt-get update -qq >/dev/null && apt-get install -y -qq clang zlib1g-dev >/dev/null 2>&1"
  alpine="apk add -q clang build-base zlib-dev >/dev/null"
  in_docker linux-arm64 linux/arm64 mcr.microsoft.com/dotnet/sdk:10.0 "$debian"
  in_docker linux-x64 linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 "$debian"
  in_docker linux-musl-arm64 linux/arm64 mcr.microsoft.com/dotnet/sdk:10.0-alpine "$alpine"
  # ilc aborts (exit 134) on emulated x64 Alpine, so musl x64 is cross-compiled on arm64
  # against an x64 Alpine sysroot. llvm-objcopy splits the x64 debug symbols.
  repo='$(sed -n "s:/main$::p" /etc/apk/repositories | head -1)'
  x64root="apk add -q clang lld llvm build-base zlib-dev >/dev/null && mkdir /x64 && apk add -q --arch x86_64 --root /x64 --initdb --allow-untrusted --no-scripts -X $repo/main -X $repo/community musl-dev libgcc gcc libstdc++-dev zlib-dev zlib-static >/dev/null"
  in_docker linux-musl-x64 linux/arm64 mcr.microsoft.com/dotnet/sdk:10.0-alpine "$x64root" "-p:SysRoot=/x64 -p:LinkerFlavor=lld"

  # Each Linux build on its own platform: the archive's binary 20 times (a flaky stack check
  # once failed one run in four), then `dotnet tool install`, which must pick that RID, then
  # tests/Extrode.Pare.Integration/native/smoke.c built against the libpare archive, 20 runs.
  for spec in linux-arm64:linux/arm64:10.0 linux-x64:linux/amd64:10.0 \
      linux-musl-arm64:linux/arm64:10.0-alpine linux-musl-x64:linux/amd64:10.0-alpine; do
    rid=${spec%%:*}; rest=${spec#*:}; platform=${rest%%:*}; tag=${rest#*:}
    case "$tag" in *alpine) cc_setup="apk add -q build-base >/dev/null" ;; *) cc_setup="apt-get update -qq >/dev/null && apt-get install -y -qq gcc libc6-dev >/dev/null 2>&1" ;; esac
    docker run --rm --platform "$platform" -v "$out":/dist:ro -v "$root/tests/Extrode.Pare.Integration/native":/smoke:ro "mcr.microsoft.com/dotnet/sdk:$tag" sh -c "
      set -e
      $cc_setup
      mkdir /a && tar -xzf /dist/pare-$version-$rid.tar.gz -C /a
      tar -xzf /dist/libpare-$version-$rid.tar.gz -C /a
      lib=/a/libpare-$version-$rid
      cc -std=c11 -Wall -Wextra -Werror -I \$lib /smoke/smoke.c -L \$lib -lpare -Wl,-rpath,\$lib -lpthread -o /smoke-bin
      for i in \$(seq 20); do /smoke-bin >/dev/null; done
      printf 'type T = 1;\nlet x: number = 1;\n' > /in.ts
      printf '           \nlet x         = 1;\n' > /want.js
      for i in \$(seq 20); do /a/pare-$version-$rid/pare --stdin < /in.ts > /got.js; cmp -s /got.js /want.js; done
      dotnet tool install Extrode.Pare.Cli --version $version --tool-path /t --add-source /dist >/dev/null 2>&1
      test -d /t/.store/extrode.pare.cli/$version/extrode.pare.cli.$rid
      /t/pare --stdin < /in.ts | cmp -s - /want.js"
    echo "verified $rid"
  done
fi
echo "packed Extrode.Pare.Cli $version into tmp/dist"
