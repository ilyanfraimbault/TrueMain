<script setup lang="ts">
import type { DraftCandidate, DraftPool } from '~/types/draft'

/**
 * "Your pick": the candidates the draft endpoint ranked for our lane, best
 * first, as a podium of posters. A click on one opens its build against this
 * draft — the app never picks; the player does, in the client.
 *
 * The pool is the lane's tier list — the meta picks, or the ones below them —
 * until the app knows the player's own pool (#1682), which is why "My pool"
 * is there and not yet on.
 */
const props = defineProps<{
  candidates: DraftCandidate[]
  /** The pool as it was asked for — the lane's tier-list order. */
  pool: number[]
  pending: boolean
  position: string
  error: string | null
}>()

const poolKind = defineModel<DraftPool>('poolKind', { default: 'meta' })
const emit = defineEmits<{ preview: [championId: number] }>()

const { nameOf } = useChampionStatics()
const { entryOf } = useTierList()

const search = ref('')
const hovered = ref<number | null>(null)

/**
 * Whether the draft gave the endpoint anything to measure. With no lane
 * opponent resolved and no ally locked, every score is a zero over no games —
 * so the podium shows the lane's tier-list order and win rates instead of a
 * row of "+0.0%".
 */
const measured = computed(() => props.candidates.some(candidate => candidate.matchupGames > 0 || candidate.synergyGames > 0))

const ranked = computed(() => {
  if (measured.value) return props.candidates
  const order = new Map(props.pool.map((id, index) => [id, index]))
  return [...props.candidates].sort((a, b) => (order.get(a.championId) ?? 999) - (order.get(b.championId) ?? 999))
})

const shown = computed(() => {
  const query = search.value.trim().toLowerCase()
  return query ? ranked.value.filter(candidate => nameOf(candidate.championId).toLowerCase().includes(query)) : ranked.value
})

/** The card in focus: the hovered one, else the best. */
const focused = computed(() => hovered.value ?? shown.value[0]?.championId ?? null)

/** The podium: the best card full height, each next one a step lower, down to a floor. */
const heightOf = (index: number) => Math.max(0.78, 1 - index * 0.035)

const pools = [
  { label: 'Meta', value: 'meta' },
  { label: 'Off-meta', value: 'offmeta' },
]
</script>

<template>
  <section class="flex h-full min-h-0 flex-col gap-3">
    <header class="flex items-center gap-4">
      <h2 class="text-base font-semibold text-highlighted">Your pick</h2>
      <UTooltip v-if="!measured && candidates.length" text="Nothing to measure against yet: the lane's tier list, until an enemy picks or an ally locks in">
        <UBadge color="neutral" variant="soft" size="sm" icon="i-lucide-trending-up">Lane meta</UBadge>
      </UTooltip>
      <UIcon v-if="pending" name="i-lucide-loader-circle" class="size-4 text-dimmed" />

      <div class="ml-auto flex items-center gap-3">
        <span class="stat-label">Pool</span>
        <UTabs v-model="poolKind" :items="pools" :content="false" size="xs" color="neutral" :ui="{ list: 'bg-elevated' }" />
        <UTooltip text="Soon: ranks the champions you play">
          <USwitch label="My pool" size="sm" disabled :ui="{ label: 'stat-label' }" />
        </UTooltip>
        <UInput v-model="search" icon="i-lucide-search" placeholder="Search a champion" size="sm" class="w-48" />
      </div>
    </header>

    <div v-if="shown.length" class="flex min-h-0 flex-1 items-end gap-2.5 overflow-x-auto pb-1" @mouseleave="hovered = null">
      <button
        v-for="(candidate, index) in shown"
        :key="candidate.championId"
        type="button"
        class="h-full rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-primary"
        :aria-label="`Build for ${nameOf(candidate.championId)}`"
        @mouseenter="hovered = candidate.championId"
        @focus="hovered = candidate.championId"
        @click="emit('preview', candidate.championId)"
      >
        <DraftSuggestionCard
          :candidate="candidate"
          :entry="entryOf(candidate.championId, position)"
          :focused="focused === candidate.championId"
          :height="heightOf(index)"
          :measured="measured"
        />
      </button>
    </div>

    <div v-else class="flex flex-1 flex-col items-center justify-center gap-2 text-center">
      <UIcon :name="error ? 'i-lucide-wifi-off' : pending ? 'i-lucide-loader-circle' : 'i-lucide-search-x'" class="size-6 text-dimmed" />
      <p class="text-sm text-muted">
        {{ error ? 'Could not rank the picks' : pending ? 'Ranking the picks for this draft…' : 'No champion matches' }}
      </p>
      <p v-if="error" class="max-w-sm text-xs text-dimmed">{{ error }}</p>
    </div>
  </section>
</template>
