<script setup lang="ts">
import { formatCount } from '#shared/utils/counts'
import type { CompositionBuildResponse } from '#shared/types/composition'
import type { ChampionPosition } from '#common/utils/positions'
import type { RateBand } from '#common/utils/rate-tone'
import { formatPercentage } from '#shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { winRateBand } from '#common/utils/rate-tone'

/**
 * The page's single line of numbers (#1111): what the recommendation below was
 * computed from. How the lane itself goes at 15 minutes is read over the same
 * games, and drawn in the board's lane duel (`LaneDuel`), between the two teams,
 * the way the desktop app's champion select draws it.
 *
 * <b>Why one strip and not two.</b> This used to be two — a "This matchup" card
 * here and a sample strip inside `RecommendationPanel` — which put two `games`
 * figures and two `win rate` figures a few centimetres apart with nothing saying
 * they measured different things. Every game in the recommendation's sample is a
 * game of this matchup, so one line can carry both; the win rate shown is the
 * sample's, and the lane figures are the matchup's, which is why each cell states
 * its own denominator.
 *
 * <b>Why it stays on the page rather than moving into the panel.</b> #1098's
 * reason still holds: `RecommendationPanel` does not render on the standard-build
 * fallback path, and mounting these figures inside it made them vanish precisely
 * when the reader most needs to know how thin the ground is.
 *
 * <b>Every cell now counts the same games (#1117).</b> The lane half used to read
 * the matchup aggregate, whose champion side is mains-only (#1087) while a
 * composition sample takes any pilot — so "8 games used · 0 by mains" sat beside a
 * lane win rate of "—", which is exactly the contradiction merging the strips was
 * meant to end. The backend now judges the lane over the selection itself, so this
 * component has a single source and makes a single request.
 */
const props = defineProps<{
  position: ChampionPosition
  championName: string | null
  opponentName: string | null
  /** Null until the recommendation resolves, and on the fallback path. */
  recommendation: CompositionBuildResponse | null
  /**
   * A refetch is in flight. The strip shows its skeleton cells rather than the
   * previous numbers — the same treatment the recommendation card gets. It used
   * to keep them, dimmed, to hold the layout steady; over the seconds a cold
   * recommendation takes, figures that describe the *previous* draft are read as
   * describing the current one, which is the failure #1117 exists to prevent.
   */
  loading?: boolean
}>()

const emit = defineEmits<{ 'show-games': [] }>()

const build = computed(() => props.recommendation?.build ?? null)
const confidence = computed(() => props.recommendation?.confidence ?? null)
// The jungle has no lane (#939): the same figures are read as a matchup tempo.
const laneNoun = computed<'lane' | 'matchup'>(() => (props.position === 'JUNGLE' ? 'matchup' : 'lane'))

const sampleWinRate = computed(() => {
  const games = build.value?.gamesConsidered ?? 0
  return games > 0 ? (build.value?.wins ?? 0) / games : null
})

interface StatCell {
  key: string
  label: string
  value: string
  caption: string
  hint: string
  tone: RateBand
}

const stats = computed<StatCell[]>(() => {
  const sample = build.value
  const conf = confidence.value
  const champion = props.championName ?? 'the champion'

  return [
    {
      key: 'games',
      label: 'Games used',
      value: sample === null ? '—' : formatCount(sample.gamesConsidered),
      caption: conf === null
        ? 'no build sampled'
        : `${conf.truemainGameCount} by mains · of ${formatCount(conf.candidatePoolSize)} scanned`,
      hint: 'The build below is computed from these games only — games piloted by a main of '
        + 'the champion first, then the most similar to your draft, out of all recent games '
        + `scanned for ${champion} at this role.`,
      tone: 'default',
    },
    {
      key: 'similarity',
      label: 'Draft match',
      // maxPossibleScore is 0 when nothing but the matchup was pinned: there is
      // no draft to be similar to, and 0% would read as "nothing resembles it".
      value: conf !== null && conf.maxPossibleScore > 0 ? formatPercentage(conf.meanSimilarity) : '—',
      caption: 'avg similarity',
      hint: 'Average similarity between those games and the rest of your draft.',
      tone: 'default',
    },
    {
      key: 'winRate',
      label: 'Win rate',
      value: sampleWinRate.value === null ? '—' : formatPercentage(sampleWinRate.value),
      caption: 'across those games',
      hint: 'Win rate across the games the build is computed from — the same games every '
        + `cell of this line counts, and the ${laneNoun.value} win rate in the duel above too.`,
      tone: winRateBand(sampleWinRate.value),
    },
  ]
})

/** Nothing recorded at all — said in one line rather than as three em dashes. */
const emptyNotice = computed(() => {
  const champion = props.championName ?? 'This champion'
  const opponent = props.opponentName ?? 'that champion'
  const role = POSITION_BY_VALUE.get(props.position)?.label.toLowerCase()
  return `No recorded ${champion} vs ${opponent} game${role ? ` at ${role}` : ''} yet.`
})
</script>

<template>
  <SectionCard
    title="Behind this build"
    :level="2"
  >
    <!-- Skeleton on every fetch, not only the first (#1659 follow-up). A cold
         recommendation can take tens of seconds, and a 40%-dimmed strip of real
         numbers over that long reads as a styling quirk rather than as work in
         progress — worse, the stale figures stay legible enough to be believed. -->
    <div
      v-if="recommendation === null || loading"
      class="grid grid-cols-2 gap-4 sm:grid-cols-3"
    >
      <div
        v-for="index in 3"
        :key="index"
        class="space-y-1.5"
      >
        <USkeleton class="h-6 w-16" />
        <USkeleton class="h-2.5 w-12" />
      </div>
    </div>

    <UEmpty
      v-else-if="(build?.gamesConsidered ?? 0) === 0"
      size="sm"
      icon="i-lucide-swords"
      :description="emptyNotice"
    />

    <div
      v-else
      class="grid grid-cols-2 gap-4 sm:grid-cols-3"
    >
      <div
        v-for="stat in stats"
        :key="stat.key"
        :title="stat.hint"
        class="flex flex-col gap-1"
      >
        <div class="flex items-start gap-1">
          <StatBlock
            :value="stat.value"
            :label="stat.label"
            :caption="stat.caption"
            :tone="stat.tone"
          />
          <!-- Opens the provenance drawer, which `RecommendationPanel` owns
               along with the item / rune / spell maps it needs — this only asks
               for it. Only meaningful once there is a sample to list. -->
          <UTooltip
            v-if="stat.key === 'games' && (build?.gamesConsidered ?? 0) > 0"
            text="See the games this build was computed from"
            :delay-duration="150"
          >
            <UButton
              icon="i-lucide-eye"
              color="neutral"
              variant="subtle"
              size="xs"
              square
              aria-label="See the games this build was computed from"
              @click="emit('show-games')"
            />
          </UTooltip>
        </div>

      </div>
    </div>
  </SectionCard>
</template>
