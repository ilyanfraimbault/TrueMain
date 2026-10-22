/**
 * The site's per-game performance score (#918), ported from
 * `backend/Core/Lol/Performance/PerformanceScore.cs` so the dashboard grades
 * the games only the player's client knows (#2075). A port, not a variant: the
 * shared vectors (`web/shared/fixtures/performance-score-vectors.json`) are run
 * by both test suites, so the two cannot drift apart. The model is documented
 * on the C# side; the comments here only say where the port keeps its rules.
 *
 * Eight components graded 0..1, folded into a weighted average rescaled to
 * 0..100 by a per-role weight profile. A component whose input is missing is
 * dropped and its weight redistributed — never scored as zero.
 */

/** One canonical mark's lead over the lane opponent (`LaneLead`). */
export interface LaneLead {
  minute: number
  goldDiff: number
  csDiff: number
  xpDiff: number
}

/** Everything one participant is graded on (`PerformanceScoreInput`). */
export interface PerformanceScoreInput {
  /** `TOP` … `UTILITY`; anything else grades on the neutral profile. */
  teamPosition: string
  kills: number
  deaths: number
  assists: number
  /** 0 drops kill participation. */
  teamKills: number
  damageToChampions: number
  /** 0 drops the damage share. */
  teamDamageToChampions: number
  goldEarned: number
  /** 0 drops the gold share. */
  teamGoldEarned: number
  /** Lane minions and jungle camps together. */
  cs: number
  visionScore: number
  /** 0 or less drops every per-minute component. */
  gameDurationMinutes: number
  laneLeads: LaneLead[]
}

/** Marks up to this minute are the laning phase, later ones the mid game. */
export const LANING_PHASE_LAST_MINUTE = 15

const GOLD_SPAN_PER_MINUTE = 100
const CS_SPAN_PER_MINUTE = 2
const XP_SPAN_PER_MINUTE = 100
const KDA_FULL_MARKS = 6
const DAMAGE_SHARE: Band = [0.05, 0.35]
const GOLD_SHARE: Band = [0.10, 0.30]
const LEAD_GOLD_WEIGHT = 0.5
const LEAD_CS_WEIGHT = 0.25
const LEAD_XP_WEIGHT = 0.25

type Band = [floor: number, ceiling: number]

/** Combat, KP, damage, gold, farm, vision, laning, mid game | cs/min ref, vision/min ref. */
type RoleProfile = [number, number, number, number, number, number, number, number, number, number]

const PROFILES: Record<string, RoleProfile> = {
  TOP: [20, 19, 16, 7, 14, 5, 12, 7, 9.0, 0.9],
  JUNGLE: [18, 18, 14, 7, 14, 7, 12, 10, 6.5, 1.2],
  MIDDLE: [20, 20, 18, 7, 14, 5, 10, 6, 9.0, 0.9],
  BOTTOM: [20, 15, 20, 7, 16, 4, 10, 8, 9.5, 0.8],
  UTILITY: [18, 28, 7, 4, 5, 24, 8, 6, 2.0, 2.4],
}
const NEUTRAL: RoleProfile = [20, 20, 16, 7, 12, 7, 10, 8, 8.0, 1.0]

/** Grades one participant on 0..100 (`PerformanceScore.Compute`). */
export function performanceScore(input: PerformanceScoreInput): number {
  const profile = PROFILES[input.teamPosition.trim().toUpperCase()] ?? NEUTRAL
  const minutes = input.gameDurationMinutes
  // In the C# order: the sums are taken in this order, so the float result is the same.
  const graded: [weight: number, value: number | null][] = [
    [profile[0], combat(input)],
    [profile[1], input.teamKills <= 0 ? null : clamp01((input.kills + input.assists) / input.teamKills)],
    [profile[2], share(input.damageToChampions, input.teamDamageToChampions, DAMAGE_SHARE)],
    [profile[3], share(input.goldEarned, input.teamGoldEarned, GOLD_SHARE)],
    [profile[4], perMinute(input.cs, minutes, profile[8])],
    [profile[5], perMinute(input.visionScore, minutes, profile[9])],
    [profile[6], leads(input.laneLeads, true)],
    [profile[7], leads(input.laneLeads, false)],
  ]

  let totalWeight = 0
  let weighted = 0
  for (const [weight, value] of graded) {
    if (value === null || weight <= 0) continue
    totalWeight += weight
    weighted += weight * value
  }
  // Math.round rounds a half up, which on a non-negative score is the C# AwayFromZero.
  return totalWeight <= 0 ? 0 : Math.round(100 * weighted / totalWeight)
}

function combat(input: PerformanceScoreInput): number {
  const kda = (input.kills + input.assists) / Math.max(1, input.deaths)
  return clamp01(kda / KDA_FULL_MARKS)
}

function share(value: number, teamTotal: number, [floor, ceiling]: Band): number | null {
  return teamTotal <= 0 ? null : clamp01((value / teamTotal - floor) / (ceiling - floor))
}

function perMinute(total: number, minutes: number, fullMarks: number): number | null {
  return minutes <= 0 || fullMarks <= 0 ? null : clamp01(total / minutes / fullMarks)
}

/** One phase's marks, each against a span proportional to its minute, averaged weighted by that minute. */
function leads(laneLeads: LaneLead[], laningPhase: boolean): number | null {
  let totalWeight = 0
  let weighted = 0
  for (const lead of laneLeads) {
    if (lead.minute <= 0 || (lead.minute <= LANING_PHASE_LAST_MINUTE) !== laningPhase) continue
    const value = (LEAD_GOLD_WEIGHT * centered(lead.goldDiff, GOLD_SPAN_PER_MINUTE * lead.minute))
      + (LEAD_CS_WEIGHT * centered(lead.csDiff, CS_SPAN_PER_MINUTE * lead.minute))
      + (LEAD_XP_WEIGHT * centered(lead.xpDiff, XP_SPAN_PER_MINUTE * lead.minute))
    totalWeight += lead.minute
    weighted += lead.minute * value
  }
  return totalWeight <= 0 ? null : weighted / totalWeight
}

function centered(diff: number, span: number): number {
  return clamp01(0.5 + (diff / (2 * span)))
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value))
}
