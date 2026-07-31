<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatGoldDiff, goldDiffTone, laneVerdict, LANE_VERDICT_MIN_GAMES } from '~/utils/lane-verdict'

/**
 * How the matchup itself goes, above the build it produces (#976). The rest of
 * the page answers "what do I build into this opponent"; this strip answers the
 * question that comes first — am I ahead in this lane, and by how much — from the
 * average gold gap at 15 minutes.
 *
 * Deliberately independent of the recommendation: it reads the matchup aggregate,
 * not the composition sample, so it still stands when the build degrades to the
 * champion's baseline (#921) and does not refetch when a context slot changes.
 */
const props = defineProps<{
  championId: number
  position: ChampionPosition
  opponentChampionId: number
  championName: string | null
  opponentName: string | null
}>()

// The jungle has no lane — the opponent is across the map, not across the wave —
// so every noun on this strip follows the position (#939).
const isJungle = computed(() => props.position === 'JUNGLE')
const noun = computed<'lane' | 'matchup'>(() => (isJungle.value ? 'matchup' : 'lane'))

// Global slice, every patch: the strip has no filters of its own, and the widest
// scope is also the one with enough judged lanes to band. The head-to-head itself
// stays live (floor of one game); the lane counters come from the aggregate.
const { data, status } = useChampionMatchups(
  () => props.championId,
  () => props.position,
  { opponentChampionId: () => props.opponentChampionId },
)

const entry = computed(() =>
  data.value?.matchups.find(m => m.opponentChampionId === props.opponentChampionId) ?? null)

const isLoading = computed(() => status.value === 'pending' && !data.value)

const goldDiff = computed(() => entry.value?.averageGoldDiffAt15 ?? null)
const goldLanes = computed(() => entry.value?.goldDiffLaneGames ?? 0)

const verdict = computed(() => laneVerdict(goldDiff.value, goldLanes.value, noun.value))

/**
 * What to say when there is no verdict — never silence, since the strip is only
 * mounted once an opponent is pinned and an empty box would read as "even".
 * The two cases are different facts: nothing measured yet, versus measured on a
 * sample too thin to band.
 */
const shortfall = computed(() => {
  if (verdict.value !== null) return null
  if (goldDiff.value === null) {
    return 'No game of this matchup has been measured at 15 minutes yet.'
  }
  // "games", not "lanes": one measured lane is one game, and the jungle has no
  // lane to count — the noun only varies where it says something ("hard matchup").
  return `Measured on ${goldLanes.value} game${goldLanes.value === 1 ? '' : 's'} — `
    + `too few to call the ${noun.value} (${LANE_VERDICT_MIN_GAMES} needed).`
})

const subject = computed(() => {
  const champion = props.championName ?? 'This champion'
  return props.opponentName ? `${champion} vs ${props.opponentName}` : champion
})

const stats = computed(() => [
  {
    label: 'Gold @15',
    value: goldDiff.value === null ? '—' : formatGoldDiff(goldDiff.value),
    tone: goldDiff.value === null ? 'text-dimmed' : goldDiffTone(goldDiff.value),
    caption: goldDiff.value === null
      ? 'not measured yet'
      : `avg over ${goldLanes.value.toLocaleString('en-US')} games`,
    hint: 'Average gold the champion holds over its opponent at 15 minutes, across every '
      + 'measured game of this matchup. The verdict bands this number.',
  },
  {
    // Lane win rate counts *decided* lanes only, so it rests on a different (and
    // larger) sample than the gap — the captions carry both rather than letting
    // one number look like it qualifies the other.
    label: isJungle.value ? 'Matchup ahead' : 'Lane win rate',
    value: entry.value?.laneWinRate == null ? '—' : formatPercentage(entry.value.laneWinRate, 0),
    tone: entry.value?.laneWinRate == null
      ? 'text-dimmed'
      : entry.value.laneWinRate >= 0.5 ? 'text-emerald-400' : 'text-red-400',
    caption: entry.value?.laneWinRate == null
      ? 'nothing decided yet'
      : `of ${entry.value.decidedLaneGames.toLocaleString('en-US')} decided`,
    hint: 'Share of games that ended 15 minutes clearly ahead, out of those that ended '
      + 'clearly ahead or behind. Lanes inside the threshold band count as neither.',
  },
  {
    label: 'Win rate',
    value: entry.value === null ? '—' : formatPercentage(entry.value.winRate, 0),
    tone: entry.value === null
      ? 'text-dimmed'
      : entry.value.winRate >= 0.5 ? 'text-emerald-400' : 'text-red-400',
    caption: entry.value === null
      ? 'no recorded game'
      : `of ${entry.value.games.toLocaleString('en-US')} games`,
    hint: 'Games won in this matchup — the whole game, not the lane. Winning lane and '
      + 'winning the game are different questions, which is why both are here.',
  },
])
</script>

<template>
  <USkeleton
    v-if="isLoading"
    class="h-24 w-full rounded-2xl"
  />

  <!-- No aggregate row at all: the matchup has never been recorded, and the page
       already says so above the build. Nothing to add here. -->
  <section
    v-else-if="entry"
    class="glass rounded-2xl p-4 sm:p-5"
    :aria-label="`${noun} verdict`"
  >
    <div class="flex flex-wrap items-center justify-between gap-x-6 gap-y-4">
      <div class="flex min-w-0 flex-col gap-1.5">
        <UBadge
          v-if="verdict"
          :color="verdict.color"
          :variant="verdict.variant"
          size="lg"
          class="w-fit font-semibold"
        >
          {{ verdict.label }}
        </UBadge>
        <p
          v-else
          class="text-sm font-medium text-muted"
        >
          {{ isJungle ? 'Matchup' : 'Lane' }} not called
        </p>
        <p class="text-xs text-dimmed">
          {{ shortfall ?? `${subject} — from the gold gap at 15 minutes.` }}
        </p>
      </div>

      <dl class="grid grow grid-cols-3 gap-4 sm:max-w-md">
        <div
          v-for="stat in stats"
          :key="stat.label"
          :title="stat.hint"
        >
          <dt class="text-xs text-muted">
            {{ stat.label }}
          </dt>
          <dd
            class="text-lg font-semibold leading-tight tabular-nums"
            :class="stat.tone"
          >
            {{ stat.value }}
          </dd>
          <dd class="text-xs text-dimmed">
            {{ stat.caption }}
          </dd>
        </div>
      </dl>
    </div>
  </section>
</template>
