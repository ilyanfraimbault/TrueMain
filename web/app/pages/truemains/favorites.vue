<script setup lang="ts">
// Followed truemains + their latest games (#531). The list is browser-local
// (localStorage) until Riot SSO lands, so the page is personal: it must never
// be indexed, and it must not claim "no favorites" before storage has been read.

const MATCHES_PER_FAVORITE = 3

useSeoMeta({
  title: 'Favorites',
  description: 'The truemains you follow in this browser, with their latest ranked games.',
})

// Per-visitor content — nothing here is meaningful to a crawler, and the page
// is excluded from the sitemap in nuxt.config.
useRobotsRule('noindex, follow')

const { favorites, count, hydrated, clear } = useFavoriteTruemains()

const breadcrumbItems = [
  { label: 'Truemains', to: '/truemains' },
  { label: 'Favorites' },
]

// ─── Clear all ─────────────────────────────────────────────────────────────
// Two-step, because wiping the list is destructive and unrecoverable: there is
// no undo and no server copy to restore from, so a misclick would cost someone
// every player they follow. The repo has no confirmation-dialog convention yet,
// so this stays in the button itself rather than introducing a modal for one
// action. The armed state disarms itself so it can't be left hanging.
const CLEAR_CONFIRM_TIMEOUT_MS = 5000

const clearArmed = ref(false)
let disarmTimer: ReturnType<typeof setTimeout> | null = null

function disarmClear() {
  if (disarmTimer) {
    clearTimeout(disarmTimer)
    disarmTimer = null
  }
  clearArmed.value = false
}

function onClearClick() {
  if (clearArmed.value) {
    disarmClear()
    clear()
    return
  }
  clearArmed.value = true
  disarmTimer = setTimeout(disarmClear, CLEAR_CONFIRM_TIMEOUT_MS)
}

onBeforeUnmount(disarmClear)

// ─── Static bundle for MatchRow ────────────────────────────────────────────
// Fetched once here and passed into every card, under the same canonical cache
// keys the profile and champion pages use — so navigating in from either
// reuses the payload instead of refetching it per card.
const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

const { data: championsData } = useChampionStaticList()
const champions = computed(() => championsData.value ?? [])

const { data: itemsData } = useStaticItems(latestPatch)
const items = computed(() => itemsData.value ?? {})

const { data: summonerSpellsData } = useStaticSummonerSpells(latestPatch)
const summonerSpells = computed(() => summonerSpellsData.value ?? {})

const { data: runeTreeData } = useStaticRuneTree(latestPatch)
const runeTree = computed(() => runeTreeData.value ?? null)
</script>

<template>
  <main class="mx-auto w-full max-w-7xl space-y-6 p-4 md:p-6">
    <UBreadcrumb :items="breadcrumbItems" />

    <PageHeader
      eyebrow="Truemains"
      title="Favorites"
      description="Players you follow, with their latest ranked games. Saved in this browser only — signing in with Riot will sync them across devices later."
    >
      <div v-if="hydrated && count > 0" class="flex flex-wrap items-center gap-3">
        <span class="text-sm text-muted tabular-nums">{{ count }} followed</span>
        <UButton
          size="xs"
          :color="clearArmed ? 'error' : 'neutral'"
          :variant="clearArmed ? 'soft' : 'ghost'"
          icon="i-lucide-trash-2"
          :label="clearArmed ? `Unfollow all ${count}?` : 'Clear all'"
          :aria-label="clearArmed
            ? `Confirm unfollowing all ${count} players`
            : `Clear all ${count} favorites`"
          @click="onClearClick"
          @blur="disarmClear"
        />
        <!-- role=status so the armed state is announced, not just recoloured. -->
        <span v-if="clearArmed" role="status" class="text-xs text-muted">
          Click again to confirm — this can't be undone.
        </span>
      </div>
    </PageHeader>

    <!--
      `hydrated` is false during SSR *and* during the client's hydration render
      (the shared state starts empty and is only filled in onMounted), so the
      skeletons below are exactly what the server emitted — no mismatch. The
      real list appears as a normal post-hydration update.
    -->
    <div v-if="!hydrated" class="space-y-4">
      <FavoritesPlayerCardSkeleton
        v-for="i in 2"
        :key="`fav-page-skel-${i}`"
        :match-count="MATCHES_PER_FAVORITE"
      />
    </div>

    <div v-else-if="favorites.length === 0" class="glass rounded-lg px-6 py-12 text-center">
      <UIcon name="i-lucide-star" class="size-8 text-primary" aria-hidden="true" />
      <p class="mt-3 text-base font-semibold">
        No favorites yet
      </p>
      <p class="mx-auto mt-1 max-w-md text-sm text-muted">
        Follow a player from the leaderboard or their profile and their latest games show up here.
      </p>
      <UButton
        to="/truemains"
        class="mt-4"
        color="primary"
        variant="soft"
        icon="i-lucide-trophy"
        label="Browse the leaderboard"
      />
    </div>

    <div v-else class="space-y-4">
      <FavoritesPlayerCard
        v-for="favorite in favorites"
        :key="favorite.nameTag"
        :favorite="favorite"
        :champions="champions"
        :items="items"
        :summoner-spells="summonerSpells"
        :rune-tree="runeTree"
        :patch="latestPatch"
        :match-count="MATCHES_PER_FAVORITE"
      />
    </div>
  </main>
</template>
