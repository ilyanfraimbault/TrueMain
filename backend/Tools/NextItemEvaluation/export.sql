-- Inputs of the next-item evaluation (#1749), as one psql session writing to stdout.
-- Split the output on the ===NAME=== lines into NAME.csv. Every statement is bounded:
-- the test games are the cohort games of the last day of the test patch, reached
-- through the match index, never a scan of a champion's whole history.
--   psql -v test_patch=16.19 -v since=2026-09-30 -v train="'16.16','16.17','16.18'" < export.sql
set statement_timeout = '120s';
create temp table s as
  select "ChampionId", "Position" from champion_item_context_totals
  where "Patch" = :'test_patch' and "Axis" = 'Overall' and "Slot" = 'Build' and "ParentItemId" = 0
  order by "Games" desc limit 30;
\echo ===scopes===
\copy (select * from s) to stdout with csv header
\echo ===stats===
\copy (select t.* from champion_item_context_stats t join s using ("ChampionId", "Position") where t."Patch" in (:train) and t."Slot" in ('Build', 'Boots')) to stdout with csv header
\echo ===totals===
\copy (select t.* from champion_item_context_totals t join s using ("ChampionId", "Position") where t."Patch" in (:train) and t."Slot" in ('Build', 'Boots')) to stdout with csv header
\echo ===profiles===
\copy (select * from champion_profile_stats) to stdout with csv header
create temp table mm as
  select m."Id", m."PlatformId", m."GameStartTimeUtc", m."GameVersion" from matches m
  where m."QueueId" = 420 and m."Patch" = :'test_patch' and m."GameStartTimeUtc" >= :'since'
    and m."TimelineIngested" and m."GameDurationSeconds" >= 300;
create temp table k as select * from (
  select p."MatchId", p."ParticipantId",
         row_number() over (partition by p."ChampionId", p."TeamPosition" order by mm."GameStartTimeUtc" desc) rn
  from mm
  join match_participants p on p."MatchId" = mm."Id"
  join s on s."ChampionId" = p."ChampionId" and s."Position" = p."TeamPosition"
  join main_champion_stats st on st."PlatformId" = mm."PlatformId" and st."Puuid" = p."Puuid"
    and st."ChampionId" = p."ChampionId" and st."IsMain"
) x where rn <= 500;
\echo ===tp===
\copy (select p."MatchId", p."ParticipantId", p."ChampionId", p."TeamPosition", p."TeamId", p."Win", p."Item0", p."Item1", p."Item2", p."Item3", p."Item4", p."Item5", p."RoleBoundItemId", mm."GameVersion", p."ItemEvents" from k join match_participants p using ("MatchId", "ParticipantId") join mm on mm."Id" = p."MatchId" where p."RiotAccountId" is not null) to stdout with csv header
\echo ===roster===
\copy (select p."MatchId", p."ParticipantId", p."ChampionId", p."TeamId", p."TeamPosition", p."Win" from match_participants p where p."MatchId" in (select distinct "MatchId" from k)) to stdout with csv header
\echo ===leads===
\copy (select sn."MatchId", sn."ParticipantId", sn."TotalGold" from match_participant_timeline_snapshots sn where sn."IntervalMinute" = 15 and sn."MatchId" in (select distinct "MatchId" from k)) to stdout with csv header
