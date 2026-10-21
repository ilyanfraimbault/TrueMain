<script setup lang="ts">
import type { ApexCutoff } from '~~/shared/types/apex-cutoffs'
import type { ProfileRanked } from '~~/shared/types/profile'
import type { RankHistoryEntry } from '~~/shared/types/rank-history'

useSeoMeta({
  title: 'Rank chart playground',
  description: 'The ranked card LP curve against mock histories and apex cutoffs.',
})

// Deterministic mock histories for the ranked card's LP curve, each built
// from a score per day and the day's mock Grandmaster / Challenger cutoffs.
// The tier of every snapshot is derived from those cutoffs, as Riot would.
const MASTER_FLOOR = 2800
const DIVISIONS = ['IV', 'III', 'II', 'I']
const TIERS = ['IRON', 'BRONZE', 'SILVER', 'GOLD', 'PLATINUM', 'EMERALD', 'DIAMOND']
const END = Date.parse('2026-09-28T12:00:00Z')
const DAY = 24 * 60 * 60 * 1000

interface Cutoffs { gm: number, ch: number }

function entryOf(at: number, score: number, cutoffs: Cutoffs): RankHistoryEntry {
  const capturedAtUtc = new Date(at).toISOString()
  const rounded = Math.round(score)
  if (rounded >= MASTER_FLOOR) {
    const lp = rounded - MASTER_FLOOR
    let tier = 'MASTER'
    if (lp >= cutoffs.ch) tier = 'CHALLENGER'
    else if (lp >= cutoffs.gm) tier = 'GRANDMASTER'
    return { capturedAtUtc, tier, division: 'I', leaguePoints: lp }
  }
  const clamped = Math.max(0, rounded)
  return {
    capturedAtUtc,
    tier: TIERS[Math.floor(clamped / 400)]!,
    division: DIVISIONS[Math.floor((clamped % 400) / 100)]!,
    leaguePoints: clamped % 100,
  }
}

// A smooth, repeatable wobble — no Math.random, so every reload draws the same.
const wobble = (i: number, amplitude: number) => amplitude * (Math.sin(i * 1.7) + 0.6 * Math.sin(i * 0.53 + 1))

interface Scenario {
  id: string
  title: string
  note: string
  history: RankHistoryEntry[]
  cutoffs: ApexCutoff[]
  ranked: ProfileRanked
}

function scenario(
  id: string,
  title: string,
  note: string,
  days: number,
  scoreAt: (t: number, i: number) => number,
  cutoffsAt: (t: number, i: number) => Cutoffs,
  measuredFrom = 0,
): Scenario {
  const history: RankHistoryEntry[] = []
  const cutoffs: ApexCutoff[] = []
  for (let i = 0; i < days; i++) {
    const t = days === 1 ? 1 : i / (days - 1)
    const at = END - (days - 1 - i) * DAY
    const day = cutoffsAt(t, i)
    history.push(entryOf(at, scoreAt(t, i), day))
    if (t >= measuredFrom) {
      cutoffs.push({
        dateUtc: new Date(at).toISOString().slice(0, 10),
        grandmasterMinLp: Math.round(day.gm),
        challengerMinLp: Math.round(day.ch),
      })
    }
  }
  const last = history.at(-1)!
  return {
    id,
    title,
    note,
    history,
    cutoffs,
    ranked: { tier: last.tier, division: last.division, leaguePoints: last.leaguePoints, wins: 407, losses: 323, winRate: 407 / 730 },
  }
}

const euwCutoffs = (t: number, i: number): Cutoffs => ({ gm: 410 - 40 * t + wobble(i, 6), ch: 880 - 60 * t + wobble(i + 3, 10) })

const scenarios: Scenario[] = [
  scenario(
    'decline',
    'Grandmaster → Diamond',
    'The screenshot\'s history: a slide from 530 LP Grandmaster down to Diamond I.',
    58,
    (t, i) => 3330 - 855 * t ** 0.9 + wobble(i, 8),
    euwCutoffs,
  ),
  scenario(
    'climb',
    'Master → Challenger',
    'A climb through a rising Grandmaster line, then past the Challenger one.',
    60,
    (t, i) => 2860 + 1000 * t + wobble(i, 18),
    (t, i) => ({ gm: 300 + 120 * t + wobble(i, 8), ch: 820 + 60 * t + wobble(i + 2, 12) }),
  ),
  scenario(
    'hover',
    'Master under the GM line',
    'Hovering around a moving Grandmaster cutoff: in and out of the zone.',
    30,
    (t, i) => 2800 + 385 + wobble(i, 12),
    (t, i) => ({ gm: 380 + 30 * Math.sin(t * 6) + wobble(i, 4), ch: 860 }),
  ),
  scenario(
    'rising-gm',
    'Challenger over a rising GM line',
    'The Grandmaster cutoff climbs into view: Master\'s crest still reads under Grandmaster\'s, or is left out.',
    60,
    (t, i) => 2800 + 1100 + 500 * Math.cos(t * 6) + wobble(i, 20),
    (t, i) => ({ gm: 200 + 900 * t + wobble(i, 6), ch: 1000 + 300 * t + wobble(i + 2, 12) }),
  ),
  scenario(
    'partial',
    'Cutoffs measured recently',
    'Master → Grandmaster, cutoffs only known for the last 40%: before that, each snapshot takes the nearest cutoff for its band.',
    50,
    (t, i) => 2900 + 520 * t + wobble(i, 14),
    euwCutoffs,
    0.6,
  ),
  scenario(
    'emerald',
    'Emerald ↔ Diamond',
    'Below Master every threshold is fixed: the Diamond IV line at 2400.',
    40,
    (t, i) => 2400 + 160 * Math.sin(t * 9) + wobble(i, 20),
    euwCutoffs,
  ),
  scenario(
    'gold',
    'Gold → Emerald climb',
    'A long climb crossing three fixed tier lines.',
    70,
    (t, i) => 1300 + 800 * t + wobble(i, 25),
    euwCutoffs,
  ),
  scenario(
    'single',
    'Single snapshot',
    'One snapshot draws a level line.',
    1,
    () => 1540,
    euwCutoffs,
  ),
  scenario(
    'flat-apex',
    'Flat Master, no cutoffs',
    'An apex history with no measured cutoff: no GM / Challenger line at all.',
    20,
    () => 2950,
    euwCutoffs,
    2,
  ),
]
</script>

<template>
  <div class="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 md:p-6">
    <header class="flex flex-col gap-1">
      <p class="text-xs font-semibold uppercase tracking-wide text-muted">
        Dev playground
      </p>
      <h1 class="text-2xl font-semibold">
        Rank chart thresholds
      </h1>
      <p class="text-sm text-muted">
        The ranked card's LP curve, coloured by the tier band it crosses. Mock histories and mock apex cutoffs.
      </p>
    </header>

    <div class="grid grid-cols-[repeat(auto-fill,minmax(18rem,1fr))] gap-4">
      <div v-for="item in scenarios" :key="item.id" class="flex flex-col gap-2">
        <div>
          <h2 class="text-sm font-semibold">
            {{ item.title }}
          </h2>
          <p class="text-xs text-muted">
            {{ item.note }}
          </p>
        </div>
        <ProfileRankedCard :ranked="item.ranked" :history="item.history" :cutoffs="item.cutoffs" />
      </div>
    </div>
  </div>
</template>
