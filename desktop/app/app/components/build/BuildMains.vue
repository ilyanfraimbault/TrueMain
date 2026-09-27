<script setup lang="ts">
import type { LeaderboardResponse, LeaderboardRowResponse } from '~~/shared/types/leaderboard'

/**
 * The champion's best true mains — the site's champion-page sidebar
 * (`Champion/Truemains.vue`): the leaderboard filtered to the champion, each
 * row the site's own `LeaderboardRow`, compact in a narrow column. Where the
 * reference apps list pro players, TrueMain lists the people who main the
 * champion.
 */
const props = defineProps<{ championId: number }>()

const TOP_N = 5

const cache = useState<Record<number, LeaderboardRowResponse[]>>('champion-mains', () => ({}))
const failed = ref(false)

const rows = computed(() => cache.value[props.championId] ?? null)
const deepestRank = computed(() => (rows.value ?? []).reduce((max, row) => Math.max(max, row.rank), 0))

watch(() => props.championId, async (id) => {
  failed.value = false
  if (cache.value[id]) return
  try {
    const answer = await apiGet<LeaderboardResponse>('/truemains', { championId: id, pageSize: TOP_N })
    cache.value = { ...cache.value, [id]: answer.rows }
  }
  catch {
    failed.value = true
  }
}, { immediate: true })

const championsById = useChampionsById()
const { runeTree, items } = useStaticData()
const { patch } = useChampionStatics()
</script>

<template>
  <div class="flex flex-col gap-1">
    <h3 class="px-1 pb-1 stat-label">Truemains</h3>

    <template v-if="rows === null && !failed">
      <LeaderboardRowSkeleton v-for="index in 3" :key="index" />
    </template>
    <p v-else-if="failed" class="px-1 text-xs text-muted">The truemains could not be loaded</p>
    <UEmpty v-else-if="rows && rows.length === 0" size="sm" icon="i-lucide-trophy" description="No tracked truemains on this champion yet." />
    <template v-else>
      <LeaderboardRow
        v-for="row in rows ?? []"
        :key="row.rank"
        :row="row"
        :champions-by-id="championsById"
        :rune-tree="runeTree"
        :items-map="items"
        :patch="patch"
        :max-rank="deepestRank"
      />
    </template>
  </div>
</template>
