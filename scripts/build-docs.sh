#!/usr/bin/env bash
# Builds the docs site with the shared Docs tool (sibling repo: ../docs).
# Only the numbered sections (docs/00-*, 01-*, ...) and docs/assets go into the site; the other
# files under docs/ (decisions, plans, handoffs) are project history, so the tool never sees them.
# Output: dist/docs-site (gitignored).
# Rendering the ```mermaid diagrams needs Python with Playwright and a Chromium; a warm
# ../docs/cache/mermaid avoids that on repeat builds. The tool targets .NET 8; on a machine with
# only a newer runtime, set DOTNET_ROLL_FORWARD=Major.
set -euo pipefail
cd "$(dirname "$0")/.."
tool="../docs/src/Docs"
if [ ! -d "$tool" ]; then
  echo "build-docs.sh: the shared Docs tool is not part of this repository; expected a sibling checkout at $tool" >&2
  exit 1
fi
rm -rf tmp/docs-src dist/docs-site
mkdir -p tmp/docs-src
cp -R docs/[0-9][0-9]-* docs/assets tmp/docs-src/
dotnet run --project "$tool" -- tmp/docs-src dist/docs-site --site-name pare
# GitHub Pages serves the site at this domain; the file must be in the artifact.
printf 'pare.extrode.com\n' > dist/docs-site/CNAME
