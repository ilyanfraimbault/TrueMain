# PR body

English, brief, no preamble, in the project's language (`.claude/docs/glossary.md`). The squash commit takes the
PR title; the body is what the reviewer and the next reader of `git log` see.

```markdown
## Summary

<one or two sentences, then the smallest visual that makes the change clear>

## Evidence

- **Before:** <screenshot / output / failing test>
  **After:** <screenshot / output / passing test>

## Merge danger

**Door:** <one-way | two-way> — <why, one line>
**Blast radius:** <one word> — <what could break, for whom>

Closes #<issue>
```

## Summary — pick the smallest view

One of these, sometimes two, never all:

- **Pseudocode** for logic or an algorithm (a scoring rule, a fold, a pacing decision).
- **Call tree** for runtime flow: `endpoint → query service → projection`.
- **Component tree** for UI structure, with the state and layer boundaries that matter (`web/layers/common` vs the app).
- **Shallow file tree** for a refactor or a split.
- **Mermaid** for interaction or data flow between services (ingestor → Postgres → API → web).
- **`diff` block** of one of the above when the shape already exists and the point is what changes:

```diff
 GET /champions/{id}/builds
   builds query service
     patch filter
+    rank-scope filter
     project → build read-model
```

## Evidence — show, don't claim

Screenshots are best for a visual change (the preview you already took while verifying). Then execution: the test
that failed and now passes, the command output, the endpoint's JSON before/after, a query result. Only what you
actually observed (CLAUDE.md, "Verify, don't ask" and "No fabricated numbers"); host names and prod metrics stay out.

## Merge danger

- **Door**: a **two-way** door is cheap to walk back (revert the squash, redeploy). A **one-way** door is not: a
  migration that drops or rewrites data, a wipe-and-refold of an aggregate, a public URL or API contract change, a
  Riot budget change that risks the key, a desktop version bump users install.
- **Blast radius**: who and what is hit if it is wrong — one page, every champion page, the ingestor's whole
  pipeline, the desktop beta users, SEO. Say how it would show (layout shift, empty table, 429 storm, crash loop).
