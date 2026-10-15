// Strips src/ into js/ once. Re-run on every save with a runtime's file watcher, from the repository root:
//   node --watch-path=samples/CliSite/src samples/CliSite/watch.mjs
//   deno run --watch=samples/CliSite/src --allow-run=pare --allow-env samples/CliSite/watch.mjs
import { execFileSync } from "node:child_process";

try {
  execFileSync("pare", ["src", "--outdir", "js"], { cwd: import.meta.dirname, stdio: "inherit" });
  console.log("stripped src/ into js/");
} catch (e) {
  // A non-zero exit: pare has printed the error, and the watcher runs this again on the next save.
  if (e.status == null) throw e;
}
