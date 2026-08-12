<script setup lang="ts">
import type { CompositionBuildResponse } from '~~/shared/types/composition'
import type { ChampionPosition } from '~/utils/positions'
import type { RateBand } from '~/utils/rate-tone'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { isLoadingStatus } from '~/utils/async-data'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { formatGoldDiff, formatXpDiff, laneVerdict } from '~/utils/lane-verdict'
import { winRateBand } from '~/utils/rate-tone'

/**
 * The page's single line of numbers (#1111): what the recommendation below was
 * computed from, and how the matchup itself goes at 15 minutes.
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
 * when the reader most needs to know how thin the ground is. So the lane half
 * renders from the champion / role / opponent triple alone, and the sample half
 * degrades to em dashes when there is no recommendation to describe.
 *
 * The lane half is unscoped by patch on purpose: the matchup aggregate outlives
 * the raw matches, and "is this matchup winnable" is better served by every game
 * we hold than by a thin slice of the current patch.
 */
const props = defineProps<{
  championId: number
  position: ChampionPosition
  opponentChampionId: number
  championName: string | null
  opponentName: string | null
  /** Null until the recommendation resolves, and on the fallback path. */
  recommendation: CompositionBuildResponse | null
}>()

const emit = defineEmits<{ 'show-games': [] }>()

const { data, status } = useChampionMatchups(
  () => props.championId,
  () => props.position,
  { opponentChampionId: () => props.opponentChampionId },
)

const matchup = computed(() =>
  data.value?.matchups.find(m => m.opponentChampionId === props.opponentChampionId) ?? null)

const build = computed(() => props.recommendation?.build ?? null)
const confidence = computed(() => props.recommendation?.confidence ?? null)

// The jungle has no lane (#939): the same figures are read as a matchup tempo
// there, and the labels say so rather than claiming a lane that isn't one.
const isJungle = computed(() => props.position === 'JUNGLE')
const laneNoun = computed<'lane' | 'matchup'>(() => (isJungle.value ? 'matchup' : 'lane'))

const sampleWinRate = computed(() => {
  const games = build.value?.gamesConsidered ?? 0
  return games > 0 ? (build.value?.wins ?? 0) / games : null
})

const goldDiff = computed(() => matchup.value?.averageGoldDiffAt15 ?? null)
const goldLanes = computed(() => matchup.value?.goldDiffLaneGames ?? 0)
const xpDiff = computed(() => matchup.value?.averageXpDiffAt15 ?? null)
const verdict = computed(() => laneVerdict(goldDiff.value, goldLanes.value, laneNoun.value))

/**
 * The two gaps on one line under the lane rate. Gold and XP sit together because
 * they are the same measurement of the same moment and are only interesting
 * *against each other*: a lead in gold over a deficit in XP is a lane won on
 * kills and lost on waves, which the next all-in reverses. Separating them across
 * two cells would have hidden exactly that.
 */
const gapLine = computed(() => {
  if (goldDiff.value === null && xpDiff.value === null) return null
  const parts: string[] = []
  if (goldDiff.value !== null) parts.push(`${formatGoldDiff(goldDiff.value)} gold`)
  if (xpDiff.value !== null) parts.push(`${formatXpDiff(xpDiff.value)} xp`)
  return `${parts.join(' · ')} @15`
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
  const entry = matchup.value
  const sample = build.value
  const conf = confidence.value
  const champion = props.championName ?? 'the champion'
  const opponent = props.opponentName ?? 'this opponent'

  return [
    {
      key: 'games',
      label: 'Games used',
      value: sample === null ? '—' : sample.gamesConsidered.toLocaleString('en-US'),
      caption: conf === null
        ? 'no build sampled'
        : `${conf.truemainGameCount} by mains · of ${conf.candidatePoolSize.toLocaleString('en-US')} scanned`,
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
      hint: 'Win rate across the games the build is computed from — a smaller sample than '
        + `the ${laneNoun.value} figures beside it, which cover every recorded game of the pair.`,
      tone: winRateBand(sampleWinRate.value),
    },
    {
      key: 'lane',
      label: isJungle.value ? 'Ahead at 15' : 'Lane win rate',
      value: entry?.laneWinRate == null ? '—' : formatPercentage(entry.laneWinRate, 0),
      caption: entry?.laneWinRate == null
        ? 'nothing decided yet'
        : `of ${entry.decidedLaneGames.toLocaleString('en-US')} decided`,
      hint: `Share of ${champion} vs ${opponent} games that reached 15 minutes clearly ahead, `
        + 'out of those that ended clearly ahead or behind. Measured over every recorded game '
        + 'of the pair, not over the sample the build uses.',
      tone: winRateBand(entry?.laneWinRate ?? null),
    },
  ]
})

const isLoading = computed(() => isLoadingStatus(status.value))

/** Nothing recorded at all — said in one line rather than as four em dashes. */
const emptyNotice = computed(() => {
  const champion = props.championName ?? 'This champion'
  const opponent = props.opponentName ?? 'that champion'
  const role = POSITION_BY_VALUE.get(props.position)?.label.toLowerCase()
  return `No recorded ${champion} vs ${opponent} game${role ? ` at ${role}` : ''} yet.`
})
</script>

<template>
  <SectionCard
    title="This matchup"
    subtitle="The games the build below is computed from, and how the matchup itself goes at 15 minutes."
    :level="2"
  >
    <div
      v-if="isLoading && matchup === null"
      class="grid grid-cols-2 gap-4 lg:grid-cols-4"
    >
      <div
        v-for="index in 4"
        :key="index"
        class="space-y-1.5"
      >
        <USkeleton class="h-6 w-16" />
        <USkeleton class="h-2.5 w-12" />
      </div>
    </div>

    <p
      v-else-if="matchup === null && recommendation === null"
      class="text-sm text-muted"
    >
      {{ emptyNotice }}
    </p>

    <!-- Dimmed rather than replaced while the next matchup loads: the strip
         keeps its height, so picking a new opponent doesn't jump the build
         below it up the page. -->
    <div
      v-else
      class="grid grid-cols-2 gap-4 transition-opacity duration-200 lg:grid-cols-4"
      :class="isLoading ? 'opacity-60' : ''"
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

        <!-- The two gaps and the verdict ride under the lane rate they explain,
             never as cells of their own: a reader comparing "+967 gold" with a
             win rate three columns away has to be told they share a sample, and
             they don't. -->
        <template v-if="stat.key === 'lane'">
          <!-- Uncoloured on purpose: one line carries two gaps that can point
               opposite ways, and a single tone would have to pick one of them
               to speak for both. The verdict badge below bands the gold gap,
               which is the only one with product-defined edges. -->
          <p
            v-if="gapLine"
            class="text-xs tabular-nums text-muted"
          >
            {{ gapLine }}
          </p>
          <div v-if="verdict">
            <UBadge
              :color="verdict.color"
              :variant="verdict.variant"
              size="sm"
              class="font-semibold"
            >
              {{ verdict.label }}
            </UBadge>
          </div>
        </template>
      </div>
    </div>
  </SectionCard>
</template>
