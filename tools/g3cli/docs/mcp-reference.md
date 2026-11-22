# g3cli MCP Server Reference

`g3cli mcp` exposes every `g3cli` subcommand as a Model Context Protocol (MCP) tool over `stdio` using newline-delimited JSON-RPC 2.0.

---

## Protocol & Tool Schema

- **Transport:** `stdio` (newline-delimited JSON-RPC 2.0; diagnostic logs go to `stderr`).
- **Supported protocol versions:** `2025-06-18`, `2025-03-26`, `2024-11-05`.
- **Tool definitions:** Generated from the exact same parameter definitions as the CLI. Parameter names use `snake_case` with typed JSON schemas (`inputSchema`).
- **Tool results:** Every response returns the JSON result payload both in `structuredContent` and as formatted text. `isError` is `true` whenever `code != "ok"`.
- **File paths:** Relative paths resolve against the MCP server process's working directory.
- **Concurrency:** Requests are processed serially one after another. Long-running operations (such as solidification repairs or very large booleans) block subsequent calls until finished.

---

## Client Setup

Build `g3cli` in Release first:

```bash
dotnet build tools/g3cli -c Release
```

### Claude Code

```bash
claude mcp add g3 -- dotnet /path/to/geometry3Sharp/tools/g3cli/bin/Release/net10.0/g3cli.dll mcp
```

### Generic JSON Client Config (`mcpServers`)

```json
{
  "mcpServers": {
    "g3": {
      "command": "dotnet",
      "args": ["/path/to/geometry3Sharp/tools/g3cli/bin/Release/net10.0/g3cli.dll", "mcp"]
    }
  }
}
```

---

## Raw JSON-RPC Example

```bash
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"boolean","arguments":{"a":"a.stl","b":"b.stl","output":"u.stl","op":"union"}}}' \
  | dotnet tools/g3cli/bin/Release/net10.0/g3cli.dll mcp
```

---

## Agent Workflow Tips

- **Inline geometry where supported:** `mesh2d` accepts inline `--geometry` JSON, `polygon2d` accepts `--subject-geojson` / `--pattern-geojson` / `--return-geojson`, `slope` accepts `--toes-geojson`, `dgm_tin` accepts `--breaklines-geojson`, and `query3d` accepts inline JSON arrays for rays and points—avoiding temporary files for small inputs.
- **Always check `code` and `warnings`:** Commands such as `quad --pure-quads` (fallback to quad-dominant), `wrap` (`point_limit_reached`), and `slope` (`no_daylight`) return `code: "ok"` with diagnostic flags and warnings rather than failing.
- **Skip expensive exact self-intersection scans when not needed:** Pass `"self_intersections": false` (`--no-self-intersections`) on `measure` for quick topology checks on very large meshes.

See the command references for full parameter and response details:
- [Mesh Commands](mesh-commands.md)
- [Analysis & Query Commands](analysis-commands.md)
- [2D, Terrain & Lattice Commands](2d-and-terrain.md)
