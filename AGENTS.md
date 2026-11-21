# AGENTS.md

Guidance for AI coding agents (and humans) working on **Wydscop**, a Spotlight-style search bar for
Windows: Alt+Space opens a frosted bar that finds apps, files and folders. C# / WPF on .NET 10,
Windows only. See `README.md` for what the app does from a user's point of view, and
`HANDOVER.md` for setting up a new machine and where the work stands (keep its "Where the work stands"
section current when you finish a feature).

## Build and run

```bash
dotnet build Wydscop.slnx
```

```bash
dotnet run --project src/Wydscop
```

- **Stop the running app before building.** It runs from `src/Wydscop/bin/Debug/net10.0-windows10.0.19041.0/Wydscop.exe`
  and locks it, so the build fails with "file is being used". Use `Stop-Process -Name Wydscop`, then start
  it again after building so the user keeps a running copy.
- Only one instance runs at a time (mutex `Local\Wydscop.SingleInstance`); a second launch exits immediately.
- **Microsoft Store package (MSIX):** `.\packaging\build-msix.ps1` (see `packaging/README.md`); add
  `-Register` to install it for your user and try it (needs Developer Mode). Bump `<Version>` in the csproj
  for each Store upload.
- Release / CI build: `dotnet build Wydscop.slnx -c Release`. CI (`.github/workflows/build.yml`) also
  publishes a self-contained, compressed single-file `Wydscop.exe` as an artifact. A `v*` tag also has
  SignPath sign it and creates a GitHub Release (`CODE_SIGNING.md`); only CI builds are ever signed.
- There are **no automated tests**. Verify changes by building, running the app, and reading the log at
  `%LocalAppData%\Wydscop\wydscop.log` (reset on every launch). For UI changes, check both themes.

## Architecture

| Area | Files |
|---|---|
| Startup: single instance, hotkey, tray, settings carry-over | `src/Wydscop/App.xaml.cs` |
| The bar and results list (layout, row template, red pin drawing `RedPinImage`) | `src/Wydscop/SearchWindow.xaml` |
| The bar window (`partial class SearchWindow`), one file per area: | |
| &nbsp;&nbsp;shared stores, constructor, show/hide/toggle, pin the bar, deactivate/close | `src/Wydscop/SearchWindow.xaml.cs` |
| &nbsp;&nbsp;typing, app scoring, ordering (`SortByScore`), Windows Search + local index queries | `src/Wydscop/SearchWindow.Search.cs` |
| &nbsp;&nbsp;the list: paging/lazy loading, highlight, opening, mouse clicks on rows | `src/Wydscop/SearchWindow.Results.cs` |
| &nbsp;&nbsp;empty bar: Recent and Pinned sections, pinning results, "Show more", removing | `src/Wydscop/SearchWindow.Recent.cs` |
| &nbsp;&nbsp;looking inside folders (→ / ←), folder chip, scroll restore | `src/Wydscop/SearchWindow.Folders.cs` |
| &nbsp;&nbsp;keyboard shortcuts (`OnPreviewKeyDown`) | `src/Wydscop/SearchWindow.Keyboard.cs` |
| &nbsp;&nbsp;position on the monitor, drag to move, resize the width, save position | `src/Wydscop/SearchWindow.Placement.cs` |
| &nbsp;&nbsp;acrylic and Light/Dark/Match Windows theme | `src/Wydscop/SearchWindow.Theme.cs` |
| &nbsp;&nbsp;rotating placeholder taglines | `src/Wydscop/SearchWindow.Tagline.cs` |
| Light and Dark palettes | `src/Wydscop/Themes.cs` |
| Start menu apps (`shell:AppsFolder`), icons, dates | `src/Wydscop/Search/AppIndex.cs` |
| Windows Search index queries (OLE DB, `Search.CollatorDSO`) | `src/Wydscop/Search/FileSearch.cs` |
| Own in-memory name index for folders Windows doesn't index | `src/Wydscop/Search/LocalIndex.cs` |
| Match scoring and typo tolerance | `src/Wydscop/Search/Matcher.cs` |
| What file search skips | `src/Wydscop/Search/SearchFilters.cs` |
| Saved recent items, pins, open counts (JSON) | `src/Wydscop/Search/RecentStore.cs`, `PinnedStore.cs`, `UsageStore.cs` |
| Shell icons with alpha, background loading | `src/Wydscop/Search/ShellIcons.cs`, `IconLoader.cs` |
| Hotkey, tray, startup, settings, taglines, migration | `src/Wydscop/Services/*.cs` |
| Logo and app/tray icon (generated; edit the script, not the images) | `tools/make-icon.py` → `src/Wydscop/Assets/Wydscop.ico`, `assets/logo.*` |
| MSIX: manifest, Store images, build script, submission guide | `packaging/` (`Package.appxmanifest`, `build-msix.ps1`, `README.md`) |
| Win32 / DWM imports | `src/Wydscop/Interop/NativeMethods.cs` |

**Result ordering** (`SortByScore` in `SearchWindow.Search.cs`): by `Matcher` score (exact 1000, starts with 900,
word start 800, initials 700, contains 500, letters in order ≤300, typos ≤150; name length does **not** count),
then newest date (modified, else created), then shorter name. Apps and files are one list; there are no
sections or "top hit" in search results (the user explicitly rejected those).

**Empty bar** shows "Recent" (10, then "Show more") and "Pinned" as heading rows (`ResultKind.Header`) inside the
same list; keyboard and mouse skip headings.

## Where state lives

- Settings: registry `HKCU\Software\Wydscop` (theme, bar position/width, pinned bar, first-run flag).
- Start with Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → value `Wydscop`; when installed as
  MSIX, the package's startup task `WydscopStartup` instead (`StartupManager.IsPackaged` decides).
- Data: `%LocalAppData%\Wydscop\` → `recent.json`, `pinned.json`, `usage.json`, `wydscop.log`. Windows may redirect
  new files of a packaged (MSIX) process, including one started from a packaged terminal such as the Claude
  desktop app's, to `%LocalAppData%\Packages\<family name>\LocalCache\Local\Wydscop\`. If the log looks stale,
  look there.
- The app used to be called **EasyFind**. `Services/LegacyMigration.cs` copies old settings once; keep it, but
  don't reintroduce the old name anywhere else.

## Conventions and gotchas

- **Colors only through theme resources.** Every color in `SearchWindow.xaml` is `{DynamicResource Key}` set in
  `Themes.cs` for both Light and Dark. Never hard-code a hex color in XAML; add a key to **both** palettes.
  The red push pin (`RedPinImage`) is intentionally the same in both themes.
- **The window is a non-layered acrylic window** (`AllowsTransparency="False"`, `WindowChrome`, DWM
  `DWMWA_SYSTEMBACKDROP_TYPE`). Window opacity animations don't work on it; animate `BarGrid` instead. Don't
  switch back to `AllowsTransparency="True"` or acrylic breaks. Windows draws the corners, border and shadow.
- **Positions are in physical pixels** (`SetWindowPos`, `GetDpiForMonitor`); sizes in XAML are DIPs. Convert
  with the monitor's scale. The window sizes itself to content (`SizeToContent="Height"`).
- **Shell COM needs STA threads** (app enumeration, icons). Background work must never touch UI elements
  directly; use `Dispatcher.InvokeAsync`. `SearchResult` raises `PropertyChanged` for late-arriving icons/dates.
- **Windows Search dates are UTC.** Read them with `DateTime.SpecifyKind(..., Utc)`.
- **Target framework is `net10.0-windows10.0.19041.0`** (for WinRT's `StartupTask` in the MSIX build), still
  supporting Windows 10 1809 (`SupportedOSPlatformVersion`). Keep the startup task id in `StartupManager` and
  `packaging/Package.appxmanifest` the same.
- **Name clashes in .NET 10 WPF:** don't name types `ThemeMode` (WPF has one; ours is `BarTheme`).
  `System.Windows.Forms` and `System.Drawing` implicit usings are removed in the csproj; qualify them.
  WPF projects don't get `System.IO` implicitly; add `using System.IO;`.
- **Comments explain why**, in plain language, like the existing code: XML doc comments on members,
  short inline comments for non-obvious behavior. Keep the user-facing wording friendly and short.
- **Behavior choices the user made** (don't undo without asking): Alt+Space hotkey; bar top at 14% of the
  screen by default; nothing highlighted on the empty bar until ↓; ✕ on recent rows is on the **left**,
  the row pin on the **right**; placeholder taglines rotate every 30 s; Dark theme is the default (it matches the dark logo); the bar's search icon uses the logo's aurora gradient (`SearchIcon` in `Themes.cs`);
  "Clear all" on Recent is red and clears in **one** click (no confirmation step);
  Ctrl+click / Ctrl+Space mark several results and Enter opens all marked.
- **Privacy:** never commit personal data (names, paths under a real user profile, screenshots of the user's
  screen). Examples in docs use generic paths like `C:\xampp`.

## Git

- Default branch `main`, remote `https://github.com/xnoire666/wydscop` (public; anything committed is visible to everyone, so the privacy rule above matters).
- This repo commits as `xnoire666 <7259786+xnoire666@users.noreply.github.com>` (repo-local `git config`).
  Don't change it, and don't use the machine's global git identity here.
- Pushing `.github/workflows/*` needs a GitHub login with the `workflow` scope (`gh auth refresh -s workflow`).
- Commit or push only when asked.
