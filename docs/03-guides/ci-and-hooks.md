# CI, hooks and Docker

Pare has no server and no config file, so each of these is a few lines. All of them run
`pare --check-only`, which reports syntax Pare cannot erase and writes nothing. Exit code 1 means
a rejection, 2 means an I/O or usage error; see [The pare command](cli.md).

## GitHub Actions

```yaml
steps:
  - uses: actions/checkout@v7
  - uses: actions/setup-dotnet@v6
    with:
      dotnet-version: 10.0.x
  - run: dotnet tool install -g Extrode.Pare.Cli
  - run: pare src --check-only
```

The .NET tool needs the .NET 10 runtime, which `setup-dotnet` installs. To skip .NET, download the
archive for `linux-x64` from the [releases page](https://github.com/extrode/pare/releases) and put
`pare` on the `PATH`. For type errors, add a `tsc --noEmit` step; see
[Type checking in CI](type-checking.md).

For a project that uses the MSBuild package, `dotnet build` already fails on a rejection, so no
extra step is needed.

## A git pre-commit hook

Save as `.git/hooks/pre-commit` and make it executable (`chmod +x`). It checks only the staged
`.ts` and `.mts` files, so a commit that touches none of them costs nothing.

```sh
#!/bin/sh
files=$(git diff --cached --name-only --diff-filter=ACM -- '*.ts' '*.mts' ':!*.d.ts')
[ -z "$files" ] && exit 0
pare --check-only $files
```

A staged `enum` stops the commit with `bad.ts:1:1: error PARE100: ...`. File names with spaces
need a `while read` loop instead of the unquoted `$files`.

Hook frameworks work the same way. For [pre-commit](https://pre-commit.com), a local hook:

```yaml
repos:
  - repo: local
    hooks:
      - id: pare
        name: pare --check-only
        entry: pare --check-only
        language: system
        files: '\.m?ts$'
        exclude: '\.d\.ts$'
```

## Docker

Strip in a build stage and copy only the output. The tool image needs the .NET 10 SDK:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS strip
RUN dotnet tool install -g Extrode.Pare.Cli
ENV PATH="$PATH:/root/.dotnet/tools"
COPY src /src
RUN pare /src --outdir /out

FROM nginx:alpine
COPY --from=strip /out /usr/share/nginx/html
```

`--outdir` mirrors the input folder under `/out` (`src/lib/a.ts` becomes `/out/lib/a.js`).
