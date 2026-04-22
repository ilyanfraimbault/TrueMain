# Phase 5 RFC — rune pages as a champion-aggregate dimension

## Why

The champion aggregate today exposes spells, skills, starter items and
build paths — but **no runes**. Runes are the missing piece of the
playstyle fingerprint:

- Conqueror vs Lethal Tempo on AD carries correlates with build direction
  (AD-crit vs bruiser).
- Keystones from Inspiration / Sorcery swap in and out depending on the
  rest of the build.
- Shards (adaptive, MS, armor, HP) fine-tune the early game.

A user looking at "what build do top mains run on this champion" gets
half the answer without seeing the rune page alongside.

## What lives in a rune page

A Riot rune page is **11 integer identifiers**:

| Bloc | IDs | Notes |
|---|---|---|
| Primary tree | `primary_style_id` | e.g. 8000 Precision |
| Primary tree | `primary_keystone_id` | slot 0 of the primary selections |
| Primary tree | `primary_perk_1_id` | slot 1 |
| Primary tree | `primary_perk_2_id` | slot 2 |
| Primary tree | `primary_perk_3_id` | slot 3 |
| Secondary tree | `secondary_style_id` | e.g. 8100 Domination |
| Secondary tree | `secondary_perk_1_id` | slot 0 of the secondary selections |
| Secondary tree | `secondary_perk_2_id` | slot 1 |
| Stat shards | `stat_offense` | adaptive / attack speed / ability haste |
| Stat shards | `stat_flex` | adaptive / armor / magic resist |
| Stat shards | `stat_defense` | HP / HP scaling / tenacity |

Eleven columns, plus `games` / `wins`, plus the foreign key to the scope.
The user explicitly asked to correlate the whole page rather than splitting
by bloc, so we model it as a single wide dimension row.

## Target table

```
champion_aggregate_rune_pages      (dimension, referenced by scope)
  id                      uuid pk
  scope_id                uuid fk -> champion_aggregate_scopes cascade
  primary_style_id        int
  primary_keystone_id     int
  primary_perk_1_id       int
  primary_perk_2_id       int
  primary_perk_3_id       int
  secondary_style_id      int
  secondary_perk_1_id     int
  secondary_perk_2_id     int
  stat_offense            int
  stat_flex               int
  stat_defense            int
  games                   int
  wins                    int
  unique (scope_id,
          primary_style_id, primary_keystone_id,
          primary_perk_1_id, primary_perk_2_id, primary_perk_3_id,
          secondary_style_id, secondary_perk_1_id, secondary_perk_2_id,
          stat_offense, stat_flex, stat_defense)
```

12-column unique index. This is a **single dimension** within a scope —
not a return to the wide-table pattern the previous RFC buried. Typical
cardinality per scope should be under a dozen distinct pages.

## Data source per participant

At the match-participant grain we have:
- `match_participants.PrimaryStyleId`, `SubStyleId` — the two tree ids.
- `match_participants.PerksOffense`, `PerksFlex`, `PerksDefense` — the
  three stat shard ids.
- `participant_perk_selections (MatchId, ParticipantId, PerkSelectionCatalogId)` ⋈
  `perk_selection_catalog (StyleId, SelectionIndex, PerkId, StyleDescription)` —
  the six primary/secondary perk slots.

Pivot:

```text
SELECT
  participant.MatchId,
  participant.ParticipantId,
  participant.PrimaryStyleId,
  participant.SubStyleId,
  participant.PerksOffense,
  participant.PerksFlex,
  participant.PerksDefense,
  (c.StyleDescription, c.SelectionIndex, c.PerkId)    -- 6 rows per participant
```

At build time we materialise `participant.ParticipantPerkSelections` as a
list and pivot in C# into one `RunePage` record per participant, keyed by
(primaryStyle, primaryKeystone, primary1, primary2, primary3, secondaryStyle,
secondary1, secondary2, offense, flex, defense).

## Backfill

The scope + dimension tables come into existence empty in the
Sprint 5.2 migration and are hydrated from the wide table. That wide
table **never stored the six individual perk ids** — it only kept
PrimaryStyleId / SubStyleId / PerksOffense/Flex/Defense. So we cannot
backfill rune pages from `champion_pattern_aggregates` alone.

We can, however, rebuild the rune pages from the source of truth:
`match_participants` plus `participant_perk_selections` ⋈ `perk_selection_catalog`.
For each (scope, rune page) the counters are `SUM(games)` where each
participant contributes 1 game (1 win if `participant.Win`).

The backfill groups by scope key — same six columns as
`champion_aggregate_scopes` — plus the 11 rune page columns, and sums
across the matching `match_participants` rows.

## Invariants for the data-preservation tests

- For each scope S, Σ `rune_pages.games where scope_id = S.id` equals S.games.
- For each scope S, Σ `rune_pages.wins where scope_id = S.id` equals S.wins.
- One `rune_page` per distinct 12-tuple per scope (unique index).

Same tests already exist for the four existing dimensions; extending them
costs one extra `.Sum` assertion.

## API surface

- New `ChampionRunePageOptionReadModel` with the 11 ids + games / play rate
  / win rate.
- `ChampionAdvancedDetailsReadModel.RunePageOptions` = top-3 pages.
- `ChampionCoreReadModel.RunePage` = top-1 page (by games).

## Migration of the ingestion pipeline

- Extend `AggregateSourceRow` with the six primary/secondary perk ids
  (plus the existing style ids + stat shards).
- Extend the source reader to eager-load the perk selections + catalog
  join per participant and pivot in C#.
- Extend `ChampionPatternAggregateBuilder` to emit the new `RunePages`
  collection on each scope it produces.
- Extend `ChampionPatternAggregatePersister` — same flow, one extra
  `db.ChampionAggregateRunePages.AddRange` in the transaction.

## What this PR does NOT do

- Does **not** touch the legacy wide table (already dual-written, staying
  that way until the drop migration).
- Does **not** introduce the per-bloc split (keystone-only, stat-shards-only)
  — deliberate: correlation full-page is the whole point.
- Does **not** expose per-rune rate of play decoupled from the page — if
  we ever want that, derive it from the `champion_aggregate_rune_pages`
  rows by GROUP BY on the individual perk column.
