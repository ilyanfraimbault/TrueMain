//! One game opened in full, for the match row's accordion: the scoreboard of
//! all ten, their rune pages, and — from the game's timeline — each player's
//! build order, skill order and lane standing at fifteen minutes.
//!
//! Serialised in the shape of the site's match-detail payload
//! (`web/shared/types/match-detail.ts`), so the app draws it with the site's
//! own detail panel. Only what the client measured is filled in: TrueMain's
//! performance score, placement and accolades do not exist for these games and
//! stay at zero, and a player's rank at the time is not in the client's
//! history.

use std::collections::HashMap;

use serde::{Deserialize, Serialize};

use crate::record::{HistoryGame, HistoryParticipant};

/// `GET /lol-match-history/v1/game-timelines/{gameId}`: one frame a minute.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct GameTimeline {
    pub frames: Vec<TimelineFrame>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct TimelineFrame {
    pub timestamp: i64,
    /// Keyed by participant id, as a string.
    pub participant_frames: HashMap<String, ParticipantFrame>,
    pub events: Vec<TimelineEvent>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ParticipantFrame {
    pub total_gold: i64,
    pub xp: i64,
    pub minions_killed: i64,
    pub jungle_minions_killed: i64,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct TimelineEvent {
    #[serde(rename = "type")]
    pub kind: String,
    pub timestamp: i64,
    pub participant_id: i64,
    pub item_id: i64,
    pub before_id: i64,
    pub after_id: i64,
    pub skill_slot: i64,
    /// `CHAMPION_KILL` only. Zero when no champion landed the kill — a tower,
    /// minions or a monster executed the victim.
    pub killer_id: i64,
    pub victim_id: i64,
    pub assisting_participant_ids: Vec<i64>,
}

/// The minute the lane standing is read at, as on the site.
const LANING_MINUTE: i64 = 15;

const ITEM_EVENTS: [&str; 4] = ["ITEM_PURCHASED", "ITEM_SOLD", "ITEM_DESTROYED", "ITEM_UNDO"];

#[derive(Debug, Clone, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GameDetail {
    pub match_id: String,
    pub queue_id: i64,
    pub game_mode: String,
    pub game_start_time_utc: String,
    pub game_duration_seconds: i64,
    pub game_version: String,
    pub participants: Vec<DetailParticipant>,
}

#[derive(Debug, Clone, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DetailParticipant {
    pub participant_id: i64,
    pub champion_id: i64,
    pub champ_level: i64,
    pub summoner_name: String,
    pub game_name: Option<String>,
    pub tag_line: Option<String>,
    pub team_id: i64,
    pub team_position: String,
    pub win: bool,
    pub kills: i64,
    pub deaths: i64,
    pub assists: i64,
    pub items: Vec<i64>,
    pub trinket_item_id: i64,
    pub role_bound_item_id: i64,
    pub summoner1_id: i64,
    pub summoner2_id: i64,
    pub primary_style_id: i64,
    pub sub_style_id: i64,
    pub keystone_id: i64,
    pub total_damage_dealt_to_champions: i64,
    pub vision_score: i64,
    pub gold_earned: i64,
    pub cs: i64,
    /// Never in the client's history.
    pub rank: Option<()>,
    pub kill_participation: f64,
    pub cs_per_min: f64,
    pub damage_per_min: f64,
    pub gold_per_min: f64,
    pub vision_per_min: f64,
    /// TrueMain never scored these games: zero, and the app does not show it.
    pub performance_score: i64,
    pub placement: i64,
    pub is_mvp: bool,
    pub is_ace: bool,
    pub laning15: Option<Laning15>,
    pub first_to_level_two: Option<bool>,
    pub runes: Vec<DetailRune>,
    pub stat_perk_offense: i64,
    pub stat_perk_flex: i64,
    pub stat_perk_defense: i64,
    pub item_events: Vec<DetailItemEvent>,
    pub skill_events: Vec<DetailSkillEvent>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Laning15 {
    pub cs_diff: i64,
    pub gold_diff: i64,
    pub xp_diff: i64,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DetailRune {
    pub style_id: i64,
    pub selection_index: i64,
    pub perk_id: i64,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DetailItemEvent {
    pub timestamp_ms: i64,
    pub event_type: String,
    pub item_id: i64,
    pub before_id: Option<i64>,
    pub after_id: Option<i64>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DetailSkillEvent {
    pub timestamp_ms: i64,
    pub skill_slot: i64,
}

impl GameDetail {
    /// Build the detail from a full scoreboard and, when it could be read, the
    /// game's timeline. Without the timeline the build order, skill order and
    /// lane standing are simply empty.
    pub fn from_game(game: &HistoryGame, timeline: Option<&GameTimeline>) -> Self {
        let minutes = game.duration_seconds().max(1) as f64 / 60.0;
        let team_kills = |team: i64| -> i64 {
            game.participants
                .iter()
                .filter(|p| p.team_id == team)
                .map(|p| p.stats.kills)
                .sum()
        };
        let events = timeline.map(events_by_participant).unwrap_or_default();
        let lane_frames = timeline.and_then(frame_at_laning_minute);

        let participants = game
            .participants
            .iter()
            .map(|p| {
                let stats = &p.stats;
                let player = game
                    .participant_identities
                    .iter()
                    .find(|identity| identity.participant_id == p.participant_id)
                    .map(|identity| &identity.player);
                let non_empty = |value: &str| Some(value.to_string()).filter(|v| !v.is_empty());
                let cs = stats.total_minions_killed + stats.neutral_minions_killed;
                let team = team_kills(p.team_id);
                let opponent = opponent_of(game, p);
                let (item_events, skill_events) =
                    events.get(&p.participant_id).cloned().unwrap_or_default();

                DetailParticipant {
                    participant_id: p.participant_id,
                    champion_id: p.champion_id,
                    champ_level: stats.champ_level,
                    summoner_name: player
                        .map(|player| player.summoner_name.clone())
                        .unwrap_or_default(),
                    game_name: player.and_then(|player| non_empty(&player.game_name)),
                    tag_line: player.and_then(|player| non_empty(&player.tag_line)),
                    team_id: p.team_id,
                    team_position: p.timeline.position().unwrap_or_default().to_string(),
                    win: stats.win,
                    kills: stats.kills,
                    deaths: stats.deaths,
                    assists: stats.assists,
                    items: vec![
                        stats.item0,
                        stats.item1,
                        stats.item2,
                        stats.item3,
                        stats.item4,
                        stats.item5,
                    ],
                    trinket_item_id: stats.item6,
                    role_bound_item_id: stats.role_bound_item,
                    summoner1_id: p.spell1_id,
                    summoner2_id: p.spell2_id,
                    primary_style_id: stats.perk_primary_style,
                    sub_style_id: stats.perk_sub_style,
                    keystone_id: stats.perk0,
                    total_damage_dealt_to_champions: stats.total_damage_dealt_to_champions,
                    vision_score: stats.vision_score,
                    gold_earned: stats.gold_earned,
                    cs,
                    rank: None,
                    kill_participation: if team > 0 {
                        (stats.kills + stats.assists) as f64 / team as f64
                    } else {
                        0.0
                    },
                    cs_per_min: cs as f64 / minutes,
                    damage_per_min: stats.total_damage_dealt_to_champions as f64 / minutes,
                    gold_per_min: stats.gold_earned as f64 / minutes,
                    vision_per_min: stats.vision_score as f64 / minutes,
                    performance_score: 0,
                    placement: 0,
                    is_mvp: false,
                    is_ace: false,
                    laning15: opponent.zip(lane_frames).and_then(|(foe, frame)| {
                        laning(frame, p.participant_id, foe.participant_id)
                    }),
                    first_to_level_two: opponent.and_then(|foe| {
                        let mine = level_two_at(&skill_events)?;
                        let theirs = level_two_at(&events.get(&foe.participant_id)?.1)?;
                        Some(mine < theirs)
                    }),
                    runes: runes_of(p),
                    stat_perk_offense: stats.stat_perk0,
                    stat_perk_flex: stats.stat_perk1,
                    stat_perk_defense: stats.stat_perk2,
                    item_events,
                    skill_events,
                }
            })
            .collect();

        Self {
            match_id: game.game_id.to_string(),
            queue_id: game.queue_id,
            game_mode: game.game_mode.clone(),
            game_start_time_utc: iso_utc(game.game_creation),
            game_duration_seconds: game.duration_seconds(),
            game_version: game.game_version.clone(),
            participants,
        }
    }
}

/// The participant on the other side holding the same lane, when lanes exist.
fn opponent_of<'a>(
    game: &'a HistoryGame,
    me: &HistoryParticipant,
) -> Option<&'a HistoryParticipant> {
    let lane = me.timeline.position()?;
    game.participants
        .iter()
        .find(|p| p.team_id != me.team_id && p.timeline.position() == Some(lane))
}

fn runes_of(p: &HistoryParticipant) -> Vec<DetailRune> {
    let stats = &p.stats;
    let primary = [stats.perk0, stats.perk1, stats.perk2, stats.perk3];
    let secondary = [stats.perk4, stats.perk5];
    let rune = |style_id: i64| {
        move |(index, perk_id): (usize, &i64)| DetailRune {
            style_id,
            selection_index: index as i64,
            perk_id: *perk_id,
        }
    };
    primary
        .iter()
        .enumerate()
        .map(rune(stats.perk_primary_style))
        .chain(secondary.iter().enumerate().map(rune(stats.perk_sub_style)))
        .filter(|rune| rune.perk_id > 0)
        .collect()
}

type ParticipantEvents = (Vec<DetailItemEvent>, Vec<DetailSkillEvent>);

fn events_by_participant(timeline: &GameTimeline) -> HashMap<i64, ParticipantEvents> {
    let mut by_participant: HashMap<i64, ParticipantEvents> = HashMap::new();
    for event in timeline.frames.iter().flat_map(|frame| &frame.events) {
        let entry = by_participant.entry(event.participant_id).or_default();
        if ITEM_EVENTS.contains(&event.kind.as_str()) {
            entry.0.push(DetailItemEvent {
                timestamp_ms: event.timestamp,
                event_type: event.kind.clone(),
                item_id: event.item_id,
                before_id: Some(event.before_id).filter(|id| *id > 0),
                after_id: Some(event.after_id).filter(|id| *id > 0),
            });
        } else if event.kind == "SKILL_LEVEL_UP" && event.skill_slot > 0 {
            entry.1.push(DetailSkillEvent {
                timestamp_ms: event.timestamp,
                skill_slot: event.skill_slot,
            });
        }
    }
    for (items, skills) in by_participant.values_mut() {
        items.sort_by_key(|event| event.timestamp_ms);
        skills.sort_by_key(|event| event.timestamp_ms);
    }
    by_participant
}

/// The first frame at or past the laning minute; `None` for a game over by then.
fn frame_at_laning_minute(timeline: &GameTimeline) -> Option<&TimelineFrame> {
    timeline
        .frames
        .iter()
        .find(|frame| frame.timestamp >= LANING_MINUTE * 60_000)
}

fn laning(frame: &TimelineFrame, me: i64, foe: i64) -> Option<Laning15> {
    let mine = frame.participant_frames.get(&me.to_string())?;
    let theirs = frame.participant_frames.get(&foe.to_string())?;
    let cs = |f: &ParticipantFrame| f.minions_killed + f.jungle_minions_killed;
    Some(Laning15 {
        cs_diff: cs(mine) - cs(theirs),
        gold_diff: mine.total_gold - theirs.total_gold,
        xp_diff: mine.xp - theirs.xp,
    })
}

/// The second skill point is level two, as the site reads it.
fn level_two_at(skills: &[DetailSkillEvent]) -> Option<i64> {
    skills.get(1).map(|event| event.timestamp_ms)
}

/// Epoch milliseconds as an ISO-8601 UTC timestamp, without a date crate.
fn iso_utc(epoch_ms: i64) -> String {
    let seconds = epoch_ms.div_euclid(1000);
    let millis = epoch_ms.rem_euclid(1000);
    let days = seconds.div_euclid(86_400);
    let rest = seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    format!(
        "{year:04}-{month:02}-{day:02}T{:02}:{:02}:{:02}.{millis:03}Z",
        rest / 3600,
        (rest % 3600) / 60,
        rest % 60
    )
}

/// Days since 1970-01-01 to a proleptic Gregorian date (Howard Hinnant's algorithm).
fn civil_from_days(days: i64) -> (i64, i64, i64) {
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = yoe + era * 400 + i64::from(month <= 2);
    (year, month, day)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn game() -> HistoryGame {
        serde_json::from_str(
            r#"{ "gameId": 42, "gameCreation": 1790710000000, "gameDuration": 1800, "queueId": 420,
              "gameMode": "CLASSIC", "gameVersion": "16.19.1",
              "participantIdentities": [
                { "participantId": 1, "player": { "gameName": "Me", "tagLine": "TAG", "summonerName": "Me" } },
                { "participantId": 6, "player": { "gameName": "Foe", "tagLine": "EUW", "summonerName": "Foe" } }
              ],
              "participants": [
                { "participantId": 1, "teamId": 100, "championId": 103, "timeline": { "lane": "MIDDLE", "role": "SOLO" },
                  "stats": { "win": true, "kills": 6, "assists": 4, "totalMinionsKilled": 200, "neutralMinionsKilled": 10,
                    "perk0": 8112, "perk1": 8139, "perk2": 8138, "perk3": 8135, "perk4": 8226, "perk5": 8210,
                    "perkPrimaryStyle": 8100, "perkSubStyle": 8200, "statPerk0": 5008, "statPerk1": 5008, "statPerk2": 5001 } },
                { "participantId": 2, "teamId": 100, "championId": 64, "stats": { "kills": 4 } },
                { "participantId": 4, "teamId": 100, "championId": 222, "timeline": { "lane": "BOTTOM", "role": "CARRY" },
                  "stats": { "item0": 3031, "item6": 3363, "roleBoundItem": 3006 } },
                { "participantId": 6, "teamId": 200, "championId": 7, "timeline": { "lane": "MIDDLE", "role": "SOLO" },
                  "stats": { "kills": 3 } }
              ] }"#,
        )
        .unwrap()
    }

    fn timeline() -> GameTimeline {
        serde_json::from_str(
            r#"{ "frames": [
              { "timestamp": 0, "participantFrames": {}, "events": [
                { "type": "ITEM_PURCHASED", "timestamp": 1200, "participantId": 1, "itemId": 1056 },
                { "type": "SKILL_LEVEL_UP", "timestamp": 1500, "participantId": 1, "skillSlot": 1 },
                { "type": "SKILL_LEVEL_UP", "timestamp": 1600, "participantId": 6, "skillSlot": 3 }
              ] },
              { "timestamp": 90000, "participantFrames": {}, "events": [
                { "type": "SKILL_LEVEL_UP", "timestamp": 95000, "participantId": 1, "skillSlot": 2 },
                { "type": "SKILL_LEVEL_UP", "timestamp": 99000, "participantId": 6, "skillSlot": 1 },
                { "type": "ITEM_UNDO", "timestamp": 99500, "participantId": 1, "beforeId": 1001, "afterId": 0 }
              ] },
              { "timestamp": 900100, "events": [], "participantFrames": {
                "1": { "totalGold": 6000, "xp": 7000, "minionsKilled": 120, "jungleMinionsKilled": 4 },
                "6": { "totalGold": 5200, "xp": 6600, "minionsKilled": 110, "jungleMinionsKilled": 0 }
              } }
            ] }"#,
        )
        .unwrap()
    }

    #[test]
    fn reads_the_scoreboard_in_the_sites_shape() {
        let detail = GameDetail::from_game(&game(), Some(&timeline()));
        assert_eq!(detail.match_id, "42");
        assert_eq!(detail.game_start_time_utc, "2026-09-29T19:26:40.000Z");
        let me = &detail.participants[0];
        assert_eq!(me.game_name.as_deref(), Some("Me"));
        assert_eq!(me.team_position, "MIDDLE");
        assert_eq!(me.cs, 210);
        assert!((me.kill_participation - 1.0).abs() < 1e-9);
        assert_eq!(me.runes.len(), 6);
        assert_eq!(me.runes[4].style_id, 8200);
        assert_eq!(me.runes[4].selection_index, 0);
        let adc = &detail.participants[2];
        assert_eq!(adc.items[0], 3031);
        assert_eq!(adc.trinket_item_id, 3363);
        assert_eq!(adc.role_bound_item_id, 3006);
    }

    #[test]
    fn the_timeline_gives_the_build_the_skills_and_the_lane_at_fifteen() {
        let detail = GameDetail::from_game(&game(), Some(&timeline()));
        let me = &detail.participants[0];
        assert_eq!(me.item_events.len(), 2);
        assert_eq!(me.item_events[1].before_id, Some(1001));
        assert_eq!(me.item_events[1].after_id, None);
        assert_eq!(me.skill_events.len(), 2);
        assert_eq!(
            me.laning15,
            Some(Laning15 {
                cs_diff: 14,
                gold_diff: 800,
                xp_diff: 400
            })
        );
        assert_eq!(me.first_to_level_two, Some(true));
        assert_eq!(detail.participants[1].laning15, None);
    }

    #[test]
    fn without_a_timeline_the_rest_still_reads() {
        let detail = GameDetail::from_game(&game(), None);
        assert!(detail.participants[0].item_events.is_empty());
        assert_eq!(detail.participants[0].laning15, None);
        assert_eq!(detail.participants[0].first_to_level_two, None);
    }
}
