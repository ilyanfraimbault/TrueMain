# Phase 5 — Data-split RFC: normalising `ChampionPatternAggregate`

## Why

The existing `champion_pattern_aggregates` table packs 23 dimension columns
into a single wide row, with a **23-column unique index** keeping each
combination distinct and `Games` / `Wins` counting how many matches landed
on that exact combo. The pain today:

- Insert / upsert cost on the wide row (every match triggers index maintenance
  on all 23 columns).
- Any new dimension we might care about (bans, timelines, etc.) is a
  schema migration on a multi-million-row table.
- The 23-column B-tree is wider than any single dimension the read path
  actually groups on — we carry index weight for the write-side grain
  rather than the read-side access patterns.

Paired with the 35 GB pressure on `match_participants`, the wide aggregate
is the first thing worth normalising.

## Scope

What this change touches:

- `Data.Entities.ChampionPatternAggregate` (the wide table) is **kept in
  place for this PR**. The drop migration is deliberately scheduled to a
  later PR once the new schema has been running on develop / prod for
  long enough to prove correctness.
- `Data.Entities.ChampionAggregate*` (5 new tables) land now, populated
  by a backfill SQL snippet in the same migration.
- `ChampionPatternAggregationPersister` rewires to write **only** the new
  tables going forward (the old table won't receive new rows after this PR).
- Query services (`ChampionFoundationQueryService`,
  `ChampionBuildTreeQueryService`) read **only** from the new tables.
- Tests are added to lock the backfill invariants.

Out of scope:

- Dropping the old table (5.5, later PR).
- Further splits (e.g. splitting `match_participants`) — that's a
  separate RFC.

## Target schema

Five tables: one master scope + four narrow dimension tables. Each
dimension row carries its own `Games` / `Wins` counter so the read path
can sum a single dimension without scanning the full pattern row.

```
champion_aggregate_scopes            (master — narrow)
  id                   uuid          pk
  riot_account_id      uuid          fk -> riot_accounts.id
  champion_id          int
  game_version         varchar(32)
  platform_id          varchar(8)
  queue_id             int
  position             varchar(16)
  games                int
  wins                 int
  last_game_start_time_utc  timestamptz
  aggregated_at_utc         timestamptz
  unique (riot_account_id, champion_id, game_version, platform_id, queue_id, position)
  index  (riot_account_id, champion_id, game_version, platform_id, position)
    // mirrors the secondary index on the current wide table

champion_aggregate_spell_pairs       (dimension)
  id                   uuid          pk
  scope_id             uuid          fk -> champion_aggregate_scopes.id cascade
  spell1_id            int
  spell2_id            int
  games                int
  wins                 int
  unique (scope_id, spell1_id, spell2_id)

champion_aggregate_skill_orders      (dimension)
  id                   uuid          pk
  scope_id             uuid          fk -> champion_aggregate_scopes.id cascade
  skill_order_key      varchar(32)
  games                int
  wins                 int
  unique (scope_id, skill_order_key)

champion_aggregate_starter_items     (dimension)
  id                   uuid          pk
  scope_id             uuid          fk -> champion_aggregate_scopes.id cascade
  starter_items_key    varchar(64)
  starter_items        jsonb         // raw item id list
  games                int
  wins                 int
  unique (scope_id, starter_items_key)

champion_aggregate_builds            (dimension, keeps correlation across boots + items)
  id                   uuid          pk
  scope_id             uuid          fk -> champion_aggregate_scopes.id cascade
  boots_item_id        int
  build_item_0         int
  build_item_1         int
  build_item_2         int
  build_item_3         int
  build_item_4         int
  build_item_5         int
  build_item_6         int
  games                int
  wins                 int
  unique (scope_id, boots_item_id, build_item_0, build_item_1, build_item_2,
          build_item_3, build_item_4, build_item_5, build_item_6)
```

Notes:

- The five-column unique on `champion_aggregate_scopes` keeps the
  read-side "slice" grain (account / champion / patch / platform / queue
  / position) — the API today filters on exactly these plus
  `riot_account_id`.
- Dimension tables summarise **independently within a scope**. We
  deliberately drop the cross-dimension correlation the old table kept
  (same row for spells × skills × starter × build), because:
  - The API's `ChampionCoreReadModel` picks the top-1 spells, top-1
    skill order, top-1 starter items and top-1 build path *independently*.
  - The `CorrelatedPatterns` list was only ever an internal scratchpad
    for `ChampionCoreBuilder` — it's now `[JsonIgnore]` and will go
    away.
  - The build path tree (`ChampionBuildTreeBuilder`) works directly
    from `champion_aggregate_builds`, which still keeps boots + items
    in a single row so the tree grouping stays identical.
- Perk configuration (`primary_style_id`, `sub_style_id`, `perks_*`)
  is **dropped** — nothing reads it today except the wide unique index.
  If it ever becomes part of the API contract we'll add a sixth
  dimension table.

## Backfill

The migration inserts:

1. One `champion_aggregate_scopes` row per **distinct** (riot_account_id,
   champion_id, game_version, platform_id, queue_id, position) in
   `champion_pattern_aggregates`, aggregating `games` / `wins` /
   `last_game_start_time_utc` / `aggregated_at_utc` via SUM / MAX.
2. Dimension rows grouped on `(scope_id, dimension_key)` with SUM of
   games / wins.

See the migration SQL for the exact UPSERT-free INSERT-only form (the
table is new, so there is nothing to merge into).

## Invariants the tests must prove

- `COUNT(distinct scope-key) == COUNT(scopes)`.
- `SUM(scopes.games) == SUM(champion_pattern_aggregates.games)`.
- For each scope S and each dimension D,
  `SUM(D.games where D.scope_id = S.id) == S.games`.
- For each scope S and each dimension D,
  `SUM(D.wins where D.scope_id = S.id) == S.wins`.

If any of these break the backfill is wrong and we do **not** roll the
old table away.

## Rollback

- `down` migration drops the five new tables. The old
  `champion_pattern_aggregates` table is still the write target nowhere
  in code, but if we revert the code change it resumes receiving writes
  from `ChampionPatternAggregationProcess` as if nothing happened.
  There's no destructive step in Phase 5 proper.
