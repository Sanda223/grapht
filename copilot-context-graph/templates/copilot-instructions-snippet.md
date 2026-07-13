<!-- Add this to your project's .github/copilot-instructions.md -->

## Codebase knowledge graph

This workspace runs a `context-graph` MCP server that holds an always-fresh knowledge
graph of the whole solution (types, methods, Blazor components, routes, JS interop).

For questions about architecture, relationships, call paths, or the impact of a change,
call the context-graph tools **before** grepping or reading files:

- `query_graph` — plain-language question → relevant subgraph with source locations
- `shortest_path` — how two things connect, hop by hop
- `impact_of_change` — what depends on a symbol (check before modifying shared code)
- `get_node` / `get_neighbors` — one symbol in detail

Every result carries `path:line` references; open those instead of searching.
