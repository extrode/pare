# Troubleshooting

## The page is blank, or the script 404s

- Is the page asking for `.js`? The `.ts` is never served in a published site. Point your
  `<script>` at `/js/app.js`.
- Did the build run? Look for `obj/<configuration>/<tfm>/pare/js/app.js`.
- Did you call `app.MapStaticAssets()`? The build registers Pare's output as a static web asset,
  and `MapStaticAssets` serves it. With `CreateSlimBuilder`, also call
  `builder.WebHost.UseStaticWebAssets()`; without it an unpublished run answers requests that send
  `Accept-Encoding` with the build's compressed copies it cannot find, 200 and an empty body.
- Is `/` a 404 while `/js/app.js` works? Add `app.UseDefaultFiles();` and `app.UseRouting();`
  before `app.MapStaticAssets();`.

## `warning PARE200: not served`

A `foo.js` sits next to `foo.ts` in `wwwroot`. Delete it (and any `.gitignore` rule for it). The
build serves the stripped `foo.ts` from `obj/`.

## `warning PARE202: not served`

A source outside `wwwroot` has no `PareServeAt`. Add one, or add `PARE202` to `NoWarn`. See
[TypeScript outside wwwroot](../03-guides/typescript-outside-wwwroot.md).

## An edit does not show up under `dotnet watch`

- Did you call `builder.AddPareDevelopment()`? Without it a `.ts` save runs no build.
- Run `dotnet watch` from the project directory, not with `--project` from elsewhere.
- A `PublishAot` project needs `<StartupHookSupport Condition="'$(Configuration)' == 'Debug'">true</StartupHookSupport>`.
- A `.ts` file you **added** during the session is 404 until the next build. Press Ctrl+R.

More in [Hot reload](../03-guides/hot-reload.md).

## The build says `error PARE100` to `PARE107`

Your TypeScript uses syntax that needs generated code. [Error codes](error-codes.md) shows the
plain JavaScript to write for each. Turn on `"erasableSyntaxOnly": true` in `tsconfig.json` so the
editor warns you first.

## `error PARE001: syntax error`

The file is not valid TypeScript at that position. Pare stops at the first syntax error. `.tsx` and
JSX are not supported.

## A type error went through

Pare does not check types. See [Type checking in CI](../03-guides/type-checking.md).

## An import fails at run time but the file stripped fine

You imported a type without `type`: `import { Foo } from "./foo.ts"` where `Foo` is an interface.
Pare cannot know, so it keeps the import. Write `import type { Foo }` or `import { type Foo }`, and
set `"verbatimModuleSyntax": true` so `tsc` reminds you.

## Visual Studio

Visual Studio 2026 (MSBuild 18) runs the task in MSBuild's .NET task host, a separate process.
Visual Studio 2022 cannot build `net10.0` projects at all.

## linux-musl-arm64 and NativeAOT

.NET misreads the main thread's stack size there, so the deep-nesting check fails at once. Call
`TypeStripper.Strip` (or `pare_strip`) from a thread you created.
