<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { RegionSlug } from '~~/shared/types/leaderboard'

// The homepage title leads with the brand, so opt out of the global
// `%s · TrueMain` template — it would duplicate the name in search results.
useHead({ titleTemplate: null })
useSeoMeta({
  title: 'TrueMain — Champion builds from real mains',
  description: 'League of Legends champion builds, runes and skill orders from true main players.',
})

// Champion summaries for the active patch — drives both the tier-list panel
// and the hero stat chips. Client-only (`server: false`) with a homepage-own
// key: the /champions page keys by filter state, and sharing its key would
// couple the two pages' cache lifecycles for no gain.
const {
  data: summaries,
  status: summariesStatus,
} = useLazyAsyncData<ChampionSummaryResponse[]>(
  'home-champion-summaries',
  () => $fetch<ChampionSummaryResponse[]>('/api/champions'),
  { server: false, default: () => [] },
)

// Shared static list (champion id/name/icon) — same cache key as the unified
// search and the other pages, so the prefetch-warmed payload is reused.
const { data: staticList } = useChampionStaticList()

const championsById = useChampionsById(staticList)

const { data: versions } = useDDragonVersions()
const ddragonPatch = computed(() => versions.value?.[0] ?? null)

// Rune tree + item icons for the truemains teaser's main-champion builds.
const { runeTree, itemsMap } = useBuildAssets(ddragonPatch)

// ─── Truemains teaser (SSR, like the /truemains page) ─────────────────────
const region = ref<RegionSlug | null>(null)
const {
  rows: truemainRows,
  total: truemainsTotal,
  isInitialLoading: truemainsInitialLoading,
  isLoading: truemainsLoading,
} = useTruemainsLeaderboard(1, { pageSize: 5, region })

// "Truemains tracked" chip: read the live total while the region filter is
// off, and fall back to a latched copy once the user flips the region tabs
// (the filtered total is the region's count, not the global one). The latch
// is client-only and the computed reads `total` directly — a watcher-set ref
// would stay null in the SSR HTML (watchers don't flush during SSR) while
// hydration sets it synchronously, a guaranteed node mismatch.
const latchedTotal = ref<number | null>(null)
if (import.meta.client) {
  watch(truemainsTotal, (value) => {
    if (region.value === null && value > 0) latchedTotal.value = value
  }, { immediate: true })
}
const trackedTruemains = computed(() =>
  region.value === null && truemainsTotal.value > 0
    ? truemainsTotal.value
    : latchedTotal.value)

// ─── Hero stat chips — every number is derived from a real payload ────────
const summariesPending = computed(() =>
  summariesStatus.value === 'idle' || summariesStatus.value === 'pending')

const championCount = computed(() =>
  new Set(summaries.value.map(summary => summary.championId)).size)
// Summary rows are per (champion, position); each games count is that
// sample's main-played games, so the sum is "main games analyzed this patch".
const gamesAnalyzed = computed(() =>
  summaries.value.reduce((acc, summary) => acc + summary.games, 0))

// Fixed locale: SSR and the user's browser must format identically or the
// truemains chip (rendered on the server) would hydration-mismatch.
function formatCount(value: number): string {
  return value.toLocaleString('en-US')
}

const steps = [
  {
    step: '01',
    icon: 'i-lucide-radar',
    title: 'We find the real mains',
    description: 'Players who actually one-trick a champion game after game — no smurfs, no off-role fill picks.',
  },
  {
    step: '02',
    icon: 'i-lucide-database',
    title: 'We read their games',
    description: 'Every item, rune and skill order from their ranked games, wins and losses alike.',
  },
  {
    step: '03',
    icon: 'i-lucide-sparkles',
    title: 'You see what wins',
    description: 'Pick a champion and get the builds, runes and skill orders their best mains are winning with right now.',
  },
]
</script>

<template>
  <div>
    <!-- Hero — the global AppBackdrop shader shows through; no per-section
         background of its own. -->
    <section class="relative">
      <div class="relative mx-auto flex max-w-3xl flex-col items-center px-6 pb-16 pt-20 text-center sm:pb-24 sm:pt-28">
        <p class="text-sm font-medium text-primary">
          Champion intelligence
        </p>
        <h1 class="mt-4 text-4xl font-semibold leading-[1.05] tracking-tighter text-highlighted sm:text-6xl">
          Real builds from<br>
          <span class="text-primary">real mains</span>.
        </h1>
        <p class="mt-5 max-w-xl text-base leading-relaxed text-muted sm:text-lg">
          See what real mains build, rune and max — straight from their ranked games.
        </p>

        <AppSearch
          variant="field"
          size="lg"
          class="mt-9 w-full max-w-xl"
        />

        <!-- Stat chips: real numbers only, skeletons until their source
             payload resolves. -->
        <dl class="mt-8 flex flex-wrap items-center justify-center gap-x-7 gap-y-3 text-sm">
          <div class="flex items-center gap-2">
            <UIcon
              name="i-lucide-swords"
              class="size-4 text-primary"
            />
            <USkeleton
              v-if="summariesPending"
              class="h-4 w-28"
            />
            <template v-else-if="championCount > 0">
              <dt class="sr-only">
                Champions ranked
              </dt>
              <dd class="text-muted">
                <span class="font-semibold tabular-nums text-default">{{ formatCount(championCount) }}</span> champions ranked
              </dd>
            </template>
          </div>

          <div class="flex items-center gap-2">
            <UIcon
              name="i-lucide-database"
              class="size-4 text-primary"
            />
            <USkeleton
              v-if="summariesPending"
              class="h-4 w-32"
            />
            <template v-else-if="gamesAnalyzed > 0">
              <dt class="sr-only">
                Main games analyzed
              </dt>
              <dd class="text-muted">
                <span class="font-semibold tabular-nums text-default">{{ formatCount(gamesAnalyzed) }}</span> main games analyzed
              </dd>
            </template>
          </div>

          <div
            v-if="trackedTruemains !== null"
            class="flex items-center gap-2"
          >
            <UIcon
              name="i-lucide-users"
              class="size-4 text-primary"
            />
            <dt class="sr-only">
              Truemains tracked
            </dt>
            <dd class="text-muted">
              <span class="font-semibold tabular-nums text-default">{{ formatCount(trackedTruemains) }}</span> truemains tracked
            </dd>
          </div>
        </dl>
      </div>
    </section>

    <!-- Live data panels — equal-width halves so the two read as a balanced
         pair and the truemains rows have room for champion + play-rate
         without truncating names. -->
    <section class="mx-auto grid max-w-6xl gap-6 px-4 pb-20 md:px-6 lg:grid-cols-2">
      <HomeTierlistPanel
        :summaries="summaries"
        :champions-by-id="championsById"
        :pending="summariesPending"
      />
      <HomeTruemainsPanel
        v-model:region="region"
        :rows="truemainRows"
        :champions-by-id="championsById"
        :rune-tree="runeTree"
        :items-map="itemsMap"
        :initial-loading="truemainsInitialLoading"
        :loading="truemainsLoading"
        :patch="ddragonPatch"
      />
    </section>

    <!-- How it works — three columns, no cards. Transparent so the global
         AppBackdrop reads through; a hairline divider is all the separation
         it needs. -->
    <section
      id="how-it-works"
      class="border-t border-default/60"
    >
      <div class="mx-auto max-w-5xl px-6 py-16 sm:py-20">
        <p class="text-center text-sm font-medium text-primary">
          How it works
        </p>
        <h2 class="mx-auto mt-3 max-w-2xl text-center text-2xl font-semibold tracking-tight sm:text-3xl">
          Real games in, real builds out.
        </h2>

        <div class="mt-12 grid gap-8 sm:grid-cols-3">
          <div
            v-for="step in steps"
            :key="step.step"
            class="space-y-3"
          >
            <div class="flex items-center gap-3">
              <span class="text-sm font-semibold tabular-nums text-primary">{{ step.step }}</span>
              <UIcon
                :name="step.icon"
                class="size-5 text-primary"
              />
            </div>
            <h3 class="text-lg font-semibold text-highlighted">
              {{ step.title }}
            </h3>
            <p class="text-sm leading-relaxed text-muted">
              {{ step.description }}
            </p>
          </div>
        </div>
      </div>
    </section>

    <!-- CTA -->
    <section class="border-t border-default/60">
      <div class="mx-auto max-w-3xl px-6 py-16 text-center sm:py-20">
        <h2 class="text-2xl font-semibold tracking-tight sm:text-3xl">
          Find <span class="text-primary">your</span> real build.
        </h2>
        <p class="mx-auto mt-3 max-w-xl text-base text-muted">
          Open the champion you actually play and see what their mains are buying this patch.
        </p>
        <div class="mt-7 flex flex-wrap justify-center gap-3">
          <UButton
            to="/champions"
            color="primary"
            size="lg"
            icon="i-lucide-swords"
            label="Explore champions"
          />
          <UButton
            to="/truemains"
            color="neutral"
            variant="subtle"
            size="lg"
            icon="i-lucide-trophy"
            label="Truemains leaderboard"
          />
        </div>
      </div>
    </section>
  </div>
</template>
