<!-- Add this to your project's .github/copilot-instructions.md -->

## Codebase knowledge graph (use as an index, not an answer)

This workspace runs a `context-graph` MCP server holding an always-fresh knowledge
graph of the whole solution (types, methods, Blazor components, routes, events,
DI bindings, SQL objects, config keys).

For questions about architecture, relationships, call paths, or the impact of a
change, use the graph tools **to find out which files matter — then read those
files before answering**:

1. Call `query_graph` / `shortest_path` / `impact_of_change` / `get_node` first
   (instead of grepping) to locate the relevant code precisely.
2. Results carry `path Lstart-Lend` **spans**. Read those spans — not whole files;
   reading files the graph didn't point at wastes tokens.
3. Answer from the code, cite the locations. When answering a person, lead with a
   one-paragraph overview (the result's community-context line tells you the wider
   area), then the detailed trace.

Never answer from graph output alone: the graph shows *structure* (what connects
to what), not *behavior* — ordering, conditions, and transaction semantics live
only in the code bodies.
