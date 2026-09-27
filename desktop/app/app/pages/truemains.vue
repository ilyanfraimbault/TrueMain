<script setup lang="ts">
import type { Lane } from '~/types/draft'
import type { LeaderboardResponse, TruemainRow } from '~/types/truemains'

/**
 * The site's leaderboard of true mains, by lane, a page at a time. A row opens
 * the player's page on the site; a champion opens its builds here.
 */
const PAGE_SIZE = 25

const lane = ref<Lane | null>(null)
const rows = ref<TruemainRow[]>([])
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
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-6">
    <PageHeader title="Truemains" icon="i-lucide-trophy">
      <span v-if="total" class="stat-label tabular-nums">{{ total.toLocaleString('en-US') }} players</span>
    </PageHeader>

    <LaneTabs v-model="lane" all labels class="self-start" />

    <div class="surface flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl">
      <div class="grid grid-cols-[2.5rem_minmax(0,1fr)_10rem_7.5rem_9rem_2rem] items-center gap-3 border-b border-default px-4 py-2.5">
        <span class="stat-label">#</span>
        <span class="stat-label">Player</span>
        <span class="stat-label">Rank</span>
        <span class="stat-label">Season</span>
        <span class="stat-label">Mains</span>
        <span />
      </div>

      <div class="min-h-0 flex-1 overflow-y-auto">
        <TruemainRow v-for="row in rows" :key="`${row.identity.platformId}-${row.identity.gameName}-${row.identity.tagLine}`" :row="row" />

        <div v-if="pending && !rows.length" class="flex flex-col gap-2 p-4">
          <USkeleton v-for="index in 8" :key="index" class="h-11 w-full" />
        </div>
        <p v-else-if="failed && !rows.length" class="p-8 text-center text-sm text-muted">The leaderboard could not be loaded</p>

        <div v-if="more && rows.length" class="flex justify-center p-3">
          <UButton label="Load more" color="neutral" variant="subtle" size="sm" :loading="pending" @click="load(page + 1)" />
        </div>
      </div>
    </div>
  </div>
</template>
