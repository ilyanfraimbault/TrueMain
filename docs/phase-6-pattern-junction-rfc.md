# Phase 6 — Pattern junction RFC: bringing back cross-dimension correlation

## Why

Sprint 5 normalised the wide `champion_pattern_aggregates` table into a
master `champion_aggregate_scopes` plus 5 dimension tables (`*_builds`,
`*_rune_pages`, `*_skill_orders`, `*_spell_pairs`, `*_starter_items`).

That change bought us narrow indexes and cheap inserts, but it also
**lost the correlation between dimensions**. Every dimension row counts
games independently inside its scope:

```
Build:        (scope=yasuo-mid, AD-crit)     Games=3
RunePage:     (scope=yasuo-mid, Conqueror)   Games=3
SkillOrder:   (scope=yasuo-mid, Q→W→E)       Games=3
```

We can answer "top build" or "top runes" individually, but we can't
answer "when this player picks AD-crit on Yasuo, what runes do they
actually run?" The data needed to answer that — the actual co-occurrence
of (build, runes, skill, spells, starters) per match — is gone the
moment we group it independently.

The half-measure visible today: `ChampionAggregateRunePage.FirstItemId`
correlates a rune page with the first item of the build (`BuildItem0`).
That's partial (one item, not the build), brittle (different rune
groupings explode when the first item differs by one slot), and shaped
by the migration's inability to recompute it from raw SQL — see commit
`fix(migrations): drop the rune-page backfill SQL` for the rationale.

Frontend backlog (`project_runes_backlog.md`) explicitly calls for
"runes correlated with build path, mirror the correlated-boots pattern".
The legacy `champion_pattern_aggregates` row got that right by
construction (one row = one full combo) — but at the cost of 23
inlined columns and the 23-column unique index that Phase 5 set out
to kill. **We want both: normalised storage AND preserved correlation.**

## Target shape

```
champion_aggregate_scopes               (unchanged from Phase 5)
├── one row per (account, champion, version, platform, queue, position)
└── carries scope-level totals: Games, Wins, AggregatedAtUtc, LastGameStartTimeUtc

champion_aggregate_patterns             (NEW — junction)
├── ScopeId         FK → scopes
├── BuildId         FK → builds       (deduplicated)
├── RunePageId      FK → rune_pages   (deduplicated)
├── SkillOrderId    FK → skill_orders (deduplicated)
├── SpellPairId     FK → spell_pairs  (deduplicated)
├── StarterItemsId  FK → starter_items(deduplicated)
├── Games
└── Wins
   UNIQUE (ScopeId, BuildId, RunePageId, SkillOrderId, SpellPairId, StarterItemsId)

champion_dim_builds                     (CHANGED — global reference)
├── (BootsItemId, BuildItem0..6)
└── UNIQUE on the items combination
   ↑ no ScopeId, no Games, no Wins

champion_dim_rune_pages                 (CHANGED — global reference)
├── (PrimaryStyleId, PrimaryKeystoneId, PrimaryPerk1..3Id,
│    SecondaryStyleId, SecondaryPerk1..2Id,
│    StatOffense, StatFlex, StatDefense)
└── UNIQUE on the perk combination
   ↑ no ScopeId, no FirstItemId, no Games, no Wins

champion_dim_skill_orders               (CHANGED — global reference)
├── SkillOrderKey
└── UNIQUE (SkillOrderKey)

champion_dim_spell_pairs                (CHANGED — global reference)
├── (Spell1Id, Spell2Id)
└── UNIQUE (Spell1Id, Spell2Id)

champion_dim_starter_items              (CHANGED — global reference)
├── StarterItemsKey + StarterItems[]
└── UNIQUE (StarterItemsKey)
```

Naming convention: `champion_dim_*` for the 5 deduplicated reference
tables clearly separates them from `champion_aggregate_*` (scope and
pattern), which carry per-scope counts. The current `champion_aggregate_builds`
etc. tables get renamed + restructured during the migration.

### Why no double-counting

A match contributes to **exactly one** pattern row — the row that has
its specific combo of FKs. Summing `Games` across patterns by any FK
gives the right total:

| Query | SQL |
|-------|-----|
| Top build for a scope | `SELECT BuildId, SUM(Games) FROM patterns WHERE ScopeId = ? GROUP BY BuildId` |
| Top runes for a scope | `SELECT RunePageId, SUM(Games) FROM patterns WHERE ScopeId = ? GROUP BY RunePageId` |
| Top runes when the player picks build X | `SELECT RunePageId, SUM(Games) FROM patterns WHERE ScopeId = ? AND BuildId = ? GROUP BY RunePageId` |
| Pattern of patterns: top complete combo | `SELECT * FROM patterns WHERE ScopeId = ? ORDER BY Games DESC LIMIT 1` |

## Aggregation (write path)

Per scope, the aggregator already groups source rows into combos
(`ChampionPatternAggregateBuilder.BuildLegacyAggregates`). The new flow:

1. **Source rows**: same as today — read from `match_participants` ⋈
   `participant_perk_selections` ⋈ `perk_selection_catalog`, filtered by
   `IsMain = TRUE` etc.
2. **Group by full combo per scope**: produce a list of
   `(ScopeKey, BuildContent, RunePageContent, SkillOrderKey,
   SpellPairContent, StarterItemsContent, Games, Wins)`.
3. **Lookup-or-insert each dimension** (batched per aggregation cycle):
   - Collect distinct contents per dimension.
   - `INSERT INTO champion_dim_builds (...) VALUES (...) ON CONFLICT DO NOTHING`
   - Then `SELECT Id FROM champion_dim_builds WHERE (...) IN (list)` to
     get IDs for both freshly inserted and pre-existing rows.
   - Same for the 4 other dimensions.
4. **Replace the scope's patterns wholesale**:
   - `DELETE FROM champion_aggregate_patterns WHERE ScopeId = @ScopeId`
   - `INSERT INTO champion_aggregate_patterns (...) VALUES (...)` with
     the resolved FKs.

Step 4 mirrors today's "delete + reinsert per scope" semantics
(`ChampionPatternAggregatePersister.PersistAsync` already does this for
the dimension tables via cascade). Pattern rows replace scope-by-scope,
no partial state.

The dimension tables are **append-only** in steady state. New combos
add rows, existing combos return existing IDs. They grow until they
saturate at the natural number of unique combos in the meta — bounded
in practice (a handful of meta builds × meta runes per champion per
patch).

### Concurrency

The aggregator runs single-instance today (per `Worker.cs`'s sequential
per-process loop). The lookup-or-insert pattern still needs to be
race-safe for two reasons:

1. Future multi-instance ambitions (capacity scaling).
2. Within one aggregation cycle, multiple scopes may reference the same
   newly-inserted dimension row.

`INSERT ... ON CONFLICT DO NOTHING` + a follow-up `SELECT` is the
standard race-safe pattern. Postgres serialises the unique index
violation at row level; the `SELECT` returns the row regardless of
which transaction committed it.

## Read API (query side)

Today's `ChampionFoundationQueryService` reads each dimension table
independently and presents the per-dimension top items. The new
service queries the pattern table and computes per-dimension stats via
`GROUP BY` + `JOIN` to the dim reference tables.

Bonus: with patterns as the source, we can expose a new endpoint
shape that the frontend's "runes correlated with build" backlog item
needs:

```
GET /champions/{id}?riotAccountId=...&buildId=<dim-build-id>
  → returns the foundation/build-tree, but rune/skill/spell/starter
    blocks are filtered to patterns matching that build only.
```

Without the new build filter, the response is the unconditional top
across all patterns — same as today's behaviour. With the filter, the
read-side becomes "given they pick this build, what does the rest of
the pattern look like".

The exact query parameter name and which dimensions can act as a
"correlation pivot" are decided in PR 6.3 — but the schema supports
all 5 from day one.

## Migration plan

The legacy `champion_pattern_aggregates` table still holds the source
of truth for combos (each row = one full combo with Games/Wins). The
migration in PR 6.4 backfills the new schema directly from it:

1. For each legacy row:
   - Lookup-or-insert into the 5 dim tables → 5 IDs.
   - Insert into `champion_aggregate_patterns` with those IDs +
     legacy `Games`/`Wins`.
2. Validate: per-scope `SUM(patterns.Games) = SUM(legacy.Games)`.
3. Drop the legacy table + drop the old `champion_aggregate_*`
   dimension tables (they had per-scope counts, now obsolete).

The migration is batched by `ScopeId` chunks (the same DO-block walk
pattern as the rune-page backfill we tried earlier — but this time
each chunk produces meaningful, correlated rows instead of
`FirstItemId = 0` placeholders).

If the migration is too large for a single startup window, we can ship
PR 6.4 with a feature flag that runs the backfill as a one-shot
ingestor process instead of an EF migration — keeps the rollback story
clean (just truncate the new tables and rerun).

## Sub-PR breakdown

Each PR is independently mergeable; the API stays functional throughout.

**PR 6.1 — Schema + entities (data-only):**
- Add `champion_aggregate_patterns` table.
- Add `champion_dim_*` reference tables with UNIQUE constraints.
- Add EF entities + configurations.
- No aggregator/API code changes yet — new tables stay empty.
- Tests: schema integration test that asserts uniqueness + FK behaviour.

**PR 6.2 — Aggregator dual-write:**
- Modify `ChampionPatternAggregateBuilder` + `ChampionPatternAggregatePersister`
  to write to BOTH the legacy `champion_aggregate_*` dim tables AND
  the new pattern + dim tables.
- Lookup-or-insert helper for the 5 dim tables (batched).
- Tests: assert that for any source-row set, the new-schema sums match
  the legacy-schema sums.
- Read side untouched — still uses legacy dims.

**PR 6.3 — Read-side migration:**
- Rewrite `ChampionFoundationQueryService` + `ChampionBuildTreeQueryService`
  to read from the new patterns + dim tables.
- Add the optional `?buildId=` (or whatever pivot we settle on)
  correlation parameter.
- Update integration tests to assert correlations work correctly.
- Aggregator still dual-writes (rollback safety).

**PR 6.4 — Backfill historical data + drop legacy:**
- Migration: batched DO-block backfill from `champion_pattern_aggregates`
  into the new schema.
- Validation queries embedded in the migration (RAISE EXCEPTION on
  mismatch).
- Aggregator stops dual-writing — only writes the new schema.
- Drop the legacy `champion_pattern_aggregates` + the old
  `champion_aggregate_builds` / `_rune_pages` / `_skill_orders` /
  `_spell_pairs` / `_starter_items` tables.
- Drop `FirstItemId` (it's gone with the table).
- OPTION-C entry C-2 is now done.

**PR 6.5 — Cleanup:**
- Remove the dual-write code paths.
- Remove unused indexes / dead code.
- Update OPTION-C.md.

## Out of scope

- Cross-champion correlation (e.g. "what builds do players who main
  Yasuo also run on Yone"). Stays in the per-scope grain.
- Materialised views / projection caches. The simple `GROUP BY` + index
  approach is the baseline; only optimise if metrics show a hot path.
- Frontend changes — exposed in a separate `truemain-web` PR once
  PR 6.3 ships the new endpoint shape.

## Risks and open questions

1. **Pattern-table row count.** Bounded by `scopes × distinct combos
   per scope`. In the meta (most players have 1-3 distinct combos per
   champion-patch-position), typical scopes will have 1-5 pattern rows.
   Outliers are 1-percent players; estimate stays well under
   `2 × current dim-table row count`.

2. **Migration window.** The legacy table has N rows. Backfill cost
   ≈ N inserts + 5N dim lookups. At ~10K patterns/s on a vanilla
   Postgres node, a million-row backfill is ~2 minutes. PR 6.4 will
   measure on a snapshot before shipping.

3. **API contract change.** PR 6.3 changes the read-side semantics
   subtly: per-dimension stats are now sums-over-patterns rather than
   independent counts. They produce the same numbers as long as no
   data was missing, but any consumer that compared
   `Build.Games` to `RunePage.Games` and expected them to match would
   already have been wrong (they sum to the same scope total but their
   row counts differ). Worth a frontend smoke test.

4. **Index choice on `champion_aggregate_patterns`.** The unique index
   on the 6 FKs is mandatory for upsert correctness. We also need
   `(ScopeId, BuildId)`, `(ScopeId, RunePageId)`, etc. for the per-pivot
   correlation queries. PR 6.3 will tune index list once the actual
   query plans are visible.

5. **Frontend coupling.** The current API surface returns
   `Core.Boots`, `Core.SummonerSpells`, etc. as independent picks. PR
   6.3 keeps that contract — the new correlation parameter is purely
   additive. No breaking change unless we deliberately deprecate the
   old shape later.
