<script setup lang="ts">
import type { ChampionComparisonSide } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatRiotId, isValidRiotId } from '~/utils/riot-id'

const props = defineProps<{
  championId: number
  /** Lane both sides are narrowed to; null compares across every lane. */
  position: ChampionPosition | null
}>()

// Same page size as the Truemains sidebar card so both share one
// `useTruemainsLeaderboard` cache key — the mains list is fetched once, not
// twice, on a page that already renders it.
const TOP_MAINS = 10

/**
 * Sentinel for "compare against the whole pool". A `USelect` item may not carry
 * an empty-string value (Reka UI reserves it for "cleared, show the
 * placeholder" and throws otherwise), and a real Riot ID always contains a
 * `#`, so this can never collide with a main's option value.
 */
const ALL_MAINS = 'all-mains'

/** What the user is typing; only committed to a fetch on submit. */
const draft = ref('')
/** The Riot ID actually being compared. Null until the first valid submit. */
const submitted = ref<string | null>(null)
/** Riot ID of the targeted main, or ALL_MAINS for the whole pool. */
const target = ref(ALL_MAINS)

const canSubmit = computed(() => isValidRiotId(draft.value))

function submit() {
  if (!canSubmit.value) return
  submitted.value = draft.value.trim()
}

const { rows: mainRows } = useTruemainsLeaderboard(1, {
  pageSize: TOP_MAINS,
  championId: () => props.championId,
})

// "All mains" plus the champion's top tracked mains. Rows without a tag line
// can't be addressed as a Riot ID, so they're dropped rather than offered as a
// target the API would fail to resolve.
const targetOptions = computed(() => [
  { label: 'All mains', value: ALL_MAINS },
  ...mainRows.value.flatMap((row) => {
    const riotId = formatRiotId(row.identity.gameName, row.identity.tagLine)
    return riotId ? [{ label: riotId, value: riotId }] : []
  }),
])

// Reset the target when the champion changes: a main of the previous champion
// is meaningless here, and the API would compare against their games on a
// champion they may never play.
watch(() => props.championId, () => { target.value = ALL_MAINS })

const { data, status, error } = useChampionMainsComparison(
  () => props.championId,
  submitted,
  {
    mainRiotId: () => (target.value === ALL_MAINS ? null : target.value),
    position: () => props.position,
  },
)

const isLoading = computed(() => submitted.value !== null && status.value === 'pending')

const comparison = computed(() => data.value)
const player = computed(() => comparison.value?.player ?? null)
const mains = computed(() => comparison.value?.mains ?? null)

const playerLabel = computed(() => {
  const identity = player.value?.identity
  if (!identity) return 'This account'
  return formatRiotId(identity.gameName, identity.tagLine) ?? identity.gameName
})

const mainsLabel = computed(() => {
  const identity = mains.value?.identity
  if (identity) return formatRiotId(identity.gameName, identity.tagLine) ?? identity.gameName
  const players = mains.value?.players ?? 0
  return players > 0 ? `${players} mains` : 'Mains'
})

/**
 * The four side-by-side metrics (#528 scope). Build / skill order and
 * early-game leads are a separate issue and deliberately absent.
 *
 * `unit: 'points'` marks a rate whose delta reads in percentage points rather
 * than as a percentage of a percentage.
 */
interface MetricRow {
  label: string
  format: (value: number) => string
  formatDelta: (value: number) => string
  read: (side: ChampionComparisonSide) => number
}

const signed = (value: number, body: string) => `${value > 0 ? '+' : value < 0 ? '−' : ''}${body}`

const METRICS: MetricRow[] = [
  {
    label: 'Win rate',
    read: side => side.winRate,
    format: value => formatPercentage(value, 1),
    formatDelta: value => signed(value, `${Math.abs(value * 100).toFixed(1)} pts`),
  },
  {
    label: 'KDA',
    read: side => side.kda,
    format: value => value.toFixed(2),
    formatDelta: value => signed(value, Math.abs(value).toFixed(2)),
  },
  {
    label: 'CS / min',
    read: side => side.csPerMin,
    format: value => value.toFixed(1),
    formatDelta: value => signed(value, Math.abs(value).toFixed(1)),
  },
  {
    label: 'Gold / min',
    read: side => side.goldPerMin,
    format: value => Math.round(value).toLocaleString('en-US'),
    formatDelta: value => signed(value, Math.round(Math.abs(value)).toLocaleString('en-US')),
  },
]

// Higher is better for all four metrics, so one tone rule covers them: ahead of
// the mains reads as success, behind as error, a dead heat stays neutral. The
// epsilon keeps a rounding-level difference from being coloured as a real gap.
function deltaTone(delta: number, epsilon: number) {
  if (Math.abs(delta) < epsilon) return 'text-muted'
  return delta > 0 ? 'text-success' : 'text-error'
}

const metricRows = computed(() => {
  const left = player.value
  const right = mains.value
  if (!left || !right) return []
  return METRICS.map((metric) => {
    const playerValue = metric.read(left)
    const mainsValue = metric.read(right)
    const delta = playerValue - mainsValue
    return {
      label: metric.label,
      playerValue: metric.format(playerValue),
      mainsValue: metric.format(mainsValue),
      delta: metric.formatDelta(delta),
      // Half of the smallest rendered step, so "+0.0" is never coloured.
      tone: deltaTone(delta, metric.label === 'Win rate' ? 0.0005 : 0.005),
    }
  })
})

const showMetrics = computed(() => comparison.value?.status === 'OK' && metricRows.value.length > 0)

/** Which side is short of the floor, for the insufficient-sample notice. */
const thinSides = computed(() => {
  const short: string[] = []
  if (player.value && !player.value.sampleMet) short.push(playerLabel.value)
  if (mains.value && !mains.value.sampleMet) short.push(mainsLabel.value)
  return short
})
</script>

<template>
  <SectionCard
    :level="2"
    title="Compare with the mains"
    subtitle="See how any tracked account stacks up against this champion's mains."
  >
    <div class="flex flex-col gap-3">
      <form
        class="flex flex-col gap-2"
        @submit.prevent="submit"
      >
        <div class="flex items-center gap-2">
          <UInput
            v-model="draft"
            placeholder="Name#TAG"
            size="sm"
            class="min-w-0 flex-1"
            aria-label="Riot ID to compare"
            autocomplete="off"
          />
          <UButton
            type="submit"
            size="sm"
            color="primary"
            :disabled="!canSubmit"
            label="Compare"
          />
        </div>
        <USelect
          v-model="target"
          :items="targetOptions"
          size="sm"
          class="w-full"
          aria-label="Compare against"
        />
      </form>

      <template v-if="isLoading">
        <USkeleton
          v-for="i in 5"
          :key="`cmp-skel-${i}`"
          class="h-8 w-full rounded-md"
        />
      </template>

      <p
        v-else-if="error"
        class="py-6 text-center text-sm text-muted"
      >
        Couldn't load the comparison. Please try again.
      </p>

      <!-- Nothing submitted yet, or a Riot ID the API can't parse. -->
      <p
        v-else-if="!comparison"
        class="py-6 text-center text-sm text-muted"
      >
        Enter a Riot ID as <span class="text-default">Name#TAG</span> to compare it with the players
        who main this champion.
      </p>

      <!--
        We only compare accounts already in our database — there is no
        on-demand lookup against Riot — so an account we've never ingested is a
        normal answer, not an error. Say so plainly and point at the tracked
        list rather than leaving a dead end.
      -->
      <div
        v-else-if="comparison.status === 'UNKNOWN_ACCOUNT'"
        class="flex flex-col items-center gap-1 py-6 text-center"
      >
        <p class="text-sm font-medium text-default">
          We don't track this account yet
        </p>
        <p class="text-sm text-muted">
          The comparison only covers players already in our database.
          <NuxtLink
            to="/truemains"
            class="rounded text-primary transition-colors hover:text-primary/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            Browse the tracked players</NuxtLink>.
        </p>
      </div>

      <p
        v-else-if="comparison.status === 'UNKNOWN_TARGET'"
        class="py-6 text-center text-sm text-muted"
      >
        We don't track the main you picked. Compare against all mains instead.
      </p>

      <!--
        Below the games floor on one side or the other. The numbers exist but
        would be noise, so the metrics stay hidden and we say exactly how far
        the sample is from the bar rather than showing an unreliable verdict.
      -->
      <div
        v-else-if="!showMetrics"
        class="flex flex-col items-center gap-1 py-6 text-center"
      >
        <p class="text-sm font-medium text-default">
          Not enough games to compare
        </p>
        <p class="text-sm text-muted">
          <template v-if="player && !player.sampleMet">
            {{ playerLabel }} has {{ player.games }} recorded
            {{ player.games === 1 ? 'game' : 'games' }} on this champion; we need
            {{ comparison.minGames }}.
          </template>
          <template v-else-if="thinSides.length">
            {{ thinSides.join(' and ') }} {{ thinSides.length > 1 ? 'have' : 'has' }} too few
            recorded games on this champion.
          </template>
          <template v-else>
            One side is below the {{ comparison.minGames }}-game floor.
          </template>
        </p>
      </div>

      <div
        v-else
        class="flex flex-col gap-2"
      >
        <!-- Column headers: who each column is, and the sample behind it. -->
        <div class="grid grid-cols-[minmax(0,1fr)_auto_auto] items-end gap-x-3 gap-y-1">
          <span class="sr-only">Metric</span>
          <div class="flex flex-col items-end">
            <span class="max-w-[9rem] truncate text-xs font-medium text-default">
              {{ playerLabel }}
            </span>
            <span class="text-xs text-dimmed">{{ player?.games }} games</span>
          </div>
          <div class="flex flex-col items-end">
            <span class="max-w-[9rem] truncate text-xs font-medium text-default">
              {{ mainsLabel }}
            </span>
            <span class="text-xs text-dimmed">{{ mains?.games }} games</span>
          </div>

          <template
            v-for="row in metricRows"
            :key="row.label"
          >
            <span class="glass-hover -mx-1 rounded-md px-1 py-1 text-sm text-muted">
              {{ row.label }}
            </span>
            <div class="flex flex-col items-end py-1">
              <span
                class="text-sm font-semibold tabular-nums"
                :class="row.tone"
              >{{ row.playerValue }}</span>
              <span
                class="text-xs tabular-nums"
                :class="row.tone"
              >{{ row.delta }}</span>
            </div>
            <span class="py-1 text-right text-sm tabular-nums text-default">
              {{ row.mainsValue }}
            </span>
          </template>
        </div>

        <p class="text-xs text-dimmed">
          Ranked solo/duo games we hold for both sides{{ comparison.position ? `, ${comparison.position.toLowerCase()} only` : '' }}.
        </p>
      </div>
    </div>
  </SectionCard>
</template>
