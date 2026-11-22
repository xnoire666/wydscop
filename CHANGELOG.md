# Changelog

What changed in each release of Wydscop. The newest is at the top. Versions follow
[semantic versioning](https://semver.org/): a bigger first number means something works differently.

## 1.0.1 (23 Nov 2026)

- Folders you excluded from Windows Search in Indexing Options (a code folder inside your user folder, for
  example) are now found too: Wydscop's own index covers them.

Around the app:

- Every exe built by CI comes with a `SHA256SUMS.txt` and a GitHub build attestation, so you can check
  it's the genuine file (see the README's "Check your download").
- GitHub releases stay unsigned for now: SignPath Foundation, which gives free code signing to
  open-source projects, only accepts widely known ones and turned Wydscop down for the time being. The
  Microsoft Store version is signed by Microsoft.
- Releases made from a version tag are published automatically and scanned on VirusTotal, with the
  result added to the release notes.
- Automated tests for match scoring, what file search skips, and the saved recent, pinned and usage
  lists.
- Contributing guide, bug report form, security policy and code of conduct.
- The Store description no longer calls Wydscop "Spotlight-style".

## 1.0.0 (21 Nov 2026)

First release, on [GitHub](https://github.com/xnoire666/wydscop/releases/tag/v1.0.0) and the
[Microsoft Store](https://apps.microsoft.com/detail/9P47NSX5N7RJ).

- Alt+Space opens the bar on the monitor your mouse is on.
- Apps, files and folders in one list, from the Start menu, the Windows Search index and Wydscop's own
  index for folders Windows doesn't index.
- Best match first, then newest first; tolerates typos.
- Look inside folders with → and go back with ←.
- Recent and Pinned items on the empty bar; mark several results and open them all.
- Pin, move and resize the bar.
- Frosted acrylic look with Dark, Light and Match Windows themes.
