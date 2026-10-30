# Handoff: what Husk needs from Pare before Husk can go public

Date: 2026-10-30. From: Husk (`~/Developer/extrode.com/husk`, branch `dev`). Husk's tasklist T13 and
`work/tasklist.md` there track the other side.

## Context

Husk (Svelte for .NET, built on Pare's `PareCompileSource` extension point from 0.6.0) is about
to be published to NuGet and its GitHub repo made public. Its packages depend on
`Extrode.Pare` / `.Build` / `.AspNetCore`. Checked 2026-10-30:

- `https://api.nuget.org/v3-flatcontainer/extrode.pare/index.json` returns 404, and so do `.build`
  and `.aspnetcore`. Pare 0.6.0 exists only as a GitHub prerelease tag. A stranger cannot restore
  Husk until Pare is on nuget.org.
- `extrode/pare` is a private repo. Husk's README and docs link to it.
- `.github/workflows/` has `ci.yml` only: there is no release workflow.

## Needs

### N1. Release Pare to nuget.org (required, blocks Husk's publish)

Release **0.6.1** (patch: the only change since 0.6.0 is the PARE200 fix already merged on `dev`,
`fix/pare200-same-name-output`). Bump `Version` in `Directory.Build.props`, move the changelog's
`[Unreleased]` entry under `[0.6.1]`, tag `v0.6.1`. Packages: `Extrode.Pare`, `Extrode.Pare.Build`,
`Extrode.Pare.AspNetCore`, and `Extrode.Pare.Cli` if it is meant to be public. Tell Husk the final
version and ids; Husk then moves its `Extrode.Pare*` references to it and stops using `packages/`.

### N2. A release workflow, the way jauntyq does it (required)

Copy the shape of `~/Developer/extrode.com/jauntyq/.github/workflows/release.yml`:

- Triggered by `v*` tags; a guard step fails when the tag differs from `<Version>`.
- Job `environment: packages`, `permissions: id-token: write`.
- `NuGet/login` (OIDC, pinned by digest, `user: ${{ vars.NUGET_USER }}`), then `dotnet nuget push
  --skip-duplicate`. No API key secret anywhere.
- A `workflow_dispatch` job that only proves the trusted-publishing exchange works.
- GitHub Release for the tag, marked prerelease while `v0.*`.

The workflow file must be named `release.yml`: the policy on nuget.org names the file.

**The user does these by hand, once, and cannot be done from the repo:**

1. nuget.org, Trusted Publishing: add a policy for repository `extrode/pare`, workflow `release.yml`,
   environment `packages`.
2. GitHub, `extrode/pare` Settings: a variable `NUGET_USER` (the nuget.org profile name, `sbeparey`
   in jauntyq), an environment `packages` with a required reviewer.

### N3. Public readiness (Pare's call, but Husk's docs link to it)

Pare must be public before, or together with, Husk. It already has `LICENSE.md` (ISL-P 1.2) and
`NOTICE.md`. Not checked from Husk's side: a secret scan of Pare's history, a `SECURITY.md`,
`CONTRIBUTING.md`, the repo description and topics, and whether `docs/handoffs/` and `work/` should
be public. Husk's own audit found: no secrets at HEAD, one author email in all commits, internal
workflow files (`AGENTS.md`, `work/`, `docs/lessons/`) that will become visible.

### N4. Optional, can follow later: `buildTransitive`

Ship `Extrode.Pare.Build`'s `build/` as `buildTransitive/` too, so a project that references only
`Extrode.Husk.Build` still gets Pare's targets. Husk can then drop HUSK002 (the error that tells
users to reference Pare.Build directly). Husk works without it; the README documents the
three-reference install.

## Order

1. N1 + N2 on Pare `dev`, merged, unpushed (the user pushes).
2. User sets up N2's nuget.org policy and GitHub environment, makes Pare public, pushes the tag.
3. Husk bumps to the published Pare version, then goes public.
