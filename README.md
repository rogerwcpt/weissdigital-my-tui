# my-tui

<img src="images/logo.jpg" alt="my-tui" width="160" />

<img src="images/screenshot.png" alt="my-tui running pi, with the session list on the left and the terminal on the right." width="720" />

A minimal, terminal-first session orchestrator for bring-your-own CLI agent harnesses.

## Non-goals

- No native agent harness or model integration
- No accounts, login, upgrade prompts, or telemetry
- No git integration, worktrees, or branching UI
- No embedded browser
- No commercial upsell surface
- No attempt to replace Ghostty or iTerm or Warp as a daily terminal

## Install

### macOS

Apple silicon:

```
brew trust rogerwcpt/my-tui
brew install rogerwcpt/my-tui/my-tui
```

Homebrew asks for trust once. The formula keeps `libmytui_pty.dylib` next to the real binary and symlinks `my-tui` onto your PATH.

The release archive is the other route. Download `my-tui-<version>-osx-arm64.tar.gz` from the [latest release](https://github.com/rogerwcpt/weissdigital-my-tui/releases/latest), unpack it, and leave `libmytui_pty.dylib` next to `my-tui`. A browser download is quarantined by Gatekeeper. Clear it with `xattr -d com.apple.quarantine my-tui`.

## Run from source

```
dotnet run --project src/MyTui
```

F9 opens the Command Palette, where you will find all the commands in one list, grouped by area. Type to filter, then press Enter to run the selected command. It works even while a terminal session is running.

### Commands

Host terminals and agent CLIs swallow most key chords, so my-tui has no per-command shortcuts. F9 is the one key it keeps, plus Ctrl+Shift+C and Ctrl+Shift+V for copy and paste when your host terminal passes them through.

| Group | Command | What it does |
| --- | --- | --- |
| Session | Close the session | Close the selected session, asking first if it is running. |
| Session | Duplicate session | Start a fresh copy of the selected session. |
| Session | Edit the session | Edit the selected session's settings. |
| Session | New session | Create a session. The command defaults to `$SHELL` and can be edited before starting. |
| Session | Open folder | Choose a folder and start a new shell session there. |
| Session | Rename the session | Change the selected session's name. |
| Session | Start the session | Start a stopped session. Enter in the session list does the same. |
| Session | Templates | Apply a saved template in the new-session dialog. |
| Layout | Hide or show Sessions | Toggle the session list. |
| Layout | List, tab, or preview | Move focus between the session list, the open tab, and the preview. |
| Layout | Maximize or restore | Maximize the focused panel or restore the layout. |
| Layout | Next / previous panel | Move focus between visible panels. |
| Layout | Open or close the preview | Toggle the preview panel on the right; it stays open while you use the terminal. |
| Layout | Terminal or Files | Switch between the Terminal and Files tabs. |
| Files | Open selected file | Open the selected file in its system-default application while Files is active. |
| Files | Refresh Files | Refresh the file tree. |
| Clipboard | Copy selection or screen (Ctrl+Shift+C) | Copy selected text from the focused preview or terminal; with no selection, copy the preview contents or visible terminal screen. |
| Clipboard | Paste (Ctrl+Shift+V) | Paste clipboard text into the terminal. |
| App | About | Show application information. |
| App | Quit | Quit my-tui, asking first if any session is running. |
| App | Theme | Choose and save the UI theme. |

Drag in a preview to select text; releasing copies it, as in the terminal. Double-click selects a word and triple-click a line in plain-text previews. In plain-text previews, Ctrl+A selects all and Shift+Left/Right extends the selection. Previews are read-only: Ctrl+Shift+V pastes into the session's terminal, not the file.

Drag the vertical border between Sessions and the main panel to resize the session list. The width is kept while you hide or maximize panels, until you quit my-tui. Both panels keep a minimum width, and the terminal resizes to fit.

Alt+Up and Alt+Down reorder sessions in the list. Shift+PageUp and Shift+PageDown scroll terminal history; Shift+Home goes to the top and Shift+End returns to the prompt. The mouse wheel scrolls shell history or is passed to a full-screen program.

A session you are not viewing shows ● after new output and ! after a bell. Sessions are restored from `~/.config/my-tui/state.json` but are not started automatically; select one and press Enter. Templates are stored in `~/.config/my-tui/templates.json`.

Open the command palette with F9 and choose **Duplicate session** to immediately start a fresh terminal using the selected session’s folder, command, arguments, and environment. The copy is named `<original>-2`, then `-3`, and so on, skipping names already in use. The original session keeps running unchanged.

While Files is active, open the command palette with F9 and choose **Open selected file** to launch the selected file in its system-default application (`open` on macOS, `xdg-open` on Linux). Select a file rather than a folder.

## Configuration

Settings live in `~/.config/my-tui/config.json`. Every key is optional.

```json
{
  "theme": "Terminal",
  "terminalPalette": "vscode"
}
```

- `theme` sets the chrome colors. Pick it from the command palette, which saves it here.
- `terminalPalette` sets the 16 ANSI colors inside sessions. Presets are `vscode` (VS Code Dark+, the default), `xterm`, and `tomorrow-night`. An array of up to 16 `#RRGGBB` strings sets colors 0–15 in order (black, red, green, yellow, blue, magenta, cyan, white, then the bright versions); entries you leave out keep VS Code Dark+. An unknown name or a bad color falls back to VS Code Dark+. Colors 16–255 and 24-bit colors are not affected. Restart my-tui after changing it.

```json
{
  "terminalPalette": ["#000000", "#FF5555", "#50FA7B", "#F1FA8C", "#6272FF"]
}
```

## Publish

A push to `main` publishes the next patch release. The assets are `my-tui-<version>-osx-arm64.tar.gz` and `my-tui-<version>-linux-x64.tar.gz`, plus `SHA256SUMS`. The Mac archive includes `libmytui_pty.dylib`, which has to stay next to `my-tui`.

A local folder build is the same pair of runtime identifiers:

```
dotnet publish src/MyTui -c Release -r osx-arm64 --self-contained
dotnet publish src/MyTui -c Release -r linux-x64 --self-contained
```

On macOS the publish step ad-hoc signs `my-tui` and the dylib. If Gatekeeper blocks a copied binary, `xattr -d com.apple.quarantine my-tui`. The Linux build is `linux-x64`, which is the RID the VT library ships. It does not need libicu.
