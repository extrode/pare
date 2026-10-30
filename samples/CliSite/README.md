# CliSite

A static page whose scripts are written in TypeScript and stripped by the `pare` CLI. No .NET
project, no Node packages, no bundler. Walk-through: [docs/01-getting-started/first-static-site.md](../../docs/01-getting-started/first-static-site.md).

From the repository root:

```sh
pare samples/CliSite/src --outdir samples/CliSite/js
python3 -m http.server 8000 -d samples/CliSite
```

Open http://localhost:8000. The page lists three tasks, sorted by priority, under "2 of 3 open";
"Add task" adds one. Before `pare` has run, it says "JavaScript did not run".

Any static server works; Deno's is `deno run --allow-net --allow-read --allow-sys jsr:@std/http/file-server --port 8000 samples/CliSite`.

To strip again on every save (either one; both need `pare` on `PATH`):

```sh
node --watch-path=samples/CliSite/src samples/CliSite/watch.mjs
deno run --watch=samples/CliSite/src --allow-run=pare --allow-env samples/CliSite/watch.mjs
```

`js/` is generated and ignored by git.
