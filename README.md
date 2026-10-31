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

## Run

```
dotnet run --project src/MyTui
```

Ctrl+N creates a session. The command defaults to `$SHELL` and can be edited before it starts. Ctrl+O moves between the session list and the open tab, Ctrl+T cycles Terminal, Files, and Preview, Ctrl+E edits the session, and Delete closes it. Enter starts a stopped session. Ctrl+R refreshes Files. Alt+Up and Alt+Down reorder the list. Ctrl+Q quits. Sessions are restored from `~/.config/my-tui/state.json` and are not started until Enter.
