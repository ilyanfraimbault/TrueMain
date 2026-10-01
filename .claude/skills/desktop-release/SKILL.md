---
name: desktop-release
description: Ship a new version of the TrueMain desktop app (Tauri, `desktop/`) — bump its version in a PR so develop builds a beta pre-release that preprod serves, then promote that build to production by hand with the `Desktop promote` workflow, or roll the download page back. Use whenever the user says "nouvelle version de l'app desktop", "bump l'app", "build l'exe / le .app", "sors une beta desktop", "passe l'app desktop en prod", "promeus la 0.x.y", "rollback de l'app", or asks where the desktop installers are.
---

# Desktop release

The desktop app has its **own version and its own release cycle** — a site release is never an app release
(#1772, `docs/ci.md` "Desktop releases", `decisions/desktop.md`). Two steps, both deliberate:

```
PR bump tauri.conf.json → squash-merge to develop → desktop-release.yml builds .dmg + .exe, twice
  (production flavour → truemain.lol, preprod flavour → preprod)
  → pre-release desktop-vX.Y.Z → preprod (channel beta) serves its flavour
                    … later, by hand …
Actions → Desktop promote (X.Y.Z) → full release → truemain.lol (channel stable) serves the production flavour + auto-update
```

There is no `.exe` / `.app` anywhere else: no build on ordinary merges, no `desktop-v*` tag trigger. The
installers are assets of the `desktop-vX.Y.Z` GitHub release, in two flavours (#1779) — each build reads the site
it was built for, so the app downloaded from preprod reads preprod and the one from truemain.lol reads production:

| flavour | installers | updater archives | manifest |
| --- | --- | --- | --- |
| production (*TrueMain*) | `truemain.dmg`, `truemain.exe` | `truemain.app.tar.gz`, `truemain.exe` + `.sig` | `latest.json` |
| preprod (*TrueMain Beta*) | `truemain-X.Y.Z.dmg`, `truemain-X.Y.Z.exe` | `truemain-X.Y.Z.app.tar.gz`, `.exe` + `.sig` | `latest-beta.json` |

The preprod origin comes from the `DESKTOP_BETA_SITE_URL` repository secret (a bare origin, `http(s)://host[:port]`);
the build job fails without it. Never write that origin in the repo, an issue or a PR body.

## 1. Bump — a new beta build

1. The version lives in **one place**: `version` in `desktop/src-tauri/tauri.conf.json` (the Cargo crate takes the
   workspace version, the sidebar reads `getVersion()` from the Tauri config). It must be semver `X.Y.Z`
   (optionally `-suffix`): the updater compares versions, so `0.2`, `v1` or a bare `1` fail the build job.
2. Pick the bump the user names, with the same vocabulary as the site's `release` skill (« majeur » → first
   component, nothing said → second, « mineur » → third). Read the current one with
   `gh release list --limit 100 --json tagName -q '.[].tagName' | grep '^desktop-v' | sort -V | tail -1` — the
   config can be ahead of the last published build if a bump's build failed.
3. Branch `chore/<issue>-desktop-<version>` (or bump inside the feature PR that needs it), commit
   `chore(desktop): bump to <version>`, land it with the `ship` skill. develop is protected: never push the bump
   directly.
4. After the merge, follow the build: `gh run list --workflow "Desktop release" --limit 1`, then
   `gh run watch <id>`. Three jobs: `New app version` (refuses non-`develop` refs and non-semver versions, and
   **skips the build** when the tag already exists — a notice says so), `Build (macOS)` / `Build (Windows)`
   (~15–30 min), `Publish the pre-release`. A failed build is retried with
   `gh workflow run "Desktop release" --ref develop`; it rebuilds the version currently in the config.
5. Check preprod serves it: `GET /api/desktop/release` on the preprod site (host in `CLAUDE.local.md`) answers
   the new `version` within five minutes (server cache), and `/api/desktop/download/mac` redirects to
   `truemain-X.Y.Z.dmg`. Production keeps serving the promoted one.

A version that already has a tag is never rebuilt: to publish a fix, bump again.

## 2. Promote — production serves it

**Ask the user before promoting**: it changes what every visitor downloads and what every installed app
auto-updates to. Promotion is never part of a site release or of `ship`.

Before promoting, the endpoints the build reads must be live in production: the production flavour calls the
production API, and the pages it shares with the site (`web/layers/common`) call whatever `develop` had when it was
built. If the build
contains shared-page or API changes newer than the last site release, cut the site release first (`release`
skill) — otherwise those pages fail in the installed app.

```bash
gh workflow run "Desktop promote" -f version=<X.Y.Z>
gh run watch "$(gh run list --workflow 'Desktop promote' --limit 1 --json databaseId -q '.[0].databaseId')"
```

The workflow refuses a missing, draft or incomplete release (it needs `truemain.dmg`, `truemain.exe` and
`latest.json` — the production flavour), flips it to a full release **without** GitHub's "Latest" badge (that badge is the site's), and
moves any previously promoted desktop release back to pre-release — exactly one release is stable at a time.
It never deploys anything: `deploy-prod.yml` listens to `published` only.

Verify: `curl -s https://truemain.lol/api/desktop/release | jq .version` answers `<X.Y.Z>` within five minutes,
and `curl -sI https://truemain.lol/api/desktop/download/windows` redirects to that version's `truemain.exe`.

## Rollback

Promote the previous version: the download page and the update feed go back to it. The updater **never
downgrades** an installed app, so players already on the bad version stay there until a newer one is promoted —
the real fix is bump → build → promote.

## Traps

- **Prod runs the channel code only from the site release that shipped #1772.** A prod site older than that serves
  the newest `desktop-v*`, pre-releases included — a bump merged then would go straight to players.
- Until the first promotion, production's download page shows "Coming soon" and the update feed answers 204.
- `NUXT_DESKTOP_CHANNEL` is `beta` in `compose.preprod.yaml`, `stable` in `compose.prod.yaml`; any other value
  reads as `stable`.
- Each flavour's updater polls its own site: the preprod app (*TrueMain Beta*) updates from preprod to every new
  build, the production app only to promoted ones. The two install side by side (different bundle identifiers).
- The builds are unsigned (no Apple notarisation, no Windows certificate): the download page explains the
  first-launch steps. The updater key is the `TAURI_SIGNING_PRIVATE_KEY` secret — losing it strands every
  installed app.
