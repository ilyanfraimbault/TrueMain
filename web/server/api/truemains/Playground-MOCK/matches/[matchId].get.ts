import type { MatchDetailJungleClear, MatchDetailParticipant, MatchDetailResponse } from '~~/shared/types/match-detail'
import { createError, defineEventHandler, getRouterParam } from 'h3'

// Dev fixture backing the `/dev/match-row` playground so the MatchRow accordion
// can be exercised against a realistic 10-player detail payload without a live
// backend. Scoped to the `Playground-MOCK` name tag (same folder pattern as the
// Sheiden-1234 mocks) so it never shadows real `/api/truemains/**/matches/**`
// proxying. Dev-only — 404s in production.

interface RosterEntry {
  championId: number
  teamId: number
  position: string
  gameName: string
  kills: number
  deaths: number
  assists: number
}

const ROSTER: RosterEntry[] = [
  { championId: 157, teamId: 100, position: 'TOP', gameName: 'BlueTop', kills: 6, deaths: 4, assists: 7 },
  { championId: 64, teamId: 100, position: 'JUNGLE', gameName: 'BlueJng', kills: 8, deaths: 3, assists: 10 },
  { championId: 99, teamId: 100, position: 'MIDDLE', gameName: 'BlueMid', kills: 11, deaths: 2, assists: 8 },
  { championId: 222, teamId: 100, position: 'BOTTOM', gameName: 'BlueBot', kills: 13, deaths: 3, assists: 6 },
  { championId: 412, teamId: 100, position: 'UTILITY', gameName: 'BlueSup', kills: 1, deaths: 5, assists: 22 },
  { championId: 86, teamId: 200, position: 'TOP', gameName: 'RedTop', kills: 3, deaths: 6, assists: 4 },
  { championId: 121, teamId: 200, position: 'JUNGLE', gameName: 'RedJng', kills: 5, deaths: 7, assists: 6 },
  { championId: 103, teamId: 200, position: 'MIDDLE', gameName: 'RedMid', kills: 7, deaths: 8, assists: 5 },
  { championId: 51, teamId: 200, position: 'BOTTOM', gameName: 'RedBot', kills: 9, deaths: 6, assists: 3 },
  { championId: 117, teamId: 200, position: 'UTILITY', gameName: 'RedSup', kills: 0, deaths: 9, assists: 14 },
]

const TIERS = ['CHALLENGER', 'GRANDMASTER', 'MASTER', 'DIAMOND', 'EMERALD']

// A representative ADC-ish build/rune/skill template reused across participants
// — the playground only needs each tab to render meaningful, non-empty content.
const ITEM_TIMELINE = [1055, 3006, 6672, 3094, 3036, 3072]
const BUILD_EVENTS = ITEM_TIMELINE.map((itemId, i) => ({
  timestampMs: (2 + i * 6) * 60 * 1000,
  eventType: 'ITEM_PURCHASED',
  itemId,
  beforeId: null,
  afterId: null,
}))

// Q max first, R at 6/11/16 — a plausible level order across 18 points.
const SKILL_SLOTS = [1, 2, 3, 1, 1, 4, 1, 2, 1, 2, 4, 2, 2, 3, 3, 4, 3, 3]
const SKILL_EVENTS = SKILL_SLOTS.map((skillSlot, i) => ({
  timestampMs: (2 + i) * 60 * 1000,
  skillSlot,
}))

// First-clear fixtures (#1188), keyed by roster index. Modelled on real preprod
// numbers: buffs spawn at 1:30 so minute 1 is 0 jungle CS, a standard clear is
// ~12 CS by 2:00 and a full clear's worth by 3:00, and an interrupted clear
// stalls short of it.
const JUNGLE_CLEARS: Record<number, MatchDetailJungleClear> = {
  1: { // BlueJng — clean full clear, done by 3:00
    startCamp: 'BlueRedBuff',
    samples: [
      { timestampMs: 0, jungleCs: 0, x: 1500, y: 1200 },
      { timestampMs: 60_000, jungleCs: 0, x: 7770, y: 3800 },
      { timestampMs: 120_000, jungleCs: 12, x: 6900, y: 5500 },
      { timestampMs: 180_000, jungleCs: 21, x: 2150, y: 8420 },
      { timestampMs: 240_000, jungleCs: 24, x: 4400, y: 9700 },
    ],
    fullClearTimeMs: 180_000,
    fullClearJungleCs: 20,
  },
  6: { // RedJng — invaded early, never completes the clear in the window
    startCamp: 'RedBlueBuff',
    samples: [
      { timestampMs: 60_000, jungleCs: 0, x: 10930, y: 7060 },
      { timestampMs: 120_000, jungleCs: 7, x: 11100, y: 8480 },
      { timestampMs: 180_000, jungleCs: 7, x: 7850, y: 9480 },
      { timestampMs: 240_000, jungleCs: 14, x: 6980, y: 11180 },
    ],
    fullClearTimeMs: null,
    fullClearJungleCs: 20,
  },
}

const RUNES = [
  { styleId: 8000, selectionIndex: 0, perkId: 8005 }, // Press the Attack
  { styleId: 8000, selectionIndex: 1, perkId: 9111 }, // Triumph
  { styleId: 8000, selectionIndex: 2, perkId: 9105 }, // Legend: Alacrity
  { styleId: 8000, selectionIndex: 3, perkId: 8014 }, // Coup de Grace
  { styleId: 8100, selectionIndex: 0, perkId: 8135 }, // Treasure Hunter
  { styleId: 8100, selectionIndex: 1, perkId: 8106 }, // Ultimate Hunter
]

function buildParticipant(entry: RosterEntry, index: number, durationSeconds: number): MatchDetailParticipant {
  const minutes = durationSeconds / 60
  const cs = 140 + index * 9
  const win = entry.teamId === 100
  const damage = 12000 + entry.kills * 1800 + entry.assists * 250

  return {
    participantId: index + 1,
    championId: entry.championId,
    champLevel: 16 + (index % 3),
    summonerName: entry.gameName,
    gameName: entry.gameName,
    tagLine: 'EUW',
    teamId: entry.teamId,
    teamPosition: entry.position,
    win,
    kills: entry.kills,
    deaths: entry.deaths,
    assists: entry.assists,
    items: [...ITEM_TIMELINE, 0],
    trinketItemId: 3340,
    summoner1Id: 4,
    summoner2Id: entry.position === 'JUNGLE' ? 11 : 7,
    primaryStyleId: 8000,
    subStyleId: 8100,
    keystoneId: 8005,
    totalDamageDealtToChampions: damage,
    visionScore: 18 + index * 3,
    goldEarned: 11000 + index * 700,
    cs,
    rank: { tier: TIERS[index % TIERS.length]!, division: 'I', leaguePoints: 120 + index * 7 },
    killParticipation: 0.4 + (index % 5) * 0.1,
    csPerMin: cs / minutes,
    damagePerMin: damage / minutes,
    goldPerMin: (11000 + index * 700) / minutes,
    visionPerMin: (18 + index * 3) / minutes,
    // Filled in by rankFixture() once the whole roster exists — the score is a
    // match-relative metric, so it can't be decided one participant at a time.
    performanceScore: 0,
    placement: 0,
    isMvp: false,
    isAce: false,
    laning15: { csDiff: (index % 3 === 0 ? 12 : -8) + index, goldDiff: win ? 320 : -210, xpDiff: win ? 180 : -140 },
    firstToLevelTwo: win ? index % 2 === 0 : null,
    runes: RUNES,
    statPerkOffense: 5005,
    statPerkFlex: 5008,
    statPerkDefense: 5001,
    itemEvents: BUILD_EVENTS,
    skillEvents: SKILL_EVENTS,
    jungleClear: JUNGLE_CLEARS[index] ?? null,
  }
}

// Fixture stand-in for the API's real performance score — the authoritative
// model lives in backend/Core/Lol/Performance/PerformanceScore.cs and is not
// reproduced here. The playground only needs an internally consistent payload:
// a plausible 0–100 spread, strictly distinct placements, and exactly one MVP
// on the winning side / one ACE on the losing side.
function rankFixture(participants: MatchDetailParticipant[]): MatchDetailParticipant[] {
  const scored = participants.map(p => ({
    p,
    score: Math.max(0, Math.min(100, Math.round(
      30 + 8 * ((p.kills + p.assists) / Math.max(1, p.deaths)) + 25 * p.killParticipation,
    ))),
  }))

  // Same tiebreak chain as MatchPerformanceRanker: score, takedowns, deaths, id.
  const order = [...scored].sort((a, b) =>
    b.score - a.score
    || (b.p.kills + b.p.assists) - (a.p.kills + a.p.assists)
    || a.p.deaths - b.p.deaths
    || a.p.participantId - b.p.participantId)

  const mvpId = order.find(e => e.p.win)?.p.participantId
  const aceId = order.find(e => !e.p.win)?.p.participantId
  const placementById = new Map(order.map((e, i) => [e.p.participantId, i + 1]))

  return scored.map(({ p, score }) => ({
    ...p,
    performanceScore: score,
    placement: placementById.get(p.participantId) ?? 0,
    isMvp: p.participantId === mvpId,
    isAce: p.participantId === aceId,
  }))
}

export default defineEventHandler((event): MatchDetailResponse => {
  if (!import.meta.dev) {
    throw createError({ statusCode: 404, statusMessage: 'Not Found' })
  }

  const matchId = getRouterParam(event, 'matchId') ?? 'PLAYGROUND_MOCK'
  const gameDurationSeconds = 1876

  return {
    matchId,
    queueId: 420,
    gameMode: 'CLASSIC',
    gameStartTimeUtc: new Date(Date.now() - 60 * 60 * 1000).toISOString(),
    gameDurationSeconds,
    gameVersion: '15.1.1',
    participants: rankFixture(
      ROSTER.map((entry, i) => buildParticipant(entry, i, gameDurationSeconds)),
    ),
  }
})
