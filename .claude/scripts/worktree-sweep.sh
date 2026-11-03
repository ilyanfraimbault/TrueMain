#!/usr/bin/env bash
# Classifies the worktrees under .claude/worktrees (states: cleanup-worktrees skill).
#   worktree-sweep.sh           report
#   worktree-sweep.sh --apply   report and remove the landed ones; their branches stay
#   worktree-sweep.sh --hint    one line for the SessionStart hook, past the threshold
set -u

HINT_THRESHOLD=5
IDLE_MINUTES=1440
APP_MANAGED='-[0-9a-f]{6}$'

mode=${1:-report}
main=$(git worktree list --porcelain | sed -n '1s/^worktree //p')
worktrees=$(git worktree list --porcelain | sed -n 's/^worktree //p' | grep -F "$main/.claude/worktrees/")

if [ "$mode" = "--hint" ]; then
  count=$(printf '%s\n' "$worktrees" | grep -cvE -e "$APP_MANAGED" -e '^$')
  if [ "$count" -gt "$HINT_THRESHOLD" ]; then
    echo "$count worktrees under .claude/worktrees: run the cleanup-worktrees skill in the background."
  fi
  exit 0
fi

git fetch --quiet origin develop || { echo "fetch failed: nothing classified" >&2; exit 1; }
merged=$(gh pr list --state merged --limit 1000 --json headRefName --jq '.[].headRefName') \
  || { echo "gh failed: nothing classified" >&2; exit 1; }
cwds=$(lsof -d cwd -Fn 2>/dev/null | sed -n 's/^n//p' | sed 's#$#/#')
current=$(git rev-parse --show-toplevel)

classify() {
  local wt=$1 gitdir branch idle
  [[ $(basename "$wt") =~ $APP_MANAGED ]] && { echo app; return; }
  [ -d "$wt" ] || { echo missing; return; }
  gitdir=$(git -C "$wt" rev-parse --absolute-git-dir)
  [ -f "$gitdir/locked" ] && { echo locked; return; }
  if [ "$wt" = "$current" ] || printf '%s\n' "$cwds" | grep -Fq "$wt/"; then
    echo in-use
    return
  fi
  # Read before `git status`, which refreshes the index.
  idle=$(find "$gitdir/index" -mmin "+$IDLE_MINUTES" 2>/dev/null)
  [ -n "$(git -C "$wt" status --porcelain)" ] && { echo dirty; return; }
  branch=$(git -C "$wt" symbolic-ref --short -q HEAD)
  if [ -n "$branch" ] && printf '%s\n' "$merged" | grep -Fxq "$branch"; then
    echo landed
  elif git -C "$wt" cherry origin/develop HEAD | grep -q '^+'; then
    echo unmerged
  elif [ -n "$idle" ]; then
    echo landed
  else
    # No commit of its own yet: a subagent may have just started in it.
    echo fresh
  fi
}

removed=0
while IFS= read -r wt; do
  [ -n "$wt" ] || continue
  state=$(classify "$wt")
  branch=$(git -C "$wt" symbolic-ref --short -q HEAD 2>/dev/null || echo "(detached)")
  printf '%-9s %-44s %s\n' "$state" "$(basename "$wt")" "$branch"
  if [ "$mode" = "--apply" ] && [ "$state" = "landed" ]; then
    if git worktree remove "$wt"; then
      removed=$((removed + 1))
    fi
  fi
done <<< "$worktrees"

if [ "$mode" = "--apply" ]; then
  git worktree prune
  echo "removed: $removed"
fi
