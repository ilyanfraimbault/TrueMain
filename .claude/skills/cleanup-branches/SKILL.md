---
name: cleanup-branches
description: Sweep stale git branches locally and on origin — delete branches whose PR merged, prune gone upstreams, never touch develop/master or branches checked out in worktrees. Use when the user says "nettoie les branches", "delete merged branches", "trop de branches", or after a batch of merges leaves clutter.
---

# Cleanup branches

Protected — never deleted: `develop`, `master`, and any branch checked out in a worktree (`git worktree list` first).

1. `git fetch --all --prune`
2. **Local branches merged into develop** (`git branch --merged origin/develop`): delete with `git branch -D`. The safety comes from the `--merged origin/develop` filter, not from the flag — `-d` re-validates against the current `HEAD` (or the branch's own upstream), not against the ref you filtered on, so from a worktree sitting on a feature branch it refuses branches that really are merged into develop. Running the step from a develop checkout is the other way to make `-d` agree with the filter.
3. **Local branches with a `[gone]` upstream** (`git branch -vv | grep '\[gone\]'`): these were usually squash-merged, so git doesn't see them as merged. For each, confirm with `gh pr list --head <branch> --state merged --json number`; if a merged PR exists → `git branch -D`. No merged PR → keep and report.
4. **Remote branches** (`git branch -r`): for each non-protected one, check its PR state. MERGED → `git push origin --delete <branch>`. OPEN PR or no PR → keep and report (it may be someone's WIP or an unmerged experiment).
5. Report two lists: deleted, and kept-with-reason. If anything is ambiguous (local commits not on any remote, no PR trace), keep it and flag it rather than guessing.
