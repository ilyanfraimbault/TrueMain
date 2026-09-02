---
name: release
description: Cut a release to production — open the develop→master PR, resolve the recurring false conflicts, merge as a merge commit (never squash, never delete develop), tag, then publish the GitHub Release that deploys prod and follow the run. Use whenever the user says "release", "déploie en prod", "passe en prod", "PR vers master", or asks to ship develop to production.
---

# Release (develop → master)

This is the **only** PR allowed to target master, and the **only** merge where the source branch survives.

## Version

Versions are bare tags like `1.6.3` (no `v` prefix). Read the latest with

```
git tag --list --sort=-v:refname | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | head -1
```

**The filter is not optional.** Preprod stamps every deploy with a prerelease tag
(`1.20.0-rc.4`, see `deploy-preprod.yml`), and git's version sort ranks those **above** the
release they precede — `1.20.0-rc.4` sorts higher than `1.20.0`. An unfiltered `head -1`
therefore reads a preprod build as the last release and bumps from it, skipping a version
every time.

**The user's word decides the bump, and their vocabulary is not semver's.** Take it literally —
do not re-derive the bump from what the diff contains when they have named one:

| The user says | From `1.17.0` you cut | Which component moves |
|---|---|---|
| « majeur » / "major" | `2.0.0` | first |
| *nothing* — just "release", "passe en prod" | `1.18.0` | second |
| « mineur » / "minor" | `1.17.1` | third |

So « mineur » means the **smallest** bump — what semver calls a patch — and saying nothing gets
what semver calls a minor. Never answer a « mineur » request with `1.18.0`: to them that is not
a smaller release, it is the default one.

The table is the whole rule. What the release contains does not change the bump — a release
full of features is still `1.17.1` if that is what was asked for.

## PR

```
gh pr create --base master --head develop --title "release: <version>" --body "<grouped changelog since last release>"
```

Build the changelog from `git log origin/master..origin/develop --oneline`, grouped by type (feat/fix/perf/...).

## Conflicts are usually false

Past squash-merges make develop→master PRs report conflicts on files that are actually identical. If GitHub says conflicting:

1. Verify locally whether they're real (`git merge-tree origin/master origin/develop` or a scratch merge).
2. If false: resync history — on develop, `git merge -s ours origin/master`, push — the PR becomes mergeable without changing any content.

## Merge

- **Merge commit**: `gh pr merge <n> --merge`. Never squash a release — squash is precisely what creates the false conflicts next time.
- **Never delete develop.** No `--delete-branch` here.
- The done-criteria from the `ship` skill apply before merging (CI green on HEAD, review verdict clean for HEAD).

## After the merge

1. Tag the master merge commit: `git fetch origin master && git tag <version> origin/master && git push origin <version>`.
2. **Publish the GitHub Release — this, and nothing else, deploys production**: `gh release create <version> --generate-notes`. `deploy-prod.yml` is `on: release: types: [published]`; the pushed tag alone builds and deploys nothing, and the merge to master changes nothing on the VPS either. This step is not optional.
3. **Follow the run** (`gh run list --workflow "Deploy Prod"`, then `gh run watch <id>`). Four jobs, in order (`preflight` first: it fails the run when any secret or variable of the deployment configuration is missing, never a green skip):
   - `publish` builds and pushes the `:<version>` and `:latest` images for api / ingestor / web / admin from the released commit.
   - `rollout / migrate` generates an idempotent EF script and pipes it into the prod Postgres over SSH. A failing script means the deploy never runs.
   - `rollout / deploy` redeploys the `truemain` Docker Manager project against `compose.prod.yaml` at the released commit, with `IMAGE_TAG`/`APP_VERSION=<version>`. All four services roll together — there is no service-by-service redeploy to do by hand. Check it actually redeployed instead of trusting a green run.
4. If master gained anything develop doesn't have (the merge commit), resync: merge master back into develop.
5. Report what the automation does **not** cover: one-off ops (collections to drop, config drift), and a new compose variable needing `PROD_ENV_FILE` updated first — the action overwrites the VPS `.env` on every run. Confirm the version shipped by checking the prod footer, which is stamped from `APP_VERSION`.
