# Publishing Wydscop to the Microsoft Store (MSIX)

Everything needed to build the Store package lives in this folder:

| File | What it is |
|---|---|
| `Package.appxmanifest` | The package manifest: identity, tiles, the "Start with Windows" startup task, `runFullTrust` |
| `Images/` | Tiles and icons at every scale, drawn by `tools/make-icon.py` (don't edit by hand) |
| `build-msix.ps1` | Publishes the app self-contained and packs `artifacts/msix/Wydscop_<version>_x64.msix` |

You need the .NET 10 SDK and the Windows SDK (`makeappx.exe`, `makepri.exe`; it comes with Visual Studio
or from [the Windows SDK page](https://developer.microsoft.com/windows/downloads/windows-sdk/)).

## 1. Reserve the name (once)

1. Sign up for a developer account at [Partner Center](https://partner.microsoft.com/dashboard) (free for
   individuals as of 2025; check the current terms when you sign up).
2. **Apps and games → New product → MSIX or PWA app**, reserve the name **Wydscop**.
3. Open **Product management → Product identity** and copy three values:
   - `Package/Identity/Name` (looks like `12345YourName.Wydscop`)
   - `Package/Identity/Publisher` (looks like `CN=1A2B3C4D-...`)
   - `Package/Properties/PublisherDisplayName` (your publisher name)

## 2. Build the package

Bump `<Version>` in `src/Wydscop/Wydscop.csproj` for every new upload (the Store needs a higher version each
time; the package version is that plus `.0`). Then, from the repo root:

```powershell
.\packaging\build-msix.ps1 -IdentityName "12345YourName.Wydscop" -Publisher "CN=1A2B3C4D-..." -PublisherDisplayName "Your Name"
```

The package is `artifacts/msix/Wydscop_<version>_x64.msix`. Upload it **unsigned**; the Store signs it.

## 3. Try it before uploading

With **Developer Mode** on (Settings → System → For developers), install the unpacked package for your
user, no certificate needed:

```powershell
.\packaging\build-msix.ps1 -Register
```

It stops a running plain copy of Wydscop first (only one can run). Start **Wydscop** from the Start menu.
Check: Alt+Space opens the bar, the tray icon and menu work, **Start with Windows** in the tray menu shows
up in Task Manager → Startup apps. Remove it afterwards:

```powershell
Get-AppxPackage Wydscop.Dev | Remove-AppxPackage
```

To try the real `.msix` file on another PC instead, it must be signed with a certificate that PC trusts and
whose subject equals `-Publisher`; pass its thumbprint with `-CertificateThumbprint`. The Store doesn't need this.

You can also run the **Windows App Certification Kit** (installed with the Windows SDK, "Windows App Cert Kit"
in Start) on the `.msix`; the Store runs the same checks after upload.

## 4. Submission in Partner Center

- **Packages:** upload the `.msix`. Device family: Desktop.
- **Restricted capability `runFullTrust`:** Partner Center asks why. Suggested answer:
  > Wydscop is a desktop search bar (WPF). It needs full trust to register its global Alt+Space hotkey,
  > show a notification-area icon, query the Windows Search index, and open the apps, files and folders
  > the user picks.
- **Privacy policy URL:** the app reads file and app names on the PC, so the Store may ask for one. Use
  `https://github.com/xnoire666/wydscop/blob/main/PRIVACY.md` (the repository is public).
- **Store listing:** the description, features, keywords, screenshot captions and other fields are ready to
  paste in [`store-listing.md`](store-listing.md). Screenshots (1920×1080, generic paths on a drawn
  background) and the box and poster art are in `assets/store/`.
- **Pricing and availability, Age ratings:** fill in as you like; the questionnaire is short for a utility.

## What changes inside the package

- **Start with Windows** uses the package's startup task (`WydscopStartup`) instead of the Run key, because
  Windows gives packaged apps a private copy of the registry. `StartupManager` picks the right one by itself.
  The first launch turns it on; after that, the user's choice in the tray menu or in Settings → Apps →
  Startup wins (Windows won't let the app turn it back on once the user switched it off there).
- **Settings and data:** the registry and `%LocalAppData%\Wydscop\` work as before, but files a packaged app
  creates may be kept in `%LocalAppData%\Packages\<package family name>\LocalCache\Local\Wydscop\`.
  Uninstalling the package removes those.
- **Size:** the package carries its own .NET runtime (self-contained), so it's about 80 MB.
