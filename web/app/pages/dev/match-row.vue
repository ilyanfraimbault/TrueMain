<script setup lang="ts">
useSeoMeta({
  title: 'MatchRow playground',
  description: 'Isolated visual review of the match history row component.',
})

// The page's own static lookups, under the keys the profile page shares.
const { data: championsData } = useChampionStaticList()
const champions = computed(() => championsData.value ?? [])
const { data: items } = useStaticItems(null)
const { data: summonerSpells } = useStaticSummonerSpells(null)
const { data: runeTree } = useStaticRuneTree(null)

// The fixture rows: the first page of the dev mock's first ladder account
// (run with NUXT_DEV_MOCK_API=1), whose match details the mock also serves.
const FIXTURE_NAME_TAG = 'Sheiden-1234'
const { matches: mockMatches } = useTruemainMatches(FIXTURE_NAME_TAG, ref(1), { pageSize: 3 })

// ─── Live data toggle ──────────────────────────────────────────────────────
// Let the reviewer paste a real nameTag to see the row against live data.

const nameTagInput = ref('')
const liveNameTag = ref('')
const livePage = ref(1)
const {
  matches: liveMatches,
  total: liveTotal,
  pageSize: livePageSize,
  isInitialLoading: liveIsInitialLoading,
  notFound: liveNotFound,
} = useTruemainMatches(liveNameTag, livePage, { pageSize: 5 })

function applyLive() {
  liveNameTag.value = nameTagInput.value.trim()
  livePage.value = 1
}

const staticBundleReady = computed(() =>
  champions.value.length > 0
  && Object.keys(items.value ?? {}).length > 0
  && Object.keys(summonerSpells.value ?? {}).length > 0
  && (runeTree.value?.styles.length ?? 0) > 0,
)
</script>

<template>
  <div class="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8">
    <header class="space-y-1">
      <h1 class="text-2xl font-semibold">
        MatchRow playground
      </h1>
      <p class="text-sm text-muted">
        Isolated visual review for <code>MatchRow</code> — mock fixtures on top, optional live fetch by Riot ID below.
        Click a row to expand the inline detail accordion (scoreboard / details / runes). The fixture rows are
        the dev mock's (<code>NUXT_DEV_MOCK_API=1</code>); live rows fetch the real detail endpoint.
      </p>
    </header>

    <section class="flex flex-col gap-2">
      <h2 class="text-lg font-semibold">
        Mock fixtures
      </h2>
      <template v-if="staticBundleReady">
        <MatchRow
          v-for="match in mockMatches"
          :key="match.matchId"
          :match="match"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
          :name-tag="FIXTURE_NAME_TAG"
        />
      </template>
      <template v-else>
        <MatchRowSkeleton v-for="i in 3" :key="i" />
      </template>
    </section>

    <!-- Narrow containers: MatchRow is an @container, so the only honest way
         to review its compact tiers is to render it in boxes the width of the
         surfaces that use them — the games drawer (#963) gives it ~33rem, a
         sidebar ~28rem, a phone ~21rem. The drawer keeps the compositions
         and drops CS/m; the two narrower boxes keep the bare line (portrait,
         KDA, build, score), the phone with tighter gaps. All three used to overflow into the row's `overflow-hidden`
         clip, because the breakpoints keyed off the viewport rather than the
         row (#967). -->
    <section class="flex flex-col gap-2">
      <h2 class="text-lg font-semibold">
        Narrow containers
      </h2>
      <template v-if="staticBundleReady">
        <div
          v-for="width in ['33rem', '28rem', '21rem']"
          :key="width"
          class="flex flex-col gap-2 rounded-lg border border-dashed border-accented p-2"
          :style="{ width }"
        >
          <p class="text-xs text-muted">
            {{ width }}
          </p>
          <MatchRow
            v-for="match in mockMatches.slice(0, 2)"
            :key="`${width}-${match.matchId}`"
            :match="match"
            :champions="champions"
            :items="items"
            :summoner-spells="summonerSpells"
            :rune-tree="runeTree"
          />
          <MatchRowSkeleton />
        </div>
      </template>
    </section>

    <section class="flex flex-col gap-2">
      <h2 class="text-lg font-semibold">
        Live data
      </h2>
      <div class="flex gap-2">
        <UInput
          v-model="nameTagInput"
          placeholder="GameName-TagLine"
          class="flex-1"
          @keyup.enter="applyLive"
        />
        <UButton color="primary" :disabled="!nameTagInput.trim()" @click="applyLive">
          Fetch
        </UButton>
      </div>

      <template v-if="!liveNameTag">
        <p class="text-sm text-muted">
          Enter a Riot ID (e.g. <code>Phantasm-EUW1</code>) to fetch live matches from
          <code>/api/truemains/{nameTag}/matches</code>.
        </p>
      </template>
      <template v-else-if="liveIsInitialLoading">
        <MatchRowSkeleton v-for="i in 3" :key="`live-skel-${i}`" />
      </template>
      <template v-else-if="liveNotFound">
        <MatchHistoryEmpty not-found />
      </template>
      <template v-else-if="liveMatches.length === 0">
        <MatchHistoryEmpty />
      </template>
      <template v-else>
        <MatchRow
          v-for="match in liveMatches"
          :key="match.matchId"
          :match="match"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
          :name-tag="liveNameTag"
        />
        <UPagination
          v-if="liveTotal > livePageSize"
          v-model:page="livePage"
          :total="liveTotal"
          :items-per-page="livePageSize"
          :sibling-count="1"
          color="neutral"
          variant="ghost"
          active-color="primary"
          active-variant="soft"
          class="self-center"
        />
      </template>
    </section>
  </div>
</template>
