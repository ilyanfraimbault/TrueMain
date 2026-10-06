<!--
  The site's search field (web/app/components/AppSearch.vue, "field" variant,
  through `useTruemainSearch`): type a Riot ID and the true mains matching it
  drop down — icon, name, region, rank, top champions — from
  `GET /truemains/search`, once two letters of the name are in and typing has
  settled. With `champions`, matching champions come first, to filter by. With
  `championId`, only the mains of that champion are offered.
-->
<script setup lang="ts">
import type { SearchResponse, SearchResult } from '#shared/types/search'
import { getProfileIconUrl } from '#shared/utils/ddragon'

const props = withDefaults(defineProps<{
  placeholder?: string
  /** Offer champions too, before the players. */
  champions?: boolean
  /** Keep only the players who main this champion. */
  championId?: number | null
}>(), { placeholder: 'Search a player…', champions: false, championId: null })

const emit = defineEmits<{
  player: [result: SearchResult]
  champion: [championId: number]
}>()

const MIN_LENGTH = 2
const DEBOUNCE_MS = 250

const term = ref('')
const open = ref(false)
const results = ref<SearchResult[]>([])
const status = ref<'idle' | 'pending' | 'ready' | 'error'>('idle')

const { champions: allChampions, portraitOf, patch } = useChampionStatics()
const championById = useStaticChampionsById()

/** The backend searches the name part, before the `#`. */
const namePart = computed(() => term.value.split('#')[0]!.trim())

let timer: ReturnType<typeof setTimeout> | null = null
let latest = 0
watch(term, () => {
  if (timer) clearTimeout(timer)
  if (namePart.value.length < MIN_LENGTH) {
    results.value = []
    status.value = 'idle'
    return
  }
  status.value = 'pending'
  timer = setTimeout(async () => {
    const id = ++latest
    try {
      const answer = await apiGet<SearchResponse>('/truemains/search', { q: term.value.trim(), limit: 20 })
      if (id !== latest) return
      results.value = answer.results
      status.value = 'ready'
    }
    catch {
      if (id === latest) status.value = 'error'
    }
  }, DEBOUNCE_MS)
})

const players = computed(() => {
  const list = props.championId === null ? results.value : results.value.filter(result => result.topChampionIds.includes(props.championId!))
  return list.slice(0, 8)
})

const championMatches = computed(() => {
  if (!props.champions) return []
  const query = term.value.trim().toLowerCase()
  if (query.length < MIN_LENGTH) return []
  return [...allChampions.value.values()].filter(champion => champion.name.toLowerCase().includes(query)).slice(0, 4)
})

function pickPlayer(result: SearchResult) {
  emit('player', result)
  term.value = ''
  open.value = false
}
function pickChampion(id: number) {
  emit('champion', id)
  term.value = ''
  open.value = false
}

const showPanel = computed(() => open.value && term.value.trim().length > 0)
</script>

<template>
  <div class="relative" @focusout="(event) => { if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node)) open = false }">
    <UInput
      v-model="term"
      icon="i-lucide-search"
      :placeholder="placeholder"
      size="sm"
      class="w-full"
      :loading="status === 'pending'"
      @focus="open = true"
      @keydown.escape="open = false"
    />

    <div v-if="showPanel" class="surface absolute inset-x-0 top-full z-50 mt-1 max-h-80 overflow-y-auto rounded-lg p-1 shadow-xl">
      <template v-if="championMatches.length">
        <p class="stat-label px-2 pb-1 pt-1.5">Champions</p>
        <button
          v-for="champion in championMatches"
          :key="champion.id"
          type="button"
          class="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors hover:bg-accented"
          @click="pickChampion(champion.id)"
        >
          <img v-if="portraitOf(champion.id)" :src="portraitOf(champion.id)!" alt="" class="img-skeleton size-6 rounded">
          <span class="text-highlighted">{{ champion.name }}</span>
          <span class="ml-auto text-[11px] text-dimmed">Filter</span>
        </button>
      </template>

      <p v-if="championMatches.length" class="stat-label px-2 pb-1 pt-2">Players</p>
      <p v-if="namePart.length < MIN_LENGTH" class="px-2 py-2 text-xs text-muted">Type at least {{ MIN_LENGTH }} letters of the name.</p>
      <p v-else-if="status === 'error'" class="px-2 py-2 text-xs text-muted">The search failed — try again.</p>
      <p v-else-if="status === 'ready' && !players.length" class="px-2 py-2 text-xs text-muted">
        {{ championId === null ? 'No truemain matches.' : 'No truemain of this champion matches.' }}
      </p>
      <button
        v-for="result in players"
        :key="`${result.region}-${result.identity.gameName}-${result.identity.tagLine}`"
        type="button"
        class="flex w-full items-center gap-2.5 rounded-md px-2 py-1.5 text-left transition-colors hover:bg-accented"
        @click="pickPlayer(result)"
      >
        <SkeletonImage :src="getProfileIconUrl(result.identity.profileIconId, patch)" :width="28" :height="28" class="size-7 shrink-0 rounded" />
        <span class="min-w-0 flex-1">
          <span class="flex items-baseline gap-1">
            <span class="truncate text-sm font-semibold text-highlighted">{{ result.identity.gameName }}</span>
            <span v-if="result.identity.tagLine" class="shrink-0 text-[11px] text-muted">#{{ result.identity.tagLine }}</span>
          </span>
          <LeaderboardRegionFlag :region="result.region" :width="14" />
        </span>
        <RankIcon v-if="result.ranked" :tier="result.ranked.tier" :size="20" />
        <span class="flex gap-0.5">
          <img
            v-for="id in result.topChampionIds.slice(0, 3)"
            :key="id"
            :src="championById.get(id)?.iconUrl"
            :alt="championById.get(id)?.name"
            class="img-skeleton size-5 rounded-sm"
            :class="id === championId ? 'ring-1 ring-primary' : ''"
          >
        </span>
      </button>
    </div>
  </div>
</template>
