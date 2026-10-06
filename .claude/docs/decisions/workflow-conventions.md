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

## The frontends lint with ESLint, with a severity ratchet (2026-10-06)

**Decision:** `web/` (shared layer included), `admin/` and `desktop/app` run ESLint through `@nuxt/eslint` —
the recommended preset, Vue's rules and `eslint-plugin-vuejs-accessibility` — as `npm run lint`, a CI step in
each app's job — #1440.

- **Severities are a ratchet**, the reasoning of the file-size guardrail (#1430): a rule clean when the linter
  landed is an error; each rule already broken sits in its app's `ratchet` list as a warning until its last
  violation is fixed, then leaves the list. A wall of red on the first run would have got the linter switched off.
- **Accessibility rules are all warnings first**, an owner's call (2026-10-05): the plugin is the noisiest of
  the set, and the repo's a11y work (#1402) is the reason to have it at all.
- Fixing the existing warnings is follow-up work, sized by the first run's per-rule counts in the PR.

## Load tests run against preprod from GitHub Actions, never from the preprod host (2026-09-14)

**Decision:** the load test is a k6 script in `loadtest/k6/`, started by hand from `loadtest-preprod.yml` on a
GitHub-hosted runner, against preprod — #1559.

- **Not from the preprod host.** The host also runs other workloads, and a generator there takes CPU from the
  system under test; the result would measure both.
- **Not against prod.** A test that finds the ceiling finds it for real visitors. Preprod runs prod's parameters
  (#1558), so its results are comparable in kind; they are a lower bound in size, and are reported as one.
- **Visitors are replayed over HTTP; a few browsers time the pages.** k6's HTTP client does not run the site's
  JavaScript, so each page view requests the SSR HTML and then the calls the hydrated page makes, with the
  champion slices drawn at random so the test reaches the database rather than the cache. The request lists
  mirror the page composables and have to follow them. That makes the load, but not the page-load time: most
  of the champion page and its icons exist only once its JavaScript ran. A handful of `k6/browser` VUs load the
  same pages during the hold (and alone, in the `browser` scenario) and record data ready, view loaded and page
  loaded with every rendered image — #1572 (2026-09-15). Browsers stay few: one costs the runner what
  hundreds of HTTP visitors do.
- **Nothing published names the host.** The repository is public: requests are tagged by route template, and the
  workflow refuses to publish output containing the host.

## Agent disciplines are adapted from mattpocock/skills, not installed as a plugin (2026-10-05)

**Decision:** `diagnosing-bugs`, `grilling`, `grill-with-docs`, `domain-modeling`, `retro` and
`writing-for-agents` are copied into `.claude/skills/` and rewritten for this repo (MIT notice in
`.claude/skills/THIRD-PARTY.md`); his `code-review` and `pr` are merged into ours instead — #1938.

- **Not the plugin.** It updates behind our back and collides with what we have: his `code-review` shadows Claude
  Code's built-in one, his `pr`, `to-tickets` and `setup-matt-pocock-skills` assume their own tracker setup where
  `ship`, `new-issue` and Project #2 already decide.
- **ADRs map to the decision log**, the glossary lives beside it in `.claude/docs/glossary.md` rather than at the
  root, so the knowledge base stays in one place.
- **The review gained his two axes**: `claude-review.yml` reviews **Spec** (the linked issue's scope and
  acceptance) apart from **Standards** (our written rules), so a change that follows every rule but builds the
  wrong thing is still caught; Fowler code smells are `NIT:` only, so they never block a merge.
- **PR bodies** follow `ship/PR-BODY.md`: the smallest visual of the change, before/after evidence, and a merge
  danger call (one-way or two-way door, blast radius), because migrations and aggregate wipes are one-way doors
  and the body is where a reviewer should see it.
