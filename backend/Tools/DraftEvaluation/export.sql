-- Inputs of the draft-suggestion back-test (#1906), as one psql session writing to stdout.
-- Split the output on the ===NAME=== lines into NAME.csv. The aggregates are read on the
-- training patches only and the test games come from the test patch, so no test game is
-- in the numbers that score it. Every statement is bounded: the test games are capped and
-- reached through the match index.
--
-- Server-side `copy … to stdout` rather than `\copy`: psql does not interpolate its
-- variables inside a `\copy` line. No temporary table, so the script runs in a read-only
-- session.
--   psql -v test_patch=16.19 -v train="'16.18','16.17'" -v since=2026-09-23 < export.sql
set statement_timeout = '120s';
\echo ===matchups===
copy (
  select "ChampionId", "TeamPosition", "OpponentChampionId", sum("Games") as "Games", sum("Wins") as "Wins"
  from champion_matchup_stats where "Patch" in (:train) group by 1, 2, 3
) to stdout with csv header;
\echo ===baselines===
copy (
  select "Side", "ChampionId", "TeamPosition", sum("Games") as "Games", sum("Wins") as "Wins"
  from champion_synergy_baseline_stats where "Patch" in (:train) group by 1, 2, 3
) to stdout with csv header;
\echo ===synergy===
copy (
  select "ChampionId", "TeamPosition", "PartnerChampionId", "PartnerPosition", sum("Games") as "Games", sum("Wins") as "Wins"
  from champion_synergy_stats where "Patch" in (:train) group by 1, 2, 3, 4
) to stdout with csv header;
-- The opposing pairs (#1713). Empty until ChampionOpponentAggregation has drained the
-- training patches; the tool skips the enemy-team section without these two files.
\echo ===opponents===
copy (
  select "ChampionId", "TeamPosition", "OpponentChampionId", "OpponentPosition", sum("Games") as "Games", sum("Wins") as "Wins"
  from champion_opponent_stats where "Patch" in (:train) group by 1, 2, 3, 4
) to stdout with csv header;
\echo ===opponentbaselines===
copy (
  select "Side", "ChampionId", "TeamPosition", sum("Games") as "Games", sum("Wins") as "Wins"
  from champion_opponent_baseline_stats where "Patch" in (:train) group by 1, 2, 3
) to stdout with csv header;
\echo ===roster===
copy (
  with mm as (
    select m."Id", m."PlatformId" from matches m
    where m."QueueId" = 420 and m."Patch" = :'test_patch' and m."GameStartTimeUtc" >= :'since'
      and m."GameDurationSeconds" >= 300
    order by m."GameStartTimeUtc" desc limit 20000
  )
  select p."MatchId", p."ChampionId", p."TeamId", p."TeamPosition", p."Win",
         exists (select 1 from main_champion_stats st
                 where st."PlatformId" = mm."PlatformId" and st."Puuid" = p."Puuid"
                   and st."ChampionId" = p."ChampionId" and st."IsMain") as "IsMain"
  from mm join match_participants p on p."MatchId" = mm."Id"
) to stdout with csv header;
