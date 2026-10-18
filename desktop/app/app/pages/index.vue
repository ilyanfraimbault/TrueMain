<script setup lang="ts">
import type { GameflowPhase } from '~/types/lcu'
import type { QueueFilter } from '~/utils/player-form'
import type { GoalDraft } from '~/utils/goals'
import { QUEUE_FILTERS, championLines, counted, gamesIn, laneLines } from '~/utils/player-form'
import { toMatchSummary, toProfileRanked } from '~/utils/match-summary'
import { activityFromGames } from '~/utils/activity-from-games'

/**
 * Home: the player, read from their own client, on the profile page the site
 * shows for a true main (`PagePlayerProfile`): the banner on the skin they chose
 * in the client, their form over the latest games, a match history whose rows
 * open the shared detail panel, and their ranked card, activity, goals,
 * champions and roles beside it. The banner and the tiles read the latest page
 * of games; the history pages further back. It needs no account on TrueMain:
 * the client knows every player, TrueMain only the true mains (#1682). With no
 * client there is nothing personal to show, so the patch's best picks stand in.
 */
const { state } = useLcuState()
const { record, games: loadedGames, rankHistory, status, refresh, hasOlder, loadingOlder, loadOlder } = usePlayerRecord()
const championsById = useStaticChampionsById()
const { items, summoners, runeTree } = useStaticData()
const { aliasOf, patch } = useChampionStatics()

const filter = ref<QueueFilter>('all')
const latest = computed(() => record.value?.games ?? [])
const games = computed(() => gamesIn(latest.value, filter.value))
const formMatches = computed(() => counted(games.value).map(game => toMatchSummary(game, null)))
const pending = computed(() => !record.value && (status.value === 'pending' || status.value === 'idle'))

const champions = computed(() => [...championsById.value.values()])
const ranked = computed(() => toProfileRanked(record.value?.ranked.find(queue => queue.queueType === 'RANKED_SOLO_5x5') ?? null))
const nameTag = computed(() => state.value.riotId?.replace('#', '-') ?? '')
const historyGames = computed(() => gamesIn(loadedGames.value, filter.value))
const filterLabel = computed(() => QUEUE_FILTERS.find(option => option.value === filter.value)?.label ?? 'All')
const activity = computed(() => activityFromGames(loadedGames.value))

const today = useToday(historyGames, loadedGames, rankHistory)

const identity = computed(() => ({
  gameName: state.value.riotId?.split('#')[0] ?? 'Logging in…',
  tagLine: state.value.riotId?.split('#')[1] ?? null,
  profileIconId: state.value.profileIconId,
  platformId: record.value?.platformId ?? null,
}))

/** Without a chosen background the client shows the most-mastered champion; so does the app. */
const splashUrl = computed(() => {
  const skinId = record.value?.backgroundSkinId
    ?? (state.value.championPool[0] ? state.value.championPool[0] * 1000 : null)
  const alias = skinId ? aliasOf(Math.floor(skinId / 1000)) : backdropAlias()
  if (!alias) return null
  return skinId ? splashOfSkin(alias, skinId % 1000) : splashOfAlias(alias)
})

const queueTabs = QUEUE_FILTERS.map(option => ({ label: option.label, value: option.value }))

const { goals, evaluations, canAdd, create, abandon, suggestions } = useGoals()
const editing = ref(false)
const editingFrom = ref<GoalDraft | null>(null)
function editGoal(draft: GoalDraft | null) {
  editingFrom.value = draft
  editing.value = true
}

onMounted(() => refresh())
watch(() => state.value.riotId, () => refresh())

// A game that ends reaches the history once the client has its stats: read
// again at the end-of-game screen, and once more on the way back to the lobby.
const POST_GAME: GameflowPhase[] = ['WaitingForStats', 'PreEndOfGame', 'EndOfGame']
watch(() => state.value.phase, (next, previous) => {
  if (next === 'EndOfGame' || (POST_GAME.includes(previous) && !POST_GAME.includes(next))) void refresh(true)
})
</script>

<template>
  <div class="flex h-full flex-col gap-4 overflow-y-auto p-5">
    <template v-if="state.connected && state.riotId">
      <PagePlayerProfile
        :identity="identity"
        :patch="patch || null"
        :level="state.summonerLevel"
        :ranked="ranked"
        :splash-url="splashUrl"
        :matches="formMatches"
        :loading="pending"
      >
        <template #header>
          <div class="flex items-center gap-3">
            <UIcon name="i-lucide-house" class="size-6 text-primary" />
            <h1 class="text-2xl font-semibold tracking-tight text-highlighted">Dashboard</h1>
            <div class="ml-auto flex items-center gap-2">
              <UTabs
                v-model="filter"
                :items="queueTabs"
                :content="false"
                variant="pill"
                color="neutral"
                size="xs"
                :ui="{ list: 'ring ring-default', indicator: 'rounded-md bg-accented shadow-none', trigger: 'px-3 data-[state=active]:text-highlighted' }"
              />
              <UButton
                icon="i-lucide-refresh-cw"
                color="neutral"
                variant="ghost"
                size="sm"
                :loading="status === 'pending'"
                aria-label="Read your games again"
                @click="refresh(true)"
              />
            </div>
          </div>
          <UAlert
            v-if="status === 'error' && !record"
            color="neutral"
            variant="subtle"
            icon="i-lucide-circle-alert"
            title="Your client did not share your match history"
            description="It sometimes needs a moment after logging in."
            :actions="[{ label: 'Try again', color: 'neutral', variant: 'outline', onClick: () => refresh(true) }]"
          />
        </template>

        <template #history>
          <DashboardMatchHistory
            :games="historyGames"
            :all-games="loadedGames"
            :rank-history="rankHistory"
            :queue="filterLabel"
            :pending="pending"
            :has-older="hasOlder"
            :loading-older="loadingOlder"
            :champions="champions"
            :items="items"
            :summoner-spells="summoners"
            :rune-tree="runeTree"
            :name-tag="nameTag"
            @load-older="loadOlder"
          />
        </template>

        <template #aside>
          <ProfileRankedCardSkeleton v-if="pending" />
          <ProfileRankedCard v-else :ranked="ranked" :history="rankHistory" />
          <DashboardTodayCard v-if="!pending" :summary="today" />
          <DashboardClipsCard />
          <template v-if="pending">
            <USkeleton class="h-40 rounded-lg" />
            <USkeleton class="h-56 rounded-lg" />
            <USkeleton class="h-40 rounded-lg" />
          </template>
          <template v-else>
            <!-- Folded from the client's games: the patch window needs a patch
                 they do not carry, so the dashboard offers day, week and month. -->
            <ProfileActivityHeatmap :data="activity" :modes="['day', 'week', 'month']" />
            <DashboardGoalsCard
              :goals="goals"
              :evaluations="evaluations"
              :suggestions="suggestions(filter)"
              :can-add="canAdd"
              :latest-game-id="latest[0]?.gameId ?? null"
              @edit="editGoal"
              @abandon="abandon"
            />
            <DashboardChampionsCard :lines="championLines(games)" :champions="champions" />
            <DashboardRolesCard :lanes="laneLines(games)" />
          </template>
        </template>
      </PagePlayerProfile>

      <DashboardGoalEditor v-model:open="editing" :games="loadedGames" :initial="editingFrom" @save="create" />
    </template>

    <template v-else>
      <section class="relative flex min-h-56 items-end overflow-hidden rounded-2xl border border-default p-7">
        <ChampionArt :alias="backdropAlias()" fade="x" position="70% 22%" />
        <div class="relative flex max-w-lg flex-col gap-3">
          <span class="flex items-center gap-2">
            <span class="relative flex size-2">
              <span class="absolute inline-flex size-full animate-tm-pulse rounded-full bg-primary" />
              <span class="relative inline-flex size-2 rounded-full bg-primary/80" />
            </span>
            <span class="eyebrow">{{ state.connected ? 'Client found' : 'Companion' }}</span>
          </span>
          <h1 class="text-4xl font-semibold tracking-tight text-balance text-highlighted">
            <template v-if="state.connected">Logging <span class="text-primary">in</span>…</template>
            <template v-else>Waiting for <span class="text-primary">your client</span>.</template>
          </h1>
          <p class="text-sm text-muted">
            Start League of Legends: your profile opens here, and the app follows you into champion select.
          </p>
        </div>
      </section>

      <DashboardMeta />
    </template>

    <RiotDisclaimer class="mt-auto pt-2" />
  </div>
</template>
