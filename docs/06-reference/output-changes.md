# Output changes

Pare's output differs from its input only by spaces, with these exceptions.

## Relative import extensions

Relative module specifiers `./x.ts` are rewritten to `./x.js` (`.mts` to `.mjs`, `.cts` to `.cjs`)
in `import`, `export ... from` and `import("...")`.

| Where | Default |
|---|---|
| `pare` CLI | on |
| MSBuild package | on |
| library (`StripOptions.RewriteRelativeImportExtensions`) | off, opt in |
| C library (`PARE_REWRITE_RELATIVE_IMPORT_EXTENSIONS`) | off, opt in |

## Automatic semicolon insertion fixes

Erasing a statement can leave two lines that JavaScript would join into one. Pare repairs these
cases and no others:

- a `)` moved to the end of an erased multi-line return type;
- a `(` moved to the start of erased type parameters that are followed by a line break;
- a `;` written at the start of an erased statement or class member when the previous line could
  otherwise merge with the next;
- a `;` written at the end of an erased `as` or `satisfies` type when the next line starts with `(`,
  `[` or a template;
- a `;` written at the end of the erased type of a field named `get`, `set` or `static`.

## Everything else

Spaces. The positions of all other characters, including comments and tabs, are the same as in your
source. A comment inside an erased range, such as between a parameter list and its return type, is
blanked with the rest of it.
