# CDN fixtures

Copies of the two files `samples/WebApp` loads from jsDelivr, served to the browser tests by
`RouteCdn` in `BuildIntegrationTests.cs` so a test never needs the CDN. Unmodified.

| File | Source | Licence |
|---|---|---|
| `htmx.min.js` | https://cdn.jsdelivr.net/npm/htmx.org@2.0.4/dist/htmx.min.js (matches the SRI hash in `index.html`) | 0BSD |
| `alpine-module.esm.js` | https://cdn.jsdelivr.net/npm/alpinejs@3.14.8/dist/module.esm.js, Caleb Porzio | MIT, see `LICENSE-alpine.md` |

When the sample's versions change, re-download both files from the new URLs and update the keys in
`CdnFixtures`.
