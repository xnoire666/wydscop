# Contributing to Wydscop

Thanks for helping! Bug reports and small, focused pull requests are welcome. There's no roadmap, so for
anything bigger than a fix, open an issue first and ask whether it fits before you spend time on it.

## Reporting a bug

Use **Issues → New issue → Bug report** and fill in the form. A security problem is different: please
don't open a public issue for it, see [SECURITY.md](SECURITY.md).

## Building and running

You need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build Wydscop.slnx
```

```bash
dotnet run --project src/Wydscop
```

- Only one copy runs at a time, and the running app locks its `.exe`. Exit it from the tray menu before
  rebuilding.
- The log is at `%LocalAppData%\Wydscop\wydscop.log` and starts fresh on every launch.

[AGENTS.md](AGENTS.md) explains how the code is laid out, where settings and data live, and the gotchas
(acrylic window, DPI, background threads). Read it before changing anything non-trivial.

## Pull requests

- **Commit with your own GitHub identity**, ideally your GitHub no-reply email (GitHub → Settings → Emails).
  Never use the maintainer's.
- **No personal data** in commits: no real names, paths from your own user folder or screenshots of your
  screen. Use generic examples like `C:\xampp`.
- **Colors only through theme keys.** Every color in `SearchWindow.xaml` comes from `Themes.cs`; add a new key
  to **both** the Light and Dark palettes. Never hard-code a color in XAML.
- **Check both themes** for any visual change.
- **There are no automated tests.** Build, run the app, try your change, and say in the pull request what you
  tried.
- Some behavior is a deliberate choice (Alt+Space, no sections or "top hit" in results, the ✕ on the left of
  recent rows and the pin on the right, one-click "Clear all", and others listed in AGENTS.md). Please don't
  change those without asking first.
- Keep comments explaining *why*, in plain language, like the existing code.

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
