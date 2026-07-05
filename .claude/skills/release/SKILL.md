---
name: release
description: Cut a release to production — open the develop→master PR, resolve the recurring false conflicts, merge as a merge commit (never squash, never delete develop), tag, and list the prod deploy steps. Use whenever the user says "release", "déploie en prod", "passe en prod", "PR vers master", or asks to ship develop to production.
---

# Release (develop → master)

This is the **only** PR allowed to target master, and the **only** merge where the source branch survives.

## Version

Versions are bare tags like `1.6.3` (no `v` prefix). Pick the next one from the latest tag (`git tag --sort=-v:refname | head -1`) by what shipped since master: any feat → minor bump, fixes/chores only → patch.

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

1. Tag the master merge commit: `git fetch origin master && git tag <version> origin/master && git push origin <version>`. If GitHub releases are in use (`gh release list`), also `gh release create <version> --generate-notes`.
2. If master gained anything develop doesn't have (the merge commit), resync: merge master back into develop.
3. **Deploy checklist** — prod runs an unversioned compose copy on the VPS; merging changes nothing by itself. Call out which services need redeploy (api / web / admin / ingestor — the ingestor only picks up pipeline changes on restart), and any one-off ops (migrations that need watching, collections to drop, config drift from the repo compose files).
