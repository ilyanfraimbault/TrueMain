<script setup lang="ts">
import type {
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { describeFetchError } from '~/utils/errors'

// Player-scoped mirror of pages/champions/[id].vue. The static-data fetches,
// loading bar and build tabs are intentionally identical so the page looks
// exactly like the global champion page; the ONLY difference is that
// useChampion is given the route's nameTag, which swaps the data source to
// /api/truemains/{nameTag}/champions/{id} (every aggregate scoped to this
// player's games). Keeping the static fetches aligned (same keys) with both
// the global champion page and the profile page keeps Nuxt's patch-keyed
// caches deduped across navigations.
const route = useRoute()

const championId = computed(() => Number.parseInt(String(route.params.id), 10))
const nameTag = computed(() => {
  const param = route.params.nameTag
  return Array.isArray(param) ? param[0] ?? '' : (param ?? '')
})

const { filters, setFilter } = useChampionFilters()
const nuxtApp = useNuxtApp()

const {
  data: champion,
  error: championError,
  status: championStatus,
  notEnoughData,
} = useChampion(championId, filters, { nameTag })

// A 404 here is the expected "not enough games" empty state (handled below),
// so useChampion never raises it. Anything that does reach championError is a
// real failure — surface it as a toast on top of the inline alert.
useErrorToast(championError, { title: 'Failed to load champion' })

// Identity for the breadcrumb / header fallback. Cheap and client-cached —
// the profile page primes the same request, so this rarely hits the network.
const { data: profile } = useTruemainProfile(nameTag)
const playerLabel = computed(() => {
  const identity = profile.value?.identity
  if (!identity) return nameTag.value
  return identity.tagLine ? `${identity.gameName}#${identity.tagLine}` : identity.gameName
})
const profilePath = computed(() => `/truemains/${encodeURIComponent(nameTag.value)}`)

const activePatch = computed(() => champion.value?.patch || filters.value.patch || null)

const { data: staticData, status: staticStatus } = useChampionStatic(championId, activePatch)
const { data: versions } = useDDragonVersions()

const { data: staticList, status: staticListStatus } = useChampionStaticList()
const { data: runeTree, status: runeTreeStatus } = useLazyAsyncData<RuneTreeResponse>(
  () => `rune-tree-${activePatch.value || 'latest'}`,
  async () => {
    const key = `rune-tree-${activePatch.value || 'latest'}`
    const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)
const { data: itemsMap, status: itemsStatus } = useLazyAsyncData<Record<number, StaticItemData>>(
  () => `static-items-${activePatch.value || 'latest'}`,
  async () => {
    const key = `static-items-${activePatch.value || 'latest'}`
    const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)
const { data: summonersMap, status: summonersStatus } = useLazyAsyncData<Record<number, StaticSummonerSpellData>>(
  () => `static-summoners-${activePatch.value || 'latest'}`,
  async () => {
    const key = `static-summoners-${activePatch.value || 'latest'}`
    const data = await $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)

const championListEntry = computed(() =>
  (staticList.value ?? []).find(item => item.championId === championId.value) ?? null,
)
const displayName = computed(() =>
  staticData.value?.championName || championListEntry.value?.name || null,
)
const displayIconUrl = computed(() =>
  staticData.value?.championIconUrl || championListEntry.value?.iconUrl || null,
)

useSeoMeta({
  title: () => {
    const champ = displayName.value ?? `Champion ${championId.value}`
    return `${champ} · ${playerLabel.value} · TrueMain`
  },
  description: () => `How ${playerLabel.value} plays ${displayName.value ?? `champion ${championId.value}`}: their build path, runes and skill order.`,
})

const patchOptions = computed(() => {
  const seen = new Set<string>(
    (versions.value ?? [])
      .map(p => p.split('.').slice(0, 2).join('.'))
      .filter(Boolean)
      .slice(0, 12),
  )
  if (champion.value?.patch) seen.add(champion.value.patch)
  if (filters.value.patch) seen.add(filters.value.patch)
  return [...seen]
    .map(p => ({ label: p, value: p }))
    .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
})

const selectedPatch = computed(() => filters.value.patch || champion.value?.patch || '')
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = champion.value?.position || filters.value.position || ''
  return isChampionPosition(value) ? value : null
})

const isLoadingStatus = (s: 'idle' | 'pending' | 'success' | 'error') => s === 'idle' || s === 'pending'
const isRefetching = computed(() =>
  isLoadingStatus(championStatus.value)
  || isLoadingStatus(staticStatus.value)
  || isLoadingStatus(staticListStatus.value)
  || isLoadingStatus(runeTreeStatus.value)
  || isLoadingStatus(itemsStatus.value)
  || isLoadingStatus(summonersStatus.value),
)

// Patch for the profile icon in the player header.
const latestPatch = computed(() => versions.value?.[0] ?? null)

// ─── Match history ─────────────────────────────────────────────────────────
// This player's recent games on THIS champion. The champion is fixed to the
// page; the lane filter is its OWN control, independent of the build's position
// filter, so you can browse games on any lane without re-scoping the build.
const matchesPage = ref(1)
const matchPosition = ref<ChampionPosition | null>(null)
const {
  matches,
  total: matchesTotal,
  pageSize: matchesPageSize,
  isInitialLoading: matchesInitialLoading,
  notFound: matchesNotFound,
} = useTruemainMatches(nameTag, matchesPage, {
  championId,
  position: matchPosition,
})
function setMatchesPage(next: number) {
  matchesPage.value = Math.max(1, Math.floor(next))
}
function setMatchPosition(next: ChampionPosition | null) {
  matchPosition.value = next
  matchesPage.value = 1
}

const staticBundleReady = computed(() =>
  Boolean(staticList.value && itemsMap.value && summonersMap.value && runeTree.value),
)
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
    <!-- Breadcrumb: Truemain {name} > {champion}, linking back to the profile. -->
    <nav aria-label="Breadcrumb" class="text-sm text-muted">
      <ol class="flex flex-wrap items-center gap-1.5">
        <li>
          <NuxtLink
            :to="profilePath"
            class="rounded text-primary transition-colors hover:text-primary/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            Truemain {{ playerLabel }}
          </NuxtLink>
        </li>
        <li aria-hidden="true" class="text-muted/60">
          /
        </li>
        <li class="truncate font-medium text-default">
          {{ displayName ?? `Champion ${championId}` }}
        </li>
      </ol>
    </nav>

    <!-- Player identity up top so it's obvious this is the truemain's page,
         not the global champion page. -->
    <ProfileHeaderSkeleton v-if="!profile" />
    <ProfileHeader
      v-else
      :identity="profile.identity"
      :patch="latestPatch"
    />

    <div class="h-0.5">
      <UProgress
        v-if="isRefetching"
        size="xs"
        color="primary"
        aria-label="Loading champion"
      />
    </div>

    <UAlert
      v-if="championError"
      color="error"
      variant="soft"
      title="Failed to load champion"
      :description="describeFetchError(championError)"
    />

    <!--
      Empty / fallback state: the player has fewer than the backend's
      min-games floor on this champion (or the account is unknown). We show a
      small notice rather than fabricating a build from one or two games, and
      point back to the global champion page for the meta view.
    -->
    <div
      v-else-if="notEnoughData && !isRefetching"
      class="flex flex-col items-center gap-3 glass rounded-lg px-6 py-12 text-center"
    >
      <SkeletonImage
        v-if="displayIconUrl"
        :src="displayIconUrl"
        :alt="displayName ?? ''"
        width="64"
        height="64"
        class="size-16 rounded opacity-80"
      />
      <div class="space-y-1">
        <p class="text-sm font-medium text-default">
          Not enough games to build a profile
        </p>
        <p class="text-sm text-muted">
          {{ playerLabel }} hasn't played {{ displayName ?? 'this champion' }} enough for a
          personal build breakdown yet.
        </p>
      </div>
      <NuxtLink
        :to="`/champions/${championId}`"
        class="rounded text-sm text-primary transition-colors hover:text-primary/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      >
        See the global build for {{ displayName ?? `champion ${championId}` }}
      </NuxtLink>
    </div>

    <template v-else-if="champion && staticData">
      <header class="flex flex-wrap items-center gap-4">
        <ChampionHeader
          :champion-name="displayName"
          :champion-icon-url="displayIconUrl"
          :champion-id="championId"
          :position="champion.position"
          :total-games="champion.totalGames"
          :total-wins="champion.totalWins"
        />
        <ChampionFilters
          :selected-patch="selectedPatch"
          :selected-position="selectedPosition"
          :patch-options="patchOptions"
          @update:patch="value => setFilter({ patch: value })"
          @update:position="value => setFilter({ position: value })"
        />
      </header>

      <ChampionBuildTabs
        :builds="champion.builds"
        :champion-static="staticData"
        :items-map="itemsMap ?? {}"
        :summoners-map="summonersMap ?? {}"
        :rune-tree="runeTree ?? null"
      />

      <ChampionMatchups
        :champion-id="championId"
        :position="selectedPosition"
        :champions="staticList ?? []"
        :name-tag="nameTag"
      />

      <!-- This player's recent games on this champion. The champion is fixed;
           the lane filter is its own RolePicker, independent of the build's
           position filter above. -->
      <section class="flex min-w-0 flex-col gap-3">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
            Recent {{ displayName ?? '' }} games
          </h2>
          <RolePicker
            :position="matchPosition"
            @update:position="setMatchPosition"
          />
        </div>

        <template v-if="matchesInitialLoading || !staticBundleReady">
          <MatchRowSkeleton v-for="i in 5" :key="`match-skel-${i}`" />
        </template>
        <template v-else-if="matchesNotFound || matches.length === 0">
          <MatchHistoryEmpty :not-found="matchesNotFound" />
        </template>
        <template v-else>
          <MatchRow
            v-for="match in matches"
            :key="match.matchId"
            :match="match"
            :champions="staticList ?? []"
            :items="itemsMap ?? {}"
            :summoner-spells="summonersMap ?? {}"
            :rune-tree="runeTree!"
          />
          <div
            v-if="matchesTotal > matchesPageSize"
            class="flex justify-center pt-2"
          >
            <UPagination
              :page="matchesPage"
              :total="matchesTotal"
              :items-per-page="matchesPageSize"
              :sibling-count="1"
              color="neutral"
              variant="ghost"
              active-color="primary"
              active-variant="soft"
              @update:page="setMatchesPage"
            />
          </div>
        </template>
      </section>
    </template>
  </main>
</template>
