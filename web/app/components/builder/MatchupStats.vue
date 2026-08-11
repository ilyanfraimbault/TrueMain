<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import type { LaneVerdict } from '~/utils/lane-verdict'
import type { RateBand } from '~/utils/rate-tone'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { isLoadingStatus } from '~/utils/async-data'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { formatGoldDiff, goldDiffBand, laneVerdict } from '~/utils/lane-verdict'
import { pickRateBand, winRateBand } from '~/utils/rate-tone'

/**
 * What the matchup itself measures (#1098): how often it happens, how it ends,
 * and how the lane goes — over every recorded game of the pair, not over the
 * sample the recommended build was computed from.
 *
 * It lives on the page rather than inside `RecommendationPanel` because those
 * are two different populations, and conflating them cost the page its numbers:
 * the recommendation is a live query over the retention window, so a matchup
 * with nothing left in it falls back to the champion's standard build and used
 * to take every figure down with it — precisely when the reader most needs to
 * know how thin the ground is. This strip reads `champion_matchup_stats`, which
 * keeps every patch it ever folded, so it usually still has an answer there.
 *
 * Unscoped by patch on purpose: the aggregate outlives the raw matches, and the
 * question here ("is this matchup winnable") is better served by every game we
 * hold than by a thin slice of the current patch.
 */
const props = defineProps<{
  championId: number
  position: ChampionPosition
  opponentChampionId: number
  championName: string | null
  opponentName: string | null
}>()

const { data, status } = useChampionMatchups(
  () => props.championId,
  () => props.position,
  { opponentChampionId: () => props.opponentChampionId },
)

const matchup = computed(() =>
  data.value?.matchups.find(m => m.opponentChampionId === props.opponentChampionId) ?? null)

// The jungle has no lane (#939): the same two figures are read as a matchup
// tempo there, and the labels say so rather than claiming a lane that isn't one.
const isJungle = computed(() => props.position === 'JUNGLE')
const laneNoun = computed<'lane' | 'matchup'>(() => (isJungle.value ? 'matchup' : 'lane'))

const roleLabel = computed(() =>
  POSITION_BY_VALUE.get(props.position)?.label.toLowerCase() ?? null)

const goldDiff = computed(() => matchup.value?.averageGoldDiffAt15 ?? null)
const goldLanes = computed(() => matchup.value?.goldDiffLaneGames ?? 0)
const verdict = computed(() => laneVerdict(goldDiff.value, goldLanes.value, laneNoun.value))

/**
 * The gap's caption. Never blank, and never the same sentence for the two reasons
 * a verdict can be missing: nothing measured at all, versus measured on a sample
 * too thin to band (the number still shows — it is the label that would overclaim).
 */
const goldCaption = computed(() => {
  if (goldDiff.value === null) return 'not measured yet'
  const games = `${goldLanes.value.toLocaleString('en-US')} game${goldLanes.value === 1 ? '' : 's'}`
  return verdict.value === null ? `${games} — too few to call` : `avg over ${games}`
})

interface StatCell {
  label: string
  value: string
  caption: string
  hint: string
  tone: RateBand
  badge: LaneVerdict | null
}

const stats = computed<StatCell[]>(() => {
  const entry = matchup.value
  if (entry === null) return []

  const opponent = props.opponentName ?? 'this opponent'
  const champion = props.championName ?? 'the champion'

  return [
    {
      label: 'Games',
      value: entry.games.toLocaleString('en-US'),
      caption: `recorded vs ${opponent}`,
      hint: `Every recorded ${champion} vs ${opponent} game at this role, across every `
        + 'patch the matchup aggregate still holds.',
      // A count is not better for being larger — no reading on the data axis.
      tone: 'default',
      badge: null,
    },
    {
      label: 'Win rate',
      value: formatPercentage(entry.winRate),
      caption: `${entry.wins.toLocaleString('en-US')} won`,
      hint: 'Share of those games the champion won. The whole game, not the lane — '
        + 'see the lane figures for how it goes before that.',
      tone: winRateBand(entry.winRate),
      badge: null,
    },
    {
      label: 'Matchup rate',
      // 0 means the backend had no total to divide by, not a matchup nobody
      // plays — and a hard 0.0% would read as the latter.
      value: entry.playRate > 0 ? formatPercentage(entry.playRate) : '—',
      caption: entry.playRate > 0
        ? `of ${champion}'s${roleLabel.value ? ` ${roleLabel.value}` : ''} games`
        : 'not measured',
      hint: `How often this opponent is the one across the ${laneNoun.value}, out of every `
        + `recorded ${champion} game at this role.`,
      tone: entry.playRate > 0 ? pickRateBand(entry.playRate) : 'default',
      badge: null,
    },
    {
      label: isJungle.value ? 'Ahead at 15' : 'Lane win rate',
      value: entry.laneWinRate == null ? '—' : formatPercentage(entry.laneWinRate, 0),
      caption: entry.laneWinRate == null
        ? 'nothing decided yet'
        : `of ${entry.decidedLaneGames.toLocaleString('en-US')} decided`,
      hint: 'Share of games that reached 15 minutes clearly ahead, out of those that '
        + 'ended clearly ahead or behind — lanes inside the threshold are decided by '
        + 'neither side and count for neither.',
      tone: winRateBand(entry.laneWinRate),
      badge: null,
    },
    {
      label: 'Gold @15',
      value: goldDiff.value === null ? '—' : formatGoldDiff(goldDiff.value),
      caption: goldCaption.value,
      hint: 'Average gold held over the opponent at 15 minutes. The '
        + `${laneNoun.value} verdict bands this number: even inside ±150, decided past ±300.`,
      tone: goldDiffBand(goldDiff.value),
      badge: verdict.value,
    },
  ]
})

const isLoading = computed(() => isLoadingStatus(status.value))

/** Nothing recorded at all — said in one line rather than as five em dashes. */
const emptyNotice = computed(() => {
  const champion = props.championName ?? 'This champion'
  const opponent = props.opponentName ?? 'that champion'
  const role = roleLabel.value ? ` at ${roleLabel.value}` : ''
  return `No recorded ${champion} vs ${opponent} game${role} yet.`
})
</script>

<template>
  <SectionCard
    title="This matchup"
    subtitle="Every recorded game of the pair — not the sample the build below is computed from."
    :level="2"
  >
    <div
      v-if="isLoading && matchup === null"
      class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5"
    >
      <div
        v-for="index in 5"
        :key="index"
        class="space-y-1.5"
      >
        <USkeleton class="h-6 w-16" />
        <USkeleton class="h-2.5 w-12" />
      </div>
    </div>

    <p
      v-else-if="matchup === null"
      class="text-sm text-muted"
    >
      {{ emptyNotice }}
    </p>

    <!-- Dimmed rather than replaced while the next matchup loads: the strip
         keeps its height, so picking a new opponent doesn't jump the build
         below it up the page. -->
    <!-- A plain grid, not a `<dl>`: `StatBlock` owns the value/label pair and
         emits neither `<dt>` nor `<dd>`, and a definition list whose children
         are anything else is one in name only. -->
    <div
      v-else
      class="grid grid-cols-2 gap-4 transition-opacity duration-200 sm:grid-cols-3 lg:grid-cols-5"
      :class="isLoading ? 'opacity-60' : ''"
    >
      <div
        v-for="stat in stats"
        :key="stat.label"
        :title="stat.hint"
        class="flex flex-col gap-1"
      >
        <StatBlock
          :value="stat.value"
          :label="stat.label"
          :caption="stat.caption"
          :tone="stat.tone"
        />
        <!-- The verdict rides under its own number, so it is never read as a
             qualifier of the win rate two cells over. -->
        <div v-if="stat.badge">
          <UBadge
            :color="stat.badge.color"
            :variant="stat.badge.variant"
            size="sm"
            class="font-semibold"
          >
            {{ stat.badge.label }}
          </UBadge>
        </div>
      </div>
    </div>
  </SectionCard>
</template>
