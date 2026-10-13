<script setup lang="ts">
import type { ChampionPosition } from '#common/utils/positions'
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

// `server: false`, like the Truemains card (#1231): the list only fills the
// target picker, and fetching it during SSR sent one uncached leaderboard read to
// the API on every render of every champion page (#1570). In the browser the
// card and this picker share the key, so the page asks for it once.
const { rows: mainRows } = useTruemainsLeaderboard(1, {
  pageSize: TOP_MAINS,
  championId: () => props.championId,
  server: false,
})

/**
 * The account under comparison, lowercased for matching. Falls back to the
 * draft so the self entry disappears as the user types, not only after submit.
 */
const comparedRiotId = computed(() =>
  (submitted.value ?? draft.value).trim().toLowerCase(),
)

// "All mains" plus the champion's top tracked mains. Two exclusions:
// rows without a tag line can't be addressed as a Riot ID at all, and the
// compared account itself is dropped — targeting yourself is a valid request
// the API answers honestly (two identical columns, every delta zero), but it
// tells the user nothing, so the picker doesn't offer it. The account can
// legitimately appear in this list: it's the champion's top mains, and the
// person comparing may well be one of them.
const targetOptions = computed(() => [
  { label: 'All mains', value: ALL_MAINS },
  ...mainRows.value.flatMap((row) => {
    const riotId = formatRiotId(row.identity.gameName, row.identity.tagLine)
    if (!riotId || riotId.toLowerCase() === comparedRiotId.value) return []
    return [{ label: riotId, value: riotId }]
  }),
])

// Reset the target when the champion changes: a main of the previous champion
// is meaningless here, and the API would compare against their games on a
// champion they may never play. Keyed on championId rather than on the options
// below because `mainRows` keeps the previous champion's rows while its refetch
// is in flight — waiting for the options to change would let one request go out
// against the stale target first.
watch(() => props.championId, () => { target.value = ALL_MAINS })

// Safety net for the other way a target can stop being offered: the user typed
// their way into *being* the selected target, so it just left the list.
watch(targetOptions, (options) => {
  if (!options.some(option => option.value === target.value)) target.value = ALL_MAINS
})

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
</script>

<template>
  <SectionCard
    :level="2"
    title="Compare with the mains"
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

      <FetchErrorAlert
        v-else-if="error"
        :error="error"
        title="Failed to load the comparison"
        class="my-2"
      />

      <!-- Figures only once the API had enough games on both sides. -->
      <ChampionMainsComparisonTable
        v-else-if="comparison?.status === 'OK' && player && mains"
        :player="player"
        :mains="mains"
        :player-label="playerLabel"
        :mains-label="mainsLabel"
      />

      <ChampionMainsComparisonEmpty
        v-else
        :comparison="comparison"
        :player-label="playerLabel"
        :mains-label="mainsLabel"
      />
    </div>
  </SectionCard>
</template>
