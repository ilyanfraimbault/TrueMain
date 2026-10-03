---
name: desktop-release
description: Ship a new version of the TrueMain desktop app (Tauri, `desktop/`) — bump its version in a PR so develop builds its production flavour, which truemain.lol serves by itself once production runs what it reads; follow the per-merge preprod betas; serve a version by hand or roll the download page back with the `Desktop promote` workflow. Use whenever the user says "nouvelle version de l'app desktop", "bump l'app", "build l'exe / le .app", "sors une beta desktop", "passe l'app desktop en prod", "promeus la 0.x.y", "rollback de l'app", "pourquoi l'app n'est pas en prod", or asks where the desktop installers are.
---

# Desktop release

The desktop app has its **own version and its own release cycle** — a site release is never an app release
(#1772) — but since #1799 only the version is by hand (`docs/ci.md` "Desktop releases", `decisions/desktop.md`):

```
any app change merged to develop (desktop/, web/layers/, web/shared/)
  → desktop-release.yml builds the preprod flavour → pre-release desktop-vX.Y.Z-beta.N → preprod serves it

a version bump merged to develop
  → the same run also builds the production flavour → draft desktop-vX.Y.Z
  → served on truemain.lol (full release) as soon as the site release in production is aligned with it:
      right after the build, or after the site release (deploy-prod.yml) that brings the endpoints it reads
```

There is no `.exe` / `.app` anywhere else. The installers are assets of the `desktop-v*` GitHub releases, one
flavour per release (#1779) — each build reads the site it was built for:

| release | flavour | installers | updater archives | manifest |
| --- | --- | --- | --- | --- |
| `desktop-vX.Y.Z` | production (*TrueMain*) | `truemain.dmg`, `truemain.exe` | `truemain.app.tar.gz`, `truemain.exe` + `.sig` | `latest.json` |
| `desktop-vX.Y.Z-beta.N` | preprod (*TrueMain Beta*) | `truemain-X.Y.Z-beta.N.dmg`, `.exe` | `truemain-X.Y.Z-beta.N.app.tar.gz`, `.exe` + `.sig` | `latest-beta.json` |

`N` is the workflow's run number (the updater only installs a strictly greater semver, so no commit SHA); `X.Y.Z`
is the configured version while its production build does not exist, its next patch once it does. Releases from
before #1799 (`desktop-v0.1.0` … `desktop-v0.3.1`) carry both flavours. The preprod origin comes from the
`DESKTOP_BETA_SITE_URL` repository secret (a bare origin); the build fails without it. Never write that origin in
the repo, an issue or a PR body.

## Betas — nothing to do

Every merge touching the app builds one (~15–30 min): `gh run list --workflow "Desktop release" --limit 3`. Preprod's
`GET /api/desktop/release` (host in `CLAUDE.local.md`) answers the new `version` within five minutes, and installed
*TrueMain Beta* apps pick it up within fifteen. Only the last ten betas are kept. A run re-run with "Re-run jobs"
keeps its run number and skips the beta (notice); start a new run instead: `gh workflow run "Desktop release" --ref develop`.

## Bump — a new production version

1. The version lives in **one place**: `version` in `desktop/src-tauri/tauri.conf.json`. It must be `X.Y.Z` — no
   suffix, the betas add their own.
2. Pick the bump the user names, with the same vocabulary as the site's `release` skill (« majeur » → first
   component, nothing said → second, « mineur » → third). Read the last production one with
   `gh release list --limit 100 --json tagName -q '.[].tagName' | grep -E '^desktop-v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -1`
   — drafts included, so the config can be ahead of it only when a bump's build failed.
3. Branch `chore/<issue>-desktop-<version>` (or bump inside the feature PR that needs it), commit
   `chore(desktop): bump to <version>`, land it with the `ship` skill. develop is protected: never push the bump
   directly.
4. **Merging the bump is the decision to ship it to players**: tell the user so before merging, since it now reaches
   truemain.lol without any further step.
5. Follow the build: `gh run watch <id>`. Jobs: `Versions to build`, `Build (macOS)` / `Build (Windows)`,
   `Publish the preprod build`, `Publish the production build`. The last one creates the draft, then reads the tag
   of the last successful **Deploy Prod** run and runs `.github/scripts/desktop-held.sh` against it:
   - "promoting it" → served now: `curl -s https://truemain.lol/api/desktop/release | jq .version` answers it within
     five minutes;
   - "waits for a site release" → the build reads site or API changes production does not run yet. It goes out by
     itself with the next site release (`release` skill): the `Serve the desktop app this release reads` job at the
     end of `Deploy Prod`. Tell the user which it is.
6. A failed build is rebuilt by the next run, or by `gh workflow run "Desktop release" --ref develop` — the
   production build is made whenever no `desktop-v<version>` release exists. A version that has one is never
   rebuilt: to publish a fix, bump again.

## By hand — serve early, or roll back

**Ask the user first**: it changes what every visitor downloads and what every installed app updates to.

```bash
gh workflow run "Desktop promote" -f version=<X.Y.Z>
gh run watch "$(gh run list --workflow 'Desktop promote' --limit 1 --json databaseId -q '.[0].databaseId')"
```

It serves any release carrying the production flavour — a held draft (skipping the alignment check: only when the
user accepts that pages may fail until the site release) or an older version (a rollback). The previous stable one
goes back to pre-release; held drafts older than the promoted version are deleted. A rolled-back version is a
pre-release, not a draft, so no site release re-promotes it. The updater **never downgrades** an installed app: the
real fix is bump → build → served.

## Traps

- Until the first production build is served, production's download page shows "Coming soon" and the update feed
  answers 204.
- `NUXT_DESKTOP_CHANNEL` is `beta` in `compose.preprod.yaml`, `stable` in `compose.prod.yaml`; any other value
  reads as `stable`.
- Each flavour's updater polls its own site: the preprod app (*TrueMain Beta*) updates to every beta, the
  production app to every served version. The two install side by side (different bundle identifiers).
- The builds are unsigned (no Apple notarisation, no Windows certificate): the download page explains the
  first-launch steps. The updater key is the `TAURI_SIGNING_PRIVATE_KEY` secret — losing it strands every
  installed app.
