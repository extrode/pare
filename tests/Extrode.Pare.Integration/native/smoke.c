/* Drives libpare through pare.h the way a C program would. Prints "ok" and exits 0 when every
   check holds; NativeLibraryTests compiles and runs it against the published library. */
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "pare.h"

static int failures;

#define CHECK(cond)                                                          \
    do {                                                                     \
        if (!(cond)) {                                                       \
            fprintf(stderr, "%s:%d: CHECK(%s)\n", __FILE__, __LINE__, #cond); \
            failures++;                                                      \
        }                                                                    \
    } while (0)

static int zeroed(const pare_result *r) {
    return r->output == NULL && r->output_length == 0 && r->diagnostics == NULL && r->diagnostic_count == 0;
}

static void strips(void) {
    const char *ts = "let a: number = 1;\n";
    pare_result r;
    CHECK(pare_strip(ts, strlen(ts), 0, &r) == PARE_OK);
    CHECK(r.output != NULL && strcmp(r.output, "let a         = 1;\n") == 0);
    CHECK(r.output_length == strlen(ts));
    CHECK(r.diagnostics == NULL && r.diagnostic_count == 0);
    pare_free(&r);
    CHECK(zeroed(&r));
    pare_free(&r);
    pare_free(NULL);
}

static void rejects(void) {
    const char *ts = "let x = 1;\nenum E { A }\n";
    pare_result r;
    CHECK(pare_strip(ts, strlen(ts), 0, &r) == PARE_REJECTED);
    CHECK(r.output == NULL && r.output_length == 0);
    CHECK(r.diagnostic_count == 1 && r.diagnostics != NULL);
    if (r.diagnostic_count == 1) {
        pare_diagnostic d = r.diagnostics[0];
        CHECK(d.code == 100 && d.line == 2 && d.column == 1 && d.start == 11 && d.length > 0);
    }
    CHECK(strcmp(pare_message(100), "'enum' generates runtime code and cannot be erased") == 0);
    CHECK(strcmp(pare_hint(100), "use a 'const' object with 'as const' instead of 'enum'") == 0);
    CHECK(strcmp(pare_message(65537), "unknown diagnostic") == 0);
    CHECK(strcmp(pare_hint(-1), "") == 0);
    pare_free(&r);
    CHECK(zeroed(&r));
}

static void invalid(void) {
    pare_result r;
    CHECK(pare_strip(NULL, 1, 0, &r) == PARE_INVALID && zeroed(&r));
    CHECK(pare_strip("x;", 2, 2u, &r) == PARE_INVALID && zeroed(&r));
    CHECK(pare_strip("\xff", 1, 0, &r) == PARE_INVALID && zeroed(&r));
    CHECK(pare_strip("x;", 2, 0, NULL) == PARE_INVALID);
    CHECK(pare_strip(NULL, 0, 0, &r) == PARE_OK && r.output_length == 0 && r.output[0] == '\0');
    pare_free(&r);
    CHECK(pare_abi_version() == PARE_ABI_VERSION);
    CHECK(strlen(pare_version()) >= 5);
}

/* Nesting deeper than any stack is a syntax error, not a crash. */
static void *deep(void *unused) {
    (void)unused;
    const int depth = 200000;
    size_t n = 4 + 2 * (size_t)depth + 2;
    char *ts = malloc(n + 1);
    memcpy(ts, "x = ", 4);
    memset(ts + 4, '(', depth);
    ts[4 + depth] = 'a';
    memset(ts + 5 + depth, ')', depth);
    strcpy(ts + 5 + 2 * depth, ";");
    pare_result r;
    CHECK(pare_strip(ts, strlen(ts), 0, &r) == PARE_REJECTED);
    CHECK(r.diagnostic_count == 1 && r.diagnostics[0].code == 1);
    pare_free(&r);
    free(ts);
    return NULL;
}

static void *repeat(void *unused) {
    (void)unused;
    for (int i = 0; i < 1000; i++) {
        strips();
        rejects();
    }
    return NULL;
}

static void run(void *(*fn)(void *), size_t stack) {
    pthread_attr_t attr;
    pthread_attr_init(&attr);
    if (stack)
        pthread_attr_setstacksize(&attr, stack);
    pthread_t t;
    CHECK(pthread_create(&t, &attr, fn, NULL) == 0);
    pthread_join(t, NULL);
    pthread_attr_destroy(&attr);
}

int main(void) {
    strips();
    rejects();
    invalid();
    deep(NULL);
    run(deep, 512 * 1024);

    pthread_t threads[4];
    for (int i = 0; i < 4; i++)
        CHECK(pthread_create(&threads[i], NULL, repeat, NULL) == 0);
    for (int i = 0; i < 4; i++)
        pthread_join(threads[i], NULL);

    if (failures)
        return 1;
    puts("ok");
    return 0;
}
