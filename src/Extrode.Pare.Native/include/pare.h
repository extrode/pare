/*
 * libpare: strips TypeScript type syntax to JavaScript by overwriting it with spaces, so every
 * remaining character keeps its line and column. https://github.com/extrode/pare
 *
 * Link: -L<dir> -lpare -Wl,-rpath,<dir>   (macOS libpare.dylib, Linux libpare.so)
 *       pare.lib, with pare.dll next to the program   (Windows)
 *
 * Every function is thread-safe. The library cannot be unloaded once loaded (NativeAOT); several
 * NativeAOT libraries in one process are fine. On linux-musl-arm64, call pare_strip from a
 * thread you created: there the main thread's stack check can misreport (see the README).
 */
#ifndef PARE_H
#define PARE_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#define PARE_API __declspec(dllimport)
#else
#define PARE_API
#endif

/* Compare with pare_abi_version() to refuse a library built for another layout. */
#define PARE_ABI_VERSION 1u

/* pare_strip return values */
#define PARE_OK 0        /* stripped: result->output holds the JavaScript */
#define PARE_REJECTED 1  /* syntax error or non-erasable syntax: result->diagnostics */
#define PARE_INVALID 2   /* NULL result, NULL source with length > 0, unknown option bits,
                            invalid UTF-8, or length over 2,147,483,591 bytes; nothing allocated */
#define PARE_INTERNAL 3  /* a bug in Pare; nothing allocated */

/* pare_strip options: rewrite relative "./x.ts" imports to "./x.js" (.mts/.cts likewise),
   as TypeScript's rewriteRelativeImportExtensions does. */
#define PARE_REWRITE_RELATIVE_IMPORT_EXTENSIONS 1u

typedef struct pare_diagnostic {
    int32_t code;    /* 1 syntax error; 100-107 non-erasable syntax (docs/06-reference/error-codes.md) */
    int32_t line;    /* 1-based */
    int32_t column;  /* 1-based, in UTF-16 code units: what browsers and the pare CLI report */
    int32_t start;   /* byte offset into the source passed in */
    int32_t length;  /* bytes */
} pare_diagnostic;

typedef struct pare_result {
    char *output;                  /* UTF-8, NUL-terminated, no BOM; NULL unless PARE_OK */
    size_t output_length;          /* bytes, without the NUL; never more than the source's
                                      length; holds any NUL the source had */
    pare_diagnostic *diagnostics;  /* in source order; NULL when there are none */
    size_t diagnostic_count;
} pare_result;

/*
 * Strips `length` bytes of UTF-8 TypeScript at `source` (NULL with length 0 is the empty
 * source). A leading BOM is dropped. Lines and UTF-16 columns are kept; byte offsets shift
 * when a blanked type contains non-ASCII text (one space per UTF-16 code unit).
 * Free *result with pare_free whatever the return value.
 */
PARE_API int32_t pare_strip(const char *source, size_t length, uint32_t options, pare_result *result);

/* Frees what pare_strip allocated and zeroes *result. NULL or a zeroed result is a no-op. */
PARE_API void pare_free(pare_result *result);

/* Static strings, never freed. Unknown codes give "unknown diagnostic" and "". */
PARE_API const char *pare_message(int32_t code);
PARE_API const char *pare_hint(int32_t code);

PARE_API const char *pare_version(void);   /* e.g. "0.6.2" */
PARE_API uint32_t pare_abi_version(void);  /* PARE_ABI_VERSION the library was built with */

#ifdef __cplusplus
}
#endif

#endif
