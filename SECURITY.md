# Security policy

## Reporting a problem

Please **don't** report security problems in a public issue. Report them privately instead:

1. Open the repository's **Security** tab.
2. Click **Report a vulnerability** and describe the problem, how to reproduce it, and which version you
   used (Microsoft Store or `Wydscop.exe` from Releases).

You'll get a reply in the report. Wydscop is maintained by one person in their spare time, so please allow
a few days.

## Supported versions

Only the latest release gets fixes. The Microsoft Store version updates itself; for `Wydscop.exe`, download
the newest one from [Releases](https://github.com/xnoire666/wydscop/releases).

## Checking that a download is genuine

See "Check your download" in the [README](README.md): every release from CI has a GitHub build attestation
and a `SHA256SUMS.txt`, and the Microsoft Store version is signed by Microsoft.

## What Wydscop does with your data

Wydscop has no network access. It reads the names, paths and dates of apps and files on your PC, never their
contents, and keeps its data only in your user profile. See [PRIVACY.md](PRIVACY.md).
