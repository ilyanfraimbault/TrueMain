# Workflow conventions

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

- **Language split**: talk to the user in French; everything committed or published (code, comments, commits,
  issues, PR titles and bodies) is in English. (`docs/api.md` is in French, predating the rule.)

- **`develop` is the default branch.** All PRs target it; the only PR allowed to target `master` is the release
  PR. Feature PRs are **squash**-merged; release PRs use a **merge commit**, because squashing there creates
  false conflicts on the next release. `develop` survives release merges and is never deleted.

- **Branch names** `<type>/<issue>-<short-kebab>`, conventional commits, no `Co-Authored-By: Claude` trailers,
  no "Generated with Claude Code" footers, `Closes #N` in the PR body.

- **A PR is done** when CI is green, the Claude review verdict is clean *on the current head SHA* (verdicts
  trail ~1 commit behind pushes), and every real finding is fixed or rebutted — then merge without asking.
  Stop after ~3 non-converging iterations and report blockers instead of looping.

- **CI traps**: backend CI builds **Release** with analyzers as errors (Debug is not enough); `nuxt typecheck`
  can pass on stale `.nuxt` types while CI's `nuxt build` fails; `web/package-lock.json` must be regenerated
  with `npx npm@11.13.0` (older npm omits sharp optional deps), the version CI pins for the frontend jobs
  since #1236 — before that, CI ran whatever npm the resolved Node 24 build shipped, so the lock file's
  generator and the installer could silently diverge.

- **API wire conventions**: camelCase JSON, RFC 7807 problem details on all 4xx/5xx, no global `/api` prefix,
  `patch` normalised to `major.minor` (invalid values treated as unfiltered), canonical Riot position values,
  `pageSize`/`limit` ≤ 0 means "default" — `docs/api.md`.

- **Every issue goes on GitHub Project #2.** Scheduling and urgency are two separate fields: **Sprint** (the
  14-day iteration field) says *when* the work is planned, **Priority** (P0–P3) says how urgent it is and
  orders work inside a sprint. Priority used to double as the sprint bucket ("P0 = current sprint"); that
  overloading was dropped because it silently competed with the real iteration field the board was already
  using. No milestones.
