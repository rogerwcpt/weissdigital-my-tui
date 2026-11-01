# my-tui

<img src="images/logo.jpg" alt="my-tui" width="160" />

<img src="images/screenshot.png" alt="my-tui running pi, with the session list on the left and the terminal on the right." width="720" />

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

Ctrl+N creates a session. The command defaults to `$SHELL` and can be edited before it starts. Direct keys work in the session list, the file tree, the preview, and an idle or exited session. Inside a live terminal, Ctrl+B is the leader: the next key is the command, and Ctrl+B a second time sends Ctrl+B to the program. Ctrl+O moves between the session list, the open tab, and the preview. Ctrl+T switches Terminal and Files. Ctrl+P opens or closes the preview panel on the right; it stays there while you use the terminal. Ctrl+E edits the session. Ctrl+R refreshes Files. Delete closes a session, including the key labeled Delete on a Mac. A running session asks first. Delete inside a running terminal still deletes a character. Enter starts a stopped session. Alt+Up and Alt+Down reorder the list. F1 lists the keys. F2 applies a template into the new-session dialog. Shift+PageUp and Shift+PageDown scroll the terminal. The mouse wheel scrolls the history in a shell, scrolls a full-screen program, and is delivered to a program that tracks the mouse. A session you are not viewing shows ● after new output and ! after a bell. Ctrl+Q quits from outside a live terminal, and quitting asks first while any session is still running. Inside a live terminal Ctrl+Q goes to the program; Ctrl+B q quits from there. An optional `leader` in `~/.config/my-tui/config.json` (`ctrl+` a letter, `ctrl+space`, or `ctrl+backslash`) replaces Ctrl+B. Sessions are restored from `~/.config/my-tui/state.json` and are not started until Enter. Templates are `~/.config/my-tui/templates.json`.

## Publish

The publish output is a folder. Run `my-tui` from that folder. There is no Homebrew formula.

```
dotnet publish src/MyTui -c Release -r osx-arm64 --self-contained
dotnet publish src/MyTui -c Release -r linux-x64 --self-contained
```

On macOS, `libmytui_pty.dylib` has to stay next to `my-tui`. The publish step ad-hoc signs both. If Gatekeeper blocks a copied binary, `xattr -d com.apple.quarantine my-tui` or `codesign --force -s - my-tui`. The Linux build is `linux-x64`, which is the RID the VT library ships. It does not need libicu.
