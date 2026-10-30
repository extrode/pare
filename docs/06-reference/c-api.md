# C API

The header is [`pare.h`](https://github.com/extrode/pare/blob/dev/src/Extrode.Pare.Native/include/pare.h).
For a walkthrough see [Using libpare from C and other languages](../03-guides/c-library.md).

## Functions

```c
int32_t     pare_strip(const char *source, size_t length, uint32_t options, pare_result *result);
void        pare_free(pare_result *result);
const char *pare_message(int32_t code);
const char *pare_hint(int32_t code);
const char *pare_version(void);
uint32_t    pare_abi_version(void);
```

## pare_strip

Strips `length` bytes of UTF-8 TypeScript at `source`. `NULL` with length 0 is the empty source.
A leading BOM is dropped. Free `*result` with `pare_free` whatever the return value.

| Return | Name | Meaning |
|---|---|---|
| 0 | `PARE_OK` | stripped; `result->output` holds the JavaScript |
| 1 | `PARE_REJECTED` | syntax error or non-erasable syntax; see `result->diagnostics` |
| 2 | `PARE_INVALID` | `NULL` result, `NULL` source with length > 0, unknown option bits, invalid UTF-8, or length over 2,147,483,591 bytes; nothing allocated |
| 3 | `PARE_INTERNAL` | a bug in Pare; nothing allocated |

### Options

| Flag | Does |
|---|---|
| `PARE_REWRITE_RELATIVE_IMPORT_EXTENSIONS` (1) | rewrites relative `./x.ts` imports to `./x.js` (`.mts` and `.cts` likewise), as TypeScript's `rewriteRelativeImportExtensions` does |

Pass 0 for no options. Unknown bits return `PARE_INVALID`.

## Types

```c
typedef struct pare_diagnostic {
    int32_t code;    /* 1 syntax error; 100-107 non-erasable syntax */
    int32_t line;    /* 1-based */
    int32_t column;  /* 1-based, in UTF-16 code units */
    int32_t start;   /* byte offset into the source passed in */
    int32_t length;  /* bytes */
} pare_diagnostic;

typedef struct pare_result {
    char *output;                  /* UTF-8, NUL-terminated, no BOM; NULL unless PARE_OK */
    size_t output_length;          /* bytes, without the NUL */
    pare_diagnostic *diagnostics;  /* in source order; NULL when there are none */
    size_t diagnostic_count;
} pare_result;
```

`pare_diagnostic` is five `int32_t`, 20 bytes with no padding.
`output_length` is never more than the source's length, and the output holds any NUL the source had.

Diagnostic codes are the numbers in [Error codes](error-codes.md): 1 is a syntax error and 100 to
107 are non-erasable syntax.

## pare_free

Frees what `pare_strip` allocated and zeroes all four fields of `*result`. `NULL` or an already
zeroed result is a no-op.

## pare_message, pare_hint

Static strings, never freed. An unknown code gives `"unknown diagnostic"` and `""`.

## pare_version, pare_abi_version

`pare_version()` returns the library version as a static string. `pare_abi_version()` returns the
`PARE_ABI_VERSION` the library was built with. Compare it with the macro from your header to refuse
a library built for another layout. It is currently 1.

## Positions

Lines and UTF-16 columns are kept, so the output's `line:column` is the source's. Byte offsets shift
when a blanked type contains non-ASCII text, because Pare writes one space per UTF-16 code unit.

## Threads and loading

Every function is thread-safe. The library cannot be unloaded once loaded. Several NativeAOT
libraries in one process are fine. On linux-musl-arm64, call `pare_strip` from a thread you
created: there the main thread's stack check can misreport.
