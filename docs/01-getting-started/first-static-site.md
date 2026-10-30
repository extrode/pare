# Your first static site

You will turn a few TypeScript files into JavaScript with the `pare` command and open them in a
browser. No .NET project, no Node, no bundler.

## 1. Install `pare`

Download `pare-<version>-<rid>.tar.gz` for your system
from the [releases page](https://github.com/extrode/pare/releases) (`.zip` on Windows), unpack it
and put the folder on your `PATH`:

```sh
tar -xzf pare-0.6.3-osx-arm64.tar.gz
export PATH="$PWD/pare-0.6.3-osx-arm64:$PATH"
pare --version
```

With the .NET 10 runtime installed you can use `dotnet tool install -g Extrode.Pare.Cli` instead; it starts slower than the archive.

## 2. Get the sample

The repository has a ready-made page in [`samples/CliSite`](https://github.com/extrode/pare/tree/dev/samples/CliSite):
three TypeScript modules in `src/`. `main.ts` imports the other two and uses interfaces,
generics, `satisfies`, `as`, `!` and a type-only import.

```sh
git clone https://github.com/extrode/pare
cd pare
```

## 3. Strip it

```sh
pare samples/CliSite/src --outdir samples/CliSite/js
```

## 4. Serve it

Any static server works:

```sh
python3 -m http.server 8000 -d samples/CliSite
```

Open <http://localhost:8000>. You should see "2 of 3 open" over three tasks sorted by priority,
and "Add task" adds one. Before you ran `pare`, the page said "JavaScript did not run".

## 5. Read the output

Open `samples/CliSite/js/main.js` next to `src/main.ts`. The types are spaces, so every line and
column matches. The imports changed from `./model.ts` to `./model.js`, because browsers load the
`.js` files:

```js
import { byPriority, summary,           } from "./model.js";
const list = document.querySelector("#tasks")                    ;
```

## Using it on your own project

```sh
pare src                        # every src/**/*.ts becomes .js next to its source
pare src --outdir dist          # mirror the folder structure into dist/
pare "src/**/*.ts" --check-only # report errors, write nothing
pare --stdin < in.ts > out.js   # one file from stdin to stdout
```

See [The pare command](../03-guides/cli.md) for the rest, including how to re-run it on every
save.
