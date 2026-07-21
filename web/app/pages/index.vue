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

// ─── Below-the-fold tier-list gating ─────────────────────────────────────
// The tier-list panel is a heavy, below-the-fold client-only render (its rows,
// names and icons all come from `server: false` sources), so its JS chunk is
// dead weight on the initial critical path. Gate it behind a visibility flag:
// `<LazyHomeTierlistPanel>` is a dynamic import, so its chunk isn't fetched
// until `tierlistVisible` flips true. The flag starts `false` on both the
// server and the client's first render (an IntersectionObserver — client-only —
// flips it after mount), so the `v-if`/`v-else` branch is identical on each
// side and hydration stays clean. The summaries fetch itself stays eager: it
// also feeds the above-the-fold hero chips, so gating the *fetch* would leave
// those chips skeletoned until scroll — only the panel *render* is deferred.
const tierlistAnchor = ref<HTMLElement | null>(null)
const tierlistVisible = ref(false)
onMounted(() => {
  const el = tierlistAnchor.value
  if (!el || typeof IntersectionObserver === 'undefined') {
    tierlistVisible.value = true
    return
  }
  const observer = new IntersectionObserver((entries) => {
    if (entries.some(entry => entry.isIntersecting)) {
      tierlistVisible.value = true
      observer.disconnect()
    }
  }, { rootMargin: '200px' })
  observer.observe(el)
  onBeforeUnmount(() => observer.disconnect())
})

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
          Builds, runes and skill orders from the players who truly mastered your champion.
        </p>

        <!-- Search is SSR'd as a plain field, but its command-palette JS
             (UModal + UCommandPalette + the truemains search composable) is
             heavy and never needed for the first paint. Delay its hydration to
             browser-idle so that chunk stays off the critical path. Safe from a
             mismatch: on the homepage the trigger renders no client-only data
             (no champion filter here), so the SSR markup and the eventual
             client render are identical. ⌘K is owned by the always-mounted
             header instance, so deferring this one doesn't affect the shortcut. -->
        <LazyAppSearch
          variant="field"
          size="lg"
          class="mt-9 w-full max-w-xl"
          hydrate-on-idle
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
      <!-- Tier list: rendered only once its anchor nears the viewport, so the
           panel's chunk is fetched on demand. Until then a skeleton that mirrors
           the panel chrome holds the layout (no shift when it swaps in). -->
      <div ref="tierlistAnchor">
        <LazyHomeTierlistPanel
          v-if="tierlistVisible"
          :summaries="summaries"
          :champions-by-id="championsById"
          :pending="summariesPending"
        />
        <section
          v-else
          class="glass rounded-2xl p-3 sm:p-4"
          aria-hidden="true"
        >
          <header class="flex items-center justify-between gap-3 pb-3">
            <span class="text-sm font-semibold text-default">Tier list</span>
            <UButton
              to="/champions"
              color="neutral"
              variant="ghost"
              size="sm"
              trailing-icon="i-lucide-arrow-right"
              label="Full tier list"
            />
          </header>
          <div class="space-y-1">
            <div
              v-for="i in 8"
              :key="i"
              class="-mx-2 flex items-center gap-3 rounded-lg px-2 py-2"
            >
              <USkeleton class="size-9 rounded-lg" />
              <USkeleton class="h-4 w-32" />
              <USkeleton class="ml-auto h-4 w-24" />
            </div>
          </div>
        </section>
      </div>
      <!-- Truemains teaser stays eagerly SSR'd + immediately hydrated: its rows
           come from a `server: true` fetch, and its profile-icon `v-if`/`v-else`
           and champion enrichment resolve from `server: false` sources, so
           delaying its hydration would flip those branches after the data lands
           and cause a structural hydration mismatch. The ~373 KiB item map it
           needs is instead deferred inside the panel (visibility-gated fetch). -->
      <HomeTruemainsPanel
        v-model:region="region"
        :rows="truemainRows"
        :champions-by-id="championsById"
        :initial-loading="truemainsInitialLoading"
        :loading="truemainsLoading"
        :patch="ddragonPatch"
      />
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
