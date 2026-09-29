<script setup lang="ts">
import type { LeaderboardResponse, LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

/**
 * The site's leaderboard of true mains, by lane, a page at a time, in the
 * site's own rows. A row opens the player's page on the site; a champion opens
 * its builds here.
 */
const PAGE_SIZE = 25

const lane = ref<ChampionPosition | null>(null)
const rows = ref<LeaderboardRowResponse[]>([])
const total = ref(0)
const page = ref(0)
const pending = ref(false)
const failed = ref(false)

let latest = 0

async function load(next: number) {
  const id = ++latest
  pending.value = true
  failed.value = false
  try {
    const answer = await apiGet<LeaderboardResponse>('/truemains', { page: next, pageSize: PAGE_SIZE, position: lane.value })
    if (id !== latest) return
    rows.value = next === 1 ? answer.rows : [...rows.value, ...answer.rows]
    total.value = answer.total
    page.value = next
  }
  catch {
    if (id === latest) failed.value = true
  }
  finally {
    if (id === latest) pending.value = false
  }
}

watch(lane, () => load(1), { immediate: true })

const more = computed(() => rows.value.length < total.value)
const deepestRank = computed(() => rows.value.reduce((max, row) => Math.max(max, row.rank), 0))

const championsById = useChampionsById()
const { runeTree, items } = useStaticData()
const { patch } = useChampionStatics()
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-6">
    <PageHeader title="Truemains" icon="i-lucide-trophy">
      <span v-if="total" class="stat-label tabular-nums">{{ total.toLocaleString('en-US') }} players</span>
    </PageHeader>

    <RolePicker v-model:position="lane" class="self-start" />

    <div class="flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto pr-1">
      <LeaderboardRow
        v-for="row in rows"
        :key="`${row.region}-${row.identity.gameName}-${row.identity.tagLine}`"
        :row="row"
        :champions-by-id="championsById"
        :rune-tree="runeTree"
        :items-map="items"
        :patch="patch"
        :max-rank="deepestRank"
      />

      <template v-if="pending && !rows.length">
        <LeaderboardRowSkeleton v-for="index in 8" :key="index" />
      </template>
      <p v-else-if="failed && !rows.length" class="p-8 text-center text-sm text-muted">The leaderboard could not be loaded</p>

      <div v-if="more && rows.length" class="flex justify-center p-3">
        <UButton label="Load more" color="neutral" variant="subtle" size="sm" :loading="pending" @click="load(page + 1)" />
      </div>
    </div>
  </div>
</template>
