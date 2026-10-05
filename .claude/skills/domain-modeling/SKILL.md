---
name: domain-modeling
description: Build and sharpen TrueMain's domain language — challenge terms against the glossary, stress-test them with edge-case scenarios, and record settled terms in `.claude/docs/glossary.md` and settled decisions in the decision log. Use when a term is ambiguous or used two ways, when adding or editing a glossary entry, or when a design discussion settles a decision worth recording.
---

# Domain modeling

The _active_ discipline: changing the model, not just reading it. (Reading the glossary for vocabulary is a habit every skill has; this skill is for when a term or a decision is being settled.)

## During the session

- **Challenge against the glossary.** When the user uses a term that conflicts with `.claude/docs/glossary.md`, say so at once: "Le glossaire définit _population_ comme X, tu sembles parler de Y — lequel ?"
- **Sharpen fuzzy language.** Overloaded words are common here (rank / tier / elo, role / position / lane, game / match, main / OTP): propose the canonical term.
- **Stress-test with scenarios.** Invent concrete edge cases that force a boundary: a player who swapped role mid-season, a champion reworked mid-patch, a game on a patch we no longer serve.
- **Cross-reference with the code.** When the user states how something works, check that the code agrees and surface contradictions.

## Glossary — `.claude/docs/glossary.md`

Update it the moment a term is resolved; don't batch. Entry format:

```md
**Term**:
What it IS, in one or two sentences.
_Avoid_: synonym, other synonym
```

- **Opinionated**: when several words name one concept, pick one, list the rest under `_Avoid_`.
- **Domain only**: League of Legends and TrueMain concepts a newcomer would misread. General programming notions stay out.
- **No implementation**: no class, table, endpoint or file names. The glossary is not a spec or a scratch pad.
- Group entries under the existing area headings; add a heading only when a real cluster appears.

## Decisions — the decision log, not ADRs

A settled decision goes into `.claude/docs/decisions/<area>.md` (format **Decision** — why — `source`) **and** its one-line entry in the index `.claude/docs/decisions.md`, in the same PR as the change (CLAUDE.md, "Project knowledge base").

Record one only when all three hold — otherwise skip it:

1. **Hard to reverse** — changing your mind later costs something real.
2. **Surprising without context** — a future reader would wonder why, or "fix" it.
3. **A real trade-off** — there were genuine alternatives, and the reason for the pick is worth keeping (rejected alternatives included, when the rejection is not obvious).
