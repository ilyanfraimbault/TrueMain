---
name: retro
description: Retrospective on a coding session — suggest improvements to the agent's environment (navigation, guardrails, review rules, steering files, tooling), most severe first.
disable-model-invocation: true
---

The user asked for a **retrospective**: improve the agent's **environment** so the next session goes better. The code itself is out of scope.

## Steps

1. Load `writing-for-agents` with the Skill tool — the style guide for every change you propose to a steering file.

2. Read the primary sources for the session the user names (default: the current one): the transcript, the PR and its review threads (`gh pr view --comments`, the Claude review's inline comments), the CI runs that failed, the commits that fixed something the agent got wrong.

3. Look for candidates in these categories. For each, the evidence is a concrete moment of the session.

   - **Navigation** — the agent spent long finding a file, a fact or a convention. Would a pointer in CLAUDE.md, a skill, or `.claude/docs` have sent it there? Is a pointer stale (it names something that no longer exists)?
   - **Guardrails** — the agent made a mistake a deterministic check could catch. Read what exists first (`ci.yml`, `.github/scripts/`, `SchemaNamingConventionTests`, the analyzers the Release build enforces): a check that exists but is unwired or skipped by the `changes` gate is the finding, not a reinvention. A **mechanical** rule (banned API, naming, file location, a missing call) gets a check — an analyzer, a convention test, a CI script — rather than prose.
   - **Review rules** — the Claude review (`.github/workflows/claude-review.yml`) missed something, or flagged a false positive that cost an iteration. Reserve its prompt for **judgement calls** no check can make; propose the rule, the removal, or the clarification.
   - **Steering files** — CLAUDE.md is loaded into every session: a rule there that belongs in a check, a skill, or a `.claude/docs` file is a finding, and so is a **no-op** (an instruction the agent already follows by default) or a duplicate of something said elsewhere.
   - **Knowledge base** — `features.md`, the decision log or the glossary was wrong, missing, or misleading, and it cost the session a wrong turn.
   - **Tool economy** — expensive or repeated tool calls that a script, a skill step or a narrower command would replace.
   - **Information access** — a fact the agent needed and could not reach (a log, a metric, a preprod read) and how it could.
   - **Memory** — something the user had to repeat that belongs in a memory file, or a memory that proved wrong and must be deleted.

4. Present the candidates to the user in French, **most severe first** (cost to the session × chance of recurring). For each: the moment it happened, the proposed change, and where it lands (file, check, skill, issue).

5. Apply what the user accepts: small edits to `.claude/` and CLAUDE.md go in a PR (`ship`); anything larger becomes an issue (`new-issue`).

## Reference — who carries what

Every change goes through implementation then review. The implementing agent carries the most **context pressure** (exploration, code, debugging); the reviewer receives a diff and has the least. Standards that only a judgement can enforce belong to the reviewer's prompt, not to CLAUDE.md, so the implementer's context stays for the task.

- `CLAUDE.md` — in every session's context: navigation pointers and the few rules every task needs.
- `CLAUDE.local.md` — operator-only context, never committed.
- `.claude/docs/` — reference reached by a pointer: `features.md`, the decision log, the glossary.
- `.claude/skills/` — procedures and disciplines; a description costs context on every turn, so only model-invoked skills that the agent must reach alone carry one.
- `claude-review.yml` prompt — the judgement-call standards.
- CI checks — every mechanical rule.
