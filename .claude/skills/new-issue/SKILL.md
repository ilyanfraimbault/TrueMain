---
name: new-issue
description: Create a well-formed GitHub issue and put it on the board — English body, added to Project #2, Priority set. Use whenever work should be captured for later — "crée une issue", "ajoute ça au backlog", "note ce bug", "il faudra faire X plus tard" — or when out-of-scope work worth tracking surfaces during another task.
---

# New issue

The backlog lives in GitHub Issues, never in local files (no BACKLOG.md, no TODO lists in the repo). Every issue belongs on Project #2.

1. **Write the issue in English**, structured:
   - `## Context` — why this matters, what prompted it (link related issues/PRs/incidents).
   - `## Scope` — what to do, as bullets.
   - `## Acceptance` — how we know it's done (skip for trivial fixes).
   Title in conventional style: `<type>: <summary>`.

2. `gh issue create --title "..." --body "..."`

3. **Board + priority**: `.claude/scripts/project-set.sh <n> Priority <P0|P1|P2|P3>` — the script adds the issue to Project #2 automatically (it lands as Todo).
   Priorities are sprint buckets: P0 = current sprint, P1 = next, P2 = after, P3 = someday. Default to P2 unless the user indicates urgency.

4. **Epics**: create a tracking issue whose body is a checklist of sub-issue links; sub-issues reference the epic. Set the epic's priority to its earliest sub-issue's bucket.
