---
name: diagnosing-bugs
description: Diagnosis loop for hard bugs and performance regressions — build a red feedback loop first, then minimise, hypothesise, instrument, fix with a regression test. Use when the user says "diagnose", "debug this", "pourquoi ça casse", or reports something broken, throwing, failing, wrong or slow (a page, an endpoint, an ingestor process, a figure that looks off).
---

# Diagnosing bugs

A discipline for hard bugs. Skip a phase only when you can say why. Read `.claude/docs/glossary.md` for the terms in play, and the decision area file for the code you touch: a "bug" is sometimes a settled decision.

**Redact** every secret and every host in what you show (`<REDACTED>`): build loops against env vars, and quote only the lines of a captured artifact that carry the signal. Prod/preprod details stay out of anything committed or published (CLAUDE.md, "Operator-only context").

## Phase 1 — Build a feedback loop

**This is the skill.** With a **tight** pass/fail signal that goes **red** on _this_ bug, bisection, hypotheses and instrumentation just consume it. Without one, staring at code will not save you. Be aggressive, creative, and refuse to give up here.

Seams that reach TrueMain code, roughly in order of preference:

1. **Failing test** at the seam that reaches the bug: `backend/tests/TrueMain.UnitTests`, `TrueMain.IntegrationTests` (Testcontainers — needs a Docker daemon, often absent), or the app's vitest suite (`npm run test` in `web/` or `admin/`).
2. **HTTP call** against the API run on the alternate port (`api-5099`, `setup-worktree` skill), diffing the JSON against the expected read-model.
3. **Headless browser script** against a local dev server (playwright-core + the baked Chromium) asserting on DOM, console or network.
4. **Replay a captured payload**: save a real Riot response or API response to a fixture and push it through the code path in isolation — the cheapest way to reproduce an ingestor bug without Riot's pacing.
5. **Differential loop**: same input through old vs new commit, or preprod vs local, diff the outputs. `git bisect run <loop>` once you have two known states.
6. **Read-only query against preprod** (access in `CLAUDE.local.md`) when the symptom only exists in real data: a SQL query or an API call whose result shows the wrong figure. Reads are pre-authorised; writes, restarts and deploys are not.
7. **Ask the user** for the artifact only they hold (a screenshot with the exact URL and filters, a desktop app log, a client state) — last resort.

**Tighten the loop** once you have one: faster (narrow the test filter, skip unrelated setup), sharper (assert on the specific symptom, not "didn't crash"), more deterministic (pin the patch, the seed, the clock). For intermittent bugs, raise the reproduction rate (loop it, parallelise, add stress) until it is debuggable.

**Ingestor and pacing bugs** (429 storms, timeouts, lease contention) often cannot be reproduced locally: the loop is then a preprod observation you can re-run — a query over process runs, a log grep with a time window — and the fix is verified there after merge (`ship` post-merge step).

**Completion criterion**: you can name **one command** you have **already run** (show it and its redacted output) that is red-capable (asserts the user's exact symptom), deterministic, fast, and runnable unattended. If you catch yourself reading code to build a theory before that command exists, stop. If you truly cannot build one, say so, list what you tried, and ask the user for access or an artifact.

## Phase 2 — Reproduce and minimise

Run the loop and watch it go red. Confirm it is the failure the **user** described, not a neighbouring one: wrong bug, wrong fix.

Then shrink: cut inputs, filters, rows, steps **one at a time**, re-running after each cut. Done when every remaining element is load-bearing — removing any one turns the loop green. The minimal repro narrows the hypothesis space and becomes the regression test.

## Phase 3 — Hypothesise

Write **3–5 ranked, falsifiable hypotheses** before testing any: "If X is the cause, changing Y makes the bug disappear." A hypothesis without a prediction is a vibe; sharpen or discard it.

Show the ranked list to the user before testing — they often re-rank instantly ("we just changed the fold for #3"). Proceed on your ranking if they are away.

## Phase 4 — Instrument

Each probe maps to one prediction; change one variable at a time. Debugger or a REPL first, then targeted logs at the boundaries that separate hypotheses. Tag every temporary log with a unique prefix (`[DEBUG-a4f2]`) so cleanup is one grep.

**Performance**: measure first (timing harness, `EXPLAIN ANALYZE`, profiler), keep the baseline number, then bisect. Report only figures you measured (CLAUDE.md, "No fabricated numbers").

## Phase 5 — Fix and regression-test

Write the regression test **before** the fix, at a **correct seam**: one that exercises the real bug pattern as it occurs at the call site. If the only seam available is too shallow to replicate it, that is a finding — say so in the PR and, if it is worth fixing, open an issue (`new-issue`).

With a correct seam: turn the minimal repro into a failing test, watch it fail, fix, watch it pass, then re-run the Phase 1 loop on the original, un-minimised scenario.

## Phase 6 — Cleanup

Done when all hold:

- [ ] The Phase 1 loop is green on the original scenario.
- [ ] The regression test passes, or the missing seam is documented.
- [ ] `grep` for the `[DEBUG-` prefix returns nothing; throwaway harnesses are deleted.
- [ ] The confirmed hypothesis is stated in the commit or PR body, so the next debugger learns from it.
