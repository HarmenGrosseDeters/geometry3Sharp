# Copilot instructions

Follow [AGENTS.md](../AGENTS.md), which summarizes the rules of [CLAUDE.md](../CLAUDE.md) and
[docs/AGENT_WORKFLOW.md](../docs/AGENT_WORKFLOW.md). Read those files before changing anything.

The rules that matter most:
- Answer the user in German. Code, comments, commits, PRs and docs are in English.
- Tests that fail on the old code; no numbers without the command that produced them.
- Run the local CI steps from CLAUDE.md before every push.
- Upstream 1.0.324 API compatibility.
- No mesh files in the repository; no GPL sources.
- Never read or port Triangle / Triangle.NET code or the owner's existing quadtree (clean room).
