# my-tui — Project Plan

**A minimal, terminal-first session orchestrator for bring-your-own CLI agent harnesses.**

> Version: 0.1 (draft)  
> Status: In progress — Phase 0 complete  
> Name: **my-tui**  
> Binary: `my-tui`  
> Target: Terminal.Gui + libghostty-vt

---

## 1. Vision & Constraints

### Goal
A single-window TUI that lets you run multiple independent CLI agent sessions (Claude Code, Codex, PI, custom harnesses, etc.) side by side, with a simple way to view Markdown and files from the working directory of each session.

### Non-goals (hard constraints)
- No native agent harness or model integration
- No accounts, login, upgrade prompts, or telemetry
- No git integration, worktrees, or branching UI
- No embedded browser
- No commercial upsell surface
- No attempt to replace Ghostty itself as a daily terminal

### Design principles
- **KISS** — vertical session list + at most three panes per session
- **Bring your own harness** — the terminal pane just runs whatever CLI you give it
- **Terminal first** — pure TUI, runs inside any host terminal
- **Viewable enough** — Markdown and plain files should be readable without becoming a full editor
- **Local and private** — everything stays on the machine; no cloud dependency

---

## 2. Core User Experience

### Layout
```
┌─────────────────┬──────────────────────────────────────────────┐
│ Sessions        │  [ Terminal | Files | Preview ]               │
│                 │                                              │
│ ● PI (main)     │  ┌────────────────────────────────────────┐  │
│ ○ Claude Code   │  │                                        │  │
│ ○ Codex         │  │          Active pane content           │  │
│ ○ Custom        │  │                                        │  │
│                 │  │                                        │  │
│                 │  └────────────────────────────────────────┘  │
│                 │                                              │
│ [+] New session │  Status: cwd · pid · exit code               │
└─────────────────┴──────────────────────────────────────────────┘
```

### Per-session tabs (exactly three)
1. **Terminal** — interactive PTY running the chosen CLI harness
2. **Files** — simple file tree rooted at the session’s working directory
3. **Preview** — Markdown or plain-text viewer for the currently selected file

Switching sessions restores the last active tab for that session.

### Minimal session metadata shown
- Name (user-editable)
- Working directory
- Command that was launched
- Running / exited status
- Optional: short custom label

No cost tracking, no context-window meters, no agent-specific icons (keep it generic).

---

## 3. Technology Stack

| Layer              | Choice                     | Rationale |
|--------------------|----------------------------|-----------|
| UI framework       | Terminal.Gui (v2)          | Already used in My Commander; mature tabs, trees, Markdown view |
| Terminal engine    | libghostty-vt              | Correct, high-quality VT state; .NET bindings exist |
| PTY                | Platform PTY (or LibGhostty.Net PTY host) | Spawn any CLI with full terminal semantics |
| Language / runtime | C# / .NET 9+ or .NET 10    | Matches existing skills and My Commander |
| Persistence        | JSON file in `~/.config/my-tui/` | Session list, last cwd, last command |
| Distribution       | Self-contained single binary (macOS / Linux / Windows) | Same model as My Commander |

### Key libraries
- `Terminal.Gui` — layout, tabs, TreeView, Markdown view, themes
- `DeBlasis.GhosttyVt` or `LibGhostty.Net` — VT state machine
- Platform PTY wrapper (or the PTY layer already in LibGhostty.Net)

---

## 4. Architecture

### High-level components

```
┌─────────────────────────────────────────────────────────┐
│ Application (Terminal.Gui)                              │
│  ┌──────────────┐  ┌─────────────────────────────────┐  │
│  │ SessionList  │  │ SessionView (Tabs)              │  │
│  │              │  │  ┌─────────┐ ┌──────┐ ┌───────┐  │  │
│  │              │  │  │Terminal │ │Files │ │Preview│  │  │
│  │              │  │  │Pane     │ │Pane  │ │Pane   │  │  │
│  │              │  │  └─────────┘ └──────┘ └───────┘  │  │
│  └──────────────┘  └─────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────┐     ┌──────────────────────┐
│ SessionManager      │────▶│ Persistence (JSON)   │
│ - create / close    │     └──────────────────────┘
│ - focus / reorder   │
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│ TerminalSession     │
│ - PTY process       │
│ - libghostty-vt     │
│ - input/output loop │
└─────────────────────┘
```

### Terminal pane responsibilities
1. Allocate PTY and spawn the configured command (default shell if none given).
2. Create a `libghostty-vt` Terminal of matching size.
3. Forward PTY bytes → `VTWrite`.
4. On dirty render state, blit cells into a Terminal.Gui View (character + attribute).
5. Forward keystrokes and mouse (if enabled) via KeyEncoder / MouseEncoder back to the PTY.
6. Handle resize: notify both PTY and libghostty-vt, then redraw.

### File / Preview panes
- File tree rooted at session working directory (refresh on demand or on focus).
- Preview pane uses Terminal.Gui’s Markdown view (with optional TextMate syntax highlighting for code blocks) or a plain TextView for non-Markdown files.
- Opening a file in the tree updates the Preview pane; double-click or a key can still open it in `$EDITOR` if desired.

---

## 5. Feature Breakdown (Phased)

### Phase 0 — Foundation (spike)
- [x] Minimal Terminal.Gui app with a vertical list and a single content area
- [x] Proof-of-concept: spawn a shell in a PTY and display raw output in a TextView (no full VT yet)
- [x] Confirm libghostty-vt .NET binding can be loaded and a simple VTWrite + cell read works
- **Exit criterion**: can launch `bash` or `zsh` and see output, even if scrolling/colors are imperfect. Met 2026-10-31. Decision is in [ROADMAP.md](./ROADMAP.md).

### Phase 1 — Real terminal pane
- [ ] Integrate libghostty-vt for correct VT state
- [ ] Cell-grid renderer inside a Terminal.Gui View (basic 256-color + bold/italic)
- [ ] Keyboard input forwarding
- [ ] Resize handling
- [ ] Multiple concurrent terminal sessions in a list
- **Exit criterion**: can run `vim`, `htop`, and a CLI agent (Claude Code / PI) with acceptable fidelity

### Phase 2 — Session model & tabs
- [ ] Persistent session list (name, cwd, command)
- [ ] Per-session tabs: Terminal / Files / Preview
- [ ] Simple file tree (no git status, no icons beyond type)
- [ ] Markdown preview using Terminal.Gui Markdown view
- [ ] Switch sessions without losing terminal state
- **Exit criterion**: can supervise 3–4 agent sessions and flip to a Markdown file in any of them

### Phase 3 — Polish & usability
- [ ] Themes that respect `NO_COLOR` and host terminal
- [ ] Keyboard-driven workflow (new session, rename, close, switch tab, focus panes)
- [ ] Session templates (pre-filled command + cwd)
- [ ] Optional: copy/paste, scrollback navigation in terminal pane
- [ ] Self-contained builds + simple install path (Homebrew formula or direct binary)
- **Exit criterion**: daily-usable for the author’s own agent workflows

### Later (explicitly out of scope for v1)
- Cost / token tracking
- Agent-specific status parsing
- Notifications
- Remote / SSH sessions
- Plugin system
- Git or worktree awareness

---

## 6. Data Model

### Session
```json
{
  "id": "uuid",
  "name": "PI – main",
  "cwd": "/Users/roger/src/my-project",
  "command": "pi",
  "args": [],
  "env": {},
  "created": "2026-10-31T12:00:00Z",
  "lastFocusedTab": "terminal"
}
```

### Application state
- List of sessions (order preserved)
- Last focused session id
- Window size / layout preferences (optional)

Stored as `~/.config/my-tui/state.json`.

---

## 7. Key Technical Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Terminal rendering quality in pure TUI is too coarse | Medium | High | Start with Phase 0/1 spike; accept that it will not match Ghostty GPU rendering; focus on “good enough for agents” |
| libghostty-vt .NET bindings are incomplete or unstable | Medium | High | Spike early; fall back to a simpler ANSI parser if needed for v1 |
| PTY + input forwarding has edge cases (mouse, bracketed paste, focus) | Medium | Medium | Implement incrementally; prioritize keyboard + resize first |
| Terminal.Gui layout performance with many sessions | Low | Medium | Virtualize session list; only render the active terminal grid |
| Scope creep toward desktop app or full IDE | High | High | Explicit non-goals in this document; review against them before adding features |

---

## 8. Success Criteria for v1

1. Launch the app and create a new session that runs any CLI agent the user chooses.
2. Switch between at least four concurrent sessions without losing state.
3. In any session, open the Files tab, select a `.md` file, and read it in the Preview tab with basic formatting.
4. No login, no upgrade button, no git UI, no worktree creation.
5. Runs as a single binary on macOS (primary) and at least Linux.
6. The author uses it for real agent work for a week without reverting to separate terminal windows.

---

## 9. Open Questions

- Exact .NET binding to prefer (`LibGhostty.Net` vs `DeBlasis.GhosttyVt`) — decided in Phase 0: DeBlasis.GhosttyVt 1.4.1. See [ROADMAP.md](./ROADMAP.md).
- Whether to support a “quick open in `$EDITOR`” action from the Files pane (recommended: yes, optional).
- Default command when creating a new session (shell vs last-used agent command).
- Whether scrollback should live entirely inside libghostty-vt or be mirrored into a Terminal.Gui buffer.
- Mouse support priority for v1 (nice-to-have vs required).

---

## 10. References

- Terminal.Gui: https://github.com/gui-cs/Terminal.Gui
- libghostty-vt concept: https://mitchellh.com/writing/libghostty-is-coming
- .NET bindings examples: https://github.com/deblasis/libghostty-dotnet
- Author’s existing TUI: https://github.com/rogerwcpt/weissdigital-my-commander
- Inspiration (what not to copy wholesale): Ghostty Dock, Otty, cmux

---

## Next Step

Phase 0 is complete. Phase 1 has not started. The build order and the D1–D3 decision are in [ROADMAP.md](./ROADMAP.md).
