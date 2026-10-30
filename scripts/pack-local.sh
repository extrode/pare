#!/bin/sh
# Packs Extrode.Pare, Extrode.Pare.Build and Extrode.Pare.AspNetCore into tmp/feed as
# 0.6.2-dev.<unix time> and re-restores samples/WebApp, whose `0.6.2-*` references then pick
# the new build. A unique version keeps NuGet from serving a cached package with the same number.
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
version="0.6.2-dev.$(date +%s)"
for project in Extrode.Pare Extrode.Pare.Build Extrode.Pare.AspNetCore; do
  dotnet pack "$root/src/$project" -c Release -o "$root/tmp/feed" -p:Version="$version" -v q -nologo
done
dotnet restore "$root/samples/WebApp" --force-evaluate -v q
echo "packed Extrode.Pare, Extrode.Pare.Build and Extrode.Pare.AspNetCore $version"
