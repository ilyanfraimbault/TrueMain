// The post-game win-probability curve and its turning points (#1911).
// `MatchWinProbability` mirrors backend/Api/ReadModels/Truemains/MatchDetailReadModel.cs
// (computed at ingest by Core/Lol/WinProbability) and is what the desktop builds
// from the client's timeline with `buildWinProbability`
// (`web/layers/common/app/utils/win-probability-timeline.ts`).
// Every probability and delta reads for team 100 (blue side); the UI turns it
// to the viewer's side.

/** The curve and the moments that swung it, always read for team 100. */
export interface MatchWinProbability {
  /** Team 100's chance to win at each of the timeline's frames (one a minute) and at the end. */
  points: WinProbabilityPoint[]
  /** The events that moved the chance most, largest |delta| first. */
  swings: WinProbabilitySwing[]
  /** Every epic monster taken, those the model does not weigh included (`delta` null). */
  objectives: WinProbabilityObjective[]
}

export interface WinProbabilityPoint {
  /** Game time, milliseconds. */
  ms: number
  /** Team 100's chance to win, 0..1. */
  p: number
}

export type WinProbabilitySwingKind = 'kill' | 'turret' | 'inhibitor' | 'dragon' | 'elder' | 'baron'

export interface WinProbabilitySwing {
  ms: number
  kind: WinProbabilitySwingKind
  /** The side the event is for: the killer's, or the side that took the building or monster. */
  teamId: number
  /** Team 100's chance after the event minus before it, at the event's time, −1..1. */
  delta: number
  /** Kills: the killer's participant id, 0 when a turret, minions or a monster executed the victim. */
  killerId: number
  /** Kills: the victim's participant id; 0 otherwise. */
  victimId: number
  /** Kills: how many assisted. */
  assists: number
  /** Kills: the gold the kill gave (bounty plus shutdown), null when the timeline does not carry it. */
  bounty: number | null
  /** Buildings: `TOP_LANE` / `MID_LANE` / `BOT_LANE`; null otherwise. */
  lane: string | null
  /** Turrets: `OUTER_TURRET` / `INNER_TURRET` / `BASE_TURRET` / `NEXUS_TURRET`; null otherwise. */
  towerType: string | null
  /** Drakes: the kind (`FIRE_DRAGON`, …); null otherwise. */
  monsterSubType: string | null
}

export interface WinProbabilityObjective {
  ms: number
  /** `DRAGON`, `BARON_NASHOR`, `RIFTHERALD`, `HORDE` (Voidgrubs), `ATAKHAN`. */
  monsterType: string
  monsterSubType: string | null
  teamId: number
  /** As a swing's; null for a monster the model does not weigh — never shown as zero. */
  delta: number | null
}

/**
 * A game's timeline reduced to what the model reads — the shape the desktop
 * shell hands over from the client's timeline and the shared fixture holds.
 * Names follow Riot's match-v5 / LCU timeline.
 */
export interface WinProbabilityTimeline {
  durationMs: number
  participants: WinProbabilityTimelineParticipant[]
  frames: WinProbabilityTimelineFrame[]
  events: WinProbabilityTimelineEvent[]
}

export interface WinProbabilityTimelineParticipant {
  participantId: number
  /** 100 = blue side, 200 = red side. */
  teamId: number
  /** Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY); empty when unknown. */
  position: string
}

export interface WinProbabilityTimelineFrame {
  ms: number
  players: { participantId: number, cs: number, level: number }[]
}

export interface WinProbabilityTimelineEvent {
  /** `CHAMPION_KILL`, `BUILDING_KILL` or `ELITE_MONSTER_KILL`; anything else is ignored. */
  type: string
  ms: number
  killerId?: number
  victimId?: number
  assistIds?: number[]
  /** Epic monsters: the side that took it, when the timeline names it. */
  killerTeamId?: number
  /** Buildings: the side that *lost* it. */
  teamId?: number
  buildingType?: string
  laneType?: string
  towerType?: string
  monsterType?: string
  monsterSubType?: string
  /** Kills: bounty plus shutdown bounty, when the timeline carries them. */
  bounty?: number | null
}
