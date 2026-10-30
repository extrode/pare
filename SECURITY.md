# Security policy

## Supported versions

Pare is 0.x. Only the latest release gets fixes.

## Reporting a vulnerability

Please do not open a public issue. Use **Security > Report a vulnerability** on
[github.com/extrode/pare](https://github.com/extrode/pare/security), which opens a private
report. If that button is missing, open an issue that says only that you have a security report,
with no details, and a maintainer will reply with a private channel.

## What counts

- Input that makes the stripper crash, hang, or use unbounded memory. Pare runs on files you
  did not write (a dependency's `.ts`, a user upload in a build service), so this matters.
- Memory safety in `libpare` (`pare_strip`, `pare_free`) when called as documented in `pare.h`.
- The MSBuild task writing or deleting a file outside the project's `obj/` output, or following
  a path out of the project.

Wrong output (Pare strips something differently from `tsc`) is a bug, not a vulnerability: use a
normal issue.
