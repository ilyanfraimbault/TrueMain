<script setup lang="ts">
import type { DraftCandidate } from '~/types/draft'

/**
 * "Your pick": the candidates the draft endpoint ranked for our lane, best
 * first, as a podium of posters. A click on one opens its build against this
 * draft — the app never picks; the player does, in the client.
 *
 * "My pool" ranks the player's own champions (their most-mastered on the
 * lane); off, every champion played on the lane. Either way the ranking is the
 * endpoint's: the matchup against the lane opponent, and the pairing with the
 * allies already locked.
 */
const props = defineProps<{
  candidates: DraftCandidate[]
  /** The pool as it was asked for — mastery order for ours, tier-list order for the lane's. */
  pool: number[]
  /** The client has told us the player's champions: "My pool" can be on. */
  hasPool: boolean
  pending: boolean
  position: string
  error: string | null
}>()

const myPool = defineModel<boolean>('myPool', { default: true })
const emit = defineEmits<{ preview: [championId: number] }>()

const { nameOf } = useChampionStatics()
const { entryOf } = useTierList()

const search = ref('')
const hovered = ref<number | null>(null)

/**
 * Whether the draft gave the endpoint anything to measure. With no lane
 * opponent resolved and no ally locked, every score is a zero over no games —
 * so the podium keeps the pool's own order and shows each champion's win rate
 * on the lane instead of a row of "+0.0%".
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
</script>

<template>
  <section class="flex h-full min-h-0 flex-col gap-3">
    <header class="flex items-center gap-4">
      <h2 class="text-base font-semibold text-highlighted">Your pick</h2>
      <UTooltip v-if="!measured && candidates.length" text="Nothing to measure against yet: win rates on the lane, until an enemy picks or an ally locks in">
        <UBadge color="neutral" variant="soft" size="sm" icon="i-lucide-hourglass">Before the enemy picks</UBadge>
      </UTooltip>
      <UIcon v-if="pending" name="i-lucide-loader-circle" class="size-4 text-dimmed" />

      <div class="ml-auto flex items-center gap-3">
        <UTooltip :text="hasPool ? 'Your ten most-played champions on this lane' : 'Needs the League client: your champions come from it'">
          <USwitch
            :model-value="myPool && hasPool"
            label="My pool"
            size="sm"
            :disabled="!hasPool"
            :ui="{ label: 'stat-label' }"
            @update:model-value="myPool = $event"
          />
        </UTooltip>
        <UInput v-model="search" icon="i-lucide-search" placeholder="Search a champion" size="sm" class="w-48" />
      </div>
    </header>

    <!-- A scroller clips what is drawn outside its box, the focused card's ring included: the padding is that ring's
         room, the negative margin keeps the cards aligned with the header. -->
    <div v-if="shown.length" class="-mx-1 flex min-h-0 flex-1 items-end gap-2.5 overflow-x-auto px-1 pb-1 pt-1" @mouseleave="hovered = null">
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
