<script setup lang="ts">
import type { DraftSuggestionItem } from '~/types/draft'
import { damageNote } from '~/utils/draft-damage'

/**
 * "Your pick" or "Your ban": what the draft endpoints ranked for our lane,
 * best first, as a podium of posters, each with the reason that carries it
 * (#1906). A click on one opens its build against this draft. The app never
 * picks or bans on its own: each card carries a separate "Hover" (or, on our
 * ban, "Ban") control the player clicks (#1909), and the search reaches every
 * other champion the same way — the lock itself is the strip's button, away
 * from the cards.
 *
 * Picks: "My pool" ranks the player's own champions (their most-mastered on
 * the lane); off, every champion played on the lane. Bans: the threats to the
 * pick the player declared, else to their pool; never a champion an ally
 * hovers or locks. Either way the order is the endpoint's.
 */
const props = defineProps<{
  mode: 'pick' | 'ban'
  suggestions: DraftSuggestionItem[]
  /** Bans only: what they protect (`pick`, `pool`, `none`) and who. */
  target?: { kind: string, championIds: number[] } | null
  /** The client has told us the player's champions: "My pool" can be on. */
  hasPool: boolean
  pending: boolean
  position: string
  error: string | null
  /** Our locked allies, ourselves excluded — what a pick's damage note is read against (#1907). */
  allies: { championId: number, position: string | null }[]
}>()

const myPool = defineModel<boolean>('myPool', { default: true })
const emit = defineEmits<{ preview: [championId: number] }>()

const { nameOf, champions } = useChampionStatics()
const { writable } = useChampSelectActions()
const { entryOf, status: tierListStatus } = useTierList()
const { profileOf } = useDamageProfiles()

/** What each pick does to our damage mix, when it answers a one-sided team (#1907). Never part of the order, never on a ban. */
const notes = computed(() => {
  if (props.mode !== 'pick') return new Map()
  const allies = props.allies.map(ally => profileOf(ally.championId, ally.position))
  return new Map(props.suggestions.map(item => [
    item.championId,
    damageNote(allies, profileOf(item.championId, props.position)),
  ]))
})

const search = ref('')
const hovered = ref<number | null>(null)

const shown = computed(() => {
  const query = search.value.trim().toLowerCase()
  return query ? props.suggestions.filter(item => nameOf(item.championId).toLowerCase().includes(query)) : props.suggestions
})

/** What the bans protect, in a few words. */
const targetLabel = computed(() => {
  const target = props.target
  if (props.mode !== 'ban' || !target) return null
  if (target.kind === 'pick' && target.championIds[0]) return `Protecting your ${nameOf(target.championIds[0])}`
  if (target.kind === 'pool') return 'Threats to your pool'
  return 'Most banned on this lane'
})

/**
 * Champions the search names that the podium does not rank — so any champion
 * can be hovered or banned from here, not only the lane's candidates.
 */
const others = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (!query || !writable.value) return []
  const ranked = new Set(props.suggestions.map(item => item.championId))
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
  if (props.error) return { icon: 'i-lucide-wifi-off', text: props.mode === 'ban' ? 'Could not rank the bans' : 'Could not rank the picks' }
  if (tierListStatus.value === 'error') return { icon: 'i-lucide-wifi-off', text: 'The tier list did not load — retrying' }
  if (tierListStatus.value !== 'ready') return { icon: 'i-lucide-loader-circle', text: 'Loading the tier list…' }
  if (props.pending) return { icon: 'i-lucide-loader-circle', text: props.mode === 'ban' ? 'Ranking the bans for this draft…' : 'Ranking the picks for this draft…' }
  if (props.mode === 'ban') return { icon: 'i-lucide-search-x', text: 'No ban to suggest for this draft' }
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
      <h2 class="text-base font-semibold text-highlighted">{{ mode === 'ban' ? 'Your ban' : 'Your pick' }}</h2>
      <UTooltip v-if="targetLabel" text="Never a champion an ally is hovering or has locked">
        <span class="text-xs text-muted">{{ targetLabel }}</span>
      </UTooltip>
      <UIcon v-if="pending" name="i-lucide-loader-circle" class="size-4 text-dimmed" />

      <div class="ml-auto flex items-center gap-3">
        <UTooltip v-if="mode === 'pick'" :text="hasPool ? 'Your ten most-played champions on this lane' : 'Needs the League client: your champions come from it'">
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
        v-for="(item, index) in shown"
        :key="item.championId"
        class="h-full shrink-0"
        @mouseenter="hovered = item.championId"
      >
        <DraftSuggestionCard
          :champion-id="item.championId"
          :reasons="item.reasons"
          :thin="item.thin"
          :position="position"
          :entry="entryOf(item.championId, position)"
          :focused="focused === item.championId"
          :height="heightOf(index)"
          :note="notes.get(item.championId) ?? null"
        >
          <!-- The card's click previews the build; the write is the control over its corner, a separate target. -->
          <button
            type="button"
            class="absolute inset-0 rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-primary"
            :aria-label="`Build for ${nameOf(item.championId)}`"
            @focus="hovered = item.championId"
            @click="emit('preview', item.championId)"
          />
          <template #action>
            <DraftChampionAction :champion-id="item.championId" />
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
