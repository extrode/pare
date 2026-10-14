# Changelog

## [Unreleased]

- Solution scaffold, public API sketch (`TypeStripper.Strip`, `Diagnostic`, `IDiagnosticSink`),
  `pare --stdin` CLI publishing as NativeAOT, CI on Linux, Windows and macOS.
- Lexer for all JS and TS tokens: templates, regex and `>` rescans, contextual keywords,
  Unicode identifiers. `scripts/get-ts-corpus.cs` fetches the TypeScript corpus at a pinned
  commit; the lexer round-trips all 17,582 script files in it.
