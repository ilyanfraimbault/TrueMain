<script setup lang="ts">
import type { DraftCandidate } from '~/types/draft'

/**
 * "Your pick": the candidates the draft endpoint ranked for our lane, best
 * first, as a podium of posters. A click on one opens its build against this
 * draft. The app never picks on its own: each card carries a separate
 * "Hover" (or, on our ban, "Ban") control the player clicks (#1909), and the
 * search reaches every other champion the same way — the lock itself is the
 * strip's button, away from the cards.
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

const { nameOf, champions } = useChampionStatics()
const { writable } = useChampSelectActions()
const { entryOf, status: tierListStatus } = useTierList()

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

/**
 * Champions the search names that the podium does not rank — so any champion
 * can be hovered or banned from here, not only the lane's candidates.
 */
const others = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (!query || !writable.value) return []
  const ranked = new Set(props.candidates.map(candidate => candidate.championId))
  return [...champions.value.values()]
    .filter(champion => !ranked.has(champion.id) && champion.name.toLowerCase().includes(query))
    .sort((a, b) => a.name.localeCompare(b.name))
    .slice(0, 12)
})

/** The card in focus: the hovered one, else the best. */
const focused = computed(() => hovered.value ?? shown.value[0]?.championId ?? null)

/**
 * Why the podium is empty. The pool is filtered through the tier list, so
 * without it nothing is ranked — that is a load to wait for, not a search
 * that missed (#1783).
 */
const empty = computed(() => {
  if (props.error) return { icon: 'i-lucide-wifi-off', text: 'Could not rank the picks' }
  if (tierListStatus.value === 'error') return { icon: 'i-lucide-wifi-off', text: 'The tier list did not load — retrying' }
  if (tierListStatus.value !== 'ready') return { icon: 'i-lucide-loader-circle', text: 'Loading the tier list…' }
  if (props.pending) return { icon: 'i-lucide-loader-circle', text: 'Ranking the picks for this draft…' }
  if (search.value.trim()) return { icon: 'i-lucide-search-x', text: 'No champion matches' }
  if (myPool.value && props.hasPool) return { icon: 'i-lucide-search-x', text: 'None of your champions is played on this lane' }
  return { icon: 'i-lucide-search-x', text: 'No champion to rank on this lane' }
})

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
    <div v-if="shown.length || others.length" class="-mx-1 flex min-h-0 flex-1 items-end gap-2.5 overflow-x-auto px-1 pb-1 pt-1" @mouseleave="hovered = null">
      <div
        v-for="(candidate, index) in shown"
        :key="candidate.championId"
        class="h-full shrink-0"
        @mouseenter="hovered = candidate.championId"
      >
        <DraftSuggestionCard
          :candidate="candidate"
          :entry="entryOf(candidate.championId, position)"
          :focused="focused === candidate.championId"
          :height="heightOf(index)"
          :measured="measured"
        >
          <!-- The card's click previews the build; the write is the control over its corner, a separate target. -->
          <button
            type="button"
            class="absolute inset-0 rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-primary"
            :aria-label="`Build for ${nameOf(candidate.championId)}`"
            @focus="hovered = candidate.championId"
            @click="emit('preview', candidate.championId)"
          />
          <template #action>
            <DraftChampionAction :champion-id="candidate.championId" />
          </template>
        </DraftSuggestionCard>
      </div>

      <div v-if="others.length" class="flex h-full shrink-0 flex-col justify-end gap-1.5">
        <span class="stat-label px-1">Not ranked for this draft</span>
        <div class="grid grid-flow-col grid-rows-3 gap-1.5">
          <div v-for="champion in others" :key="champion.id" class="flex w-44 items-center gap-2 rounded-lg bg-elevated p-1.5 ring-1 ring-default">
            <ChampionPortrait :champion-id="champion.id" size="sm" class="size-8! rounded-md!" />
            <span class="min-w-0 flex-1 truncate text-xs font-semibold text-highlighted">{{ champion.name }}</span>
            <DraftChampionAction :champion-id="champion.id" />
          </div>
        </div>
      </div>
    </div>

    <div v-else class="flex flex-1 flex-col items-center justify-center gap-2 text-center">
      <UIcon :name="empty.icon" class="size-6 text-dimmed" />
      <p class="text-sm text-muted">{{ empty.text }}</p>
      <p v-if="error" class="max-w-sm text-xs text-dimmed">{{ error }}</p>
    </div>
  </section>
</template>
