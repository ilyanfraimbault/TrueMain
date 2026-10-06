---
name: grilling
description: Interview the user relentlessly about a plan, feature or design until every branch of the decision tree is resolved, in rounds, with a recommended answer per question. Use when the user wants to stress-test an idea, says "grill me", "challenge-moi", "pose-moi des questions", or when an issue leaves product or design decisions open before coding.
---

Interview the user until you reach a shared understanding. Map the topic as a **design tree**: every decision branches into the decisions that hang off it.

Work the tree in **rounds**. The **frontier** is every decision whose prerequisites are already settled — the questions you can ask _now_ without guessing at answers you haven't heard. Ask the whole frontier in one round, number each question, and give your recommended answer. Then wait for the answers. Talk to the user in French.

```
❓ **Q1** — **<titre>** : <question, options si utile>

➡️ <ta recommandation, et pourquoi en une phrase>

---

❓ **Q2** — …
```

Each round reshapes the tree: settled decisions push the frontier outward. Recompute it and ask the next round. A question whose answer depends on another question still open belongs to a _later_ round.

**Facts are your job; decisions are the user's.** Before asking, check what is already known: `.claude/docs/features.md` (what ships), the decision index `.claude/docs/decisions.md` and its area file (what is settled — never re-litigate a listed decision unless the user opens it), `.claude/docs/glossary.md`, and the code. When a frontier question needs a fact from the environment (code, data, preprod reads), dispatch a sub-agent to find it and ask the rest of the frontier meanwhile; only the questions downstream of that fact wait.

**Completion criterion**: the frontier is empty — every branch visited, nothing silently assumed. Recap the settled decisions in a short list, and act on them only once the user confirms the recap.
