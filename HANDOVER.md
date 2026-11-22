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
| GitHub CLI | Pushing to the repo, pushing workflow files, making releases | `winget install GitHub.cli` |
| An editor | Visual Studio 2026, VS Code with C# Dev Kit, or Rider | |

Check the SDK:

```bash
dotnet --list-sdks
```

## 2. Sign in and clone

The repo is **public**: `https://github.com/xnoire666/wydscop`. Anyone can clone it; pushing needs the GitHub login.

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

> **Forked the repo or contributing?** Skip the two commands below and commit with **your own** GitHub
> identity, ideally your own no-reply email (GitHub → Settings → Emails). Never commit as the maintainer.

The maintainer (xnoire666) commits as their GitHub no-reply identity, set in the repo's **local** git
config. Local config is not part of a clone, so the maintainer sets it again on every new machine, inside
the cloned folder:

```bash
git config user.name "xnoire666"
```

```bash
git config user.email "7259786+xnoire666@users.noreply.github.com"
```

On the maintainer's machines, don't use the machine's global git identity for this repo.

## 4. Build and run

```bash
dotnet build Wydscop.slnx
```

```bash
dotnet run --project src/Wydscop
```

- The first launch turns on **Start with Windows** and puts an icon in the tray. Press **Alt+Space**.
- **Stop the app before rebuilding.** It locks `src/Wydscop/bin/Debug/net10.0-windows10.0.19041.0/Wydscop.exe`
  (exit from the tray, or `Stop-Process -Name Wydscop` in PowerShell), then start it again.
- Only one copy runs at a time; a second launch quietly exits.
- If Alt+Space does nothing, another app may own the shortcut (for example PowerToys Run). The log says
  `Hotkey Alt+Space registered: False` in that case.
- `dotnet test Wydscop.slnx` runs the unit tests (match scoring, file-search filters, recent/pinned/usage stores). For everything
  else, check changes in the running app, in both Light and Dark themes, and read
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

Last updated: 22 Nov 2026. Branch `main`, everything pushed. The repo is **public**.

### Done

- The search bar: apps, files and folders in one list, smart ordering, typo tolerance, dates on each row,
  looking inside folders, Recent and Pinned on the empty bar, pin the bar, move and resize, acrylic with
  Light / Dark / Match Windows themes, tray menu, start with Windows.
- `SearchWindow.xaml.cs` split into one partial-class file per area (see the table in AGENTS.md).
- **Clear all** on the Recent heading: a red trash button that empties Recent in one click; pins stay.
- **Open several at once:** Ctrl+click or Ctrl+Space marks results (blue tint and a bar on the left), Enter
  opens all marked, Ctrl+Enter shows them in File Explorer, Esc clears the marks. Marks are kept while you
  type, so you can mark results from several searches.
- **Automated tests:** xUnit project `tests/Wydscop.Tests` (61 tests) for `Matcher`, `SearchFilters` and the
  recent/pinned/usage stores, run in CI after the build. The stores' `Load` takes an optional file path so tests
  use a temporary folder.
- **Logo and tray icon:** a "WS" monogram in glowing aurora colors on a dark tile (`assets/logo.svg`,
  `assets/logo.png`). `src/Wydscop/Assets/Wydscop.ico` is the exe and tray icon; all of them are drawn by
  `tools/make-icon.py` (needs Pillow), so change the design there and rerun it.
- The bar's search icon uses the logo's aurora gradient; **Dark** is now the default theme.
- **Microsoft Store package:** `packaging/` has the MSIX manifest, Store tiles (from `tools/make-icon.py`), a
  build script and a step-by-step submission guide (`packaging/README.md`); `PRIVACY.md` is the privacy
  policy. Start with Windows uses a package startup task when installed as MSIX. Tested by installing the
  unpacked package with `-Register`: the bar, hotkey and startup task worked.
- **Live in the Microsoft Store** as "wydscop", product ID `9P47NSX5N7RJ`
  (https://apps.microsoft.com/detail/9P47NSX5N7RJ; MSIX, submission 1, last modified 18 Nov 2026). The README
  has the "Get it from Microsoft" badge and a Store link in its download line, and the repo's About box links
  to the Store page. Further changes go through **Start update** on the product page in Partner Center.
- **No "Spotlight" in Store-facing text:** Store policies discourage other companies' trademarks, so the MSIX
  manifest's two descriptions and `packaging/store-listing.md` don't use the word. The README and the GitHub
  description still say "Spotlight-style" (the user's choice; fine on GitHub).
- **Code signing status is stated plainly:** README and `CODE_SIGNING.md` say SignPath signing is being set
  up, GitHub releases are unsigned until then, and the Store version is signed by Microsoft. Switch both back
  to the plain SignPath credit once the first signed release is out.
- **No roadmap:** the user has none planned, so the README and this file don't list one.
- **Build attestations and checksums** (stand-in until SignPath signing): `build.yml` makes a
  `SHA256SUMS.txt` and a GitHub build attestation (`actions/attest-build-provenance`) for every exe built on a
  push (not on pull requests); the signing job does the same for the signed exe and attaches both files to
  the release. The README's "Check your download" shows `gh attestation verify` and `Get-FileHash`. v1.0.0
  was built before this, so it has no attestation; its `SHA256SUMS.txt` was added to the release by hand
  (22 Nov 2026), matching the SHA-256 in its notes.
- **VirusTotal result in release notes:** v1.0.0's notes link its scan (0 of 69 flagged, added by hand). For
  later releases, the `virustotal` job in `build.yml` runs after `sign-and-release`, uploads the released exe
  through the VirusTotal API, waits for the scan and appends the same line to the notes (a flagged result is
  written as it is and raises a warning on the run). It skips itself without the `VIRUSTOTAL_API_KEY` secret.
- **Contributor docs:** `CONTRIBUTING.md` (build, PR rules, the user's fixed behavior choices; points to
  AGENTS.md), a **Bug report** issue form (`.github/ISSUE_TEMPLATE/bug_report.yml`; asks for install source,
  version, Windows version, theme, optional log with personal paths removed), `config.yml` (no blank issues;
  links to usage and the security policy), and `SECURITY.md` (report privately via the Security tab). No
  feature request form, since there's no roadmap. Linked from the README's "Contributing and bugs".
  Private vulnerability reporting is turned on in the repo, so the Security tab has "Report a vulnerability".
- **Open-source trust signals:** `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1, short form; reports go
  through GitHub's Report content or a message to xnoire666, no email), a pull request template
  (`.github/pull_request_template.md`), `CHANGELOG.md` (1.0.0 plus Unreleased; add to it with each
  release), and README badges (build, latest release, OpenSSF Scorecard, license). Every action in the
  workflows is pinned to a commit SHA with the version in a comment. `.github/dependabot.yml` sends weekly
  grouped updates for NuGet and Actions; Dependabot alerts and security fixes are on in the repo settings.
  CodeQL code scanning uses GitHub's default setup (turned on in repo settings, not a workflow file).
  `.github/workflows/scorecard.yml` runs the OpenSSF Scorecard on each push to main and weekly, publishes the
  score (the badge reads it) and puts the details in Security → Code scanning.
- **Commit identity is the maintainer's only** (commit `482b9a5`): step 3 above, `AGENTS.md` and the README
  tell forks and contributors to commit with their own GitHub identity, and AI tools never to set the
  `xnoire666` identity themselves. Before this, a fork following these docs would have committed as
  xnoire666. The maintainer's own clone is unchanged.
- **Ready for SignPath (free code signing):** MIT `LICENSE`, `CODE_SIGNING.md` (roles, privacy, credit
  line), and a `sign-and-release` job in `build.yml` that runs only for `v*` tags once the repo has the
  `SIGNPATH_ORGANIZATION_ID` variable and `SIGNPATH_API_TOKEN` secret. Git history was checked for personal
  data before going public: clean.
- **First release, v1.0.0 (22 Nov 2026), unsigned:** on GitHub Releases with the CI-built `Wydscop.exe` from
  commit `d9e6475` (Actions run 44941565599). The notes explain the SmartScreen "Run anyway" step and give the
  exe's SHA-256. Made by hand with `gh release create`, since the signing job isn't set up yet.

### Worth checking

- **SignPath not set up yet:** apply at signpath.org (give the Releases page as the download page), then in
  SignPath create project `wydscop`, an artifact configuration (zip containing the PE file `Wydscop.exe`) and
  the signing policy `release-signing`; install the SignPath GitHub App; add the variable and secret above.
  The first signed release needs a **new** tag (e.g. `v1.0.1`, with `<Version>` in the csproj bumped to
  match), because `v1.0.0` already has a release and `gh release create` in the job would fail on it.
  Approve the request in SignPath. The job has not run yet.

- **VirusTotal job not run yet:** the user is to create a free virustotal.com account and add its API key as
  the repo secret `VIRUSTOTAL_API_KEY`. The job only runs after a signed release, so the first real run comes
  with the first SignPath release; check its log and the release notes then.

- **Store listing refresh not done yet:** new listing text in `packaging/store-listing.md` and 1920×1080
  screenshots in `assets/store/` (bar captures with generic `C:\xampp` paths on a drawn background) are ready
  to apply with **Start update** in Partner Center. A listing-only update needs no new package; any new
  package needs a higher `<Version>` than the live one. The live package (built 17–18 Nov) still has the old
  manifest descriptions with "Spotlight-style"; the reworded ones (commit `99d36cb`) go out with the next
  package.

- **Scorecard and CodeQL results:** after the first Scorecard run, look at its findings in Security → Code
  scanning; some checks (code review, maintained, fuzzing) are hard for a one-person project to pass and
  can be left. Merge Dependabot's pull requests when CI passes; it will offer newer major versions of the
  pinned actions (e.g. checkout v4 → v7), so check each one's release notes first.

- The last two features built cleanly and the app ran, but they were only lightly tried. Check "Clear all"
  and marked rows in **both** themes, and try Ctrl+click on recent and pinned rows.

### Decisions the user made (don't undo without asking)

Alt+Space hotkey; bar top at 14% of the screen; nothing highlighted on the empty bar until ↓; ✕ on recent
rows on the left and the row pin on the right; taglines rotate every 30 s; Dark theme by default; no
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
- **Dark theme as the default** (it matches the dark "WS" logo); it used to be Light. The Light theme is
  a gray that is neither too dark nor too light, a bit darker than the first try.
- The bar's **search icon uses the logo's aurora gradient** (sky blue → indigo → fuchsia → coral),
  slightly deeper in the Light theme so it keeps its contrast.
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
