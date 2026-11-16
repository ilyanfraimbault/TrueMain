---
name: cleanup-worktrees
description: Free disk by removing finished git worktrees under `.claude/worktrees`, keeping their branches and any uncommitted work. Use when the user says "nettoie les worktrees", "le repo prend trop de place", "disque plein", or when the SessionStart hook reports too many worktrees.
---

# Cleanup worktrees

Worktrees are disposable: once its work has landed, a worktree is only 1–8 GB of checkout, dependencies and builds. Removing a landed one needs no confirmation, because the branch stays and git refuses to remove a dirty tree.

1. **Report**: `bash .claude/scripts/worktree-sweep.sh` gives each worktree a state.

   | State | Meaning | Sweep |
   |---|---|---|
   | `landed` | Clean, and its branch has a merged PR, or it is idle for a day with no commit missing from `origin/develop` | removed |
   | `fresh` | Clean, no commit of its own, touched in the last day: a subagent may have just started in it | kept |
   | `in-use` | A process runs in it, or this session does | kept |
   | `dirty` | Uncommitted or untracked files | kept |
   | `unmerged` | Commits that reached neither a merged PR nor `origin/develop` | kept |
   | `locked` | `git worktree lock` | kept |
   | `missing` | Directory already gone | pruned |
   | `app` | Made by the desktop app (`-<6 hex>` suffix) | step 3 |

2. **Sweep**: `bash .claude/scripts/worktree-sweep.sh --apply`, in the background since removal can take minutes.

3. **App-managed** (desktop app only): `mcp__ccd_host__get_storage_usage`, then `mcp__ccd_host__clean_up_worktrees` with `older_than_days: 30`; the user approves its card. The app reuses or reaps its own pool. `mcp__ccd_host__discard_kept_worktree` is for an archived session's kept worktree, once the user has decided about its files (step 4).

4. **Report back**: disk freed, then each `dirty` and `unmerged` worktree with what it holds (`git status --short`, `git log origin/develop..HEAD`). These stay until the user decides: copy the files they want out of the repo, then `git worktree remove --force`.

Done when every worktree in the report is removed or named with its reason. `cleanup-branches` handles the branches the sweep left behind.
