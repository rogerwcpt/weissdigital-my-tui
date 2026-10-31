# my-tui — Implementation Roadmap

**Phased build plan for the session orchestrator described in [PLAN.md](./PLAN.md).**

> Version: 0.1 (draft)
> Status: Not started — `src/` is empty; planning docs only
> Current phase: **Phase 0**
> Source of truth: [PLAN.md](./PLAN.md) wins on vision, non-goals, and v1 success. This file only sequences the work.

Do not start a phase until the previous phase’s exit criterion is met and the Decision log below is updated. Non-goals in PLAN.md §1 are binding at every phase.

---

## How to use this roadmap

1. Work the current phase’s slices in order. A slice is one reviewable change, not a subsystem dump.
2. Stop at the phase exit criterion. Do not pull the next phase forward to “save a refactor.”
3. If a slice needs git UI, accounts, telemetry, an embedded browser, a native agent, or a Ghostty replacement, it does not belong in v1.
4. macOS is the development platform. Linux must run by the end of Phase 3. Windows is best-effort and is not a v1 gate.
5. Record binding choices in the Decision log before later slices depend on them.

### Invariants (all phases)

- my-tui itself makes no network calls and has no account or update client.
- The only app-owned writes are under `~/.config/my-tui/`. Session commands may write wherever the user pointed them.
- The file tree is rooted at the session cwd and does not offer a parent above that root.
- Unfocused sessions keep their process and VT state. Only the active terminal grid is rendered.
- Closing the app terminates session processes. v1 is not a daemon and does not reattach after restart.
- Persisted `env` is an explicit override map, never a dump of the inherited environment.

---

## Phase map

| Phase | Name | Outcome | Starts only when |
|-------|------|---------|------------------|
| 0 | Foundation spike | Stack is proven or a fallback is chosen | — |
| 1 | Real terminal pane | Several live terminals, good enough for agents | Phase 0 go / fallback decision |
| 2 | Session model and tabs | Durable sessions plus Files and Preview | Phase 1 fidelity exit |
| 3 | Polish and distribution | Daily use and a single binary | Phase 2 exit |
| — | Later | Not scheduled | v1 gate in Phase 3, including a week of real use |

```
Phase 0 ──go/fallback──▶ Phase 1 ──fidelity──▶ Phase 2 ──supervise──▶ Phase 3 ──v1 gate──▶ stop
   │                         │
   └── stop if no PTY        └── ANSI parser only if Phase 0 rejected both VT bindings
```

Suggested layout, created only as slices need it:

```
src/
  MyTui.sln
  MyTui/
    Program.cs
    UI/            session list, session view, panes
    Sessions/      model + manager          (Phase 2)
    Terminal/      PTY, VT, cell grid       (probe in Phase 0, real in Phase 1)
    Persistence/   state.json               (Phase 2)
docs/
  PLAN.md
  ROADMAP.md
```

Prefer one app project. A Phase 0 probe may live in a `spikes/` project only if it cannot sit beside the TUI; fold or delete it at the start of Phase 1. Do not leave two terminal stacks in the tree.

---

## Phase 0 — Foundation spike

**Intent.** Prove Terminal.Gui, a PTY, and a libghostty-vt binding before any architecture depends on them. Throwaway structure is fine. Unanswered risks are not.

**Exit criterion** (PLAN.md §5). Launch `bash` or `zsh` and see output, even if scrolling and color are wrong. Separately, a .NET binding loads and a `VTWrite` plus cell read works.

### In scope

- Terminal.Gui v2 window: vertical list, single content area, hardcoded placeholder sessions.
- Spawn the login shell on a PTY and show raw output in a TextView.
- A small probe that constructs a VT terminal, writes a known sequence, and reads cells back. Not wired into the UI.
- Pin the target framework and the package references used by the probe.

### Out of scope

- Cell-grid renderer, color fidelity, mouse, scrollback.
- More than one live PTY.
- Persistence, Files, Preview, themes, packaging.
- Choosing a binding without running it.

### Slices

| ID | Work | Done when |
|----|------|-----------|
| 0.1 | Solution and `my-tui` console app. Window with a left list and a right content view. | `dotnet run` shows the shell and quits cleanly. |
| 0.2 | PTY host. Spawn `$SHELL` (else `zsh` or `bash`), pump bytes into a TextView, forward ordinary keys. Naive resize is enough. | A prompt appears and typed characters echo. |
| 0.3 | VT probe for `DeBlasis.GhosttyVt` and `LibGhostty.Net`, one at a time if they conflict. Write a fixture (plain text plus one SGR attribute) and print the cells. | Probe prints the expected text on macOS, or records the load/build failure verbatim. |
| 0.4 | Decision note in the log below: binding, TFM, PTY owner (binding host vs platform wrapper). | Go, fallback, or stop is explicit. |

Also land the plan’s first-commit README note if it is still missing: point at `docs/PLAN.md` and state the non-goals. Do not expand the README into a user guide.

### Verification

- Child process is reaped on exit (no leftover shell).
- VT probe result is from this machine, not from package docs.
- List and content area are visibly separate; no tab control yet.

### Stop conditions

- No PTY can be spawned: Phase 1 does not start. Fix the host first.
- Neither binding loads: do not invent a renderer against a guessed API. Phase 1 may take the ANSI-parser fallback only after that failure is written into the Decision log.
- Terminal.Gui v2 cannot host a full-area view that will later own a cell grid: stop and re-read the layout approach before continuing. Do not switch UI frameworks inside the spike.

**Phase done when** the exit criterion passes and D1–D3 are filled in.

---

## Phase 1 — Real terminal pane

**Intent.** Replace the raw TextView with a VT-backed pane and run more than one session in memory.

**Exit criterion.** `vim`, `htop`, and one CLI agent (Claude Code or PI) are usable with acceptable fidelity. Acceptable means: cursor, alternate screen, clear, wrap, and basic color are right often enough to do real work. It will not match Ghostty’s GPU renderer.

### In scope

- One `TerminalSession` per row: PTY process, VT state, input/output loop.
- Cell-grid `View`: character + attribute, 256-color, bold, italic.
- Keyboard forwarding through the binding encoder (arrows, modifiers, Ctrl-C at minimum).
- Resize notifies both the PTY and the VT, then redraws.
- In-memory list: create, focus, close. Default command is `$SHELL` when none is given.
- `TERM=xterm-256color` (or the value the chosen binding documents) on spawn.
- Close kills the process group so agent helper processes die with the session.

### Out of scope

- JSON persistence and restore.
- Files, Preview, templates, themes, help overlay.
- Mouse, bracketed paste, and scrollback UI. Note what the binding already does; do not build those surfaces.
- Agent output parsing, icons, cost, or status beyond running / exited.

### Slices

| ID | Work | Done when |
|----|------|-----------|
| 1.1 | `TerminalSession`: allocate PTY, spawn, `VTWrite` the byte stream, track size. | A debug blit or log shows VT cells changing as the shell runs. |
| 1.2 | `CellGridView` blits dirty cells into Terminal.Gui. | The shell prompt is in the pane, not in a TextView. Colors and bold survive a simple `ls` or SGR fixture. |
| 1.3 | Key path: Terminal.Gui key → encoder → PTY. | Shell line editing and Ctrl-C work. |
| 1.4 | Resize path: host size → PTY winsize + VT resize → full redraw. | Dragging the host window does not leave a stale grid. |
| 1.5 | Session list owns N sessions. Focus swaps the visible grid only. Close disposes that session. | Two shells do not cross output. Closing one does not kill the other. |
| 1.6 | Fidelity pass against the exit commands. Fix only defects that block the criterion. | `vim`, `htop`, and one agent meet the bar, or the gap is written down as a phase blocker. |

### Fallback track

Use this only if Phase 0 rejected both bindings. Same slices, backed by a minimal ANSI parser sufficient for the exit criterion. Document missing sequences instead of growing a second emulator. Revisit the binding before Phase 3 only if the parser cannot pass the Phase 1 exit.

### Verification

- Unfocused session keeps running; switching back shows its latest screen.
- No zombie after close, including a session that launched a child.
- Rapid resize does not throw and does not desync rows/cols from the PTY.
- Known gaps (truecolor, ligatures, mouse, scrollback) are listed before Phase 2 starts.

### Phase 1 note for later decisions

Record whether libghostty-vt already keeps scrollback (D4) and whether mouse encoding is present (D5). Do not implement either here.

**Phase done when** the exit criterion is met on macOS.

---

## Phase 2 — Session model and tabs

**Intent.** Make the session list durable, and add the Files and Preview panes so an agent session can be supervised without leaving the app.

**Exit criterion.** Supervise 3–4 agent sessions and open a Markdown file from any of them.

### In scope

- Session shape from PLAN.md §6: `id`, `name`, `cwd`, `command`, `args`, `env`, `created`, `lastFocusedTab`.
- `SessionManager`: create, close, focus, reorder. Order is the list order.
- `~/.config/my-tui/state.json`: ordered sessions, last focused session id, last focused tab, optional window/layout preferences. Load on startup; write on change.
- Corrupt or unreadable state: copy the file aside, start empty, do not crash.
- Runtime-only fields (pid, running/exited, exit code) are never written to JSON.
- Tabs, exactly three, per session: **Terminal**, **Files**, **Preview**. Switching sessions restores that session’s tab and its live terminal.
- Restored sessions are not started automatically. The user starts them. Restart does not resume processes.
- File tree at the session cwd. Refresh on pane focus and on an explicit key. Distinguish directory vs file only.
- Preview: Terminal.Gui Markdown view for `.md`, plain text for other text files, a short refusal for binary. No byte dump.
- Status line: cwd, pid, exit code. Name is editable.
- New-session command defaults to `$SHELL` and is editable before start (closes D6).

### Out of scope

- In-app editing, file save, or a filesystem watcher.
- Git status, extra icons, search, or fuzzy open.
- Templates (Phase 3).
- Auto-starting every saved command on launch.
- Daemonizing, reattach, or keeping processes alive after quit.

### Slices

| ID | Work | Done when |
|----|------|-----------|
| 2.1 | `Session` + `SessionManager` in memory, including reorder. | List order and focus match the manager, with no JSON yet. |
| 2.2 | JSON store and startup load. Flush on create, close, rename, cwd/command edit, reorder, tab change, focus change. | Quit and relaunch restores metadata and starts nothing. |
| 2.3 | Per-session tab host. Terminal tab embeds the Phase 1 pane. | Tab and session switches do not dispose the PTY. |
| 2.4 | Files pane. Lazy directories, refresh, cwd as root. | `..` cannot navigate above the session cwd. |
| 2.5 | Preview pane bound to the tree selection, including when Preview is not the active tab. | `.md`, plain text, and binary follow the rules above. |
| 2.6 | Status line and rename. | A running session shows pid; an exited one shows the code; name survives restart. |
| 2.7 | Optional, non-blocking: key to spawn `$EDITOR` on the selected file and return. If it grows past spawn-and-return, defer to Phase 3 and set D7 to deferred. | Either the key works without embedding an editor, or D7 says deferred. |
| 2.8 | Manual supervise pass: 3–4 sessions, at least one real agent, open a `.md` in each. | Exit criterion observed, not inferred. |

### Verification

- `state.json` round-trips the PLAN.md §6 fields and does not contain pid, exit code, or inherited env.
- A bad JSON file is preserved as a backup and the app still opens.
- Four live sessions: each terminal is intact after a full cycle of switches and tab changes.
- Preview formatting is “basic Markdown,” not a custom renderer. Do not add a second Markdown stack.
- UI still has no login, upgrade, git, or worktree controls.

Add tests only where they lock a contract that the TUI cannot show well: JSON round-trip, corrupt-file fallback, and path confinement to cwd. No UI test harness.

**Phase done when** the exit criterion passes and D6 (and D7, even if deferred) are filled in.

---

## Phase 3 — Polish and distribution

**Intent.** Make the Phase 2 app daily-usable and shippable. No new product surface.

**Exit criterion.** Daily-usable for the author’s own agent workflows. v1 is not tagged until the release gate below is fully true, including a week of real use.

### In scope

- Keyboard-only operation: new session, rename, close, switch session, switch tab, focus panes, refresh files, quit. A short help overlay or status hint lists the keys.
- Confirm before closing a running session.
- Theme follows the host terminal and honors `NO_COLOR`.
- Session templates: a name plus command, cwd, optional args and env. Applying a template fills the new-session flow. It never launches a batch.
- Scrollback navigation if D4 says the VT already retains history. If it does not, write the limitation down. Do not build a parallel scrollback buffer unless a week of use proves the VT history is unusable.
- Copy/paste at “good enough” level: forward bracketed paste when the encoder supports it (agents need this). An explicit copy of visible VT text is enough if host selection cannot see the cell grid.
- Self-contained publish for macOS (primary) and at least one Linux RID. Direct-binary install notes. A Homebrew formula is optional and must not block the binary.
- README: what it is, non-goals, run, publish. Link this roadmap and the plan.

### Out of scope

- Everything under [Later](#later-not-scheduled).
- Theme editor, auto-update, plugin hooks, notifications.
- Code signing beyond what is required to run a local build. If Gatekeeper blocks the binary, document the local workaround; do not build a release pipeline.

### Slices

| ID | Work | Done when |
|----|------|-----------|
| 3.1 | Keymap and help. Bind existing Phase 2 actions before adding any new behavior. | The supervise flow in Phase 2.8 works without the mouse. |
| 3.2 | New, rename, and close flows, including the running-session confirm. | A session can be created and destroyed from the keyboard alone. |
| 3.3 | Host-terminal theme and `NO_COLOR`. | With `NO_COLOR` set, the UI does not force color. |
| 3.4 | Templates, stored beside app state, applied only into the new-session flow. | A template cannot start more than the one session the user confirms. |
| 3.5 | Time-boxed scrollback and paste. | Usable, or the limitation is documented and dogfood accepts it. |
| 3.6 | `dotnet publish` self-contained for the macOS RID in use, plus one Linux RID. Smoke-test the macOS binary outside `dotnet run`. | Linux binary starts far enough to show the session list (VM, container, or a second machine). |
| 3.7 | Dogfood against the v1 gate. | Gate checklist below is checked with dates, not intentions. |

Mouse support (D5) stays off unless dogfood shows a session cannot be operated from the keyboard. It is not required for v1.

### v1 release gate

All of PLAN.md §8:

1. Create a session that runs any CLI the user types.
2. At least four concurrent sessions, state kept for the life of the process.
3. Files tab → select a `.md` → Preview shows basic formatting.
4. No login, upgrade button, git UI, or worktree creation anywhere in the app.
5. One binary on macOS and a Linux build that starts.
6. The author uses it for real agent work for a week without going back to separate terminal windows.

**Phase done when** the gate is met. Tag v1 only after item 6.

---

## Later (not scheduled)

Do not open implementation spikes for these during Phases 0–3. A one-line note is enough if an idea appears; it does not enter the slice list.

- Cost or token tracking
- Agent-specific status parsing
- Notifications
- Remote or SSH sessions
- Plugin system
- Git or worktree awareness
- Embedded editor
- Replacing the host terminal as a daily driver

Re-read PLAN.md §1 before promoting anything from this list. Promotion happens only after the v1 gate, in a new roadmap revision.

---

## Decision log

Fill these in the phase that owns them. Do not leave a default unspoken once that phase has started.

| ID | Question | Owner | Resolution | Default if the spike is inconclusive |
|----|----------|-------|------------|--------------------------------------|
| D1 | `LibGhostty.Net` vs `DeBlasis.GhosttyVt` | Phase 0 | | Run both probes. Pick the one that loads and reads cells. If neither works, ANSI fallback for Phase 1. |
| D2 | PTY from the binding vs a platform PTY wrapper | Phase 0 | | Platform wrapper when the binding PTY is missing or cannot spawn a shell. |
| D3 | `net9.0` vs `net10.0` | Phase 0 | | `net9.0` unless Terminal.Gui v2 or the chosen binding requires `net10.0`. |
| D4 | Scrollback lives in libghostty-vt or a mirrored buffer | Note in Phase 1, act in Phase 3 | | Use VT history if it exists. Do not mirror unless dogfood fails without it. |
| D5 | Mouse in v1 | Phase 3 | | Not required. |
| D6 | Default command for a new session | Phase 2 | | `$SHELL`, editable before start. Last-used command is a template concern, not the default. |
| D7 | Open in `$EDITOR` from Files | Phase 2, else Phase 3 | | Yes, if it is spawn-and-return. Otherwise defer; do not block Phase 2. |

### Phase 0 record (fill when 0.4 is done)

- Date:
- Binding:
- TFM:
- PTY owner:
- Go / fallback / stop:
- Evidence (command output summary, not a paste of a whole build log):

### Fidelity gaps carried forward

Write gaps here at the end of Phase 1 and again after Phase 3.5. A gap that is not listed will be rebuilt by accident.

| Gap | Found in | Blocks v1? | Resolution |
|-----|----------|------------|------------|
| | | | |

---

## Phase checklist

Copy into the working notes for the phase in progress.

- [ ] PLAN.md non-goals re-read
- [ ] Slices still match this roadmap; no silent scope add
- [ ] Exit criterion demonstrated on macOS
- [ ] Decision log updated
- [ ] Gaps written down before the next phase starts

---

## Mapping back to the plan

| PLAN.md | Where it is implemented |
|---------|-------------------------|
| §2 Layout and three tabs | Phase 0 list shell; Phase 1 terminal; Phase 2 tabs, Files, Preview, status |
| §3 Stack | Chosen and pinned in Phase 0; published in Phase 3 |
| §4 Architecture | Session list and terminal loop in Phase 1; manager, persistence, and panes in Phase 2 |
| §5 Phases | This document’s phases, unchanged in order and exit criteria |
| §6 Data model | Phase 2 store; templates extend state in Phase 3 without changing the session shape |
| §7 Risks | Binding and PTY risks close in Phase 0; render and input risks close in Phase 1; scope risk is the invariant list |
| §8 v1 success | Phase 3 release gate |
| §9 Open questions | Decision log D1–D7 |
