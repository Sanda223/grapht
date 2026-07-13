# copilot-context-graph

Turn a .NET solution into a queryable knowledge graph that GitHub Copilot (VS Code chat / agent mode) can use instead of grepping.

Point it at a `.sln` and it builds a graph of every type, method, namespace, and Blazor component — who calls what, who inherits what, which component renders which — then exposes that graph to Copilot through an MCP server with `query`, `path`, and `explain` tools. Code extraction is deterministic (Roslyn, no LLM) and everything stays on your machine.

Inspired by [Graphify](https://github.com/Graphify-Labs/graphify) (MIT, © Safi Shamsi), rebuilt in C# and narrowed to one job: **C#/Blazor codebases, consumed through Copilot in VS Code.**

## Status

Design phase. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full design and [docs/ROADMAP.md](docs/ROADMAP.md) for the build order.

## Planned shape

```
copilot-context-graph/
├── src/
│   ├── ContextGraph.Core/    # pipeline: load → extract → build → cluster → analyze → export
│   ├── ContextGraph.Mcp/     # MCP stdio server for Copilot + file watcher
│   └── ContextGraph.Cli/     # dotnet tool: build/query/path/explain from a terminal
└── tests/
    └── ContextGraph.Tests/
```

## Requirements

- .NET 10 SDK (the tool itself)
- Analyzed projects can be anything Roslyn can load: modern SDK-style projects **and** legacy .NET Framework csproj (needs MSBuild/VS Build Tools present)
- VS Code with GitHub Copilot for the MCP integration

## License

MIT. Derived-from-design credit: [Graphify](https://github.com/Graphify-Labs/graphify), MIT License, Copyright (c) 2026 Safi Shamsi.
