#!/bin/sh
# Agent tool input decodes \uXXXX escapes into raw characters before they reach disk; a raw
# U+2028 inside a C# string literal is a compile error. This turns every non-ASCII character in
# tracked and new C# sources back into a \u or \U escape. Sources are ASCII by convention.
cd "$(dirname "$0")/.." || exit 2
git ls-files -co --exclude-standard -z '*.cs' | xargs -0 perl -CSD -pi -e '
  s/([^\x00-\x7F])/ord($1) > 0xFFFF ? sprintf("\\U%08X", ord($1)) : sprintf("\\u%04X", ord($1))/ge'
