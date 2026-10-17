# 004 Svelte support is a separate product, Husk

Date: 2026-10-17. Status: accepted (the user).

The tier 1 spike ("Svelte and SvelteKit the way Deno runs them" in
[ecosystem-comparison.md](../ecosystem-comparison.md)) worked. Svelte support does not go into
Pare: it is a separate product, **Husk** (`../husk`), with its own repository and version line,
built on Pare's packages and not a fork of them. Husk records the full reasoning in its
`docs/decisions/2026-10-17-001-separate-product.md`.

What it means for Pare:

- Pare stays a type stripper. No engine, no Svelte, no new package in Pare.
- Pare gains one public, documented MSBuild extension point that lets another package run its own
  compile-to-JavaScript step through Pare's plumbing (output under `obj/`, static web assets,
  embedded output, re-strip on save in Development). Husk is its first user. Designed in Pare
  before Husk's build integration starts; tracked in `work/todo.md`.
- The Svelte compiler strips `<script lang="ts">` itself, so `.svelte` files never reach Pare's
  stripper.

Rejected: Svelte inside Pare.Build (V8 and Newtonsoft.Json for every user, no Alpine), an extra
package in this repo (shared version line, half again the tests and README), a fork (every
plumbing fix ported twice).
