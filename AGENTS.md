# AGENTS.md

Guidance for AI coding agents (and humans) working on **Wydscop**, a Spotlight-style search bar for
Windows: Alt+Space opens a frosted bar that finds apps, files and folders. C# / WPF on .NET 10,
Windows only. See `README.md` for what the app does from a user's point of view.

## Build and run

```bash
dotnet build Wydscop.slnx
```

```bash
dotnet run --project src/Wydscop
```

- **Stop the running app before building.** It runs from `src/Wydscop/bin/Debug/net10.0-windows/Wydscop.exe`
  and locks it, so the build fails with "file is being used". Use `Stop-Process -Name Wydscop`, then start
  it again after building so the user keeps a running copy.
- Only one instance runs at a time (mutex `Local\Wydscop.SingleInstance`); a second launch exits immediately.
- Release / CI build: `dotnet build Wydscop.slnx -c Release`. CI (`.github/workflows/build.yml`) also
  publishes a self-contained, compressed single-file `Wydscop.exe` as an artifact.
- There are **no automated tests**. Verify changes by building, running the app, and reading the log at
  `%LocalAppData%\Wydscop\wydscop.log` (reset on every launch). For UI changes, check both themes.

## Architecture

| Area | Files |
|---|---|
| Startup: single instance, hotkey, tray, settings carry-over | `src/Wydscop/App.xaml.cs` |
| The bar and results list (layout, row template, red pin drawing `RedPinImage`) | `src/Wydscop/SearchWindow.xaml` |
| Search, ordering, folders, recent/pinned, keyboard, drag/resize, acrylic, themes | `src/Wydscop/SearchWindow.xaml.cs` |
| Light and Dark palettes | `src/Wydscop/Themes.cs` |
| Start menu apps (`shell:AppsFolder`), icons, dates | `src/Wydscop/Search/AppIndex.cs` |
| Windows Search index queries (OLE DB, `Search.CollatorDSO`) | `src/Wydscop/Search/FileSearch.cs` |
| Own in-memory name index for folders Windows doesn't index | `src/Wydscop/Search/LocalIndex.cs` |
| Match scoring and typo tolerance | `src/Wydscop/Search/Matcher.cs` |
| What file search skips | `src/Wydscop/Search/SearchFilters.cs` |
| Saved recent items, pins, open counts (JSON) | `src/Wydscop/Search/RecentStore.cs`, `PinnedStore.cs`, `UsageStore.cs` |
| Shell icons with alpha, background loading | `src/Wydscop/Search/ShellIcons.cs`, `IconLoader.cs` |
| Hotkey, tray, startup, settings, taglines, migration | `src/Wydscop/Services/*.cs` |
| Win32 / DWM imports | `src/Wydscop/Interop/NativeMethods.cs` |

**Result ordering** (`SortByScore` in `SearchWindow.xaml.cs`): by `Matcher` score (exact 1000, starts with 900,
word start 800, initials 700, contains 500, letters in order ≤300, typos ≤150; name length does **not** count),
then newest date (modified, else created), then shorter name. Apps and files are one list; there are no
sections or "top hit" in search results (the user explicitly rejected those).

**Empty bar** shows "Recent" (10, then "Show more") and "Pinned" as heading rows (`ResultKind.Header`) inside the
same list; keyboard and mouse skip headings.

## Where state lives

- Settings: registry `HKCU\Software\Wydscop` (theme, bar position/width, pinned bar, first-run flag).
- Start with Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → value `Wydscop`.
- Data: `%LocalAppData%\Wydscop\` → `recent.json`, `pinned.json`, `usage.json`, `wydscop.log`.
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
- **Name clashes in .NET 10 WPF:** don't name types `ThemeMode` (WPF has one; ours is `BarTheme`).
  `System.Windows.Forms` and `System.Drawing` implicit usings are removed in the csproj; qualify them.
  WPF projects don't get `System.IO` implicitly; add `using System.IO;`.
- **Comments explain why**, in plain language, like the existing code: XML doc comments on members,
  short inline comments for non-obvious behavior. Keep the user-facing wording friendly and short.
- **Behavior choices the user made** (don't undo without asking): Alt+Space hotkey; bar top at 14% of the
  screen by default; nothing highlighted on the empty bar until ↓; ✕ on recent rows is on the **left**,
  the row pin on the **right**; placeholder taglines rotate every 30 s; Light theme is the default.
- **Privacy:** never commit personal data (names, paths under a real user profile, screenshots of the user's
  screen). Examples in docs use generic paths like `C:\xampp`.

## Git

- Default branch `main`, remote `https://github.com/xnoire666/wydscop` (private).
- This repo commits as `xnoire666 <7259786+xnoire666@users.noreply.github.com>` (repo-local `git config`).
  Don't change it, and don't use the machine's global git identity here.
- Pushing `.github/workflows/*` needs a GitHub login with the `workflow` scope (`gh auth refresh -s workflow`).
- Commit or push only when asked.
