#!/bin/sh
# Packs the pare CLI into tmp/dist:
#   Extrode.Pare.Cli.<v>.nupkg          .NET tool pointer package (lists the RID packages)
#   Extrode.Pare.Cli.<rid>.<v>.nupkg    NativeAOT binary for one RID
#   Extrode.Pare.Cli.any.<v>.nupkg      framework-dependent fallback for unlisted RIDs
#   pare-<v>-<rid>.tar.gz               the same binary as a standalone archive
# NativeAOT cannot cross OS, so this packs osx-arm64 and osx-x64 on macOS, the host RID on
# Linux, and linux-{arm64,x64} and linux-musl-{arm64,x64} in Docker when it runs. win-* need a
# Windows machine: scripts/pack-cli.ps1.
# Usage: scripts/pack-cli.sh [--version 0.1.0] [--no-docker]
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
  archive "$rid" "$root/src/Extrode.Pare.Cli/bin/Release/net10.0/$rid/publish"
done

# Linux in Docker: glibc RIDs on the Debian SDK image, musl RIDs on the Alpine one. Sources
# are copied inside the container so its obj/ and bin/ never touch the host's.
in_docker() { # rid platform image setup-command [pack arguments]
  extra="${5:-}"
  docker run --rm --platform "$2" -v "$root":/repo:ro -v "$out":/out "$3" sh -c "
    set -e
    $4
    mkdir /w && cd /repo
    cp -r src Directory.Build.props Directory.Packages.props global.json LICENSE.md NOTICE.md README.md /w/
    cd /w
    dotnet pack src/Extrode.Pare.Cli -c Release -r $1 -o /out -p:Version=$version -v q -nologo $extra
    mkdir -p /out/.publish-$1 && cp src/Extrode.Pare.Cli/bin/Release/net10.0/$1/publish/pare /out/.publish-$1/"
  archive "$1" "$out/.publish-$1"
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
  # once failed one run in four), then `dotnet tool install`, which must pick that RID.
  for spec in linux-arm64:linux/arm64:10.0 linux-x64:linux/amd64:10.0 \
      linux-musl-arm64:linux/arm64:10.0-alpine linux-musl-x64:linux/amd64:10.0-alpine; do
    rid=${spec%%:*}; rest=${spec#*:}; platform=${rest%%:*}; tag=${rest#*:}
    docker run --rm --platform "$platform" -v "$out":/dist:ro "mcr.microsoft.com/dotnet/sdk:$tag" sh -c "
      set -e
      mkdir /a && tar -xzf /dist/pare-$version-$rid.tar.gz -C /a
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
