# Handover: working on Wydscop from another machine

Everything you need to pick this project up on a different Windows PC: what to install, how to clone and
build, what does **not** travel with the repo, and where the work stands.

For what the app does, see [README.md](README.md). For code conventions and gotchas (read these before
changing anything), see [AGENTS.md](AGENTS.md).

---

## 1. Install

| Tool | Why | Get it |
|---|---|---|
| Windows 10 or 11 (x64) | The app is Windows-only (WPF, Win32, DWM). Acrylic needs Windows 11 22H2+. | |
| .NET 10 SDK | Build and run | `winget install Microsoft.DotNet.SDK.10` |
| Git | Source control | `winget install Git.Git` |
| GitHub CLI | Signing in to the private repo, pushing workflow files | `winget install GitHub.cli` |
| An editor | Visual Studio 2026, VS Code with C# Dev Kit, or Rider | |

Check the SDK:

```bash
dotnet --list-sdks
```

## 2. Sign in and clone

The repo is **private**: `https://github.com/xnoire666/wydscop`.

```bash
gh auth login
```

```bash
gh auth setup-git
```

```bash
gh repo clone xnoire666/wydscop
```

If you will ever change `.github/workflows/*`, the login needs the `workflow` scope:

```bash
gh auth refresh -s workflow
```

## 3. Set the commit identity (does not come with the clone)

This repo commits as the GitHub no-reply identity, set in the repo's **local** git config. Local config is
not part of a clone, so set it again on every new machine, inside the cloned folder:

```bash
git config user.name "xnoire666"
```

```bash
git config user.email "7259786+xnoire666@users.noreply.github.com"
```

Don't use the machine's global git identity for this repo.

## 4. Build and run

```bash
dotnet build Wydscop.slnx
```

```bash
dotnet run --project src/Wydscop
```

- The first launch turns on **Start with Windows** and puts an icon in the tray. Press **Alt+Space**.
- **Stop the app before rebuilding.** It locks `src/Wydscop/bin/Debug/net10.0-windows/Wydscop.exe`
  (exit from the tray, or `Stop-Process -Name Wydscop` in PowerShell), then start it again.
- Only one copy runs at a time; a second launch quietly exits.
- If Alt+Space does nothing, another app may own the shortcut (for example PowerToys Run). The log says
  `Hotkey Alt+Space registered: False` in that case.
- There are no automated tests. Check changes in the running app, in both Light and Dark themes, and read
  the log at `%LocalAppData%\Wydscop\wydscop.log` (it starts fresh on every launch).

## 5. Bring your data over (optional)

None of this is in the repo; it lives under your Windows user on each PC. A new machine starts with an
empty Recent list, no pins and default settings. To carry them over, copy them from the old PC:

| What | Where | How to move it |
|---|---|---|
| Recent items, pinned results, open counts | `%LocalAppData%\Wydscop\recent.json`, `pinned.json`, `usage.json` | Copy the three files into the same folder on the new PC, with Wydscop not running |
| Theme, bar position and width, pinned bar | `HKCU\Software\Wydscop` | `reg export HKCU\Software\Wydscop wydscop.reg` on the old PC, double-click the file on the new one |

Recent and pinned entries are full paths. Ones that don't exist on the new PC (different user name,
missing drive) are simply hidden, not errors. Don't commit these files: they hold personal paths.

## 6. Working with an AI coding agent

- `CLAUDE.md` just points to [AGENTS.md](AGENTS.md); both are in the repo, so agents get the same project
  instructions on any machine.
- An agent's own memory (for example Claude Code's `~/.claude/projects/...` folder) is **per machine** and
  does not move with the repo. Anything worth keeping long term belongs in `AGENTS.md` or this file.
- Agents should commit or push only when asked.
- Keep sessions short: one task per session. `AGENTS.md` gives each new session the project map, so
  nothing needs re-explaining.

---

## Where the work stands

Last updated: 17 Nov 2026. Branch `main`, everything pushed.

### Done

- The search bar: apps, files and folders in one list, smart ordering, typo tolerance, dates on each row,
  looking inside folders, Recent and Pinned on the empty bar, pin the bar, move and resize, acrylic with
  Light / Dark / Match Windows themes, tray menu, start with Windows.
- `SearchWindow.xaml.cs` split into one partial-class file per area (see the table in AGENTS.md).
- **Clear all** on the Recent heading: a red trash button that empties Recent in one click; pins stay.
- **Open several at once:** Ctrl+click or Ctrl+Space marks results (blue tint and a bar on the left), Enter
  opens all marked, Ctrl+Enter shows them in File Explorer, Esc clears the marks. Marks are kept while you
  type, so you can mark results from several searches.

### Worth checking

- The last two features built cleanly and the app ran, but they were only lightly tried. Check "Clear all"
  and marked rows in **both** themes, and try Ctrl+click on recent and pinned rows.

### Ideas not started (from the README roadmap)

- Custom app and tray icon (both use the generic Windows app icon for now).
- Configurable hotkey.
- Calculator and web search results.

### Decisions the user made (don't undo without asking)

Alt+Space hotkey; bar top at 14% of the screen; nothing highlighted on the empty bar until ↓; ✕ on recent
rows on the left and the row pin on the right; taglines rotate every 30 s; Light theme by default; no
sections or "top hit" in search results; "Clear all" is red and clears in one click (no confirmation).

---

## History and decisions from earlier sessions

The project was built over three Claude Code sessions. Their conversations stay on the original PC, so the
parts that matter later are summarized here.

### Session 1: "Spotlight search alternative for Windows" (building the app)

**How it started**
- Goal: a macOS Spotlight-style search for Windows. C# / WPF / .NET was picked as a good fit.
- The first idea was **Ctrl+F on the desktop**. It was dropped because Ctrl+F is "Find" in almost every
  app. **Alt+Space** works from anywhere (`RegisterHotKey`, the app waits hidden in the tray).
- The bar's size and position were first copied from the Claude desktop app's quick-entry box. It was
  later moved up a little (top at 14% of the screen) and made draggable.

**Search**
- **No result limit:** 10 rows are visible, and more load as you scroll.
- **"Option B" file search:** the Windows Search index is fast but misses folders it doesn't index (the
  test case was `C:\xampp\htdocs\erp`). So Wydscop also keeps its own in-memory name index for the rest
  (`LocalIndex.cs`).
- **Typo tolerance** ("fuzzy search") is used only when nothing matches normally.
- **Ordering** was set by the user:
  1. how close the name is to what you typed;
  2. if that ties, newest first by date (modified, or created if never modified);
  3. if dates tie too, by time, latest on top.
- **The date** is shown on every row, under the path.

**Features added along the way**
- Pin the bar so it stays open in place, with a pin icon that is highlighted only when pinned.
- Adjustable bar width.
- **→ / ←** to look inside a folder and come back. Coming back restores the exact scroll position,
  because the jump back to the top confused the user.
- Recent items on the empty bar: nothing highlighted until ↓, ✕ to remove one, 10 shown then
  "Show more (N)".
- Pinned results shown below Recent.
- Rotating placeholder lines: at least 20 Gen Z-style lines about searching. They changed every 15 s at
  first, and the user settled on 30 s.

**Look**
- Acrylic blur background.
- **Light theme as the default.** The user wanted a gray that is neither too dark nor too light for most
  people, and asked for it a bit darker than the first try.
- The pin is a **realistic red push pin** (the pin itself is red, not its background), the same in both
  themes, and a bit bigger.

**Redesign rejected:** a bolder mockup was turned down: vivid blue selection, a "Top hit", Apps / Folders /
Files sections, filter chips, two-line rows. The user wants a **clean** list, ordered only by how close
the match is, with no grouping.

**Name**
- The app was first called **EasyFind**.
- Rejected names: Seek Peek (uncommon words), Quick Click (hard to say), First Look and Look Back.
- The user picked **Wydscop**. The local folder may still be called `easyfind`, but a fresh clone is
  `wydscop`.
- After the rename, the old EasyFind settings and data were deleted from the original PC. On a new PC
  there is nothing old to carry over; `LegacyMigration.cs` just does nothing there.

**GitHub**
- The repo was created at `xnoire666/wydscop`.
- The first commit had the wrong author name. It was rewritten as `xnoire666` and pushed again; that's
  why step 3 above matters.
- `AGENTS.md` was added for all AI coding tools, plus a one-line `CLAUDE.md` (`@AGENTS.md`) so Claude Code
  reads the same file.

**Parked:** a custom icon for the tray and the `.exe`, which still use the generic Windows app icon. It
is on the README roadmap.

**Graphify** (a code-graph tool that claims to save tokens) was looked at and **not added**:
- The project is small, about 5,000 lines.
- `AGENTS.md` already tells agents which file does what.
- Most tokens go into long conversations and screenshots, not into finding files.

The advice that came out of it:
- start a **new session for each task** (or use `/clear`, or `/compact` mid-task);
- split the big window file;
- keep `AGENTS.md` up to date.

### Session 2: "SearchWindow.xaml.cs refactoring"

- `SearchWindow.xaml.cs` (about 1,300 lines) was split into partial-class files by area: `Search`,
  `Results`, `Recent`, `Folders`, `Keyboard`, `Placement`, `Theme`, `Tagline`. An agent can now read only
  the part it needs.
- Committed and pushed.

### Session 3: Clear all, multi-open, this handover

- "Clear all" on the Recent heading:
  - first a plain gray link, which wasn't obviously clickable;
  - then a button with a trash icon;
  - then made **red**, because deleting is red;
  - its "click again to confirm" step was removed at the user's request.
- Ctrl+click / Ctrl+Space to mark several results, and Enter to open them all.
- This `HANDOVER.md`.

---

## New machine checklist

- [ ] .NET 10 SDK, Git and GitHub CLI installed
- [ ] `gh auth login` done, repo cloned
- [ ] Repo-local `user.name` / `user.email` set (step 3)
- [ ] `dotnet build Wydscop.slnx` succeeds
- [ ] App runs and Alt+Space opens the bar
- [ ] (Optional) Recent, pinned and settings copied from the old PC
