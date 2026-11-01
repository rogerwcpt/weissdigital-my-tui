# Fixes and UI changes, 2026-11-01

This file covers two sets of changes:

1. **Typing lag.** The terminal pane lagged when you typed, most visibly on Backspace and when starting a TUI such as pi. This fix is committed as `620a5e2 Performance Improvements`.

2. **Keys, quitting, and the session UI.** A live terminal now receives almost every key. Quitting asks first when sessions are running. The status line shows key hints, the session list marks activity, the mouse wheel works, and wide characters draw correctly. These changes are **not committed yet**.

---

## 1. Typing lag (committed in `620a5e2`)

### Symptom

Each keystroke showed up a visible moment after it was typed. Backspace felt worst because you watch the character disappear. Starting pi, or any program that redraws the whole screen, felt slow to settle.

### Causes

| Cause | Effect |

|---|---|

| Terminal.Gui runs at most **25 iterations per second** by default, so each iteration is 40 ms. | A key waits for one iteration before it reaches the PTY. The echo from the shell then waits for the next iteration before it is drawn. A single keystroke could take up to about 80 ms to appear. |

| The grid repainted only from the 30 ms `Tick` timer, which called `ConsumeDirty()` there. | New output was painted when the timer next fired, which is not when the main loop was about to draw. This added up to another 30 ms on top of the iteration delay. |

| The PTY reader used a **4 KiB** buffer. | A full-screen redraw from a TUI is often larger than 4 KiB. It arrived as several reads, and each read took the session lock and wrote to the VT separately. |

### Changes

- *`src/MyTui/Program.cs`**: `Application.MaximumIterationsPerSecond = 120`. One iteration is now about 8 ms instead of 40 ms.

- *`src/MyTui/UI/FramePacer.cs`** (new): decides when the grid repaints after its session has changed.

  - The first change after a quiet spell paints immediately, so an echo is never held back.

  - Steady output paints at most once per frame `FrameMilliseconds = 16`), which is about 60 paints per second, even though the loop runs at 120 iterations per second.

  - While the child is inside a synchronized update `CSI ? 2026 h` … `CSI ? 2026 l`), the half-written screen is not shown. If the child never closes the update, the grid still repaints after `SyncHoldMilliseconds = 200`.

- *`src/MyTui/UI/SessionScreen.cs`**:

  - `OnIteration` is attached to `_app.Iteration`. It runs at the top of every main-loop iteration, before Terminal.Gui lays out and draws. Output that arrived since the last frame is therefore painted in this frame.

  - It asks `FramePacer.ShouldPaint(now, synchronized)`. Only when that returns true does it call `ConsumeDirty()` and `SetNeedsDraw()`.

  - `Tick` no longer repaints the grid when output arrives. It still handles cursor blink, labels, the status line and resizing.

  - The handler is detached in `Dispose`.

- *`src/MyTui/Terminal/TerminalSession.cs`**:

  - `IsDirty` reads the dirty flag without clearing it, so `OnIteration` can check it cheaply.

  - `InSynchronizedUpdate` reports `TerminalMode.SynchronizedOutput` (mode 2026).

- *`src/MyTui/Terminal/PtySession.cs`**: the read buffer grew from 4 KiB to 64 KiB, so a typical redraw arrives as one read, one VT write and one lock.

### Tests

`tests/MyTui.Tests/FramePacerTests.cs` covers:

- painting the first change at once

- the 16 ms frame limit

- holding during synchronized output

- painting anyway after the 200 ms limit

---

## 2. Keys, quitting, and the session UI (uncommitted)

### 2.1 The problem with the old key handling

`CellGridView.OnKeyDown` offered every key to `SessionScreen.HandleChord` before forwarding it to the PTY. The app therefore took these keys even while a terminal had focus:

| Key | What the program in the terminal lost |

|---|---|

| Ctrl+R | bash/zsh reverse history search |

| Ctrl+E | end of line |

| Ctrl+P / Ctrl+N | previous / next history entry |

| Ctrl+T | transpose characters |

| Ctrl+O | operate-and-get-next in bash, jump back in vim |

| F1, F2, F4, F11 | function keys in htop, mc, and similar TUIs |

| Ctrl+Tab | tab switching in apps that use it |

| Ctrl+Q | quit **the whole app, with no confirmation**, which killed every running agent |

`KeyInput.IsReserved` also refused to encode Ctrl+Q, Ctrl+N and Ctrl+O. Those keys could never reach the child, even if the app had let them through.

### 2.2 The new model: a leader key

While a **live terminal** has focus, the app keeps only these keys:

| Key | Purpose |

|---|---|

| **Ctrl+B** | Leader. The next key is an app command. |

| **Ctrl+B Ctrl+B** | Sends a real Ctrl+B to the program. Readline uses it for back-one-character, and tmux uses it as its prefix. |

| **Ctrl+Shift+C / Ctrl+Shift+V** | Copy the visible text / paste. Shells never see these as separate keys anyway. |

| **Shift+PgUp / PgDn / Home / End** | Scroll the history, as before. |

Every other key, including Ctrl+Q, goes to the program.

After the leader:

| Key | Command | Direct key outside the terminal |

|---|---|---|

| `m` | menu | F1 |

| `n` | new session | Ctrl+N |

| `o` | go to the list, tab, or preview (the old Ctrl+O) | Ctrl+O |

| `Tab` | next panel (Sessions, Terminal, Files) | Ctrl+Tab |

| `Shift+Tab` | previous panel | Ctrl+Shift+Tab |

| `s` | hide or show Sessions | F4 |

| `z` | maximize or restore | F11 |

| `t` | switch between Terminal and Files | Ctrl+T |

| `p` | open or close the preview | Ctrl+P |

| `e` | edit the session | Ctrl+E |

| `r` | refresh Files | Ctrl+R |

| `T` | templates | F2 |

| `x` | close the session (asks when it is running) | Delete |

| `c` | copy the visible text | Ctrl+Shift+C (works in the terminal too) |

| `v` | paste | Ctrl+Shift+V (works in the terminal too) |

| `?` | help | none |

| `q` | quit (asks when sessions are running) | Ctrl+Q |

Leader rules:

- The leader times out after **3 seconds** `LeaderState.TimeoutMilliseconds`).

- **Esc** or any unbound key cancels it. That key is swallowed, not sent to the program.

- Letters and symbols after the leader are matched by character. Terminals disagree about whether `?` and `T` arrive with Shift set, so this keeps them working everywhere.

- Outside a live terminal (the session list, the file tree, the preview, or an idle or exited session), the direct keys work as before. Leader + key works there too, so the same keys work everywhere.

#### Implementation

- *`src/MyTui/UI/AppKeys.cs`** (new). It is called `AppKeys` because `KeyBindings` clashes with `Terminal.Gui.Input.KeyBindings`.

  - `enum AppCommand` names every app command.

  - `record AppBinding(Command, Label, AfterLeader, AfterLeaderText, Direct, DirectText, Hint, InTerminal)` holds one table row.

  - `AppKeys.All` is the **single table**. Key routing, the help dialog and the status-line hint are all generated from it, so they can't drift apart.

  - `AppKeys.Route(key, inTerminal, leader, now, out command)` returns `KeyRoute.Forward` (not an app key), `KeyRoute.Consumed` (the leader was armed or cancelled), or `KeyRoute.Run` (with the command).

  - `AppKeys.HelpLines()` builds the help text. `AppKeys.LeaderHint` is the status-line text shown while the leader is armed.

  - `LeaderState` holds the armed flag and the time it was armed. It is unit-testable, in the style of `FramePacer`.

- *`src/MyTui/UI/CellGridView.cs`**:

  - The constructor now takes `Func<Key, bool, bool>`. The flag says whether a live terminal has the key.

  - `OnKeyDown` works out whether the session is idle and calls `_chords(key, !idle)` first.

  - The old `Ctrl+Q → return false` passthrough is gone.

- *`src/MyTui/UI/SessionScreen.cs`**:

  - `HandleChord(Key)` now forwards to `HandleChord(Key, bool inTerminal)`, which calls `AppKeys.Route` and then a single `Run(AppCommand)` switch. This replaces the old chain of about 80 lines of `if (key == …)` blocks.

  - The statics `IsPanelCycle`, `IsHideSessions` and `IsMaximizePanel` were removed. The table covers them now, with exact-modifier matching.

  - The `Ctrl+Q → return false` passthroughs in `SessionList.OnKeyDown` and `FileTree.OnKeyDown` were removed. Ctrl+Q now goes through `HandleChord` like every other key.

- *`src/MyTui/Terminal/KeyInput.cs`**: `IsReserved` and its check in `Encode` were removed. Ctrl+Q, Ctrl+N and Ctrl+O now encode to 0x11, 0x0E and 0x0F like any other control key.

- *`src/MyTui/UI/HelpDialog.cs`**: the hard-coded `Lines` array is gone. The dialog shows `AppKeys.HelpLines()` and sizes itself from the line count (width 72).

### 2.3 Quit asks first

Quitting used to call `_app.RequestStop()` straight away, which stopped every session.

- *`SessionScreen.RequestQuit()`** is now the one way to quit. If any session is running, it shows a confirmation first. Otherwise it quits at once.

- It is reached from:

  - Ctrl+Q in the session list, file tree or preview

  - Ctrl+B q anywhere

  - the menu's **Quit** item

  - a `window.KeyDown` fallback added in `AttachTitle`, for any view that does not route keys through `HandleChord`

- *`src/MyTui/UI/CloseDialog.cs`**: `ConfirmQuit(app, running)` shows the dialog.

  - The message is "1 session is still running. Quit and stop it?" or "N sessions are still running. Quit and stop them?".

  - **Cancel has focus**, so a stray Enter keeps the sessions running. Esc also cancels.

- The app-level `Command.Quit` binding (Ctrl+Q) in `Program.cs` is unchanged. Dialogs and popovers rely on it to close themselves, and the Quit dialog is one of them, so Ctrl+Q in that dialog cancels it.

- Inside a live terminal, Ctrl+Q now goes to the program. Use **Ctrl+B q** to quit from there.

### 2.4 Status-line hints

`UpdateStatus` in `SessionScreen.cs` now builds the status line from three cases:

| State | Status line |

|---|---|

| Leader armed | `Ctrl+B: n new  o list  z max  t tab  p preview  x close  ? keys  q quit` |

| Terminal focused | Session details on the left, `Ctrl+B commands` at the right edge |

| Anything else focused | Session details on the left, `F1 menu` at the right edge |

| No sessions | `Ctrl+N creates a session. F1 opens the menu.` (unchanged) |

- `SessionScreen.StatusRow(details, hint, width)` places the hint at the right edge. When there isn't room, it shortens the details with `…`.

- When the row is too narrow (less than the hint plus 12 columns), it drops the hint and shows the details alone.

- `Tick` already refreshes the status line, so the leader hint clears itself after the timeout.

### 2.5 Activity and bell marks in the session list

When you supervise several agents, you need to see which one has done something.

- *`TerminalSession`** gained a bell flag. `TerminalOptions.OnBell` sets it, and `ConsumeBell()` reads and clears it.

- *`SessionScreen.Tick`** goes through every session.

  - It clears the bell flag of the session on screen, so a bell it rang while visible is never marked later.

  - For every other session, new output adds it to `_unseen` and a bell adds it to `_rang`.

- *`RowText`** appends `●` for unseen output or `!` for a bell to rows that aren't focused `SessionScreen.ActivityLabel`). The bell wins when both apply.

- *`ShowSession`** clears both marks when you switch to that session.

### 2.6 Mouse wheel

`CellGridView.OnMouseEvent` handles wheel-up and wheel-down over the grid. What happens depends on the session:

| Session state | Wheel does |

|---|---|

| Idle or exited | Scrolls the history by 3 lines |

| Program tracks the mouse | The program gets wheel events at the pointer position. It gets SGR `CSI < 64;x;y M`) when it enabled mode 1006, or the X10 byte form otherwise. |

| Full-screen program without mouse tracking (alternate screen, e.g. `less`, `man`) | 3 Up/Down arrow keys, as xterm's alternate-scroll does. Application-cursor mode `ESC O A`) is respected. |

| Normal shell | Scrolls the history by 3 lines |

- *`src/MyTui/Terminal/MouseInput.cs`** (new) builds the bytes.

  - `Wheel(up, column, row, sgr)` converts column and row from 0-based to 1-based. The X10 form returns nothing past column or row 223, because each value must fit in one byte.

  - `Arrows(up, count, applicationCursor)` builds the arrow-key sequences.

- *`TerminalSession.Wheel(up, column, row)`** picks the case under the session lock.

- Ghostty.Vt has a `MouseEncoder`, but its `Encode` returns a span into stack memory. That is the same unsafe pattern that `KeyInput.EncodeInto` already works around, so the bytes are built by hand instead.

- Only the wheel is forwarded. Clicks and drags stay with the app.

### 2.7 Wide characters and combined glyphs

Two drawing bugs in `CellGridView.OnDrawingContent`:

1. **Wide characters (CJK, most emoji) were erased.** A wide glyph covers two cells. The VT marks the second cell as a `WideTail`, and the old code drew a space into it. Terminal.Gui's `AddGrapheme` calls `InvalidateOverlappedWideGlyph`, so writing to the column after a wide glyph *replaces* that glyph. Every wide character was therefore overwritten by its own tail.

   - **Fix:** skip a tail cell when the cell to its left really is wide to Terminal.Gui `IsWide`, which uses `string.GetColumns() > 1`). The tail is still drawn as a space when Terminal.Gui thinks its head is narrow, so the column is never left stale.

2. **Combined glyphs were cut to their first character.** `Glyph()` drew only `Rune.GetRuneAt(grapheme, 0)`. That loses combining accents, skin-tone modifiers and ZWJ sequences such as 👨‍👩‍👧.

   - **Fix:** a grapheme with more than one rune is drawn with `AddStr(column, row, grapheme)`. Single runes still use `AddRune`.

These fixes are **not yet verified on screen**. See [Verification](#verification).

---

## 3. Tests

| File | Change |

|---|---|

| `tests/MyTui.Tests/AppKeysTests.cs` (new) | Covers routing: arming the leader, running a command after it, double leader forwards, timeout, Esc and unbound keys cancel, direct keys pass through in the terminal but run elsewhere, exact modifiers (Ctrl+Tab vs Ctrl+Alt+Tab, F4 vs Ctrl+F4), copy and paste in the terminal, `?Tt` matched by character. One test checks that every command has one table row, a leader key and a help line, and that no two commands share a leader key. |

| `tests/MyTui.Tests/AppKeysTests.cs`, `StatusRowTests` (new) | The hint sits at the right edge, long details are cut with `…`, narrow rows drop the hint, and the `●` / `!` marks. |

| `tests/MyTui.Tests/MouseInputTests.cs` (new) | SGR wheel is 1-based, X10 offsets by 32, X10 stops past column 223, and arrows follow cursor-key mode. |

| `tests/MyTui.Tests/KeyInputTests.cs` | `ReservedChords_AreNotEncoded` became `ShellChordsReachTheChild`. It asserts that Ctrl+Q, N, O, R and E encode to 0x11, 0x0E, 0x0F, 0x12 and 0x05. |

| `tests/MyTui.Tests/WorkspaceLayoutTests.cs` | `PanelKeyTests` was removed. It tested the deleted `IsPanelCycle` / `IsHideSessions` / `IsMaximizePanel`, and `AppKeysTests` now covers the same cases. |

---

## 4. UI capture setup

- *`.claude/launch.json`** (new) defines a `my-tui` configuration. It runs `ttyd -W -p 7681 dotnet run --project src/MyTui`, which serves the app as a writable terminal in a web page on port 7681.

- With it, Claude Code's preview tools can start the app, take screenshots, and type into it.

- It needs ttyd, which is **not installed yet**:

```bash

brew install ttyd

```

---

## Verification

**Done:**

- `dotnet build` for the app and the test project: success, 0 warnings. `TreatWarningsAsErrors` is on.

- 55 tests that need no PTY pass:

  - AppKeys, StatusRow, MouseInput, KeyInput

  - WorkspaceLayout, ActiveRow, CloseKey

  - FramePacer, Preview\*, SessionState, ShellLocator, TemplateStore, Workspace

**Not done.** The development sandbox cannot open a PTY, so these still need a local run:

- Run the full test suite, including `PtySessionTests` and `TerminalSessionTests`:

  ```bash

  dotnet test

  ```

- Check by hand, or through ttyd screenshots:

  - In bash: Ctrl+R opens reverse search, Ctrl+E jumps to the end of the line, and Ctrl+P / Ctrl+N walk the history.

  - pi's own shortcuts work inside its session.

  - Ctrl+B o goes to the list, Ctrl+B n creates a session, and Ctrl+B Ctrl+B reaches the program.

  - The status line changes while Ctrl+B is waiting for a key, and changes back after 3 seconds.

  - Ctrl+B q with a running session asks first. Cancel is focused, and Esc cancels.

  - Output in a session you aren't viewing shows `●`, and `printf '\a'` in one shows `!`.

  - The wheel scrolls the history in bash, scrolls `less`, and is forwarded to a program that tracks the mouse.

  - `echo '日本語 👍🏽 👨‍👩‍👧 é'` draws whole characters, with no gaps or missing halves.

## Known limitations

- **Every background session shows `●` at startup.** A new session starts dirty, and its shell prints a prompt. The mark clears when you view the session.

- **Ctrl+B is taken from the program.** Readline's back-one-character and a nested tmux's default prefix need Ctrl+B pressed twice. To change the leader, edit `AppKeys.Leader` and `AppKeys.LeaderText`.

- **X10 wheel reports stop past column or row 223.** Programs that enable SGR (mode 1006) don't have this limit, and nearly all modern ones do.

- **Only the wheel is forwarded to programs.** Clicks and drags stay with the app.

## Not done yet

Everything below is outstanding. Items 1–4 finish this change. Items 5–7 fix the known limitations above. Items 8–11 were deferred on purpose and are separate pieces of work.

### Finishing this change

#### 1. Run the PTY tests and the app locally

- **What:** run `dotnet test`, then work through the manual checks under [Verification](#verification).

- **Why:** the sandbox blocks PTYs, so `PtySessionTests`, `TerminalSessionTests` and the app itself have not run since these changes. The bell flag, `Wheel`, the leader inside a real shell, and the Quit dialog are only covered by unit tests of their parts.

#### 2. Check the rendering on screen

- **What:** run `brew install ttyd`, start the `my-tui` preview configuration, and screenshot `echo '日本語 👍🏽 👨‍👩‍👧 é'` before and after the fix.

- **Why:** the wide-glyph and grapheme fixes (§2.7) follow from reading Terminal.Gui's `AddGrapheme`, but no one has seen the result. Two things to watch:

  - The host terminal and Terminal.Gui may disagree on an emoji's width. That shows as a gap or as a shifted rest of the line.

  - A skin-tone sequence such as 👍🏽 may be measured as two graphemes.

#### 3. Update the docs that still list the old keys

- *`README.md` line 22** says Ctrl+O, Ctrl+T, Ctrl+P, Ctrl+E, Ctrl+R, F1, F2 and Ctrl+Q work everywhere. It should explain:

  - the Ctrl+B leader inside a terminal

  - that the direct keys work only outside a live terminal

  - that quitting asks first

  - the wheel, and the `●` / `!` marks

- *`docs/ROADMAP.md`, Decision log.** "How to use this roadmap", rule 5, asks for binding choices to be recorded there. Add an entry for:

  - Ctrl+B as the leader, with Ctrl+B Ctrl+B to send it

  - the list of keys a live terminal keeps

  - Ctrl+Q going to the program inside a terminal

- *`docs/ROADMAP.md` line 278** ("F1 lists the existing chords") and the **"Host eats Ctrl+Shift+C"** risk row (line 388). Add that Ctrl+B c and Ctrl+B v now copy and paste on a host that keeps Ctrl+Shift+C.

#### 4. Commit

- Everything in §2, §3 and §4 is uncommitted, including `.claude/launch.json`.

- Suggested split:

  1. the key model and quit confirmation

  2. status hints and activity marks

  3. mouse wheel

  4. wide-glyph drawing

  5. the ttyd launch config

### Known limitations to fix

#### 5. No `●` on every session at startup

- **Where:** `SessionScreen.Tick`, `TerminalSession._dirty` (starts at 1).

- **Fix:** mark a session only after it has been shown once, or only after its first frame. For example, call `ConsumeDirty()` when a session starts or is restored, and ignore output during its first second.

#### 6. Clear the marks when a session closes

- **Where:** `_unseen` and `_rang` in `SessionScreen`.

- **Fix:** remove the session from both sets wherever it is removed from the manager. Today a closed session's entry stays until the screen is disposed. That is harmless but untidy.

#### 7. Make the leader configurable

- **Where:** `AppKeys.Leader` / `AppKeys.LeaderText` are constants.

- **Why:** Ctrl+B collides with readline's back-one-character and with tmux's prefix. Ctrl+Space or Ctrl+\ would collide less often for some users.

- **Fix:** read an optional `leader` key from `~/.config/my-tui/` (the only place the app may write) and build `LeaderText` from it. Keep Ctrl+B as the default.

### Deferred features

#### 8. Select text with the mouse, and copy it

- **Today:** copy takes the whole visible screen `CopyVisible`).

- **Plan:**

  - Track a drag over `CellGridView` while the program is not tracking the mouse.

  - Draw the selection in reverse video.

  - Copy the selected cells, using `ScreenFrame.Cells` and skipping `WideTail` cells, on mouse release or on Ctrl+Shift+C / Ctrl+B c.

  - Shift+drag should select even while a program tracks the mouse, as xterm does.

- **Files:** `CellGridView.cs`, `TerminalSession.cs` (text for a cell range, including history while scrolled back), `SessionScreen.CopyVisible`.

#### 9. Cursor shape from DECSCUSR

- **Today:** the cursor is always drawn as a reverse-video block. `PaintedCell` and the frame carry visibility and blinking, but not shape.

- **Plan:**

  - Read the cursor style from Ghostty.Vt's render state into `ScreenFrame`.

  - In `CellGridView.AttributeFor`, draw a bar as an underline or a left-edge glyph, and draw an underline cursor with `TextStyle.Underline`.

  - Vim and pi switch to a bar in insert mode, so this shows up often.

#### 10. Full mouse forwarding

- **Today:** only the wheel is forwarded.

- **Plan:** forward press, release and drag (modes 1000/1002/1003) with modifier bits when the program has enabled tracking. Use the same SGR / X10 encoding as `MouseInput.Wheel`, or wrap Ghostty.Vt's `MouseEncoder` the way `KeyInput.EncodeInto` wraps the key encoder. Clicks on the app's own chrome (title bar, `+ × □ −`) must stay with the app.

- **Depends on:** item 8, so that Shift+drag still selects.

#### 11. Repaint only the rows that changed

- **Today:** `CaptureForDraw` copies the whole grid on every paint, and `CellGridView` compares every cell against its cache before writing.

- **Plan:** use the render state's per-row dirty flags to copy and compare only the rows that changed. This was deferred from the lag fix because the main delay was the 40 ms iteration time, not the copy. It matters most on large windows with programs that stream output.