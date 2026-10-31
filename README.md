# my-tui

A minimal, terminal-first session orchestrator for bring-your-own CLI agent harnesses.

The plan is in [docs/PLAN.md](docs/PLAN.md). The build order is in [docs/ROADMAP.md](docs/ROADMAP.md).

## Non-goals

- No native agent harness or model integration
- No accounts, login, upgrade prompts, or telemetry
- No git integration, worktrees, or branching UI
- No embedded browser
- No commercial upsell surface
- No attempt to replace Ghostty itself as a daily terminal

## Phase 0

```
dotnet run --project src/MyTui
```

Ctrl+Q quits. The first row is one live shell. The other rows are placeholders.
