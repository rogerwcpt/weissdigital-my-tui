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

F9 or Cmd+K opens the Command Palette, where you will find all the commands in one list, grouped by area. Type to filter, then press Enter to run the selected command. It works even while a terminal session is running. Cmd+K reaches my-tui only when the host terminal forwards it. Ghostty binds Cmd+K to clear the screen by default; `keybind = cmd+k=unbind` in the Ghostty config lets my-tui see that key. F10 quits my-tui, Norton Commander-style, asking first if any terminal session or script is running. A session that is executing a command shows a spinner, `| / - \`, after its name. The quit question names that session. F1 hides or shows the Sessions panel. F12 hides or shows the center panel.

### Commands

Host terminals and agent CLIs swallow most key chords, so most commands are in the palette only. my-tui keeps these Norton Commander-style keys, plus Ctrl+Shift+C and Ctrl+Shift+V for copy and paste when your host terminal passes them through:

| Key | Sessions panel focused | Files panel focused | Terminal or Preview focused |
| --- | --- | --- | --- |
| F1 | Toggle Sessions Panel | Toggle Sessions Panel | Toggle Sessions Panel |
| F3 | Toggle Preview | Toggle Preview | Toggle Preview |
| F6 | Rename the selected session | Rename the selected file or folder | Leave the key to the focused view/program |
| F7 | Leave the key to the session list | Create a folder | Leave the key to the focused view/program |
| F8 | Remove the selected session | Delete the selected file or folder, after confirmation | Leave the key to the focused view/program |
| F9 | Command Palette | Command Palette | Command Palette |
| Cmd+K | Command Palette | Command Palette | Command Palette |
| F10 | Quit my-tui | Quit my-tui | Quit my-tui |
| F12 | Toggle Center Panel | Toggle Center Panel | Toggle Center Panel |
| Ctrl+Option+1 … 9 | Focus sessions 1–9 | Focus sessions 1–9 | Focus sessions 1–9 |
| Ctrl+Option+0 | Focus session 10 | Focus session 10 | Focus session 10 |

F6, F7, and F8 follow keyboard focus, not just the selected tab. Removing a session does not delete its files; running sessions or scripts prompt before being stopped. File deletion is permanent (not Trash), and deleting a folder includes its contents. Its confirmation defaults to **Cancel**; Escape also cancels. Renaming does not overwrite existing files or folders. F7 asks for a folder name. The new folder is created inside the selected folder, or in the same folder as the selected file. With nothing selected, it is created in the session directory. An existing file or folder is left in place. Escape cancels.

The session list shows a number in front of each name. Ctrl+Option and that number focus the session from any panel, including a running terminal. Ctrl+Option+1 focuses session 1, through Ctrl+Option+9 for session 9. Ctrl+Option+0 focuses session 10. Sessions after 10 keep their numbers. Alt+Up and Alt+Down reorder the list, which changes which session a number focuses. Command+number stays with the host terminal's own tabs. Ghostty and iTerm deliver Ctrl+Option+number through the keyboard protocol Terminal.Gui turns on. Option is the Alt key.

F9 lists every session under **Go to**, with the same number and, for the first ten, the Ctrl+Option chord. Press that number while the filter is empty, or highlight the row and press Enter. The palette closes and that session is focused. 0 focuses session 10. Later sessions stay in the list and open from their row.

| Group | Command | What it does |
| --- | --- | --- |
| Go to | Session name (Ctrl+Option+number) | Focus that session and close the palette. 1–9 and 0 do this while the filter is empty. |
| Session | Close the session (F8 in Sessions) | Close the selected session, asking first if it is running. |
| Session | Duplicate session | Start a fresh copy of the selected session. |
| Session | Edit the session | Edit the selected session's settings. |
| Session | Force stop terminal | Forcefully terminate the selected terminal after confirmation, keeping the session so it can be restarted. |
| Session | New session | Create a session. The command defaults to `$SHELL` and can be edited before starting. |
| Session | Open folder | Choose a folder and start a new shell session there. |
| Session | Rename the session (F6 in Sessions) | Change the selected session's name. |
| Session | Sort Sessions by Name | Sort sessions alphabetically, keeping the selected session and saving the order. |
| Session | Start the session | Start a stopped session. Enter in the session list does the same. |
| Session | Templates | Apply a saved template in the new-session dialog. |
| Layout | Maximize/Restore | Maximize the focused panel or undo its maximization. |
| Layout | Restore layout | Return to the default layout: Sessions and the center panel visible, Preview closed, and Sessions at its default width. |
| Layout | Show Files | Show and focus the Files tab; restore the default layout first if Sessions or Preview is maximized. |
| Layout | Show Terminal | Show and focus the Terminal tab; restore the default layout first if Sessions or Preview is maximized. |
| Layout | Toggle Center Panel (F12) | Hide or show the session's center area containing Terminal and Files. |
| Layout | Toggle Preview Panel (F3) | Toggle the preview panel on the right; it stays open while you use the terminal. |
| Layout | Toggle Sessions Panel (F1) | Hide or show the session list. |
| Files | Create folder (F7 in Files) | Create a folder inside the selected folder, or beside the selected file. |
| Files | Delete selected file or folder (F8 in Files) | Permanently delete the selected item after confirmation. Folders include all contents. |
| Files | Rename selected file or folder (F6 in Files) | Rename the selected item in place without overwriting an existing item. |
| Files | Open selected file | Open the selected file in its system-default application while Files is active. |
| Files | Refresh Files | Refresh the file tree. |
| Files | Reveal in Finder / Reveal in File Manager | Reveal the selected file or folder in Files, or open the session folder when Terminal is active. |
| Files | Run selected script | Run the highlighted `.sh` file while Files is active; stream its output to Preview. |
| Files | Show script output | Reopen the current session's latest script output, even after browsing other files. |
| Files | Stop script | Stop the current session's running script and its child processes, keeping the output. |
| Clipboard | Copy selection or screen (Ctrl+Shift+C) | Copy selected text from the focused preview or terminal; with no selection, copy the preview contents or visible terminal screen. |
| Clipboard | Paste (Ctrl+Shift+V) | Paste clipboard text into the terminal. |
| App | About | Show application information. |
| App | Quit (F10) | Quit my-tui, asking first if any terminal session or script is running. Sessions executing a command are named in the question. |
| App | Theme | Choose and save the UI theme. |

Drag in a preview to select text; releasing copies it, as in the terminal. Double-click selects a word and triple-click a line in plain-text previews. In plain-text previews, Ctrl+A selects all and Shift+Left/Right extends the selection. Previews are read-only: Ctrl+Shift+V pastes into the session's terminal, not the file.

Drag the vertical border between Sessions and the main panel to resize the session list. The width is kept while you hide or maximize panels, and it is saved for the next launch. Restore layout returns Sessions to its default width. Both panels keep a minimum width, and the terminal resizes to fit.

Alt+Up and Alt+Down reorder sessions in the list. **Sort Sessions by Name** sorts the list case-insensitively on demand; manual reordering still works afterward. Either change renumbers the Ctrl+Option session chords. Shift+PageUp and Shift+PageDown scroll terminal history; Shift+Home goes to the top and Shift+End returns to the prompt. The mouse wheel scrolls shell history or is passed to a full-screen program.

Ctrl+C is passed to the running program to interrupt it. Child terminals start with default, unblocked signals rather than inheriting the host's ignored interrupts. If a program still will not exit, choose **Force stop terminal** from F9; this kills the shell and foreground job, discards their terminal output, and keeps the saved session. Unsaved work may be lost. Press Enter or choose **Start the session** to restart it.

Escape cancels the **Open Folder** dialog without creating a session.

A session you are not viewing shows ● after new output and ! after a bell. Sessions are restored from `~/.config/my-tui/state.json` but are not started automatically; select one and press Enter. Templates are stored in `~/.config/my-tui/templates.json`.

Open the command palette with F9 and choose **Duplicate session** to immediately start a fresh terminal using the selected session’s folder, command, arguments, and environment. The copy is named `<original>-2`, then `-3`, and so on, skipping names already in use. The original session keeps running unchanged.

While Files is active, open the command palette with F9 and choose **Open selected file** to launch the selected file in its system-default application (`open` on macOS, `xdg-open` on Linux). Select a file rather than a folder.

To run a shell script, highlight a `.sh` file in **Files**, then choose **Run selected script** from F9. Highlighting alone never executes it. Preview opens with live stdout/stderr and the final exit code; the session's terminal is left alone. The script runs from the session's working directory with its environment overrides, using its shebang interpreter (or `/bin/sh` without a shebang). Executable permission is not required. Only run scripts you trust: they have your normal user permissions.

One script can run per session. **Stop script** stops it and its child processes; you can then run it again. Selecting another file returns Preview to file contents; **Show script output** returns to the latest run. Output follows the bottom unless you scroll up or select text, and is capped at 256K characters while the script continues running. Output is plain text, and stdin is closed: use Terminal for interactive scripts. Closing the session or quitting also stops its script. Script output is not saved across app launches.

**Reveal in Finder** selects the Files tab's selected item in macOS Finder. On Linux, **Reveal in File Manager** uses `xdg-open` to open a selected directory or a file's containing directory; selecting the file itself is not portable. With Terminal active, Reveal opens the session's folder instead.

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
