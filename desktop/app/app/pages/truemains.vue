<script setup lang="ts">
import type { LeaderboardResponse, LeaderboardRowResponse, LeaderboardSort, RegionSlug } from '~~/shared/types/leaderboard'
import type { SearchResult } from '~~/shared/types/search'
import type { ChampionPosition } from '~/utils/positions'
import { TRUEMAINS_GRID } from '~/utils/truemains-table'

/**
 * The site's leaderboard of true mains (web/app/pages/truemains/index.vue) —
 * its search, its filters (lane, OTP only, sort, region), a champion to narrow
 * to, its pagination — drawn as a table whose columns line up under their
 * headers. A row opens the player's page on the site; a champion opens its
 * builds here.
 */
const PAGE_SIZE = 25

const lane = ref<ChampionPosition | null>(null)
const region = ref<RegionSlug | null>(null)
const otpOnly = ref(false)
const sort = ref<LeaderboardSort>('rank')
const championId = ref<number | null>(null)
const page = ref(1)

const rows = ref<LeaderboardRowResponse[]>([])
const total = ref(0)
const pending = ref(false)
const failed = ref(false)
let latest = 0

async function load() {
  const id = ++latest
  pending.value = true
  failed.value = false
  try {
    const answer = await apiGet<LeaderboardResponse>('/truemains', {
      page: page.value,
      pageSize: PAGE_SIZE,
      position: lane.value,
      region: region.value,
      championId: championId.value,
      otpOnly: otpOnly.value || null,
      sort: sort.value === 'dedication' ? 'dedication' : null,
    })
    if (id !== latest) return
    rows.value = answer.rows
    total.value = answer.total
  }
  catch {
    if (id === latest) failed.value = true
  }
  finally {
    if (id === latest) pending.value = false
  }
}

// Any filter change starts again from the first page, as on the site.
watch([lane, region, otpOnly, sort, championId], () => {
  if (page.value !== 1) page.value = 1
  else void load()
})
watch(page, () => void load(), { immediate: true })

const list = ref<HTMLElement | null>(null)
watch(page, () => list.value?.scrollTo({ top: 0 }))

const championsById = useChampionsById()
const { runeTree, items } = useStaticData()
const { patch, nameOf, portraitOf } = useChampionStatics()

function openPlayer(result: SearchResult) {
  const { gameName, tagLine } = result.identity
  void openOnSite(`/truemains/${encodeURIComponent(tagLine ? `${gameName}-${tagLine}` : gameName)}`)
}
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-6">
    <PageHeader title="Truemains" icon="i-lucide-trophy">
      <span v-if="total" class="stat-label tabular-nums">{{ total.toLocaleString('en-US') }} players</span>
    </PageHeader>

    <div class="flex items-center gap-3">
      <LeaderboardTruemainSearch
        champions
        placeholder="Search a champion or player…"
        class="flex-1"
        @champion="championId = $event"
        @player="openPlayer"
      />
      <UButton
        v-if="championId !== null"
        color="neutral"
        variant="soft"
        size="sm"
        trailing-icon="i-lucide-x"
        :aria-label="`Stop narrowing to ${nameOf(championId)}`"
        @click="championId = null"
      >
        <img v-if="portraitOf(championId)" :src="portraitOf(championId)!" alt="" class="size-5 rounded-sm">
        {{ nameOf(championId) }}
      </UButton>
    </div>

    <LeaderboardFilters
      :region="region"
      :position="lane"
      :otp-only="otpOnly"
      :sort="sort"
      @update:region="region = $event"
      @update:position="lane = $event"
      @update:otp-only="otpOnly = $event"
      @update:sort="sort = $event"
    />

    <div class="surface flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl">
      <div class="grid items-center gap-2 border-b border-default px-4 py-2.5" :class="TRUEMAINS_GRID">
        <span class="stat-label">#</span>
        <span class="stat-label">Player</span>
        <span class="stat-label text-center">Lanes</span>
        <span class="stat-label text-center">Champion</span>
        <span class="stat-label text-right">Score</span>
        <span class="stat-label text-center">Rank</span>
        <span class="stat-label text-right">Games</span>
        <span class="stat-label text-right">KDA</span>
        <span class="stat-label text-right">WR</span>
        <span />
      </div>

      <div ref="list" class="min-h-0 flex-1 overflow-y-auto" :class="pending && rows.length ? 'opacity-60 transition-opacity' : ''">
        <LeaderboardTruemainsTableRow
          v-for="row in rows"
          :key="`${row.region}-${row.identity.gameName}-${row.identity.tagLine}`"
          :row="row"
          :champions-by-id="championsById"
          :rune-tree="runeTree"
          :items-map="items"
          :patch="patch"
          :highlight-dedication="sort === 'dedication'"
        />

        <div v-if="pending && !rows.length" class="flex flex-col gap-2 p-4">
          <USkeleton v-for="index in 8" :key="index" class="h-10 w-full" />
        </div>
        <p v-else-if="failed" class="p-8 text-center text-sm text-muted">The leaderboard could not be loaded</p>
        <p v-else-if="!rows.length" class="p-8 text-center text-sm text-muted">No truemains match these filters yet.</p>
      </div>
    </div>

    <div v-if="total > PAGE_SIZE" class="flex justify-center">
      <UPagination
        v-model:page="page"
        :total="total"
        :items-per-page="PAGE_SIZE"
        :sibling-count="1"
        color="neutral"
        variant="ghost"
        active-color="primary"
        active-variant="soft"
        size="sm"
      />
    </div>
  </div>
</template>
