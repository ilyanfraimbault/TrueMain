<script setup lang="ts">
// Followed truemains + their latest games (#531), then the starred champions
// (#2080), as the site and the desktop app both render it (#1732).
// The lists are local to the device (localStorage) until Riot SSO lands, so the
// page must not claim "no favorites" before storage has been read.
//
// A host with favorites of its own kind (the app's clips) puts them in the
// `extra` slot and counts them in `extraCount`, so the empty state only shows
// when there is nothing at all. They are the host's to clear, not this page's.
const props = withDefaults(defineProps<{
  extraCount?: number
}>(), {
  extraCount: 0,
})

const MATCHES_PER_FAVORITE = 3

const { favorites, count: playerCount, hydrated: playersHydrated, clear: clearPlayers } = useFavoriteTruemains()
const { champions: favoriteChampions, championsHydrated, clearChampions } = useFavoriteChampions()

const hydrated = computed(() => playersHydrated.value && championsHydrated.value)
/** What "Clear all" clears: the players and the champions, never the host's own. */
const count = computed(() => playerCount.value + favoriteChampions.value.length)

function clear() {
  clearPlayers()
  clearChampions()
}

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
const championsById = useChampionsById(champions)

const { data: itemsData } = useStaticItems(latestPatch)
const items = computed(() => itemsData.value ?? {})

const { data: summonerSpellsData } = useStaticSummonerSpells(latestPatch)
const summonerSpells = computed(() => summonerSpellsData.value ?? {})

const { data: runeTreeData } = useStaticRuneTree(latestPatch)
const runeTree = computed(() => runeTreeData.value ?? null)
</script>

<template>
  <div class="mx-auto w-full max-w-7xl space-y-6 p-4 md:p-6">
    <UBreadcrumb :items="breadcrumbItems" />

    <PageHeader
      eyebrow="Truemains"
      title="Favorites"
      description="Saved on this device only."
    >
      <div v-if="hydrated && count > 0" class="flex flex-wrap items-center gap-3">
        <span class="text-sm text-muted tabular-nums">{{ count }} saved</span>
        <UButton
          size="xs"
          :color="clearArmed ? 'error' : 'neutral'"
          :variant="clearArmed ? 'soft' : 'ghost'"
          icon="i-lucide-trash-2"
          :label="clearArmed ? `Remove all ${count}?` : 'Clear all'"
          :aria-label="clearArmed
            ? `Confirm removing all ${count} favorites`
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

    <UEmpty
      v-else-if="count === 0 && props.extraCount === 0"
      icon="i-lucide-star"
      title="No favorites yet"
      description="Follow a player, or star a champion from its page, and it shows up here."
      :actions="[
        { to: '/truemains', color: 'primary', variant: 'soft', icon: 'i-lucide-trophy', label: 'Browse the leaderboard' },
        { to: '/champions', color: 'neutral', variant: 'ghost', icon: 'i-lucide-swords', label: 'Browse champions' },
      ]"
    />

    <template v-else>
      <section v-if="favorites.length > 0" class="space-y-3" aria-labelledby="favorites-players">
        <h2 id="favorites-players" class="eyebrow">
          Players
        </h2>
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
      </section>

      <section v-if="favoriteChampions.length > 0" class="space-y-3" aria-labelledby="favorites-champions">
        <h2 id="favorites-champions" class="eyebrow">
          Champions
        </h2>
        <div class="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          <FavoritesChampionRow
            v-for="favorite in favoriteChampions"
            :key="`${favorite.championId}:${favorite.position}`"
            :favorite="favorite"
            :champions-by-id="championsById"
          />
        </div>
      </section>

      <slot name="extra" />
    </template>
  </div>
</template>
