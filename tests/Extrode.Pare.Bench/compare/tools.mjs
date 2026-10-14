// Runs one TypeScript-to-JavaScript tool over a list of files, in-process, for the comparison in
// Compare.cs. Writes each output to <out>/<tool>/<relative>.js and prints one JSON line:
// { tool, version, ok, failed, bytes, passes: [ms per pass over the files it handled] }.
//
//   node --no-warnings tools.mjs <list: "path\trelative" per line> <out> <tool> <passes>

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { createRequire } from "node:module";

const [list, out, tool, passArg] = process.argv.slice(2);
const require = createRequire(import.meta.url);

const tools = {
  async "node-strip"() {
    const { stripTypeScriptTypes } = await import("node:module");
    return { version: process.version, run: (s) => stripTypeScriptTypes(s, { mode: "strip" }) };
  },
  async "ts-blank-space"() {
    const blank = (await import("ts-blank-space")).default;
    return {
      version: require("ts-blank-space/package.json").version,
      run(s) {
        let unsupported = false;
        const js = blank(s, () => { unsupported = true; });
        if (unsupported) throw new Error("unsupported syntax");
        return js;
      },
    };
  },
  async esbuild() {
    const esbuild = await import("esbuild");
    const options = {
      loader: "ts",
      tsconfigRaw: { compilerOptions: { verbatimModuleSyntax: true, useDefineForClassFields: true, target: "esnext" } },
    };
    return { version: esbuild.version, run: (s) => esbuild.transformSync(s, options).code };
  },
  async oxc() {
    const oxc = await import("oxc-transform");
    const options = { typescript: { onlyRemoveTypeImports: true } };
    return {
      version: require("oxc-transform/package.json").version,
      run(s, name) {
        const result = oxc.transformSync(name, s, options);
        if (result.errors.length) throw new Error(result.errors[0].message);
        return result.code;
      },
    };
  },
};

const { version, run } = await tools[tool]();
const files = readFileSync(list, "utf8").split("\n").filter(Boolean).map((line) => {
  const [path, relative] = line.split("\t");
  return { relative, text: readFileSync(path, "utf8") };
});

const handled = [];
let failed = 0;
for (const file of files) {
  let js;
  try {
    js = run(file.text, file.relative);
  } catch {
    failed++;
    continue;
  }
  const target = join(out, tool, file.relative + ".js");
  mkdirSync(dirname(target), { recursive: true });
  writeFileSync(target, js);
  handled.push(file);
}

const bytes = handled.reduce((n, f) => n + Buffer.byteLength(f.text), 0);
const passes = [];
for (let pass = 0; pass <= Number(passArg); pass++) {
  const start = performance.now();
  for (const file of handled) run(file.text, file.relative);
  if (pass > 0) passes.push(performance.now() - start); // pass 0 warms up
}
console.log(JSON.stringify({ tool, version, ok: handled.length, failed, bytes, passes }));
