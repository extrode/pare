#!/bin/sh
# Packs Extrode.Pare.Build into tmp/feed as 0.1.0-dev.<unix time> and re-restores
# samples/WebApp, whose `0.1.0-*` reference then picks the new build. A unique version keeps
# NuGet from serving a cached package with the same number.
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
version="0.1.0-dev.$(date +%s)"
dotnet pack "$root/src/Extrode.Pare.Build" -c Release -o "$root/tmp/feed" -p:Version="$version" -v q -nologo
dotnet restore "$root/samples/WebApp" --force-evaluate -v q
echo "packed Extrode.Pare.Build $version"
