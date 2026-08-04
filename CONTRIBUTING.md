# Contributing to grapht

Thanks for your interest! This document covers how the project is built, tested,
and how changes land.

## Getting started

- Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Build and test everything:

```bash
dotnet test grapht/Grapht.slnx
```

The full setup guide (including Linux notes) is in the [README](README.md).

## How changes land

1. **Branch off `main`** — short-lived feature branches (`feat-…`, `fix-…`, `docs-…`).
2. **Test-driven** — behavioural changes land with their test written first and seen
   failing. The repo's testing rule of thumb: *assert it runs, not that it's present*
   (see `ViewPageTests` for the canonical example).
3. **Conventional commits** — `feat(view): …`, `fix(core): …`, `docs: …`, `chore: …`.
4. **Open a pull request** — CI must pass (build + full test suite). PRs merge with a
   merge commit, not squash.
5. **Design first for bigger work** — features of any real size get a short spec in
   `grapht/docs/specs/` and, when useful, a plan in `grapht/docs/plans/`, committed
   alongside or ahead of the code. Read a couple of existing ones for the house style.

## Release pipeline (DEV → TEST → PROD)

- **DEV** — every PR runs the CI workflow (build + tests) and must be green to merge.
- **TEST** — every merge to `main` publishes `X.Y.Z-preview.N` packages to GitHub
  Packages automatically.
- **PROD** — pushing a `vX.Y.Z` tag publishes that version to NuGet.org, gated by a
  manual approval on the `production` environment, and creates a GitHub Release.

## Project layout

- `grapht/src/Grapht.Core` — extraction, graph model, clustering, query engine.
- `grapht/src/Grapht.Cli` — the `grapht` command-line tool.
- `grapht/src/Grapht.Mcp` — the MCP server for assistant integration.
- `grapht/src/Grapht.View` — the interactive 3D graph viewer (loopback web app).
- `grapht/tests` — one xUnit project covering all of the above.
- `grapht/docs` — architecture, specs, and plans.

## Reporting issues

Use the issue templates. For anything security-sensitive, see [SECURITY.md](SECURITY.md)
instead of opening a public issue.
