# Using libpare from C and other languages

`libpare` is the same stripper as a C shared library. Rust, Go (cgo), Python (ctypes), Ruby, PHP,
and Node or Deno FFI can call it in-process. No .NET runtime is needed.

## Get it

Each release has `libpare-<version>-<rid>.tar.gz` (`.zip` on Windows). Inside:

| Platform | Files |
|---|---|
| macOS | `libpare.dylib`, `pare.h` |
| Linux | `libpare.so`, `pare.h` |
| Windows | `pare.dll`, `pare.lib`, `pare.h` |

or build it yourself: `dotnet publish src/Extrode.Pare.Native -r <rid>`.

## A first program

```c
#include <stdio.h>
#include <string.h>
#include "pare.h"

int main(void) {
    const char *ts = "let a: number = 1;\n";
    pare_result r;
    if (pare_strip(ts, strlen(ts), 0, &r) == PARE_OK)
        fputs(r.output, stdout);                       /* "let a         = 1;\n" */
    else
        for (size_t i = 0; i < r.diagnostic_count; i++)
            fprintf(stderr, "app.ts:%d:%d: error PARE%03d: %s\n", r.diagnostics[i].line,
                    r.diagnostics[i].column, r.diagnostics[i].code, pare_message(r.diagnostics[i].code));
    pare_free(&r);
}
```

Build and run:

```sh
cc app.c -I<dir> -L<dir> -lpare -Wl,-rpath,<dir>    # macOS, Linux
cl app.c /I<dir> <dir>\pare.lib                      # Windows; pare.dll next to app.exe
```

`<dir>` is the folder you unpacked.

## The API in one table

| Function | Does |
|---|---|
| `pare_strip(source, length, options, &result)` | strips; returns a status code |
| `pare_free(&result)` | frees and zeroes the result; safe on a zeroed result |
| `pare_message(code)`, `pare_hint(code)` | static text for a diagnostic code |
| `pare_version()` | the library version, such as `"0.7.2"` |
| `pare_abi_version()` | compare with `PARE_ABI_VERSION` to catch a header and library from different builds |

Status codes: `PARE_OK` (0), `PARE_REJECTED` (1), `PARE_INVALID` (2), `PARE_INTERNAL` (3). The
full contract is in the [C API reference](../06-reference/c-api.md).

## Things to know

- **UTF-8 in and out.** A leading BOM is dropped, as the CLI does. Invalid UTF-8 returns
  `PARE_INVALID`.
- **Positions.** Lines and UTF-16 columns are kept, so the output's `line:column` is the source's.
  Byte offsets shift when a blanked type holds non-ASCII text.
- **Diagnostics** carry the code, line, UTF-16 column (what browsers report), and the byte offset
  and length into the source.
- **Thread-safe.** The library allocates and `pare_free` frees.
- **It cannot be unloaded.** Several NativeAOT libraries in one process are fine.
- On linux-musl-arm64, call it from a thread you created; see [Using the library](library.md).
