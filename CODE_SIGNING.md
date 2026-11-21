# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

## What gets signed

Only `Wydscop.exe` built from this repository's source by the GitHub Actions workflow
(`.github/workflows/build.yml`), for tagged releases (`v*`). Nothing built on a personal PC is ever
signed. Signed releases are published on this repository's
[Releases](https://github.com/xnoire666/wydscop/releases) page.

The Microsoft Store version is signed by Microsoft, not through SignPath.

## Team and roles

| Role | Who |
|---|---|
| Committers and reviewers | [xnoire666](https://github.com/xnoire666) |
| Approvers (approve each signing request) | [xnoire666](https://github.com/xnoire666) |

Everyone with one of these roles uses two-factor authentication on GitHub and SignPath. Changes from
outside contributors are reviewed by a committer before they are merged.

## Privacy

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it. Wydscop has no network access at all;
see [PRIVACY.md](PRIVACY.md).
